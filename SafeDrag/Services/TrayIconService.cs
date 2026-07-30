using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SafeExplorer.Shared;

namespace SafeExplorer.Tray.Services;

public sealed class TrayIconService : System.IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly TaskbarCreatedFilter _taskbarCreatedFilter;
    private readonly SafeState _state;
    private readonly SettingsService _settingsService;
    private readonly ProtectionService _protectionService;
    private readonly Logger _logger;

    private readonly ToolStripMenuItem _holdMenuItem;
    private readonly ToolStripMenuItem _toggleMenuItem;
    private readonly ToolStripMenuItem _oneDriveMenuItem;
    private readonly ToolStripMenuItem _systemFoldersMenuItem;
    private readonly ToolStripMenuItem _loggingMenuItem;
    private readonly ToolStripMenuItem _whitelistInfoMenuItem;
    private readonly Dictionary<AuthorizationKey, ToolStripMenuItem> _keyMenuItems = new();
    private bool _disposed;

    private static readonly uint WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

    public TrayIconService(
        SafeState state,
        SettingsService settingsService,
        ProtectionService protectionService,
        Logger logger,
        Action onExit)
    {
        _state = state;
        _settingsService = settingsService;
        _protectionService = protectionService;
        _logger = logger;

        _contextMenu = new ContextMenuStrip();
        var contextMenu = _contextMenu;

        var titleItem = contextMenu.Items.Add("🛡️ SafeExplorer Guard");
        titleItem.Enabled = false;
        contextMenu.Items.Add(new ToolStripSeparator());

        // --- Whitelist ---
        contextMenu.Items.Add("▶ Ajouter un dossier à la whitelist", null, (_, _) => AddFolderToWhitelist());
        _whitelistInfoMenuItem = new ToolStripMenuItem($"▶ Gérer la whitelist ({_settingsService.Current.WhitelistedFolders.Count} dossiers)", null, (_, _) => ShowWhitelistDialog());
        contextMenu.Items.Add(_whitelistInfoMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // --- Protection Status & Toggles ---
        _oneDriveMenuItem = new ToolStripMenuItem("Protection OneDrive", null, (_, _) => ToggleOneDriveProtection());
        _systemFoldersMenuItem = new ToolStripMenuItem("Protection Dossiers Système", null, (_, _) => ToggleSystemFoldersProtection());
        _loggingMenuItem = new ToolStripMenuItem("Journalisation des accès (Logs)", null, (_, _) => ToggleLogging());

        UpdateProtectionMenuLabels();

        contextMenu.Items.Add(_oneDriveMenuItem);
        contextMenu.Items.Add(_systemFoldersMenuItem);
        contextMenu.Items.Add(_loggingMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // --- Sous-menu: Touche d'autorisation ---
        var keySubMenu = new ToolStripMenuItem("Touche d'autorisation");
        AddKeyMenuItem(keySubMenu, AuthorizationKey.RightClick, "Clic Droit Souris (Recommandé)");
        AddKeyMenuItem(keySubMenu, AuthorizationKey.Space, "Barre ESPACE");
        AddKeyMenuItem(keySubMenu, AuthorizationKey.Shift, "Touche MAJ (Shift)");
        AddKeyMenuItem(keySubMenu, AuthorizationKey.Control, "Touche CTRL (Control)");
        AddKeyMenuItem(keySubMenu, AuthorizationKey.Alt, "Touche ALT (Alt)");
        contextMenu.Items.Add(keySubMenu);

        // --- Sous-menu: Mode d'activation ---
        var modeSubMenu = new ToolStripMenuItem("Mode d'activation");
        _holdMenuItem = new ToolStripMenuItem("Maintenir la touche", null, (_, _) => SetMode(RightClickMode.Hold))
            { Checked = _state.Mode == RightClickMode.Hold };
        _toggleMenuItem = new ToolStripMenuItem("Appui simple (Bascule)", null, (_, _) => SetMode(RightClickMode.Toggle))
            { Checked = _state.Mode == RightClickMode.Toggle };
        modeSubMenu.DropDownItems.Add(_holdMenuItem);
        modeSubMenu.DropDownItems.Add(_toggleMenuItem);
        contextMenu.Items.Add(modeSubMenu);

        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Quitter", null, (_, _) => onExit());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "SafeExplorer Guard Active",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _taskbarCreatedFilter = new TaskbarCreatedFilter(RestoreNotifyIcon);
        Application.AddMessageFilter(_taskbarCreatedFilter);
    }

    private void AddFolderToWhitelist()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Sélectionnez un dossier à ajouter à la Whitelist (toujours autorisé)"
        };

        if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            string path = dialog.SelectedPath;
            if (!_settingsService.Current.WhitelistedFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _settingsService.Current.WhitelistedFolders.Add(path);
                _settingsService.Save();
                UpdateProtectionMenuLabels();
                MessageBox.Show($"Dossier ajouté à la whitelist :\n{path}", "SafeExplorer Whitelist", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }

    private void ShowWhitelistDialog()
    {
        var folders = _settingsService.Current.WhitelistedFolders;
        if (folders.Count == 0)
        {
            MessageBox.Show("La whitelist est actuellement vide.", "SafeExplorer Whitelist", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string list = string.Join("\n", folders);
        var result = MessageBox.Show($"Dossiers whitelisted :\n\n{list}\n\nVoulez-vous vider la whitelist ?", "SafeExplorer Whitelist", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result == DialogResult.Yes)
        {
            folders.Clear();
            _settingsService.Save();
            UpdateProtectionMenuLabels();
        }
    }

    private void ToggleOneDriveProtection()
    {
        _settingsService.Current.ProtectOneDrive = !_settingsService.Current.ProtectOneDrive;
        _settingsService.Save();
        UpdateProtectionMenuLabels();
    }

    private void ToggleSystemFoldersProtection()
    {
        _settingsService.Current.ProtectSystemFolders = !_settingsService.Current.ProtectSystemFolders;
        _settingsService.Save();
        UpdateProtectionMenuLabels();
    }

    private void ToggleLogging()
    {
        _settingsService.Current.EnableLogging = !_settingsService.Current.EnableLogging;
        _settingsService.Save();
        UpdateProtectionMenuLabels();
    }

    private void UpdateProtectionMenuLabels()
    {
        int oneDriveCount = ProtectionService.DiscoverOneDriveFolders().Count();
        _oneDriveMenuItem.Text = $"Protection OneDrive ({oneDriveCount} dossier{(oneDriveCount > 1 ? "s" : "")})";
        _oneDriveMenuItem.Checked = _settingsService.Current.ProtectOneDrive;

        _systemFoldersMenuItem.Text = "Protection Dossiers Système";
        _systemFoldersMenuItem.Checked = _settingsService.Current.ProtectSystemFolders;

        _loggingMenuItem.Text = "Journalisation (Logs)";
        _loggingMenuItem.Checked = _settingsService.Current.EnableLogging;

        _whitelistInfoMenuItem.Text = $"▶ Gérer la whitelist ({_settingsService.Current.WhitelistedFolders.Count} dossier{(_settingsService.Current.WhitelistedFolders.Count > 1 ? "s" : "")})";
    }

    private void RestoreNotifyIcon()
    {
        if (_disposed)
            return;

        _notifyIcon.Visible = false;
        _notifyIcon.Visible = true;
    }

    private void AddKeyMenuItem(ToolStripMenuItem parent, AuthorizationKey key, string label)
    {
        var item = new ToolStripMenuItem(label, null, (_, _) => SetKey(key))
            { Checked = _state.Key == key };
        _keyMenuItems[key] = item;
        parent.DropDownItems.Add(item);
    }

    private void SetKey(AuthorizationKey key)
    {
        _state.Key = key;
        _settingsService.Current.AuthorizationKey = key.ToString();
        _settingsService.Save();

        foreach (var kvp in _keyMenuItems)
            kvp.Value.Checked = kvp.Key == key;
    }

    private void SetMode(RightClickMode mode)
    {
        _state.Mode = mode;
        _settingsService.Current.ActivationMode = mode.ToString();
        _settingsService.Save();

        _holdMenuItem.Checked = mode == RightClickMode.Hold;
        _toggleMenuItem.Checked = mode == RightClickMode.Toggle;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Application.RemoveMessageFilter(_taskbarCreatedFilter);
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern uint RegisterWindowMessage([MarshalAs(UnmanagedType.LPWStr)] string lpString);

    private sealed class TaskbarCreatedFilter : IMessageFilter
    {
        private readonly Action _onTaskbarCreated;

        public TaskbarCreatedFilter(Action onTaskbarCreated)
        {
            _onTaskbarCreated = onTaskbarCreated;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == (int)WM_TASKBARCREATED)
            {
                _onTaskbarCreated();
            }
            return false;
        }
    }
}
