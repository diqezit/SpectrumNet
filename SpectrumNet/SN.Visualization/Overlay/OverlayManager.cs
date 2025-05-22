#nullable enable

namespace SpectrumNet.SN.Visualization.Overlay;

public sealed class OverlayManager : AsyncDisposableBase, IOverlayManager
{
    private const string LogPrefix = nameof(OverlayManager);

    private readonly IMainController _mainController;
    private readonly ITransparencyManager _transparencyManager;
    private readonly ISmartLogger _logger = Instance;
    private readonly ISettings _settings = Settings.Settings.Instance;
    private OverlayWindow? _overlayWindow;
    private OverlayConfiguration _configuration;
    private bool _isActive;

    public event EventHandler<bool>? StateChanged;

    public bool IsActive => _isActive;

    public bool IsTopmost
    {
        get => _configuration.IsTopmost;
        set => _logger.Safe(() => HandleSetIsTopmost(value), LogPrefix, "Error setting overlay topmost");
    }

    public OverlayManager(
        IMainController mainController,
        ITransparencyManager transparencyManager)
    {
        _mainController = mainController ??
            throw new ArgumentNullException(nameof(mainController));

        _transparencyManager = transparencyManager ??
            throw new ArgumentNullException(nameof(transparencyManager));

        _configuration = CreateConfiguration();
    }

    public async Task OpenAsync() =>
        await _logger.SafeAsync(async () => await HandleOpenAsync(), LogPrefix, "Error opening overlay");

    public async Task CloseAsync() =>
        await _logger.SafeAsync(async () => await HandleCloseAsync(), LogPrefix, "Error closing overlay");

    public async Task ToggleAsync() =>
        await _logger.SafeAsync(async () => await HandleToggleAsync(), LogPrefix, "Error toggling overlay");

    public void SetTransparency(float level) =>
        _logger.Safe(() => HandleSetTransparency(level), LogPrefix, "Error setting overlay transparency");

    public void UpdateRenderDimensions(int width, int height) =>
        _logger.Safe(() => HandleUpdateRenderDimensions(width, height), LogPrefix, "Error updating render dimensions");

    public void ForceRedraw() =>
        _logger.Safe(() => _overlayWindow?.ForceRedraw(), LogPrefix, "Error forcing overlay redraw");

    public void Configure(OverlayConfiguration configuration) =>
        _logger.Safe(() => HandleConfigure(configuration), LogPrefix, "Error configuring overlay");

    private OverlayConfiguration CreateConfiguration() =>
        new(IsTopmost: _settings.IsOverlayTopmost, EnableHardwareAcceleration: true);

    private void HandleSetIsTopmost(bool value)
    {
        if (_configuration.IsTopmost == value)
            return;

        _configuration = _configuration with { IsTopmost = value };
        _settings.IsOverlayTopmost = value;

        if (_overlayWindow is { IsInitialized: true })
            _overlayWindow.Topmost = value;

        _mainController.OnPropertyChanged(nameof(IMainController.IsOverlayTopmost));
        StateChanged?.Invoke(this, _isActive);
    }

    private async Task HandleOpenAsync()
    {
        if (_isActive && _overlayWindow is { IsInitialized: true })
        {
            ShowExistingOverlay();
            return;
        }

        await Task.Run(() => CreateAndShowOverlay());
    }

    private async Task HandleCloseAsync()
    {
        if (!_isActive)
            return;

        await _mainController.Dispatcher.InvokeAsync(() => _overlayWindow?.Close()).Task;
    }

    private async Task HandleToggleAsync()
    {
        if (_isActive)
            await CloseAsync();
        else
            await OpenAsync();
    }

    private void HandleSetTransparency(float level)
    {
        if (_overlayWindow is { IsInitialized: true })
            _mainController.Dispatcher.Invoke(() => _overlayWindow.Opacity = level);
    }

    private void HandleUpdateRenderDimensions(int width, int height)
    {
        if (_overlayWindow is { IsInitialized: true })
            _mainController.Renderer?.UpdateRenderDimensions(width, height);
    }

    private void HandleConfigure(OverlayConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        if (_overlayWindow is { IsInitialized: true })
        {
            _mainController.Dispatcher.Invoke(() => _overlayWindow.Topmost = configuration.IsTopmost);
        }
    }

    private void ShowExistingOverlay() =>
        _logger.Safe(() => HandleShowExistingOverlay(), LogPrefix, "Error showing existing overlay");

    private void HandleShowExistingOverlay()
    {
        if (_overlayWindow == null)
            return;

        _mainController.Dispatcher.Invoke(() =>
        {
            _overlayWindow.Show();
            _overlayWindow.Topmost = IsTopmost;
        });
    }

    private void CreateAndShowOverlay() =>
        _logger.Safe(() => HandleCreateAndShowOverlay(), LogPrefix, "Error creating and showing overlay");

    private void HandleCreateAndShowOverlay()
    {
        _mainController.Dispatcher.Invoke(() =>
        {
            CreateOverlayWindow();
            RegisterOverlayEvents();
            ShowOverlay();
            EnableTransparency();
            ActivateOverlay();
            UpdateOverlayDimensions();
        });
    }

    private void CreateOverlayWindow() =>
        _overlayWindow = new OverlayWindow(_mainController, _configuration);

    private void RegisterOverlayEvents()
    {
        if (_overlayWindow != null)
            _overlayWindow.Closed += OnOverlayClosed;
    }

    private void ShowOverlay() =>
        _overlayWindow?.Show();

    private void EnableTransparency() =>
        _transparencyManager.EnableGlobalMouseTracking();

    private void ActivateOverlay()
    {
        _isActive = true;
        _mainController.OnPropertyChanged(nameof(IMainController.IsOverlayActive));
        _mainController.SpectrumCanvas.InvalidateVisual();
        StateChanged?.Invoke(this, true);
    }

    private void UpdateOverlayDimensions()
    {
        _mainController.Renderer?.UpdateRenderDimensions(
            (int)SystemParameters.PrimaryScreenWidth,
            (int)SystemParameters.PrimaryScreenHeight);
    }

    private void OnOverlayClosed(object? sender, EventArgs e) =>
        _logger.Safe(() => HandleOnOverlayClosed(), LogPrefix, "Error handling overlay closed");

    private void HandleOnOverlayClosed()
    {
        DisableTransparency();
        DisposeOverlayWindow();
        DeactivateOverlay();
        ActivateOwnerWindow();
    }

    private void DisableTransparency() =>
        _transparencyManager.DisableGlobalMouseTracking();

    private void DisposeOverlayWindow()
    {
        if (_overlayWindow is IDisposable disposable)
            disposable.Dispose();
        _overlayWindow = null;
    }

    private void DeactivateOverlay()
    {
        _isActive = false;
        _mainController.OnPropertyChanged(nameof(IMainController.IsOverlayActive));
        StateChanged?.Invoke(this, false);
    }

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
            _mainController.Dispatcher.Invoke(() => _overlayWindow?.Close());

        DisposeOverlayWindow();
    }

    protected override async ValueTask DisposeAsyncManagedResources() =>
        await _logger.SafeAsync(async () => await CloseAsync(), LogPrefix, "Error during async managed disposal");
}