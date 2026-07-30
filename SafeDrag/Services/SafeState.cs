namespace SafeExplorer.Tray.Services;

public enum RightClickMode
{
    Hold,   // Maintenir la touche / clic enfoncé
    Toggle  // Appui simple (déverrouille jusqu'au relâchement du drag)
}

public enum AuthorizationKey
{
    RightClick, // Clic Droit de la souris (Recommandé)
    Space,      // Barre Espace
    Shift,      // Touche Maj / Shift
    Control,    // Touche Ctrl
    Alt         // Touche Alt
}

public sealed class SafeState
{
    private readonly object _lock = new();

    private bool _isDragging;
    private bool _isRightClickPressed;
    private bool _isRightClickToggled;
    private RightClickMode _mode = RightClickMode.Hold;
    private AuthorizationKey _key = AuthorizationKey.RightClick;

    public event Action<bool>? DraggingStateChanged;
    public event Action? StateChanged;

    public bool IsDragging
    {
        get { lock (_lock) return _isDragging; }
    }

    public RightClickMode Mode
    {
        get { lock (_lock) return _mode; }
        set
        {
            lock (_lock)
            {
                if (_mode == value)
                    return;

                _mode = value;
                _isRightClickToggled = false;
            }
            StateChanged?.Invoke();
        }
    }

    public AuthorizationKey Key
    {
        get { lock (_lock) return _key; }
        set
        {
            lock (_lock)
            {
                _key = value;
            }
            StateChanged?.Invoke();
        }
    }

    public bool IsRightClickValidated
    {
        get
        {
            lock (_lock)
            {
                if (_mode == RightClickMode.Hold)
                {
                    return _isRightClickPressed;
                }
                else
                {
                    return _isRightClickToggled;
                }
            }
        }
    }

    public bool IsSpaceValidated => IsRightClickValidated;

    public void SetDragging(bool value)
    {
        bool changed = false;
        lock (_lock)
        {
            if (_isDragging != value)
            {
                _isDragging = value;
                changed = true;
            }
            // ⚠️ Ne pas réinitialiser _isRightClickPressed ici !
            // DragMonitor lit IsRightClickValidated AVANT d'appeler SetDragging(false),
            // puis appelle explicitement SetRightClickPressed(false) ensuite.
            if (!value)
            {
                if (changed && _mode == RightClickMode.Toggle)
                {
                    _isRightClickToggled = false;
                }
            }
        }

        if (changed)
        {
            DraggingStateChanged?.Invoke(value);
        }
    }

    public void SetRightClickPressed(bool value)
    {
        lock (_lock)
        {
            if (value && !_isRightClickPressed && _mode == RightClickMode.Toggle)
            {
                _isRightClickToggled = !_isRightClickToggled;
            }
            _isRightClickPressed = value;
        }
        StateChanged?.Invoke();
    }

    public void ToggleRightClick()
    {
        lock (_lock)
        {
            _isRightClickToggled = !_isRightClickToggled;
        }
        StateChanged?.Invoke();
    }

    public void SetSpacePressed(bool value) => SetRightClickPressed(value);
}
