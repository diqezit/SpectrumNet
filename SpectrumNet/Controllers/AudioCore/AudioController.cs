#nullable enable

namespace SpectrumNet.Controllers.AudioCore;

public class AudioController : AsyncDisposableBase, IAudioController
{
    private const string LogPrefix = "AudioController";

    private readonly IMainController _mainController;
    private readonly GainParameters _gainParameters;
    private readonly IRendererFactory _rendererFactory;
    private AudioCapture? _captureManager;

    private bool _isTransitioning;
    private FftWindowType _windowType = FftWindowType.Hann;

    public AudioController(
        IMainController mainController,
        SynchronizationContext syncContext)
    {
        _mainController = mainController ??
            throw new ArgumentNullException(nameof(mainController));

        _gainParameters = new GainParameters(
            syncContext,
            Settings.Instance.UIMinDbLevel,
            Settings.Instance.UIMaxDbLevel,
            Settings.Instance.UIAmplificationFactor
        ) ?? throw new InvalidOperationException("Failed to create gain parameters");

        _rendererFactory = RendererFactory.Instance;

        _captureManager = new AudioCapture(_mainController, _rendererFactory) ??
            throw new InvalidOperationException("Failed to create AudioCapture");
    }

    #region IAudioController Implementation

    public GainParameters GainParameters => _gainParameters;

    public bool IsRecording
    {
        get => _captureManager?.IsRecording ?? false;
        set
        {
            if (value == IsRecording) return;

            if (value)
                SafeExecute(() => StartCaptureAsync().GetAwaiter().GetResult(),
                    "Error starting capture from IsRecording setter");
            else
                SafeExecute(() => StopCaptureAsync().GetAwaiter().GetResult(),
                    "Error stopping capture from IsRecording setter");
        }
    }

    public bool CanStartCapture => _captureManager is not null && !IsRecording;

    public bool IsTransitioning
    {
        get => _isTransitioning;
        set => SetField(ref _isTransitioning, value);
    }

    public FftWindowType WindowType
    {
        get => _windowType;
        set
        {
            if (_windowType == value) return;

            _windowType = value;
            Settings.Instance.SelectedFftWindowType = value;
            _mainController.OnPropertyChanged(nameof(WindowType));
            _mainController.Analyzer?.UpdateSettings(value, _mainController.ScaleType);
        }
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

        if (_captureManager is not { } captureManager)
        {
            Log(LogLevel.Error, LogPrefix, "Attempt to start capture with no CaptureManager");
            return;
        }

        await captureManager.StartCaptureAsync();
        _mainController.OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));
    }

    public async Task StopCaptureAsync()
    {
        ThrowIfDisposed();

        if (_captureManager is not { } captureManager)
        {
            Log(LogLevel.Error, LogPrefix, "Attempt to stop capture with no CaptureManager");
            return;
        }

        await captureManager.StopCaptureAsync();
        _mainController.OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));
    }

    public async Task ToggleCaptureAsync()
    {
        ThrowIfDisposed();

        if (_captureManager is null) return;

        if (IsRecording)
            await StopCaptureAsync();
        else
            await StartCaptureAsync();
    }

    public SpectrumAnalyzer? GetCurrentAnalyzer()
    {
        ThrowIfDisposed();
        return _captureManager?.GetAnalyzer();
    }

    #endregion

    #region Helper Methods

    private void UpdateGainParameter(float newValue, Action<float> setter, string propertyName) =>
        SafeExecute(() =>
        {
            if (setter is null)
            {
                Log(LogLevel.Error, LogPrefix, "Null delegate passed for parameter update");
                return;
            }

            setter(newValue);
            _mainController.OnPropertyChanged(propertyName);
        }, "Error updating gain parameter");

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
            Log(LogLevel.Error, LogPrefix, "Gain parameters not initialized in UpdateDbLevel");
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

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        _mainController.OnPropertyChanged(propertyName ?? string.Empty);
        return true;
    }

    private static void SafeExecute(Action action, string errorMessage) =>
        Safe(action, new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = errorMessage });

    #endregion

    protected override void DisposeManaged()
    {
        Log(LogLevel.Information, LogPrefix, "AudioController disposing");

        try
        {
            if (_captureManager is IDisposable disposable)
            {
                _captureManager.StopCaptureAsync().GetAwaiter().GetResult();
                disposable.Dispose();
                _captureManager = null;
            }
            Log(LogLevel.Information, LogPrefix, "AudioController disposed successfully");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogPrefix, $"Error disposing audio capture manager: {ex.Message}");
        }
    }

    protected override async ValueTask DisposeAsyncManagedResources()
    {
        Log(LogLevel.Information, LogPrefix, "AudioController async disposing");

        try
        {
            if (_captureManager is not null)
            {
                await _captureManager.StopCaptureAsync();

                if (_captureManager is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else if (_captureManager is IDisposable disposable)
                    disposable.Dispose();

                _captureManager = null;
            }
            Log(LogLevel.Information, LogPrefix, "AudioController async disposed successfully");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogPrefix, $"Error async disposing audio capture manager: {ex.Message}");
        }
    }
}