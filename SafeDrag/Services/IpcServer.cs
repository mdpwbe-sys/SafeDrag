using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace SafeExplorer.Tray.Services;

public sealed class IpcServer
{
    private const int MaxRequestBytes = 8192;
    private static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(2);

    private readonly SafeState _state;
    private readonly AuthorizationEngine? _authorizationEngine;
    private readonly string[] _pipeNames;
    private CancellationTokenSource? _cts;

    public IpcServer(SafeState state, AuthorizationEngine? authorizationEngine = null, IEnumerable<string>? pipeNames = null)
    {
        _state = state;
        _authorizationEngine = authorizationEngine;
        string[]? configuredNames = pipeNames?.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToArray();
        _pipeNames = configuredNames is { Length: > 0 }
            ? configuredNames
            : new[] { "SafeExplorerPipe", "SafeDragIpcPipe" };
    }

    public void Start()
    {
        if (_cts is { IsCancellationRequested: false })
            return;

        var cts = new CancellationTokenSource();
        _cts = cts;

        foreach (string pipeName in _pipeNames)
            Task.Run(() => ListenAsync(pipeName, cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task ListenAsync(string pipeName, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous |
                    PipeOptions.CurrentUserOnly |
                    PipeOptions.FirstPipeInstance);

                await pipe.WaitForConnectionAsync(cancellationToken);

                using var clientTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                clientTimeout.CancelAfter(ClientTimeout);
                CancellationToken clientToken = clientTimeout.Token;

                (string reqStr, bool requestTooLarge) = await ReadRequestAsync(pipe, clientToken);
                if (requestTooLarge || reqStr.Length > 0)
                {
                    bool isAuthorized;
                    string reason = "SimpleCheck";

                    if (requestTooLarge)
                    {
                        isAuthorized = false;
                        reason = "RequestTooLarge";
                    }
                    else if (reqStr.Equals("CHECK_AUTHORIZATION", StringComparison.OrdinalIgnoreCase))
                    {
                        isAuthorized = _state.IsRightClickValidated || _state.IsSpaceValidated;
                    }
                    else if (reqStr.StartsWith('{'))
                    {
                        // Payload JSON avec SourcePath et DestinationPath
                        string sourcePath = "";
                        string destPath = "";
                        bool validRequest = false;

                        try
                        {
                            using var doc = JsonDocument.Parse(reqStr, new JsonDocumentOptions { MaxDepth = 8 });
                            var root = doc.RootElement;
                            if (root.TryGetProperty("SourcePath", out var srcProp))
                                sourcePath = srcProp.GetString() ?? "";
                            if (root.TryGetProperty("DestinationPath", out var dstProp))
                                destPath = dstProp.GetString() ?? "";
                            validRequest = !string.IsNullOrWhiteSpace(sourcePath)
                                && !string.IsNullOrWhiteSpace(destPath)
                                && sourcePath.Length <= 32767
                                && destPath.Length <= 32767;
                        }
                        catch { }

                        if (!validRequest)
                        {
                            isAuthorized = false;
                            reason = "InvalidRequest";
                        }
                        else if (_authorizationEngine != null)
                        {
                            isAuthorized = _authorizationEngine.Decide(sourcePath, destPath, out reason);
                        }
                        else
                        {
                            isAuthorized = _state.IsRightClickValidated || _state.IsSpaceValidated;
                        }
                    }
                    else
                    {
                        isAuthorized = false;
                        reason = "InvalidRequest";
                    }

                    byte[] responseBytes;
                    if (reqStr.Equals("CHECK_AUTHORIZATION", StringComparison.OrdinalIgnoreCase))
                    {
                        responseBytes = Encoding.UTF8.GetBytes(isAuthorized ? "AUTHORIZED\n" : "DENIED\n");
                    }
                    else
                    {
                        string jsonResp = JsonSerializer.Serialize(new { Allowed = isAuthorized, Reason = reason }) + "\n";
                        responseBytes = Encoding.UTF8.GetBytes(jsonResp);
                    }

                    await pipe.WriteAsync(responseBytes.AsMemory(), clientToken);
                    await pipe.FlushAsync(clientToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // Un client silencieux ou bloqué ne doit pas monopoliser le serveur.
            }
            catch
            {
                // Éviter une boucle CPU si le nom du pipe est déjà occupé ou invalide.
                try
                {
                    await Task.Delay(100, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static async Task<(string Request, bool TooLarge)> ReadRequestAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        byte[] chunk = new byte[1024];
        using var request = new MemoryStream(MaxRequestBytes);
        bool tooLarge = false;
        int discardedBytes = 0;

        while (true)
        {
            int bytesRead = await pipe.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (bytesRead == 0)
                break;

            int newlineIndex = Array.IndexOf(chunk, (byte)'\n', 0, bytesRead);
            int contentLength = newlineIndex >= 0 ? newlineIndex : bytesRead;

            if (!tooLarge && request.Length + contentLength <= MaxRequestBytes)
            {
                request.Write(chunk, 0, contentLength);
            }
            else
            {
                tooLarge = true;
                discardedBytes += contentLength;
                if (discardedBytes > MaxRequestBytes * 8)
                    break;
            }

            if (newlineIndex >= 0)
                break;
        }

        if (tooLarge)
            return (string.Empty, true);

        return (Encoding.UTF8.GetString(request.GetBuffer(), 0, (int)request.Length).Trim(), false);
    }
}
