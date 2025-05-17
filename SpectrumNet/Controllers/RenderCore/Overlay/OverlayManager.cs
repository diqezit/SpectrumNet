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
        set
        {
            if (_configuration.IsTopmost == value) return;

            _configuration = _configuration with { IsTopmost = value };
            Settings.Instance.IsOverlayTopmost = value;

            if (_overlayWindow is { IsInitialized: true })
                _overlayWindow.Topmost = value;
        }
    }

    public async Task OpenAsync()
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

    public async Task CloseAsync()
    {
        if (!_isActive) return;

        await _mainController.Dispatcher.InvokeAsync(() =>
        {
            _overlayWindow?.Close();
        }).Task;
    }

    public async Task ToggleAsync()
    {
        if (_isActive)
            await CloseAsync();
        else
            await OpenAsync();
    }

    public void SetTransparency(float level)
    {
        if (_overlayWindow is { IsInitialized: true })
        {
            _mainController.Dispatcher.Invoke(() => _overlayWindow.Opacity = level);
        }
    }

    public void UpdateRenderDimensions(int width, int height)
    {
        if (_overlayWindow is { IsInitialized: true })
        {
            _mainController.Renderer?.UpdateRenderDimensions(width, height);
        }
    }

    public void ForceRedraw()
    {
        _overlayWindow?.ForceRedraw();
    }

    public void Configure(OverlayConfiguration configuration)
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

    private void InitializeOverlayWindow()
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

    private void CreateOverlayWindow()
    {
        _overlayWindow = new OverlayWindow(_mainController, _configuration)
            ?? throw new InvalidOperationException("Failed to create overlay window");
    }

    private void RegisterOverlayEvents()
    {
        if (_overlayWindow is null) return;
        _overlayWindow.Closed += (_, _) => OnOverlayClosed();
    }

    private void ShowExistingOverlay()
    {
        if (_overlayWindow is null) return;

        _mainController.Dispatcher.Invoke(() =>
        {
            _overlayWindow.Show();
            _overlayWindow.Topmost = IsTopmost;
        });
    }

    private void ShowOverlayWindow() => _overlayWindow?.Show();

    private void ActivateOverlay()
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

    private void OnOverlayClosed()
    {
        Safe(() =>
        {
            _transparencyManager.DisableGlobalMouseTracking();
            DisposeOverlayWindow();
            ClearOverlayState();
            ActivateOwnerWindow();
        },
        new ErrorHandlingOptions
        {
            Source = LogPrefix,
            ErrorMessage = "Error handling overlay closed"
        });
    }

    private void DisposeOverlayWindow()
    {
        if (_overlayWindow is IDisposable disposable)
            disposable.Dispose();
    }

    private void ClearOverlayState()
    {
        _overlayWindow = null;
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

    protected override void DisposeManaged()
    {
        if (_isActive)
        {
            _mainController.Dispatcher.Invoke(() => _overlayWindow?.Close());
        }

        if (_overlayWindow is IDisposable disposable)
            disposable.Dispose();

        _overlayWindow = null;
    }

    protected override async ValueTask DisposeAsyncManagedResources()
    {
        await CloseAsync();
        return;
    }
}