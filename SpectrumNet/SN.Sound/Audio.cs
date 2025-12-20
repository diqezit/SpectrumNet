namespace SpectrumNet.SN.Sound;

public interface IAudioController : INotifyPropertyChanged, IAsyncDisposable, IDisposable
{
    bool IsRecording { get; set; }
    bool CanStartCapture { get; }

    FftWindowType WindowType { get; set; }
    StereoMode StereoMode { get; set; }

    float MinDbLevel { get; set; }
    float MaxDbLevel { get; set; }
    float AmplificationFactor { get; set; }

    IGainParametersProvider GainParameters { get; }

    Task StartCaptureAsync();
    Task StopCaptureAsync();
    Task ToggleCaptureAsync();

    ISpectralDataProvider? GetCurrentProvider();
}

public sealed class AudioController : ObservableObject, IAudioController
{
    private const float MinDbDiff = 1.0f;
    private const float MinAmp = 0.1f;

    private readonly AppController _main;
    private readonly ISettingsService _cfg;
    private readonly CaptureManager _cap;

    private readonly EventHandler _capStateHandler;
    private readonly PropertyChangedEventHandler _mainPropHandler;

    private FftWindowType _windowType;
    private StereoMode _stereoMode;

    private bool _disposed;

    public IGainParametersProvider GainParameters { get; }

    public AudioController(
        AppController main,
        SynchronizationContext? syncContext,
        IGainParametersProvider gainParameters,
        ISettingsService cfg,
        IRendererFactory rendererFactory,
        ISmartLogger log,
        IPerformanceMetricsManager perf,
        IBrushProvider brushes)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(gainParameters);
        ArgumentNullException.ThrowIfNull(cfg);
        ArgumentNullException.ThrowIfNull(rendererFactory);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(perf);
        ArgumentNullException.ThrowIfNull(brushes);

        _main = main;
        GainParameters = gainParameters;
        _cfg = cfg;

        _windowType = _cfg.Current.Visualization.SelectedFftWindowType;
        _stereoMode = _cfg.Current.Visualization.SelectedStereoMode;

        _cap = new CaptureManager(
            ctrl: _main,
            rf: rendererFactory,
            log: log,
            audio: this,
            ctx: syncContext,
            perf: perf,
            bp: brushes);

        _capStateHandler = (_, _) =>
        {
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(CanStartCapture));
        };

        _mainPropHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(AppController.ScaleType))
                SyncAnalyzer();
        };

        _cap.StateChanged += _capStateHandler;
        _main.PropertyChanged += _mainPropHandler;
    }

    public bool IsRecording
    {
        get => _cap.IsActive;
        set
        {
            if (value == IsRecording) return;
            _ = SetCaptureAsync(value);
        }
    }

    public bool CanStartCapture => !_disposed && !IsRecording && !_cap.IsBusy;

    public FftWindowType WindowType
    {
        get => _windowType;
        set => SetVizAndSync(
            ref _windowType,
            value,
            v => _cfg.UpdateVisualization(c => c with { SelectedFftWindowType = v }));
    }

    public StereoMode StereoMode
    {
        get => _stereoMode;
        set => SetVizAndSync(
            ref _stereoMode,
            value,
            v => _cfg.UpdateVisualization(c => c with { SelectedStereoMode = v }));
    }

    public float MinDbLevel
    {
        get => GainParameters.MinDbValue;
        set => SetDbLevels(min: value, max: null);
    }

    public float MaxDbLevel
    {
        get => GainParameters.MaxDbValue;
        set => SetDbLevels(min: null, max: value);
    }

    public float AmplificationFactor
    {
        get => GainParameters.AmplificationFactor;
        set
        {
            float v = Max(MinAmp, value);

            if (Abs(GainParameters.AmplificationFactor - v) < float.Epsilon)
                return;

            GainParameters.AmplificationFactor = v;
            _cfg.UpdateAudio(a => a with { AmplificationFactor = v });
            OnPropertyChanged();
        }
    }

    public Task StartCaptureAsync() => SetCaptureAsync(active: true);
    public Task StopCaptureAsync() => SetCaptureAsync(active: false);
    public Task ToggleCaptureAsync() => SetCaptureAsync(active: !IsRecording);

    public ISpectralDataProvider? GetCurrentProvider() => _cap.GetAnalyzer();

    private bool SetVizAndSync<T>(ref T field, T value, Action<T> persist)
    {
        if (_disposed) return false;
        if (!SetProperty(ref field, value)) return false;

        persist(value);
        SyncAnalyzer();
        return true;
    }

    private void SetDbLevels(float? min, float? max)
    {
        float newMin = min ?? GainParameters.MinDbValue;
        float newMax = max ?? GainParameters.MaxDbValue;

        if (newMin >= newMax - MinDbDiff)
        {
            float mid = (newMin + newMax) * 0.5f;
            newMin = mid - MinDbDiff;
            newMax = mid + MinDbDiff;
        }

        if (Abs(GainParameters.MinDbValue - newMin) < float.Epsilon &&
            Abs(GainParameters.MaxDbValue - newMax) < float.Epsilon)
            return;

        GainParameters.MinDbValue = newMin;
        GainParameters.MaxDbValue = newMax;

        _cfg.UpdateAudio(a => a with { MinDbLevel = newMin, MaxDbLevel = newMax });

        OnPropertyChanged(nameof(MinDbLevel));
        OnPropertyChanged(nameof(MaxDbLevel));
    }

    private Task SetCaptureAsync(bool active)
    {
        if (_disposed) return Task.CompletedTask;

        return active
            ? !IsRecording ? _cap.SetStateAsync(active: true) : Task.CompletedTask
            : IsRecording ? _cap.SetStateAsync(active: false) : Task.CompletedTask;
    }

    private void SyncAnalyzer()
    {
        SpectrumAnalyzer? a = _cap.GetAnalyzer();
        if (a == null) return;

        a.WindowType = _windowType;
        a.ScaleType = _main.ScaleType;
        a.StereoMode = _stereoMode;
    }

    private bool TryBeginDispose()
    {
        if (_disposed) return false;
        _disposed = true;

        _cap.StateChanged -= _capStateHandler;
        _main.PropertyChanged -= _mainPropHandler;

        return true;
    }

    public void Dispose()
    {
        if (!TryBeginDispose()) return;
        _cap.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (!TryBeginDispose()) return;
        await _cap.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class CaptureManager : IAsyncDisposable, IDisposable
{
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 500;
    private const int DeviceLostDelayMs = 300;

    private static readonly ResiliencePipeline<CaptureSession> RetryPipeline =
        new ResiliencePipelineBuilder<CaptureSession>()
            .AddRetry(new RetryStrategyOptions<CaptureSession>
            {
                MaxRetryAttempts = MaxRetries - 1,
                Delay = FromMilliseconds(RetryDelayMs),
                ShouldHandle = new PredicateBuilder<CaptureSession>()
                    .Handle<Exception>(ex => ex is not OperationCanceledException)
            })
            .Build();

    private readonly AppController _ctrl;
    private readonly IRendererFactory _rf;
    private readonly ISmartLogger _log;
    private readonly IAudioController _audio;
    private readonly IPerformanceMetricsManager _perf;
    private readonly IBrushProvider _bp;
    private readonly DeviceMonitor _mon;
    private readonly SynchronizationContext? _ctx;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CaptureSession? _session;
    private bool _disposed;

    public event EventHandler? StateChanged;

    public bool IsActive => _session?.IsActive ?? false;
    public bool IsBusy => _gate.CurrentCount == 0;

    public CaptureManager(
        AppController ctrl,
        IRendererFactory rf,
        ISmartLogger log,
        IAudioController audio,
        SynchronizationContext? ctx,
        IPerformanceMetricsManager perf,
        IBrushProvider bp)
    {
        ArgumentNullException.ThrowIfNull(ctrl);
        ArgumentNullException.ThrowIfNull(rf);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(perf);
        ArgumentNullException.ThrowIfNull(bp);

        _ctrl = ctrl;
        _rf = rf;
        _log = log;
        _audio = audio;
        _perf = perf;
        _bp = bp;
        _ctx = ctx;

        _mon = new DeviceMonitor(_log, OnDeviceLost);
    }

    public SpectrumAnalyzer? GetAnalyzer() => _session?.Analyzer;

    public async Task SetStateAsync(bool active)
    {
        if (_disposed) return;

        if (!await _gate.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            if (active)
                await StartAsync().ConfigureAwait(false);
            else
                await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StartAsync()
    {
        await StopAsync().ConfigureAwait(false);

        CaptureSession session = await RetryPipeline.ExecuteAsync(_ =>
        {
            MMDevice dev = _mon.GetDefaultDevice()
                ?? throw new InvalidOperationException("No audio device");

            try
            {
                var s = new CaptureSession(dev, _ctrl, _log, _audio);
                return ValueTask.FromResult(s);
            }
            catch (Exception ex)
            {
                _log.Log(LogLevel.Error, nameof(CaptureManager), $"Start failed: {ex.Message}");
                dev.Dispose();
                throw;
            }
        }).ConfigureAwait(false);

        await AttachSessionToUiAsync(session).ConfigureAwait(false);

        session.Start();
        _session = session;

        _log.Log(
            LogLevel.Information,
            nameof(CaptureManager),
            $"Started: {session.DeviceName}");

        RaiseStateChanged();
    }

    private async Task StopAsync()
    {
        if (_session == null) return;

        await _session.DisposeAsync().ConfigureAwait(false);
        _session = null;

        RaiseStateChanged();
    }

    private Task AttachSessionToUiAsync(CaptureSession s)
    {
        Dispatcher? d = Application.Current?.Dispatcher;
        return d == null
            ? Task.CompletedTask
            : d.InvokeAsync(
            () =>
            {
                _ctrl.View.Analyzer = s.Analyzer;

                if (_ctrl.View.SpectrumCanvas is { ActualWidth: > 0, ActualHeight: > 0 })
                {
                    _ctrl.View.Renderer = new Renderer(
                        _ctrl.View,
                        _bp,
                        _rf,
                        _perf,
                        _log);
                }
            },
            DispatcherPriority.Send).Task;
    }

    private void PostToCtx(Action a)
    {
        if (_ctx != null && SynchronizationContext.Current != _ctx)
            _ctx.Post(_ => a(), null);
        else
            a();
    }

    private void RaiseStateChanged() =>
        PostToCtx(() => StateChanged?.Invoke(this, EventArgs.Empty));

    private void OnDeviceLost()
    {
        if (!IsActive || _disposed) return;

        _ = Task.Run(async () =>
        {
            await SetStateAsync(active: false).ConfigureAwait(false);
            await Task.Delay(DeviceLostDelayMs).ConfigureAwait(false);
            await SetStateAsync(active: true).ConfigureAwait(false);
        });
    }

    private bool TryBeginDispose()
    {
        if (_disposed) return false;
        _disposed = true;
        return true;
    }

    private void DisposeCore()
    {
        _session?.Dispose();
        _session = null;

        _mon.Dispose();
        _gate.Dispose();
    }

    private async ValueTask DisposeCoreAsync()
    {
        if (_session != null)
            await _session.DisposeAsync().ConfigureAwait(false);

        _session = null;

        _mon.Dispose();
        _gate.Dispose();
    }

    public void Dispose()
    {
        if (!TryBeginDispose()) return;
        DisposeCore();
    }

    public async ValueTask DisposeAsync()
    {
        if (!TryBeginDispose()) return;
        await DisposeCoreAsync().ConfigureAwait(false);
    }
}

internal sealed class CaptureSession : IAsyncDisposable, IDisposable
{
    private readonly MMDevice _device;
    private readonly WasapiLoopbackCapture _wasapi;
    private readonly ISmartLogger _log;

    private bool _disposed;

    public bool IsActive { get; private set; }
    public SpectrumAnalyzer Analyzer { get; }
    public string DeviceName { get; }

    public CaptureSession(
        MMDevice device,
        AppController ctrl,
        ISmartLogger log,
        IAudioController audio)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(ctrl);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(audio);

        _device = device;
        _log = log;

        DeviceName = device.FriendlyName;

        _wasapi = new WasapiLoopbackCapture(device);

        Analyzer = new SpectrumAnalyzer(audio.GainParameters)
        {
            WindowType = audio.WindowType,
            ScaleType = ctrl.ScaleType,
            StereoMode = audio.StereoMode
        };

        _wasapi.DataAvailable += OnData;
        _wasapi.RecordingStopped += OnStopped;
    }

    public void Start()
    {
        _wasapi.StartRecording();
        IsActive = true;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _disposed || _wasapi.WaveFormat is not { } fmt)
            return;

        Span<float> samples = MemoryMarshal.Cast<byte, float>(
            e.Buffer.AsSpan(0, e.BytesRecorded));

        _ = Analyzer.AddSamplesAsync(
            StereoProcessor.ToMono(samples, fmt.Channels, Analyzer.StereoMode),
            fmt.SampleRate);
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        IsActive = false;

        if (e.Exception != null)
            _log.Log(LogLevel.Error, nameof(CaptureSession), $"Stopped: {e.Exception.Message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        IsActive = false;

        _wasapi.DataAvailable -= OnData;
        _wasapi.RecordingStopped -= OnStopped;

        try { _wasapi.StopRecording(); }
        catch { }

        _wasapi.Dispose();
        Analyzer.Dispose();
        _device.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class DeviceMonitor : IDisposable, IMMNotificationClient
{
    private readonly MMDeviceEnumerator _callbackEnum = new();
    private readonly ISmartLogger _log;
    private readonly Action _onLost;

    private string _currentId = "";

    public DeviceMonitor(ISmartLogger log, Action onLost)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(onLost);

        _log = log;
        _onLost = onLost;

        _callbackEnum.RegisterEndpointNotificationCallback(this);
    }

    public MMDevice? GetDefaultDevice()
    {
        try
        {
            using var localEnum = new MMDeviceEnumerator();
            MMDevice? dev = localEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _currentId = dev?.ID ?? "";
            return dev;
        }
        catch (Exception ex)
        {
            _log.Log(LogLevel.Error, nameof(DeviceMonitor), $"Enum failed: {ex.Message}");
            return null;
        }
    }

    private bool IsCurrent(string id) => id == _currentId;

    public void Dispose()
    {
        try { _callbackEnum.UnregisterEndpointNotificationCallback(this); }
        catch { }

        _callbackEnum.Dispose();
    }

    void IMMNotificationClient.OnDeviceStateChanged(string id, DeviceState s)
    {
        if (IsCurrent(id) && s != DeviceState.Active)
            _onLost();
    }

    void IMMNotificationClient.OnDeviceRemoved(string id)
    {
        if (IsCurrent(id))
            _onLost();
    }

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow f, Role r, string id)
    {
        if (f == DataFlow.Render && r == Role.Multimedia && !IsCurrent(id))
            _onLost();
    }

    void IMMNotificationClient.OnDeviceAdded(string id) { }
    void IMMNotificationClient.OnPropertyValueChanged(string id, PropertyKey k) { }
}
