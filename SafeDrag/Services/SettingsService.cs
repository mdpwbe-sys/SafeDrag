using System.IO;
using System.Text.Json;
using SafeExplorer.Shared;

namespace SafeExplorer.Tray.Services;

public sealed class SettingsService
{
    private const long MaxSettingsBytes = 1_048_576;
    private const int MaxConfiguredPaths = 256;
    private const int MaxPathLength = 32767;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _sync = new();
    private AppSettings _current = new();

    public event Action<AppSettings>? SettingsChanged;

    public AppSettings Current => _current;

    public SettingsService(string? folder = null)
    {
        string settingsFolder = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeExplorer");

        Directory.CreateDirectory(settingsFolder);
        _path = Path.Combine(settingsFolder, "Settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            lock (_sync)
            {
                if (File.Exists(_path))
                {
                    if (new FileInfo(_path).Length > MaxSettingsBytes)
                        throw new InvalidDataException("Settings.json dépasse la taille maximale autorisée.");

                    string json = File.ReadAllText(_path);
                    _current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    Normalize(_current);
                }
                else
                {
                    _current = new AppSettings();
                    WriteSettings(_current);
                }
            }
        }
        catch
        {
            _current = new AppSettings();
        }

        return _current;
    }

    public void Save(AppSettings settings)
    {
        lock (_sync)
        {
            Normalize(settings);
            _current = settings;
            WriteSettings(settings);
        }

        SettingsChanged?.Invoke(settings);
    }

    public void Save() => Save(_current);

    private void WriteSettings(AppSettings settings)
    {
        string tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(tempPath, _path, true);
    }

    private static void Normalize(AppSettings settings)
    {
        settings.AuthorizationKey = Enum.TryParse<AuthorizationKey>(settings.AuthorizationKey, true, out var key)
            ? key.ToString()
            : AuthorizationKey.RightClick.ToString();
        settings.ActivationMode = Enum.TryParse<RightClickMode>(settings.ActivationMode, true, out var mode)
            ? mode.ToString()
            : RightClickMode.Hold.ToString();
        settings.WhitelistedFolders = NormalizePaths(settings.WhitelistedFolders);
        settings.ProtectedFolders = NormalizePaths(settings.ProtectedFolders);
    }

    private static List<string> NormalizePaths(IEnumerable<string>? paths)
    {
        if (paths is null)
            return new List<string>();

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Take(MaxConfiguredPaths)
            .Select(TryNormalizePath)
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? TryNormalizePath(string path)
    {
        try
        {
            if (path.Length > MaxPathLength)
                return null;

            return Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim())));
        }
        catch
        {
            return null;
        }
    }
}
