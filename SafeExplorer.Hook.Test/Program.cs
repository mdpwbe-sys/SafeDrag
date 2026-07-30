using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SafeExplorer.Hook;
using SafeExplorer.Shared;
using SafeExplorer.Tray.Services;

namespace SafeExplorer.Hook.Test;

class Program
{
    static int Main(string[] args)
    {
        if (args.Contains("--sprint3", StringComparer.OrdinalIgnoreCase))
            return RunSprint3Tests();

        Console.WriteLine("Testing CopyHook directly...");
        var hook = new CopyHook();
        // FO_MOVE = 1
        uint result = hook.CopyCallback(IntPtr.Zero, 1, 0, @"C:\test1", 0, @"C:\test2", 0);
        Console.WriteLine($"CopyCallback result: {result} (IDYES=6, IDNO=7)");
        if (result == 7)
        {
            Console.WriteLine("✅ IDNO (7) returned -> Move is BLOCKED when SafeDrag is RED!");
        }
        else if (result == 6)
        {
            Console.WriteLine("✅ IDYES (6) returned -> Move is ALLOWED when SafeDrag is GREEN!");
        }

        return 0;
    }

    private static int RunSprint3Tests()
    {
        var settings = new AppSettings
        {
            ProtectOneDrive = false,
            ProtectSystemFolders = false,
            EnableLogging = false
        };
        var protection = new ProtectionService(settings);
        settings.ProtectedFolders.Add(@"C:\Windows");
        settings.WhitelistedFolders.Add(@"C:\Trusted");

        Check(protection.IsProtected(@"C:\Windows\System32\cmd.exe"), "Un descendant protégé doit être détecté.");
        Check(!protection.IsProtected(@"C:\WindowsOld\file.txt"), "Un simple préfixe ne doit pas être protégé.");
        Check(protection.IsWhitelisted(@"c:\trusted\child\file.txt"), "La whitelist doit ignorer la casse.");
        Check(!protection.IsWhitelisted(@"C:\TrustedBackup\file.txt"), "Un simple préfixe ne doit pas être whitelisté.");

        var state = new SafeState { Mode = RightClickMode.Toggle };
        state.SetRightClickPressed(true);
        Check(state.IsRightClickValidated, "Le premier appui doit autoriser.");
        state.SetRightClickPressed(false);
        state.SetRightClickPressed(true);
        Check(!state.IsRightClickValidated, "Le deuxième appui doit reverrouiller.");

        state.SetRightClickPressed(false);
        state.SetRightClickPressed(true);
        state.SetDragging(true);
        state.SetDragging(false);
        Check(!state.IsRightClickValidated, "La fin du drag doit réinitialiser le mode Bascule.");

        RunPersistenceAndLoggingTests(protection);
        Console.WriteLine("PASS Sprint 3: chemins, bascule, settings, logs, moteur et IPC.");
        return 0;
    }

    private static void RunPersistenceAndLoggingTests(ProtectionService protection)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "SafeExplorer-Sprint3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string settingsFolder = Path.Combine(tempRoot, "Settings");
            var settingsService = new SettingsService(settingsFolder);
            var persisted = settingsService.Load();
            int changed = 0;
            settingsService.SettingsChanged += _ => changed++;
            string trusted = Path.Combine(tempRoot, "Trusted");
            persisted.WhitelistedFolders.Add(trusted);
            persisted.WhitelistedFolders.Add(trusted + Path.DirectorySeparatorChar);
            persisted.AuthorizationKey = "InvalidKey";
            persisted.ActivationMode = "InvalidMode";
            settingsService.Save();
            Check(changed == 1, "La sauvegarde doit notifier les services en direct.");
            Check(File.Exists(Path.Combine(settingsFolder, "Settings.json")), "Settings.json doit être créé.");
            var reloaded = new SettingsService(settingsFolder).Load();
            Check(reloaded.WhitelistedFolders.Count == 1, "Les chemins persistés doivent être normalisés et dédupliqués.");
            Check(reloaded.AuthorizationKey == "RightClick", "Une touche invalide doit revenir au choix sûr par défaut.");
            Check(reloaded.ActivationMode == "Hold", "Un mode invalide doit revenir au maintien par défaut.");

            string logsFolder = Path.Combine(tempRoot, "Logs");
            Directory.CreateDirectory(logsFolder);
            string oldLog = Path.Combine(logsFolder, "safeexplorer-2000-01-01.log");
            File.WriteAllText(oldLog, "old");
            var logger = new Logger(true, logsFolder);
            Check(!File.Exists(oldLog), "Un journal antérieur à la rétention doit être supprimé.");

            bool authorized = false;
            var engine = new AuthorizationEngine(protection, logger, () => authorized);
            Check(engine.Decide(@"C:\Trusted\file.txt", "", out var reason) && reason == "Whitelisted", "La whitelist doit gagner.");
            Check(!engine.Decide(@"C:\Other\file.txt", "", out reason) && reason == "Locked", "Le verrouillage global doit aussi couvrir un chemin standard.");
            Check(!engine.Decide(@"C:\Windows\file.txt", "", out reason) && reason == "ProtectedAndLocked", "Un chemin protégé doit être bloqué.");
            authorized = true;
            Check(engine.Decide(@"C:\Windows\file.txt", "", out reason) && reason == "UserAuthorized", "L'autorisation utilisateur doit débloquer.");

            authorized = false;
            string pipeName = "SafeExplorerSprint3-" + Guid.NewGuid().ToString("N");
            var server = new IpcServer(new SafeState(), engine, new[] { pipeName });
            server.Start();
            server.Start();
            try
            {
                var blocked = QueryPipe(pipeName, JsonSerializer.Serialize(new { SourcePath = @"C:\Windows\file.txt", DestinationPath = @"C:\Temp" }));
                Check(!blocked.Allowed && blocked.Reason == "ProtectedAndLocked", "IPC doit bloquer un chemin protégé.");
                var invalid = QueryPipe(pipeName, "{}");
                Check(!invalid.Allowed && invalid.Reason == "InvalidRequest", "IPC doit rejeter une requête invalide.");
                var unknown = QueryPipe(pipeName, "UNKNOWN_COMMAND");
                Check(!unknown.Allowed && unknown.Reason == "InvalidRequest", "IPC doit refuser une commande inconnue.");
                var missingDestination = QueryPipe(pipeName, JsonSerializer.Serialize(new { SourcePath = @"C:\Windows\file.txt" }));
                Check(!missingDestination.Allowed && missingDestination.Reason == "InvalidRequest", "IPC doit exiger une destination.");
                var tooLarge = QueryPipe(pipeName, new string('X', 9000));
                Check(!tooLarge.Allowed && tooLarge.Reason == "RequestTooLarge", "IPC doit borner la taille des requêtes.");
                var whitelisted = QueryPipe(pipeName, JsonSerializer.Serialize(new { SourcePath = @"C:\Trusted\file.txt", DestinationPath = @"C:\Temp" }));
                Check(whitelisted.Allowed && whitelisted.Reason == "Whitelisted", "IPC doit appliquer la whitelist.");

                using (var silentClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
                {
                    silentClient.Connect(3000);
                    Task.Delay(2300).GetAwaiter().GetResult();
                }
                var afterTimeout = QueryPipe(pipeName, JsonSerializer.Serialize(new { SourcePath = @"C:\Trusted\file.txt", DestinationPath = @"C:\Temp" }));
                Check(afterTimeout.Allowed, "Un client silencieux ne doit pas monopoliser le serveur IPC.");
            }
            finally
            {
                server.Stop();
            }
            Parallel.For(0, 16, i => logger.Write(new SecurityLog { SourcePath = i.ToString() }));
            string todayLog = Path.Combine(logsFolder, $"safeexplorer-{DateTime.Now:yyyy-MM-dd}.log");
            Check(File.ReadLines(todayLog).Count() == 23, "Les écritures concurrentes du journal ne doivent pas être perdues.");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static (bool Allowed, string Reason) QueryPipe(string pipeName, string request)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        client.Connect(3000);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
        writer.WriteLine(request);
        string response = reader.ReadLine() ?? throw new InvalidOperationException("Réponse IPC absente.");
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        return (
            root.GetProperty("Allowed").GetBoolean(),
            root.TryGetProperty("Reason", out var reason) ? reason.GetString() ?? "" : "");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
