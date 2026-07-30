using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SafeExplorer.Tray.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace SafeExplorer.Tray;

public partial class OverlayWindow : Window
{
    private readonly SafeState _state;
    private double _dpiX = 1.0;
    private double _dpiY = 1.0;

    private bool _lastAuthorized;
    private bool _visualsDirty = true;

    private static readonly Brush LockedBackground    = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#65991B1B"));
    private static readonly Brush LockedBorder        = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0EF4444"));
    private static readonly Brush AuthorizedBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#65166534"));
    private static readonly Brush AuthorizedBorder     = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A022C55E"));

    public OverlayWindow(SafeState state)
    {
        InitializeComponent();
        _state = state;

        // Pré-créer le HWND pour éviter la latence du 1er drag
        new WindowInteropHelper(this).EnsureHandle();

        Visibility = Visibility.Hidden;

        // Lire le DPI une seule fois au chargement
        Loaded += (_, _) =>
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _dpiX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            _dpiY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
        };

        // Événement direct pour show/hide instantané (déclenché par DragMonitor timer 16ms)
        _state.DraggingStateChanged += OnDraggingStateChanged;
        _state.StateChanged += OnStateChanged;
        Closed += (_, _) =>
        {
            _state.DraggingStateChanged -= OnDraggingStateChanged;
            _state.StateChanged -= OnStateChanged;
            CompositionTarget.Rendering -= OnRendering;
        };
    }

    private void OnDraggingStateChanged(bool isDragging)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => SetDraggingVisibility(isDragging)));
            return;
        }

        SetDraggingVisibility(isDragging);
    }

    private void SetDraggingVisibility(bool isDragging)
    {
        if (isDragging)
        {
            _visualsDirty = true;
            UpdateFrame();
            Visibility = Visibility.Visible;
            CompositionTarget.Rendering -= OnRendering;
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
            Visibility = Visibility.Hidden;
        }
    }

    // Appelé à chaque frame VSync (60/120Hz selon l'écran) — uniquement pendant le drag
    private void OnRendering(object? sender, EventArgs e) => UpdateFrame();

    private void OnStateChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(RefreshAuthorizationVisuals));
            return;
        }

        RefreshAuthorizationVisuals();
    }

    private void UpdateFrame()
    {
        if (GetCursorPos(out var pt))
        {
            Left = pt.X / _dpiX + 32;
            Top  = pt.Y / _dpiY + 44;
        }

        RefreshAuthorizationVisuals();
    }

    private void RefreshAuthorizationVisuals()
    {
        bool authorized = _state.IsRightClickValidated;
        if (authorized != _lastAuthorized || _visualsDirty)
        {
            _lastAuthorized = authorized;
            _visualsDirty = false;
            ApplyVisuals(authorized);
        }
    }

    private void ApplyVisuals(bool authorized)
    {
        if (authorized)
        {
            StatusIcon.Text = "✅";
            StatusText.Text = "Déplacement autorisé";
            OverlayBorder.Background  = AuthorizedBackground;
            OverlayBorder.BorderBrush = AuthorizedBorder;
        }
        else
        {
            StatusIcon.Text = "🔒";
            string keyName = GetKeyDisplayName(_state.Key);
            StatusText.Text = _state.Mode == RightClickMode.Hold
                ? $"Maintenir {keyName}"
                : $"{keyName} pour autoriser";
            OverlayBorder.Background  = LockedBackground;
            OverlayBorder.BorderBrush = LockedBorder;
        }
    }

    private static string GetKeyDisplayName(AuthorizationKey key) => key switch
    {
        AuthorizationKey.RightClick => "Clic Droit",
        AuthorizationKey.Space      => "ESPACE",
        AuthorizationKey.Shift      => "MAJ",
        AuthorizationKey.Control    => "CTRL",
        AuthorizationKey.Alt        => "ALT",
        _ => "Clic Droit"
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
