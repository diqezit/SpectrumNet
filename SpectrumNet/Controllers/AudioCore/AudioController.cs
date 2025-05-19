// Controllers/AudioCore/AudioController.cs
#nullable enable

namespace SpectrumNet.Controllers.AudioCore;

public class AudioController : AsyncDisposableBase, IAudioController
{
    private const string LogPrefix = nameof(AudioController);

    private const int OPERATION_COOLDOWN_MS = 1000;

    private readonly IMainController _mainController;
    private readonly GainParameters _gainParameters;
    private readonly ICaptureService _captureService;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly ISmartLogger _logger = Instance;

    private DateTime _lastOperationTime = DateTime.MinValue;
    private FftWindowType _windowType = FftWindowType.Hann;

    public AudioController(
        IMainController mainController,
        SynchronizationContext syncContext)
    {
        _mainController = mainController ??
                          throw new ArgumentNullException(nameof(mainController));

        _gainParameters = CreateGainParameters(syncContext);
        _captureService = CreateCaptureService(mainController);
        _mainController.PropertyChanged += OnMainControllerPropertyChanged;
    }

    private static GainParameters CreateGainParameters(SynchronizationContext syncContext) =>
        new GainParameters(
            syncContext,
            Settings.Instance.UIMinDbLevel,
            Settings.Instance.UIMaxDbLevel,
            Settings.Instance.UIAmplificationFactor
        ) ?? throw new InvalidOperationException("Failed to create gain parameters");

    private static ICaptureService CreateCaptureService(IMainController controller)
    {
        var deviceManager = new AudioDeviceManager();
        var rendererFactory = RendererFactory.Instance;
        return new CaptureService(controller, deviceManager, rendererFactory);
    }

    #region IAudioController Implementation

    public GainParameters GainParameters => _gainParameters;

    public bool IsRecording
    {
        get => _captureService.IsRecording;
        set
        {
            if (value == IsRecording) return;

            if (value)
                _logger.Safe(() => StartCaptureAsync().GetAwaiter().GetResult(),
                    LogPrefix, "Error starting capture");
            else
                _logger.Safe(() => StopCaptureAsync().GetAwaiter().GetResult(),
                    LogPrefix, "Error stopping capture");
        }
    }

    public bool CanStartCapture =>
        !IsRecording && !IsOperationInCooldown() && !_captureService.IsInitializing;

    public bool IsTransitioning
    {
        get => _mainController.IsTransitioning;
        set => _mainController.IsTransitioning = value;
    }

    public FftWindowType WindowType
    {
        get => _windowType;
        set => UpdateWindowType(value);
    }

    private void UpdateWindowType(FftWindowType value)
    {
        if (_windowType == value) return;

        _windowType = value;
        Settings.Instance.SelectedFftWindowType = value;
        _mainController.OnPropertyChanged(nameof(WindowType));
        UpdateAnalyzerSettings();
    }

    private void OnMainControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WindowType) || e.PropertyName == nameof(_mainController.ScaleType))
        {
            UpdateAnalyzerSettings();
        }
    }

    private void UpdateAnalyzerSettings() =>
        _logger.Safe(() => HandleUpdateAnalyzerSettings(), LogPrefix, "Error updating analyzer settings");

    private void HandleUpdateAnalyzerSettings()
    {
        var analyzer = GetCurrentAnalyzer();
        if (analyzer == null) return;

        analyzer.UpdateSettings(_windowType, _mainController.ScaleType);
        _mainController.RequestRender();
    }

    public float MinDbLevel
    {
        get => _gainParameters.MinDbValue;
        set => UpdateDbLevel(
            value,
            v => v < _gainParameters.MaxDbValue,
            v => _gainParameters.MinDbValue = v,
            _gainParameters.MaxDbValue - 1,
            v => Settings.Instance.UIMinDbLevel = v,
            $"Min dB level ({value}) must be less than max ({_gainParameters.MaxDbValue})",
            nameof(MinDbLevel));
    }

    public float MaxDbLevel
    {
        get => _gainParameters.MaxDbValue;
        set => UpdateDbLevel(
            value,
            v => v > _gainParameters.MinDbValue,
            v => _gainParameters.MaxDbValue = v,
            _gainParameters.MinDbValue + 1,
            v => Settings.Instance.UIMaxDbLevel = v,
            $"Max dB level ({value}) must be greater than min ({_gainParameters.MinDbValue})",
            nameof(MaxDbLevel));
    }

    public float AmplificationFactor
    {
        get => _gainParameters.AmplificationFactor;
        set => UpdateDbLevel(
            value,
            v => v >= 0,
            v => _gainParameters.AmplificationFactor = v,
            0,
            v => Settings.Instance.UIAmplificationFactor = v,
            $"Amplification factor cannot be negative: {value}",
            nameof(AmplificationFactor));
    }

    public async Task StartCaptureAsync()
    {
        ThrowIfDisposed();

        await _operationLock.WaitAsync();

        try
        {
            if (IsRecording || IsOperationInCooldown())
            {
                _logger.Log(LogLevel.Debug,
                    LogPrefix,
                    "Start capture ignored");
                return;
            }
        }
        finally
        {
            _operationLock.Release();
        }

        await ExecuteStartCapture();
    }

    private async Task ExecuteStartCapture()
    {
        await _captureService.StartCaptureAsync();
        _lastOperationTime = DateTime.Now;
        NotifyCaptureStateChanged();
    }

    private void NotifyCaptureStateChanged() =>
        _mainController.OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));

    public async Task StopCaptureAsync()
    {
        ThrowIfDisposed();

        await _operationLock.WaitAsync();

        try
        {
            if (!IsRecording)
            {
                _logger.Log(LogLevel.Debug,
                    LogPrefix,
                    "Stop capture ignored: Not recording");
                return;
            }
        }
        finally
        {
            _operationLock.Release();
        }
        await ExecuteStopCapture();
    }

    private async Task ExecuteStopCapture()
    {
        await _captureService.StopCaptureAsync();
        _lastOperationTime = DateTime.Now;
        await Task.Delay(OPERATION_COOLDOWN_MS);
        NotifyCaptureStateChanged();
    }

    public async Task ToggleCaptureAsync()
    {
        ThrowIfDisposed();

        if (IsRecording)
            await StopCaptureAsync();
        else
            await StartCaptureAsync();
    }

    public SpectrumAnalyzer? GetCurrentAnalyzer()
    {
        ThrowIfDisposed();
        return _captureService.GetAnalyzer();
    }

    #endregion

    #region Helper Methods

    private bool IsOperationInCooldown() =>
        (DateTime.Now - _lastOperationTime).TotalMilliseconds < OPERATION_COOLDOWN_MS;

    private void UpdateGainParameter(float newValue, Action<float> setter, string propertyName) =>
        _logger.Safe(() => HandleUpdateGainParameter(newValue, setter, propertyName),
            LogPrefix, "Error updating gain parameter");

    private void HandleUpdateGainParameter(float newValue, Action<float> setter, string propertyName)
    {
        if (setter is null)
        {
            _logger.Log(LogLevel.Error, LogPrefix, "Null setter delegate provided");
            return;
        }

        setter(newValue);
        _mainController.OnPropertyChanged(propertyName);
    }

    private void UpdateDbLevel(
        float value,
        Func<float, bool> validator,
        Action<float> setter,
        float fallbackValue,
        Action<float> settingUpdater,
        string errorMessage,
        string propertyName)
    {
        if (_gainParameters is null)
        {
            _logger.Log(LogLevel.Error, LogPrefix, "Gain parameters not initialized");
            return;
        }

        if (!validator(value))
        {
            _logger.Log(LogLevel.Warning, LogPrefix, errorMessage);
            value = fallbackValue;
        }

        UpdateGainParameter(value, setter, propertyName);
        settingUpdater(value);
    }

    #endregion

    protected override void DisposeManaged() =>
        _logger.Safe(() => HandleDisposeManaged(), LogPrefix, "Error during managed disposal");

    private void HandleDisposeManaged()
    {
        _mainController.PropertyChanged -= OnMainControllerPropertyChanged;
        PerformDisposeSync();
    }

    protected override async ValueTask DisposeAsyncManagedResources() =>
        await _logger.SafeAsync(async () => await HandleDisposeAsyncManagedResources(),
            LogPrefix, "Error during async managed disposal");

    private async Task HandleDisposeAsyncManagedResources()
    {
        _mainController.PropertyChanged -= OnMainControllerPropertyChanged;
        await PerformDisposeAsync();
    }

    private void PerformDisposeSync()
    {
        _logger.Log(LogLevel.Information, LogPrefix, "AudioController disposing");
        DisposeCaptureServiceInternalSync();
        DisposeOperationLock();
        _logger.Log(LogLevel.Information, LogPrefix, "AudioController disposed successfully");
    }

    private void DisposeCaptureServiceInternalSync() =>
        _logger.Safe(() => {
            if (_captureService is IDisposable disposable)
            {
                _captureService.StopCaptureAsync().GetAwaiter().GetResult();
                disposable.Dispose();
            }
        }, LogPrefix, "Error during synchronous capture service disposal");

    private void DisposeOperationLock() =>
        _logger.Safe(() => _operationLock.Dispose(), LogPrefix, "Error disposing operation lock");

    private async Task PerformDisposeAsync()
    {
        _logger.Log(LogLevel.Information, LogPrefix, "AudioController async disposing");
        await DisposeCaptureServiceInternalAsync();
        DisposeOperationLock();
        _logger.Log(LogLevel.Information, LogPrefix, "AudioController async disposed successfully");
    }

    private async Task DisposeCaptureServiceInternalAsync() =>
        await _logger.SafeAsync(async () => {
            await _captureService.StopCaptureAsync();
            await _captureService.DisposeAsync();
        }, LogPrefix, "Error during asynchronous capture service disposal");
}