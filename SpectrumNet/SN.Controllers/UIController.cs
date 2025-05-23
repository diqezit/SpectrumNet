// SN.Controllers/UIController.cs
#nullable enable

using SpectrumNet.SN.Shared.Utils;

namespace SpectrumNet.SN.Controllers;

public class UIController : AsyncDisposableBase, IUIController
{
    private const string LogPrefix = nameof(UIController);
    private readonly ISmartLogger _logger = Instance;

    private readonly IMainController _mainController;
    private readonly ITransparencyManager _transparencyManager;
    private readonly IOverlayManager _overlayManager;
    private readonly IThemes _themeManager = ThemeManager.Instance;

    private ControlPanelWindow? _controlPanelWindow;
    private bool _isPopupOpen;

    public UIController(
        IMainController mainController,
        ITransparencyManager transparencyManager)
    {
        _mainController = mainController ??
            throw new ArgumentNullException(nameof(mainController));

        _transparencyManager = transparencyManager ??
            throw new ArgumentNullException(nameof(transparencyManager));

        _overlayManager = new OverlayManager(mainController, transparencyManager);

        _overlayManager.StateChanged += OnOverlayStateChanged;
    }

    public bool IsOverlayActive
    {
        get => _overlayManager.IsActive;
        set
        {
            if (value != _overlayManager.IsActive)
            {
                if (value)
                    OpenOverlay();
                else
                    CloseOverlay();
            }
        }
    }

    public bool IsOverlayTopmost
    {
        get => _overlayManager.IsTopmost;
        set
        {
            if (_overlayManager.IsTopmost == value) return;
            _overlayManager.IsTopmost = value;
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
        _logger.Safe(() => _themeManager.ToggleTheme(),
            LogPrefix, "Error toggling theme");

    public void OpenControlPanel() =>
        _logger.Safe(() =>
        {
            if (TryActivateExistingControlPanel()) return;

            CreateAndShowControlPanel();
            _mainController.OnPropertyChanged(nameof(IsControlPanelOpen));
        },
        LogPrefix, "Error opening control panel");

    public void CloseControlPanel() =>
        _logger.Safe(() =>
        {
            if (_controlPanelWindow is null) return;

            UnregisterControlPanelEvents();
            CloseControlPanelWindow();
            NotifyControlPanelClosed();

            Task.Run(() => ActivateOwnerWindow());
        },
        LogPrefix, "Error closing control panel");

    public void ToggleControlPanel()
    {
        if (IsControlPanelOpen)
            CloseControlPanel();
        else
            OpenControlPanel();
    }

    public void OpenOverlay() =>
        _logger.Safe(() => _ = _overlayManager.OpenAsync(),
            LogPrefix, "Error opening overlay");

    public void CloseOverlay() =>
        _logger.Safe(() => _ = _overlayManager.CloseAsync(),
            LogPrefix, "Error closing overlay");

    public void OnPropertyChanged(params string[] propertyNames) =>
        _logger.Safe(() =>
        {
            foreach (var name in propertyNames)
                _mainController.OnPropertyChanged(name);
        },
        LogPrefix, "Error propagating property changes");

    private void OnOverlayStateChanged(object? sender, bool isActive)
    {
        _mainController.OnPropertyChanged(nameof(IsOverlayActive));
    }

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

    private void CloseControlPanelWindow()
    {
        if (_controlPanelWindow is null) return;

        if (_controlPanelWindow.Dispatcher.CheckAccess())
        {
            _controlPanelWindow.Close();
        }
        else
        {
            _controlPanelWindow.Dispatcher.Invoke(() =>
            _controlPanelWindow.Close());
        }
    }

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
        var app = Application.Current;
        if (app?.Dispatcher == null) return;

        if (app.Dispatcher.CheckAccess())
        {
            var owner = app.MainWindow;
            if (owner?.IsVisible == true)
            {
                owner.Activate();
                owner.Focus();
            }
        }
        else
        {
            app.Dispatcher.BeginInvoke(() =>
            {
                var owner = app.MainWindow;
                if (owner?.IsVisible == true)
                {
                    owner.Activate();
                    owner.Focus();
                }
            });
        }
    }

    private static Window? GetOwnerWindow()
    {
        if (Application.Current?.Dispatcher.CheckAccess() == false)
            return null;

        return Application.Current?.MainWindow;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        _mainController.OnPropertyChanged(propertyName ?? string.Empty);
        return true;
    }

    protected override void DisposeManaged() =>
        _logger.Safe(() =>
        {
            _overlayManager.StateChanged -= OnOverlayStateChanged;

            if (_overlayManager is IDisposable disposableOverlay)
                disposableOverlay.Dispose();

            if (IsControlPanelOpen)
                _controlPanelWindow?.Close();

            if (_controlPanelWindow is IDisposable disposableControlPanel)
                disposableControlPanel.Dispose();

            _controlPanelWindow = null;
        },
        LogPrefix, "Error during managed disposal");

    protected override async ValueTask DisposeAsyncManagedResources() =>
        await _logger.SafeAsync(async () =>
        {
            _overlayManager.StateChanged -= OnOverlayStateChanged;
            await _overlayManager.DisposeAsync();
            CloseControlPanel();
        },
        LogPrefix, "Error during async managed disposal");
}