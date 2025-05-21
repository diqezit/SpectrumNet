#nullable enable

namespace SpectrumNet.Controllers.AudioCore;

public class AudioController : AsyncDisposableBase, IAudioController
{
    private const string LogPrefix = nameof(AudioController);
    private const int OPERATION_COOLDOWN_MS = 1000;
    private const float MIN_DB_DIFFERENCE = 1.0f;

    private readonly IMainController _mainController;
    private readonly IGainParametersProvider _gainParameters;
    private readonly ICaptureService _captureService;
    private readonly ISettings _settings;
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

        _settings = SettingsProvider.Instance.Settings;
        _gainParameters = CreateGainParameters(syncContext);
        _captureService = CreateCaptureService(mainController);
        _mainController.PropertyChanged += OnMainControllerPropertyChanged;
    }

    private static GainParameters CreateGainParameters(SynchronizationContext syncContext)
    {
        var gainProvider = SettingsProvider.Instance.GainParameters;
        return new GainParameters(
            syncContext,
            gainProvider.MinDbValue,
            gainProvider.MaxDbValue,
            gainProvider.AmplificationFactor
        );
    }

    private static ICaptureService CreateCaptureService(IMainController controller)
    {
        var deviceManager = new AudioDeviceManager();
        var rendererFactory = RendererFactory.Instance;
        return new CaptureService(controller, deviceManager, rendererFactory);
    }

    public IGainParametersProvider GainParameters => _gainParameters;

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
        SaveWindowTypeToSettings(value);
        NotifyWindowTypeChanged();
        UpdateAnalyzerSettings();
    }

    private void SaveWindowTypeToSettings(FftWindowType value) =>
        _settings.SelectedFftWindowType = value;

    private void NotifyWindowTypeChanged() =>
        _mainController.OnPropertyChanged(nameof(WindowType));

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

    private static (float minDb, float maxDb) ValidateDbLevels(float minDb, float maxDb)
    {
        if (minDb >= maxDb - MIN_DB_DIFFERENCE)
        {
            float midPoint = (minDb + maxDb) / 2f;
            return (midPoint - MIN_DB_DIFFERENCE, midPoint + MIN_DB_DIFFERENCE);
        }

        return (minDb, maxDb);
    }

    public float MinDbLevel
    {
        get => _gainParameters.MinDbValue;
        set => SetMinDbLevel(value);
    }

    private void SetMinDbLevel(float value)
    {
        if (_gainParameters is null)
        {
            LogGainParametersNotInitialized();
            return;
        }

        var (validMin, validMax) = ValidateDbLevels(value, _gainParameters.MaxDbValue);
        UpdateDbLevels(validMin, validMax);
    }

    public float MaxDbLevel
    {
        get => _gainParameters.MaxDbValue;
        set => SetMaxDbLevel(value);
    }

    private void SetMaxDbLevel(float value)
    {
        if (_gainParameters is null)
        {
            LogGainParametersNotInitialized();
            return;
        }

        var (validMin, validMax) = ValidateDbLevels(_gainParameters.MinDbValue, value);
        UpdateDbLevels(validMin, validMax);
    }

    private void UpdateDbLevels(float minDbValue, float maxDbValue)
    {
        UpdateGainParameterValue(_gainParameters.MinDbValue, minDbValue, v => 
        _gainParameters.MinDbValue = v, nameof(MinDbLevel));

        UpdateGainParameterValue(_gainParameters.MaxDbValue, maxDbValue, v => 
        _gainParameters.MaxDbValue = v, nameof(MaxDbLevel));

        SaveDbLevelsToSettings(minDbValue, maxDbValue);
    }

    private void SaveDbLevelsToSettings(float minDbValue, float maxDbValue)
    {
        _settings.UIMinDbLevel = minDbValue;
        _settings.UIMaxDbLevel = maxDbValue;
    }

    private void LogGainParametersNotInitialized() =>
        _logger.Log(LogLevel.Error, LogPrefix, "Gain parameters not initialized");

    public float AmplificationFactor
    {
        get => _gainParameters.AmplificationFactor;
        set => SetAmplificationFactor(value);
    }

    private void SetAmplificationFactor(float value)
    {
        if (_gainParameters is null)
        {
            LogGainParametersNotInitialized();
            return;
        }

        var validValue = ValidateAmplificationFactor(value);
        UpdateGainParameterValue(_gainParameters.AmplificationFactor, validValue,
            v => _gainParameters.AmplificationFactor = v, nameof(AmplificationFactor));
        SaveAmplificationFactorToSettings(validValue);
    }

    private float ValidateAmplificationFactor(float value)
    {
        if (value < 0)
        {
            LogInvalidAmplificationFactor(value);
            return 0.1f;
        }
        return value;
    }

    private void LogInvalidAmplificationFactor(float value) =>
        _logger.Log(LogLevel.Warning, LogPrefix,
            $"Amplification factor cannot be negative: {value}");

    private void SaveAmplificationFactorToSettings(float value) =>
        _settings.UIAmplificationFactor = value;

    public async Task StartCaptureAsync()
    {
        ThrowIfDisposed();

        if (!await CanStartCaptureNow())
            return;

        await ExecuteStartCapture();
    }

    private async Task<bool> CanStartCaptureNow()
    {
        await _operationLock.WaitAsync();

        try
        {
            if (IsRecording || IsOperationInCooldown())
            {
                LogStartCaptureIgnored();
                return false;
            }
            return true;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private void LogStartCaptureIgnored() =>
        _logger.Log(LogLevel.Debug, LogPrefix, "Start capture ignored");

    private async Task ExecuteStartCapture()
    {
        await _captureService.StartCaptureAsync();
        UpdateLastOperationTime();
        NotifyCaptureStateChanged();
    }

    private void UpdateLastOperationTime() =>
        _lastOperationTime = DateTime.Now;

    private void NotifyCaptureStateChanged() =>
        _mainController.OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));

    public async Task StopCaptureAsync()
    {
        ThrowIfDisposed();

        if (!await CanStopCaptureNow())
            return;

        await ExecuteStopCapture();
    }

    private async Task<bool> CanStopCaptureNow()
    {
        await _operationLock.WaitAsync();

        try
        {
            if (!IsRecording)
            {
                LogStopCaptureIgnored();
                return false;
            }
            return true;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private void LogStopCaptureIgnored() =>
        _logger.Log(LogLevel.Debug, LogPrefix, "Stop capture ignored: Not recording");

    private async Task ExecuteStopCapture()
    {
        await _captureService.StopCaptureAsync();
        UpdateLastOperationTime();
        await ApplyCooldown();
        NotifyCaptureStateChanged();
    }

    private static async Task ApplyCooldown() =>
        await Task.Delay(OPERATION_COOLDOWN_MS);

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

    private bool IsOperationInCooldown() =>
        (DateTime.Now - _lastOperationTime).TotalMilliseconds < OPERATION_COOLDOWN_MS;

    private void UpdateGainParameterValue(
        float oldValue,
        float newValue,
        Action<float> setter,
        string propertyName)
    {
        if (Math.Abs(oldValue - newValue) < float.Epsilon)
            return;

        UpdateGainParameter(newValue, setter, propertyName);
    }

    private void UpdateGainParameter(float newValue, Action<float> setter, string propertyName) =>
        _logger.Safe(() => ExecuteUpdateGainParameter(newValue, setter, propertyName),
            LogPrefix, "Error updating gain parameter");

    private void ExecuteUpdateGainParameter(float newValue, Action<float> setter, string propertyName)
    {
        if (setter is null)
        {
            LogNullSetterDelegate();
            return;
        }

        setter(newValue);
        _mainController.OnPropertyChanged(propertyName);
    }

    private void LogNullSetterDelegate() =>
        _logger.Log(LogLevel.Error, LogPrefix, "Null setter delegate provided");

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
        LogAudioControllerDisposing();
        DisposeCaptureServiceInternalSync();
        DisposeOperationLock();
        LogAudioControllerDisposed();
    }

    private void LogAudioControllerDisposing() =>
        _logger.Log(LogLevel.Information, LogPrefix, "AudioController disposing");

    private void LogAudioControllerDisposed() =>
        _logger.Log(LogLevel.Information, LogPrefix, "AudioController disposed successfully");

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
        LogAudioControllerAsyncDisposing();
        await DisposeCaptureServiceInternalAsync();
        DisposeOperationLock();
        LogAudioControllerAsyncDisposed();
    }

    private void LogAudioControllerAsyncDisposing() =>
        _logger.Log(LogLevel.Information, LogPrefix, "AudioController async disposing");

    private void LogAudioControllerAsyncDisposed() =>
        _logger.Log(LogLevel.Information, LogPrefix, "AudioController async disposed successfully");

    private async Task DisposeCaptureServiceInternalAsync() =>
        await _logger.SafeAsync(async () => {
            await _captureService.StopCaptureAsync();
            await _captureService.DisposeAsync();
        }, LogPrefix, "Error during asynchronous capture service disposal");
}