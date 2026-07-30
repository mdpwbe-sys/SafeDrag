namespace SafeExplorer.Shared;

public sealed class AppSettings
{
    public string AuthorizationKey { get; set; } = "RightClick"; // RightClick | Space | Shift | Control | Alt
    public string ActivationMode { get; set; } = "Hold";         // Hold | Toggle
    public bool ProtectOneDrive { get; set; } = true;
    public bool ProtectSystemFolders { get; set; } = true;
    public bool EnableLogging { get; set; } = false;
    public List<string> WhitelistedFolders { get; set; } = new();
    public List<string> ProtectedFolders { get; set; } = new(); // Dynamic protected paths
}
