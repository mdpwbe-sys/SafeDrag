using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;

namespace SafeExplorer.Tray.Services;

/// <summary>
/// DragMonitor avec hook ultra-léger.
///
/// RÈGLE D'OR : HookCallback ne fait QUE lire/écrire des variables volatile.
/// AUCUN appel à SafeState, Dispatcher, lock, ou API lourde dans le hook.
/// Toute la logique applicative est dans le timer UI (16ms).
/// </summary>
public sealed class DragMonitor
{
    private readonly SafeState _state;
    private readonly DispatcherTimer _uiTimer;

    private static HookProc? _mouseHookProc;
    private static IntPtr _hookId = IntPtr.Zero;

    // ── Variables du hook — volatile, accessibles sans lock ─────────────
    // Seul le hook les écrit. Le timer UI les lit uniquement.
    private static volatile bool _hLeftDown;
    private static volatile bool _hRightDown;
    private static volatile bool _hDragDetected;
    private static volatile bool _hQualifiedDrag;
    private static volatile int  _hDownX;
    private static volatile int  _hDownY;
    private static volatile int  _hGestureId;
    private static volatile bool _hSwallowNextRUp;    // absorber le prochain RBUTTONUP
    private static volatile bool _hNeedDeauthorize;   // signal: RightUp reçu pendant drag
    private static volatile bool _hAuthorizationValidated;
    private static volatile bool _hCancelDropRequested;
    private static volatile int  _hAuthorizationKey;
    private static volatile bool _hToggleMode;

    // ── État du timer UI — ne jamais lire dans le hook ──────────────────
    private bool _timerLastDragging;
    private bool _timerLastAuthorized;
    private int _timerGestureId = -1;
    private const int DragThreshold = 8;

    public DragMonitor(SafeState state)
    {
        _state = state;

        // Timer UI à 60fps — toute la logique applicative est ici
        _uiTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _uiTimer.Tick += (_, _) => UiTick();
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero)
            return;

        PrewarmAutomation();
        SyncHookAuthorizationState();
        _mouseHookProc = HookCallback;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, GetModuleHandle(null), 0);
        if (_hookId == IntPtr.Zero)
        {
            _mouseHookProc = null;
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Le hook souris global n'a pas pu être installé.");
        }
        _uiTimer.Start();
    }

    public void Stop()
    {
        _uiTimer.Stop();
        if (_hookId != IntPtr.Zero) { UnhookWindowsHookEx(_hookId); _hookId = IntPtr.Zero; }
        _mouseHookProc = null;
        _hLeftDown = false;
        _hRightDown = false;
        _hDragDetected = false;
        _hQualifiedDrag = false;
        _hCancelDropRequested = false;
        _hSwallowNextRUp = false;
        _state.SetRightClickPressed(false);
        UpdateDraggingState(false);
    }

    private static void PrewarmAutomation()
    {
        try
        {
            _ = AutomationElement.RootElement.Current.ControlType;
            if (GetCursorPos(out POINT pt))
                _ = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y));
        }
        catch { }
    }

    // ═════════════════════════════════════════════════════════════════════
    // HOOK CALLBACK — ULTRA LÉGER
    // Uniquement : lire flags, écrire volatile, décider swallow.
    // INTERDICTION : SafeState, Dispatcher, lock, I/O, API lourde.
    // ═════════════════════════════════════════════════════════════════════
    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hookId, nCode, wParam, lParam);

        var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        if ((hs.flags & LLMHF_INJECTED) != 0)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        int msg = wParam.ToInt32();

        switch (msg)
        {
            case WM_LBUTTONDOWN:
            {
                _hLeftDown     = true;
                _hDragDetected = false;
                _hQualifiedDrag = false;
                _hDownX        = hs.pt.X;
                _hDownY        = hs.pt.Y;
                unchecked { _hGestureId++; }
                break;
            }

            case WM_LBUTTONUP:
            {
                // Si drag en cours et clic droit tenu → noter pour swallow du prochain RUp
                if (_hDragDetected && _hQualifiedDrag && _hRightDown)
                    _hSwallowNextRUp = true;

                bool cancelDrop = _hDragDetected
                                  && _hQualifiedDrag
                                  && !IsAuthorizedAtRelease();
                _hCancelDropRequested = cancelDrop;
                _hLeftDown     = false;
                _hDragDetected = false;
                _hQualifiedDrag = false;
                // ⚠️ Ne pas appeler SafeState ici — le timer s'en charge
                if (cancelDrop)
                    return (IntPtr)1;
                break;
            }

            case WM_RBUTTONDOWN:
            case WM_NCRBUTTONDOWN:
            {
                _hRightDown = true;
                if (_hAuthorizationKey == (int)AuthorizationKey.RightClick && !_hToggleMode)
                    _hAuthorizationValidated = true;
                // Swallow uniquement si drag confirmé
                if (_hDragDetected && _hQualifiedDrag)
                    return (IntPtr)1;
                break;
            }

            case WM_RBUTTONUP:
            case WM_NCRBUTTONUP:
            {
                _hRightDown         = false;
                _hNeedDeauthorize   = true; // signal pour le timer
                if (_hAuthorizationKey == (int)AuthorizationKey.RightClick && !_hToggleMode)
                    _hAuthorizationValidated = false;

                if ((_hDragDetected && _hQualifiedDrag) || _hSwallowNextRUp)
                {
                    _hSwallowNextRUp = false;
                    return (IntPtr)1;
                }
                break;
            }

            case WM_MOUSEMOVE:
            {
                if (_hLeftDown && !_hDragDetected)
                {
                    int dx = Math.Abs(hs.pt.X - _hDownX);
                    int dy = Math.Abs(hs.pt.Y - _hDownY);
                    if (dx > DragThreshold || dy > DragThreshold)
                        _hDragDetected = true;
                }
                break;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // ═════════════════════════════════════════════════════════════════════
    // TIMER UI — logique applicative complète
    // Lit les volatile du hook, met à jour SafeState, overlay, etc.
    // ═════════════════════════════════════════════════════════════════════
    private void UiTick()
    {
        // Snapshot des volatiles (lecture unique pour cohérence)
        bool leftDown     = _hLeftDown;
        bool rightDown    = _hRightDown;
        bool dragDetected = _hDragDetected;
        bool needDeauth   = _hNeedDeauthorize;
        bool cancelDrop   = _hCancelDropRequested;
        int gestureId     = _hGestureId;
        if (needDeauth) _hNeedDeauthorize = false;
        if (cancelDrop) _hCancelDropRequested = false;

        if (leftDown && gestureId != _timerGestureId)
        {
            _timerGestureId = gestureId;
            var origin = new POINT { X = _hDownX, Y = _hDownY };
            QualifyGesture(gestureId, origin);
        }

        if (!leftDown)
            _hQualifiedDrag = false;

        UpdateDraggingState(leftDown && dragDetected && _hQualifiedDrag);

        if (cancelDrop)
            CancelActiveDrag();

        // ── 2. Mise à jour de l'autorisation ─────────────────────────
        bool authorized;

        if (_state.Key == AuthorizationKey.RightClick)
        {
            authorized = rightDown;
        }
        else
        {
            authorized = _state.Key switch
            {
                AuthorizationKey.Space   => (GetAsyncKeyState(VK_SPACE)   & 0x8000) != 0,
                AuthorizationKey.Shift   => (GetAsyncKeyState(VK_SHIFT)   & 0x8000) != 0,
                AuthorizationKey.Control => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0,
                AuthorizationKey.Alt     => (GetAsyncKeyState(VK_MENU)    & 0x8000) != 0,
                _ => false
            };
        }

        if (authorized != _timerLastAuthorized || needDeauth)
        {
            _timerLastAuthorized = authorized;
            _state.SetRightClickPressed(authorized);
        }

        SyncHookAuthorizationState();
    }

    private void UpdateDraggingState(bool isDragging)
    {
        if (isDragging != _timerLastDragging)
        {
            _timerLastDragging = isDragging;
            _state.SetDragging(isDragging);
        }
    }

    private void SyncHookAuthorizationState()
    {
        _hAuthorizationKey = (int)_state.Key;
        _hToggleMode = _state.Mode == RightClickMode.Toggle;
        _hAuthorizationValidated = _state.IsRightClickValidated;
    }

    private static bool IsAuthorizedAtRelease()
    {
        if (_hToggleMode)
            return _hAuthorizationValidated;

        return (AuthorizationKey)_hAuthorizationKey switch
        {
            AuthorizationKey.RightClick => _hRightDown,
            AuthorizationKey.Space => (GetAsyncKeyState(VK_SPACE) & 0x8000) != 0,
            AuthorizationKey.Shift => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0,
            AuthorizationKey.Control => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0,
            AuthorizationKey.Alt => (GetAsyncKeyState(VK_MENU) & 0x8000) != 0,
            _ => _hAuthorizationValidated
        };
    }

    private static void CancelActiveDrag()
    {
        keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
        keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    private void QualifyGesture(int gestureId, POINT origin)
    {
        bool isDraggable = false;
        try
        {
            isDraggable = IsDraggableItemAt(origin);
        }
        catch
        {
        }

        if (gestureId != _hGestureId || !_hLeftDown)
            return;

        _hQualifiedDrag = isDraggable;
        UpdateDraggingState(_hDragDetected && isDraggable);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Qualification UI Automation indépendante du gestionnaire de fichiers.
    // ─────────────────────────────────────────────────────────────────────
    private static bool IsDraggableItemAt(POINT pt)
    {
        try
        {
            AutomationElement? current = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y));
            for (int depth = 0; current != null && depth < 6; depth++)
            {
                ControlType type = current.Current.ControlType;
                if (type == ControlType.ListItem || type == ControlType.TreeItem || type == ControlType.DataItem)
                    return true;

                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
        }
        catch
        {
            // Une zone non accessible est traitée comme une sélection, jamais comme un fichier.
        }

        return false;
    }

    // ── Constantes & P/Invoke ─────────────────────────────────────────────

    private const int WH_MOUSE_LL    = 14;
    private const int WM_MOUSEMOVE   = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP   = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP   = 0x0205;
    private const int WM_NCRBUTTONDOWN = 0x02A4;
    private const int WM_NCRBUTTONUP   = 0x02A5;
    private const uint LLMHF_INJECTED = 0x00000001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    private const byte VK_ESCAPE = 0x1B;
    private const int VK_SPACE   = 0x20;
    private const int VK_SHIFT   = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU    = 0x12;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr GetModuleHandle([MarshalAs(UnmanagedType.LPWStr)] string? name);
}
