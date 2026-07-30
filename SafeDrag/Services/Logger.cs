using System.IO;
using System.Text;
using System.Text.Json;
using SafeExplorer.Shared;

namespace SafeExplorer.Tray.Services;

public sealed class Logger
{
    private const long MaxDailyLogBytes = 10 * 1024 * 1024;
    private const int MaxLoggedPathLength = 32767;

    private readonly string _folder;
    private readonly object _sync = new();
    private bool _enabled;

    public Logger(bool enabled, string? folder = null)
    {
        _enabled = enabled;

        _folder = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeExplorer",
            "Logs");

        Directory.CreateDirectory(_folder);
        PurgeOldLogs(14);
    }

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            _enabled = enabled;
        }
    }

    public void PurgeOldLogs(int maxDays = 14)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDays, 1);

        try
        {
            lock (_sync)
            {
                DateTime cutoffDate = DateTime.Today.AddDays(-(maxDays - 1));
                var logFiles = Directory.EnumerateFiles(_folder, "safeexplorer-*.log");
                foreach (var file in logFiles)
                {
                    string dateText = Path.GetFileNameWithoutExtension(file)
                        ["safeexplorer-".Length..];

                    DateTime fileDate = DateTime.TryParseExact(
                        dateText,
                        "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out var parsedDate)
                        ? parsedDate
                        : File.GetLastWriteTime(file).Date;

                    if (fileDate < cutoffDate)
                    {
                        File.Delete(file);
                    }
                }
            }
        }
        catch
        {
            // Ignore purge errors
        }
    }

    public void Write(SecurityLog entry)
    {
        try
        {
            lock (_sync)
            {
                if (!_enabled) return;

                string file = Path.Combine(_folder, $"safeexplorer-{DateTime.Now:yyyy-MM-dd}.log");
                entry.SourcePath = Truncate(entry.SourcePath, MaxLoggedPathLength);
                entry.DestinationPath = Truncate(entry.DestinationPath, MaxLoggedPathLength);
                entry.Reason = Truncate(entry.Reason, 128);
                string line = JsonSerializer.Serialize(entry) + Environment.NewLine;
                long currentLength = File.Exists(file) ? new FileInfo(file).Length : 0;
                if (currentLength + Encoding.UTF8.GetByteCount(line) > MaxDailyLogBytes)
                    return;

                File.AppendAllText(file, line);
            }
        }
        catch
        {
            // Ignore log write errors
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value ?? string.Empty;

        return value[..maxLength];
    }
}
