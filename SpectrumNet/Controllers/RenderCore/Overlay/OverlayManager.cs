#nullable enable

namespace SpectrumNet.Controllers.RenderCore.Overlay;

public sealed class OverlayManager(
    IMainController mainController,
    ITransparencyManager transparencyManager)
    : AsyncDisposableBase, IOverlayManager
{
    private const string LogPrefix = nameof(OverlayManager);

    private readonly IMainController _mainController = mainController ??
            throw new ArgumentNullException(nameof(mainController));

    private readonly ITransparencyManager _transparencyManager = transparencyManager ??
            throw new ArgumentNullException(nameof(transparencyManager));

    private readonly ISmartLogger _logger = Instance;

    private OverlayWindow? _overlayWindow;

    private OverlayConfiguration _configuration = new(
            IsTopmost: Settings.Instance.IsOverlayTopmost,
            EnableHardwareAcceleration: true
        );

    private bool _isActive;

    public event EventHandler<bool>? StateChanged;

    public bool IsActive => _isActive;

    public bool IsTopmost
    {
        get => _configuration.IsTopmost;
        set => _logger.Safe(() => HandleSetIsTopmost(value), LogPrefix, "Error setting overlay topmost");
    }

    private void HandleSetIsTopmost(bool value)
    {
        if (_configuration.IsTopmost == value) return;

        _configuration = _configuration with { IsTopmost = value };
        Settings.Instance.IsOverlayTopmost = value;

        if (_overlayWindow is { IsInitialized: true })
            _overlayWindow.Topmost = value;

        _mainController.OnPropertyChanged(nameof(IMainController.IsOverlayTopmost));
        StateChanged?.Invoke(this, _isActive);

        _logger.Log(LogLevel.Information,
            LogPrefix,
            $"Overlay topmost changed to: {value}");
    }

    public async Task OpenAsync() =>
        await _logger.SafeAsync(async () => await HandleOpenAsync(), LogPrefix, "Error opening overlay");

    private async Task HandleOpenAsync()
    {
        if (_isActive && _overlayWindow is { IsInitialized: true })
        {
            ShowExistingOverlay();
            return;
        }

        await Task.Run(() =>
        {
            InitializeOverlayWindow();
        });
    }

    public async Task CloseAsync() =>
        await _logger.SafeAsync(async () => await HandleCloseAsync(), LogPrefix, "Error closing overlay");

    private async Task HandleCloseAsync()
    {
        if (!_isActive) return;

        await _mainController.Dispatcher.InvokeAsync(() =>
        {
            _overlayWindow?.Close();
        }).Task;
    }

    public async Task ToggleAsync() =>
        await _logger.SafeAsync(async () => await HandleToggleAsync(), LogPrefix, "Error toggling overlay");

    private async Task HandleToggleAsync()
    {
        if (_isActive)
            await CloseAsync();
        else
            await OpenAsync();
    }

    public void SetTransparency(float level) =>
        _logger.Safe(() => HandleSetTransparency(level), LogPrefix, "Error setting overlay transparency");

    private void HandleSetTransparency(float level)
    {
        if (_overlayWindow is { IsInitialized: true })
        {
            _mainController.Dispatcher.Invoke(() => _overlayWindow.Opacity = level);
        }
    }

    public void UpdateRenderDimensions(int width, int height) =>
        _logger.Safe(() => HandleUpdateRenderDimensions(width, height),
            LogPrefix, "Error updating render dimensions");

    private void HandleUpdateRenderDimensions(int width, int height)
    {
        if (_overlayWindow is { IsInitialized: true })
        {
            _mainController.Renderer?.UpdateRenderDimensions(width, height);
        }
    }

    public void ForceRedraw() =>
        _logger.Safe(() => _overlayWindow?.ForceRedraw(),
            LogPrefix, "Error forcing overlay redraw");

    public void Configure(OverlayConfiguration configuration) =>
        _logger.Safe(() => HandleConfigure(configuration), LogPrefix, "Error configuring overlay");

    private void HandleConfigure(OverlayConfiguration configuration)
    {
        _configuration = configuration;

        if (_overlayWindow is { IsInitialized: true })
        {
            _mainController.Dispatcher.Invoke(() =>
            {
                _overlayWindow.Topmost = configuration.IsTopmost;
            });
        }
    }

    private void InitializeOverlayWindow() =>
        _logger.Safe(() => HandleInitializeOverlayWindow(), LogPrefix, "Error initializing overlay window");

    private void HandleInitializeOverlayWindow()
    {
        _mainController.Dispatcher.Invoke(() =>
        {
            CreateOverlayWindow();
            RegisterOverlayEvents();
            ShowOverlayWindow();
            _transparencyManager.EnableGlobalMouseTracking();
            ActivateOverlay();
            UpdateOverlayDimensions();
        });
    }

    private void CreateOverlayWindow() =>
        _logger.Safe(() =>
        {
            _overlayWindow = new OverlayWindow(_mainController, _configuration)
                ?? throw new InvalidOperationException("Failed to create overlay window");
        }, LogPrefix, "Error creating overlay window");

    private void RegisterOverlayEvents()
    {
        if (_overlayWindow is null) return;
        _overlayWindow.Closed += (_, _) => OnOverlayClosed();
    }

    private void ShowExistingOverlay() =>
        _logger.Safe(() => HandleShowExistingOverlay(), LogPrefix, "Error showing existing overlay");

    private void HandleShowExistingOverlay()
    {
        if (_overlayWindow is null) return;

        _mainController.Dispatcher.Invoke(() =>
        {
            _overlayWindow.Show();
            _overlayWindow.Topmost = IsTopmost;
        });
    }

    private void ShowOverlayWindow() => _overlayWindow?.Show();

    private void ActivateOverlay() =>
        _logger.Safe(() => HandleActivateOverlay(), LogPrefix, "Error activating overlay");

    private void HandleActivateOverlay()
    {
        _isActive = true;
        _mainController.OnPropertyChanged(nameof(IMainController.IsOverlayActive));
        _mainController.SpectrumCanvas.InvalidateVisual();
        StateChanged?.Invoke(this, true);
    }

    private void UpdateOverlayDimensions() =>
        _mainController.Renderer?.UpdateRenderDimensions(
            (int)SystemParameters.PrimaryScreenWidth,
            (int)SystemParameters.PrimaryScreenHeight);

    private void OnOverlayClosed() =>
        _logger.Safe(() => HandleOnOverlayClosed(), LogPrefix, "Error handling overlay closed");

    private void HandleOnOverlayClosed()
    {
        _transparencyManager.DisableGlobalMouseTracking();
        DisposeOverlayWindow();
        ClearOverlayState();
        ActivateOwnerWindow();
    }

    private void DisposeOverlayWindow() =>
        _logger.Safe(() =>
        {
            if (_overlayWindow is IDisposable disposable)
                disposable.Dispose();
        }, LogPrefix, "Error disposing overlay window");

    private void ClearOverlayState() =>
        _logger.Safe(() =>
        {
            _overlayWindow = null;
            _isActive = false;
            _mainController.OnPropertyChanged(nameof(IMainController.IsOverlayActive));
            StateChanged?.Invoke(this, false);
        }, LogPrefix, "Error clearing overlay state");

    private static void ActivateOwnerWindow()
    {
        var owner = Application.Current?.MainWindow;
        if (owner is { IsVisible: true })
        {
            owner.Activate();
            owner.Focus();
        }
    }

    protected override void DisposeManaged() =>
        _logger.Safe(() => HandleDisposeManaged(), LogPrefix, "Error during managed disposal");

    private void HandleDisposeManaged()
    {
        if (_isActive)
        {
            _mainController.Dispatcher.Invoke(() => _overlayWindow?.Close());
        }

        if (_overlayWindow is IDisposable disposable)
            disposable.Dispose();

        _overlayWindow = null;
    }

    protected override async ValueTask DisposeAsyncManagedResources() =>
        await _logger.SafeAsync(async () => await CloseAsync(),
            LogPrefix, "Error during async managed disposal");
}