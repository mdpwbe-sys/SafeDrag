using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SafeExplorer.Hook;

[ComImport]
[Guid("000214EF-0000-0000-C000-000000000464")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ICopyHookW
{
    [PreserveSig]
    uint CopyCallback(
        IntPtr hwnd,
        uint wFunc,
        uint wFlags,
        [MarshalAs(UnmanagedType.LPWStr)] string pszSrcFile,
        uint dwSrcAttribs,
        [MarshalAs(UnmanagedType.LPWStr)] string pszDestFile,
        uint dwDestAttribs);
}

[ComVisible(true)]
[Guid("7B896489-3F1E-4E7B-8B2B-987813C12A34")]
[ClassInterface(ClassInterfaceType.None)]
public class CopyHook : ICopyHookW
{
    private const uint FO_MOVE = 0x0001;
    private const uint FO_COPY = 0x0002;
    private const uint IDYES = 6;
    private const uint IDNO = 7;

    public uint CopyCallback(
        IntPtr hwnd,
        uint wFunc,
        uint wFlags,
        string pszSrcFile,
        uint dwSrcAttribs,
        string pszDestFile,
        uint dwDestAttribs)
    {
        // On ne s'intéresse qu'aux déplacements (FO_MOVE = 1) et copies (FO_COPY = 2)
        if (wFunc != FO_MOVE && wFunc != FO_COPY)
            return IDYES;

        if (string.IsNullOrEmpty(pszSrcFile) || string.IsNullOrEmpty(pszDestFile))
            return IDYES;

        int operation = (wFunc == FO_MOVE) ? 1 : 0;
        string jsonRequest = JsonSerializer.Serialize(new
        {
            SourcePath = pszSrcFile,
            DestinationPath = pszDestFile,
            Operation = operation
        });

        bool allowed = PipeClient.Query(jsonRequest);
        return allowed ? IDYES : IDNO;
    }
}

public static class PipeClient
{
    [SuppressMessage("Design", "CA1031", Justification = "Une extension Shell doit rester fail-open pour ne jamais bloquer Explorer sur une erreur IPC.")]
    public static bool Query(string jsonRequest)
    {
        string[] pipeNames = new[] { "SafeExplorerPipe", "SafeDragIpcPipe" };

        foreach (var pipeName in pipeNames)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                pipe.Connect(500);

                byte[] reqBytes = Encoding.UTF8.GetBytes(jsonRequest + "\n");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                pipe.WriteAsync(reqBytes.AsMemory(), timeout.Token).AsTask().GetAwaiter().GetResult();
                pipe.FlushAsync(timeout.Token).GetAwaiter().GetResult();

                byte[] buffer = new byte[512];
                int read = pipe.ReadAsync(buffer.AsMemory(), timeout.Token).AsTask().GetAwaiter().GetResult();
                if (read > 0)
                {
                    string response = Encoding.UTF8.GetString(buffer, 0, read);
                    using var document = JsonDocument.Parse(response);
                    if (document.RootElement.TryGetProperty("Allowed", out var allowed) &&
                        (allowed.ValueKind == JsonValueKind.True || allowed.ValueKind == JsonValueKind.False))
                        return allowed.GetBoolean();
                }
            }
            catch
            {
                // En cas d'erreur ou pipe non disponible -> fail-open (sécurité)
            }
        }

        return true;
    }
}
