using System.IO;
using Microsoft.Win32;
using SafeExplorer.Shared;

namespace SafeExplorer.Tray.Services;

public sealed class ProtectionService
{
    private readonly object _sync = new();
    private AppSettings _settings;

    public ProtectionService(AppSettings settings)
    {
        _settings = settings;
        RefreshProtectedFolders();
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_sync)
        {
            _settings = settings;
            RefreshProtectedFoldersCore();
        }
    }

    public void RefreshProtectedFolders()
    {
        lock (_sync)
        {
            RefreshProtectedFoldersCore();
        }
    }

    private void RefreshProtectedFoldersCore()
    {
        _settings.ProtectedFolders.Clear();

        if (_settings.ProtectOneDrive)
        {
            foreach (var path in DiscoverOneDriveFolders())
            {
                if (!_settings.ProtectedFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
                    _settings.ProtectedFolders.Add(path);
            }
        }

        if (_settings.ProtectSystemFolders)
        {
            AddIfValid(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            AddIfValid(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            AddIfValid(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            AddIfValid(Environment.GetFolderPath(Environment.SpecialFolder.System));
        }
    }

    private void AddIfValid(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !_settings.ProtectedFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            _settings.ProtectedFolders.Add(path);
        }
    }

    public bool IsWhitelisted(string path)
    {
        lock (_sync)
        {
            return _settings.WhitelistedFolders.Any(folder => IsSameOrDescendant(path, folder));
        }
    }

    public bool IsProtected(string path)
    {
        lock (_sync)
        {
            return _settings.ProtectedFolders.Any(folder => IsSameOrDescendant(path, folder));
        }
    }

    private static bool IsSameOrDescendant(string path, string folder)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder))
            return false;

        try
        {
            string normalizedPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)));
            string normalizedFolder = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder)));

            if (string.Equals(normalizedPath, normalizedFolder, StringComparison.OrdinalIgnoreCase))
                return true;

            string folderPrefix = Path.EndsInDirectorySeparator(normalizedFolder)
                ? normalizedFolder
                : normalizedFolder + Path.DirectorySeparatorChar;

            return normalizedPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<string> DiscoverOneDriveFolders()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Découverte via le Registre Windows (HKCU\Software\Microsoft\OneDrive\Accounts)
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
            if (key != null)
            {
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey != null)
                    {
                        var userFolder = subKey.GetValue("UserFolder") as string;
                        userFolder = Environment.ExpandEnvironmentVariables(userFolder ?? "");
                        if (!string.IsNullOrWhiteSpace(userFolder) && Directory.Exists(userFolder))
                        {
                            found.Add(userFolder);
                        }
                    }
                }
            }
        }
        catch { }

        // 2. Variables officielles exposées par le client OneDrive
        foreach (string variable in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
        {
            string? path = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                found.Add(path);
            }
        }

        // 3. Découverte pragmatique dans le profil utilisateur
        try
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Directory.Exists(userProfile))
            {
                foreach (var dir in Directory.EnumerateDirectories(userProfile))
                {
                    string name = Path.GetFileName(dir);
                    if (name.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(dir);
                    }
                }
            }
        }
        catch { }

        return found;
    }
}
