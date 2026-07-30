using SafeExplorer.Shared;

namespace SafeExplorer.Tray.Services;

public sealed class AuthorizationEngine
{
    private readonly ProtectionService _protection;
    private readonly Logger _logger;
    private readonly Func<bool> _isCurrentlyAuthorized;

    public AuthorizationEngine(
        ProtectionService protection,
        Logger logger,
        Func<bool> isCurrentlyAuthorized)
    {
        _protection = protection;
        _logger = logger;
        _isCurrentlyAuthorized = isCurrentlyAuthorized;
    }

    public bool Decide(string sourcePath, string destinationPath, out string reason)
    {
        // 1. Whitelist → toujours autorisé
        if (_protection.IsWhitelisted(sourcePath))
        {
            reason = "Whitelisted";
            Log(sourcePath, destinationPath, true, reason);
            return true;
        }

        // 2. Hors whitelist, le verrouillage global reste la règle.
        bool isProtected = _protection.IsProtected(sourcePath);
        bool allowed = _isCurrentlyAuthorized();
        reason = allowed
            ? "UserAuthorized"
            : isProtected ? "ProtectedAndLocked" : "Locked";

        Log(sourcePath, destinationPath, allowed, reason);
        return allowed;
    }

    private void Log(string source, string dest, bool allowed, string reason)
    {
        _logger.Write(new SecurityLog
        {
            SourcePath = source,
            DestinationPath = dest,
            Allowed = allowed,
            Reason = reason
        });
    }
}
