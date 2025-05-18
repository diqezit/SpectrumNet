// Controllers/AudioCore/AudioController.cs
#nullable enable

namespace SpectrumNet.Controllers.AudioCore;

public class AudioController : AsyncDisposableBase, IAudioController
{
    private const string LogPrefix = nameof(AudioController);

    private const int
        OPERATION_COOLDOWN_MS = 1000,
        OPERATION_TIMEOUT_MS = 5000;

    private readonly IMainController _mainController;
    private readonly GainParameters _gainParameters;
    private readonly ICaptureService _captureService;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

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
                Safe(() => StartCaptureAsync().GetAwaiter().GetResult(),
                    new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error starting capture" });
            else
                Safe(() => StopCaptureAsync().GetAwaiter().GetResult(),
                    new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error stopping capture" });
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

    private void UpdateAnalyzerSettings()
    {
        var analyzer = GetCurrentAnalyzer();
        if (analyzer == null) return;

        Safe(() =>
        {
            analyzer.UpdateSettings(_windowType, _mainController.ScaleType);
            _mainController.RequestRender();
        },
        new ErrorHandlingOptions
        {
            Source = LogPrefix,
            ErrorMessage = "Error updating analyzer settings"
        });
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

        if (!await TryAcquireOperationLock())
            return;

        try
        {
            if (ShouldSkipStartCapture())
                return;

            await ExecuteStartCapture();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private bool ShouldSkipStartCapture()
    {
        if (IsRecording || IsOperationInCooldown())
        {
            Log(LogLevel.Debug, LogPrefix,
                $"Start capture ignored: Already recording: {IsRecording}, In cooldown: {IsOperationInCooldown()}");
            return true;
        }
        return false;
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

        if (!await TryAcquireOperationLock())
            return;

        _lastOperationTime = DateTime.Now;

        try
        {
            if (!IsRecording)
            {
                Log(LogLevel.Debug, LogPrefix, "Stop capture ignored: Not recording");
                return;
            }

            await ExecuteStopCapture();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task ExecuteStopCapture()
    {
        await _captureService.StopCaptureAsync();
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

    private async Task<bool> TryAcquireOperationLock()
    {
        try
        {
            if (!await _operationLock.WaitAsync(TimeSpan.FromMilliseconds(OPERATION_TIMEOUT_MS)))
            {
                Log(LogLevel.Warning, LogPrefix, "Cannot acquire operation lock: timeout");
                return false;
            }
            return true;
        }
        catch
        {
            Log(LogLevel.Warning, LogPrefix, "Error acquiring operation lock");
            return false;
        }
    }

    private void UpdateGainParameter(float newValue, Action<float> setter, string propertyName) =>
        Safe(() =>
        {
            if (setter is null)
            {
                Log(LogLevel.Error, LogPrefix, "Null setter delegate provided");
                return;
            }

            setter(newValue);
            _mainController.OnPropertyChanged(propertyName);
        },
        new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error updating gain parameter" });

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
            Log(LogLevel.Error, LogPrefix, "Gain parameters not initialized");
            return;
        }

        if (!validator(value))
        {
            Log(LogLevel.Warning, LogPrefix, errorMessage);
            value = fallbackValue;
        }

        UpdateGainParameter(value, setter, propertyName);
        settingUpdater(value);
    }

    #endregion

    protected override void DisposeManaged()
    {
        _mainController.PropertyChanged -= OnMainControllerPropertyChanged;

        PerformDisposeSync();
    }

    protected override async ValueTask DisposeAsyncManagedResources()
    {
        _mainController.PropertyChanged -= OnMainControllerPropertyChanged;

        await PerformDisposeAsync();
    }

    private void PerformDisposeSync()
    {
        Log(LogLevel.Information, LogPrefix, "AudioController disposing");
        DisposeCaptureServiceInternalSync();
        DisposeOperationLock();
        Log(LogLevel.Information, LogPrefix, "AudioController disposed successfully");
    }

    private void DisposeCaptureServiceInternalSync()
    {
        try
        {
            if (_captureService is IDisposable disposable)
            {
                _captureService.StopCaptureAsync().GetAwaiter().GetResult();
                disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogPrefix, $"Error during synchronous capture service disposal: {ex.Message}");
        }
    }

    private void DisposeOperationLock()
    {
        try
        {
            _operationLock.Dispose();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogPrefix, $"Error disposing operation lock: {ex.Message}");
        }
    }

    private async Task PerformDisposeAsync()
    {
        Log(LogLevel.Information, LogPrefix, "AudioController async disposing");
        await DisposeCaptureServiceInternalAsync();
        DisposeOperationLock();
        Log(LogLevel.Information, LogPrefix, "AudioController async disposed successfully");
    }

    private async Task DisposeCaptureServiceInternalAsync()
    {
        try
        {
            await _captureService.StopCaptureAsync();
            await _captureService.DisposeAsync();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogPrefix, $"Error during asynchronous capture service disposal: {ex.Message}");
        }
    }
}