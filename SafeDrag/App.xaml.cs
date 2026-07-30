using System.Diagnostics.CodeAnalysis;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using SafeExplorer.Shared;
using SafeExplorer.Tray.Services;

namespace SafeExplorer.Tray;

[SuppressMessage("Design", "CA1001", Justification = "Les ressources sont libérées de façon déterministe dans OnExit.")]
public partial class App : System.Windows.Application
{
    private SafeState? _state;
    private DragMonitor? _monitor;
    private OverlayWindow? _overlay;
    private TrayIconService? _tray;
    private IpcServer? _ipcServer;

    private SettingsService? _settingsService;
    private Logger? _logger;
    private ProtectionService? _protectionService;
    private AuthorizationEngine? _authorizationEngine;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        string userId = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        _singleInstanceMutex = new Mutex(true, $@"Local\SafeDrag-{userId}", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show(
                "SafeDrag est déjà actif dans la zone de notification.",
                "SafeDrag",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _ownsSingleInstanceMutex = true;

        try
        {
            // 1. Charger la configuration et initialiser la persistance
            _settingsService = new SettingsService();
            var settings = _settingsService.Load();

            // 2. Initialiser le logger, le service de protection et le moteur de décision
            _logger = new Logger(settings.EnableLogging);
            _protectionService = new ProtectionService(settings);

            _state = new SafeState();
            ApplySettings(settings);
            _settingsService.SettingsChanged += ApplySettings;

            _authorizationEngine = new AuthorizationEngine(
                _protectionService,
                _logger,
                () => _state.IsRightClickValidated || _state.IsSpaceValidated);

            // 3. Démarrer le moniteur de glissement sans modifier le seuil Windows.
            _monitor = new DragMonitor(_state);
            _monitor.Start();

            // 4. Démarrer le serveur IPC connecté au moteur d'autorisation
            _ipcServer = new IpcServer(_state, _authorizationEngine);
            _ipcServer.Start();

            // 5. Initialiser la fenêtre d'overlay HUD
            _overlay = new OverlayWindow(_state);
            _overlay.Show();

            // 6. Initialiser l'icône dans la zone de notification (Tray)
            _tray = new TrayIconService(_state, _settingsService, _protectionService, _logger, ShutdownApp);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"SafeDrag n'a pas pu activer sa protection :\n{ex.Message}",
                "SafeDrag — démarrage impossible",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        if (_state != null)
        {
            if (Enum.TryParse<AuthorizationKey>(settings.AuthorizationKey, true, out var key))
                _state.Key = key;
            if (Enum.TryParse<RightClickMode>(settings.ActivationMode, true, out var mode))
                _state.Mode = mode;
        }

        _protectionService?.UpdateSettings(settings);
        _logger?.SetEnabled(settings.EnableLogging);
    }

    private void ShutdownApp()
    {
        _monitor?.Stop();
        _ipcServer?.Stop();
        _overlay?.Close();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_settingsService != null)
            _settingsService.SettingsChanged -= ApplySettings;
        _monitor?.Stop();
        _ipcServer?.Stop();
        _tray?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _ownsSingleInstanceMutex = false;
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
