namespace SafeExplorer.Shared;

public sealed class SecurityLog
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public bool Allowed { get; set; }
    public string Reason { get; set; } = "";
}
