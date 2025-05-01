#nullable enable

namespace SpectrumNet.Controllers.UICore;

public class UIController(IMainController mainController) 
    : AsyncDisposableBase, IUIController
{
    private const string LogPrefix = nameof(UIController);

    private readonly IMainController _mainController = mainController ??
        throw new ArgumentNullException(nameof(mainController));

    private OverlayWindow? _overlayWindow;
    private ControlPanelWindow? _controlPanelWindow;

    private bool
        _isOverlayActive,
        _isPopupOpen,
        _isOverlayTopmost = true;

    #region IUIController Implementation

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
        Safe(() => ThemeManager.Instance?.ToggleTheme(),
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

            _controlPanelWindow.Closed -= OnControlPanelClosed;
            _controlPanelWindow.Close();
            _controlPanelWindow = null;
            _mainController.OnPropertyChanged(nameof(IsControlPanelOpen));

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
            if (_overlayWindow is { IsInitialized: true } overlay)
            {
                overlay.Show();
                overlay.Topmost = IsOverlayTopmost;
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

    #endregion

    #region Helper Methods

    private void InitializeOverlayWindow()
    {
        var config = new OverlayConfiguration(
            RenderInterval: 16,
            IsTopmost: IsOverlayTopmost,
            ShowInTaskbar: false,
            EnableHardwareAcceleration: true
        );

        _overlayWindow = new OverlayWindow(_mainController, config)
            ?? throw new InvalidOperationException("Failed to create overlay window");

        _overlayWindow.Closed += (_, _) => OnOverlayClosed();
        _overlayWindow.Show();

        IsOverlayActive = true;
        _mainController.SpectrumCanvas.InvalidateVisual();

        _mainController.Renderer?.UpdateRenderDimensions(
            (int)SystemParameters.PrimaryScreenWidth,
            (int)SystemParameters.PrimaryScreenHeight);
    }

    private bool TryActivateExistingControlPanel()
    {
        if (_controlPanelWindow is not { IsVisible: true })
            return false;

        _controlPanelWindow.Activate();

        if (_controlPanelWindow.WindowState == WindowState.Minimized)
            _controlPanelWindow.WindowState = WindowState.Normal;

        return true;
    }

    private void CreateAndShowControlPanel()
    {
        _controlPanelWindow = new ControlPanelWindow(_mainController);
        var owner = GetOwnerWindow();

        if (owner is { IsInitialized: true, IsVisible: true })
        {
            _controlPanelWindow.Owner = owner;
            _controlPanelWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _controlPanelWindow.Left = owner.Left + (owner.ActualWidth - _controlPanelWindow.Width) / 2;
            _controlPanelWindow.Top = owner.Top + owner.ActualHeight - 250;
        }
        else
        {
            _controlPanelWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _controlPanelWindow.Closed += OnControlPanelClosed;
        _controlPanelWindow.Show();
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

    private void OnControlPanelClosed(object? sender, EventArgs e)
    {
        _controlPanelWindow = null;
        _mainController.OnPropertyChanged(nameof(IsControlPanelOpen));
        ActivateOwnerWindow();
    }

    private void OnOverlayClosed() =>
        Safe(() =>
        {
            if (_overlayWindow is IDisposable disposable)
                disposable.Dispose();

            _overlayWindow = null;
            IsOverlayActive = false;
            ActivateOwnerWindow();
        },
        new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error handling overlay closed" });

    private void UpdateOverlayTopmostState() =>
        Safe(() =>
        {
            if (_overlayWindow is { IsInitialized: true } overlay)
                overlay.Topmost = IsOverlayTopmost;
        },
        new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error updating overlay topmost state" });

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        _mainController.OnPropertyChanged(propertyName ?? string.Empty);
        return true;
    }

    #endregion

    protected override void DisposeManaged()
    {
        CloseOverlay();
        CloseControlPanel();
    }

    protected override ValueTask DisposeAsyncManagedResources()
    {
        CloseOverlay();
        CloseControlPanel();
        return ValueTask.CompletedTask;
    }
}