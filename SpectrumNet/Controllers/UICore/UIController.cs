#nullable enable

namespace SpectrumNet.Controllers.UICore;

public class UIController(
    IMainController mainController,
    ITransparencyManager transparencyManager) 
    : AsyncDisposableBase, IUIController
{
    private const string LogPrefix = nameof(UIController);

    private readonly IMainController _mainController = mainController ??
            throw new ArgumentNullException(nameof(mainController));

    private readonly ITransparencyManager _transparencyManager = transparencyManager ??
            throw new ArgumentNullException(nameof(transparencyManager));

    private readonly IThemes _themeManager = ThemeManager.Instance;

    private OverlayWindow? _overlayWindow;
    private ControlPanelWindow? _controlPanelWindow;

    private bool
        _isOverlayActive,
        _isPopupOpen,
        _isOverlayTopmost = true;

    public bool IsOverlayActive
    {
        get => _isOverlayActive;
        set
        {
            if (_isOverlayActive == value) return;
            _isOverlayActive = value;
            _mainController.OnPropertyChanged(nameof(IsOverlayActive));
        }
    }

    public bool IsOverlayTopmost
    {
        get => _isOverlayTopmost;
        set
        {
            if (_isOverlayTopmost == value) return;

            _isOverlayTopmost = value;
            Settings.Instance.IsOverlayTopmost = value;
            UpdateOverlayTopmostState();
            _mainController.OnPropertyChanged(nameof(IsOverlayTopmost));
        }
    }

    public bool IsPopupOpen
    {
        get => _isPopupOpen;
        set => SetField(ref _isPopupOpen, value);
    }

    public bool IsControlPanelOpen => _controlPanelWindow is { IsVisible: true };

    public void ToggleTheme() =>
        Safe(() => _themeManager.ToggleTheme(),
            new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error toggling theme" });

    public void OpenControlPanel() =>
        Safe(() =>
        {
            if (TryActivateExistingControlPanel()) return;

            CreateAndShowControlPanel();
            _mainController.OnPropertyChanged(nameof(IsControlPanelOpen));
        },
        new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error opening control panel" });

    public void CloseControlPanel() =>
        Safe(() =>
        {
            if (_controlPanelWindow is null) return;

            UnregisterControlPanelEvents();
            CloseControlPanelWindow();
            NotifyControlPanelClosed();
            ActivateOwnerWindow();
        },
        new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error closing control panel" });

    public void ToggleControlPanel()
    {
        if (IsControlPanelOpen)
            CloseControlPanel();
        else
            OpenControlPanel();
    }

    public void OpenOverlay() =>
        Safe(() =>
        {
            if (IsExistingOverlayValid())
            {
                ShowExistingOverlay();
                return;
            }

            InitializeOverlayWindow();
        },
        new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error opening overlay" });

    public void CloseOverlay() =>
        Safe(() => _overlayWindow?.Close(),
            new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error closing overlay" });

    public void OnPropertyChanged(params string[] propertyNames) =>
        Safe(() =>
        {
            foreach (var name in propertyNames)
                _mainController.OnPropertyChanged(name);
        },
        new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error propagating property changes" });

    private bool IsExistingOverlayValid() =>
        _overlayWindow is { IsInitialized: true };

    private void ShowExistingOverlay()
    {
        if (_overlayWindow is null) return;

        _overlayWindow.Show();
        _overlayWindow.Topmost = IsOverlayTopmost;
    }

    private void InitializeOverlayWindow()
    {
        var config = CreateOverlayConfiguration();
        CreateOverlayWindow(config);
        RegisterOverlayEvents();
        ShowOverlayWindow();
        _transparencyManager.EnableGlobalMouseTracking();
        ActivateOverlay();
        UpdateOverlayDimensions();
    }

    private OverlayConfiguration CreateOverlayConfiguration() =>
        new(
            RenderInterval: 16,
            IsTopmost: IsOverlayTopmost,
            ShowInTaskbar: false,
            EnableHardwareAcceleration: true
        );

    private void CreateOverlayWindow(OverlayConfiguration config)
    {
        _overlayWindow = new OverlayWindow(_mainController, config)
            ?? throw new InvalidOperationException("Failed to create overlay window");
    }

    private void RegisterOverlayEvents()
    {
        if (_overlayWindow is null) return;
        _overlayWindow.Closed += (_, _) => OnOverlayClosed();
    }

    private void ShowOverlayWindow() => _overlayWindow?.Show();

    private void ActivateOverlay()
    {
        IsOverlayActive = true;
        _mainController.SpectrumCanvas.InvalidateVisual();
    }

    private void UpdateOverlayDimensions() =>
        _mainController.Renderer?.UpdateRenderDimensions(
            (int)SystemParameters.PrimaryScreenWidth,
            (int)SystemParameters.PrimaryScreenHeight);

    private void OnOverlayClosed() =>
        Safe(() =>
        {
            _transparencyManager.DisableGlobalMouseTracking();
            DisposeOverlayWindow();
            ClearOverlayState();
            ActivateOwnerWindow();
        },
        new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error handling overlay closed" });

    private void DisposeOverlayWindow()
    {
        if (_overlayWindow is IDisposable disposable)
            disposable.Dispose();
    }

    private void ClearOverlayState()
    {
        _overlayWindow = null;
        IsOverlayActive = false;
    }

    private void UpdateOverlayTopmostState() =>
        Safe(() =>
        {
            if (_overlayWindow is { IsInitialized: true } overlay)
                overlay.Topmost = IsOverlayTopmost;
        },
        new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error updating overlay topmost state" });

    private bool TryActivateExistingControlPanel()
    {
        if (_controlPanelWindow is not { IsVisible: true })
            return false;

        ActivateControlPanel();
        return true;
    }

    private void ActivateControlPanel()
    {
        if (_controlPanelWindow is null) return;

        _controlPanelWindow.Activate();

        if (_controlPanelWindow.WindowState == WindowState.Minimized)
            _controlPanelWindow.WindowState = WindowState.Normal;
    }

    private void CreateAndShowControlPanel()
    {
        CreateControlPanelWindow();
        ConfigureControlPanelPosition();
        RegisterControlPanelEvents();
        ShowControlPanel();
    }

    private void CreateControlPanelWindow() =>
        _controlPanelWindow = new ControlPanelWindow(_mainController);

    private void ConfigureControlPanelPosition()
    {
        if (_controlPanelWindow is null) return;

        var owner = GetOwnerWindow();

        if (owner is { IsInitialized: true, IsVisible: true })
        {
            PositionRelativeToOwner(owner);
        }
        else
        {
            _controlPanelWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void PositionRelativeToOwner(Window owner)
    {
        if (_controlPanelWindow is null) return;

        _controlPanelWindow.Owner = owner;
        _controlPanelWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _controlPanelWindow.Left = owner.Left + (owner.ActualWidth - _controlPanelWindow.Width) / 2;
        _controlPanelWindow.Top = owner.Top + owner.ActualHeight - 250;
    }

    private void RegisterControlPanelEvents()
    {
        if (_controlPanelWindow is null) return;
        _controlPanelWindow.Closed += OnControlPanelClosed;
    }

    private void UnregisterControlPanelEvents()
    {
        if (_controlPanelWindow is null) return;
        _controlPanelWindow.Closed -= OnControlPanelClosed;
    }

    private void ShowControlPanel() => _controlPanelWindow?.Show();

    private void CloseControlPanelWindow() => _controlPanelWindow?.Close();

    private void NotifyControlPanelClosed()
    {
        _controlPanelWindow = null;
        _mainController.OnPropertyChanged(nameof(IsControlPanelOpen));
    }

    private void OnControlPanelClosed(object? sender, EventArgs e)
    {
        _controlPanelWindow = null;
        _mainController.OnPropertyChanged(nameof(IsControlPanelOpen));
        ActivateOwnerWindow();
    }

    private static void ActivateOwnerWindow()
    {
        var owner = GetOwnerWindow();
        if (owner is { IsVisible: true })
        {
            owner.Activate();
            owner.Focus();
        }
    }

    private static Window? GetOwnerWindow() =>
        Application.Current?.MainWindow;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        _mainController.OnPropertyChanged(propertyName ?? string.Empty);
        return true;
    }

    protected override void DisposeManaged()
    {
        try
        {
            if (IsOverlayActive)
                _overlayWindow?.Close();

            if (IsControlPanelOpen)
                _controlPanelWindow?.Close();
        }
        finally
        {
            if (_overlayWindow is IDisposable disposableOverlay)
                disposableOverlay.Dispose();

            if (_controlPanelWindow is IDisposable disposableControlPanel)
                disposableControlPanel.Dispose();

            _overlayWindow = null;
            _controlPanelWindow = null;
        }
    }

    protected override ValueTask DisposeAsyncManagedResources()
    {
        CloseOverlay();
        CloseControlPanel();
        return ValueTask.CompletedTask;
    }
}