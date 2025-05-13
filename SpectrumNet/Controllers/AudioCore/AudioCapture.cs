#nullable enable

namespace SpectrumNet.Controllers.AudioCore;

public sealed class AudioCapture : AsyncDisposableBase
{
    private const string LogPrefix = nameof(AudioCapture);
    private const int DefaultDeviceCheckIntervalMs = 500;

    private readonly object
        _lock = new(),
        _stateLock = new();

    private readonly IMainController _controller;
    private readonly MMDeviceEnumerator _deviceEnumerator;
    private readonly AudioEndpointNotificationHandler _notificationHandler;
    private readonly int _deviceCheckIntervalMs;
    private readonly IRendererFactory _rendererFactory;
    private readonly SemaphoreSlim _stateChangeLock = new(1, 1);

    private MMDevice? _currentDevice;
    private string _lastDeviceId = string.Empty;
    private CaptureState? _state;

    private volatile bool _isReinitializing;
    private volatile bool _isCaptureStopping;

    public bool IsRecording { get; private set; }

    private const int
        STOP_OPERATION_TIMEOUT_MS = 3000,
        STATE_LOCK_TIMEOUT_MS = 3000;

    private record CaptureState(
        SpectrumAnalyzer Analyzer,
        WasapiLoopbackCapture Capture,
        CancellationTokenSource CTS
    );

    public AudioCapture(
        IMainController controller,
        IRendererFactory rendererFactory,
        int deviceCheckIntervalMs = DefaultDeviceCheckIntervalMs)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(rendererFactory);

        _controller = controller;
        _rendererFactory = rendererFactory;
        _deviceEnumerator = new MMDeviceEnumerator()
                             ?? throw new InvalidOperationException(
                                 "Failed to create MMDeviceEnumerator");
        _notificationHandler = new AudioEndpointNotificationHandler(
                                 OnDeviceChanged);
        _deviceCheckIntervalMs = deviceCheckIntervalMs;

        Safe(
            RegisterDeviceNotificationsImpl,
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Failed to register endpoint notification callback"
            }
        );
    }

    public SpectrumAnalyzer? GetAnalyzer() => _state?.Analyzer;

    private void RegisterDeviceNotificationsImpl()
    {
        _deviceEnumerator.RegisterEndpointNotificationCallback(
            _notificationHandler
        );
        Log(LogLevel.Information,
            LogPrefix,
            "Audio device notification handler registered successfully"
        );
    }

    private void OnDeviceChanged()
    {
        Safe(
            OnDeviceChangedCore,
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Unhandled exception in OnDeviceChanged"
            }
        );
    }

    private void OnDeviceChangedCore()
    {
        lock (_lock)
        {
            if (_isDisposed
                || _isReinitializing
                || _controller.IsTransitioning
                || _isCaptureStopping)
                return;

            var device = GetDefaultAudioDevice();
            if (device is null)
            {
                Log(
                    LogLevel.Warning,
                    LogPrefix,
                    "No default audio device available"
                );
                StopCaptureIfNeeded();
                return;
            }

            if (device.ID != _lastDeviceId)
            {
                Log(
                    LogLevel.Information,
                    LogPrefix,
                    $"Audio device changed from '{_lastDeviceId}' to '{device.ID}'"
                );
                _lastDeviceId = device.ID;

                if (IsRecording && !_isReinitializing)
                {
                    _ = Task.Run(ReinitializeCaptureAsync);
                }
            }
        }
    }

    private void StopCaptureIfNeeded()
    {
        if (_state is not null && IsRecording)
        {
            Safe(
                () => StopCaptureAsync(true).GetAwaiter().GetResult(),
                new ErrorHandlingOptions
                {
                    Source = LogPrefix,
                    ErrorMessage = "Error stopping capture during device change"
                }
            );
        }
    }

    public Task ReinitializeCaptureAsync() =>
        SafeAsync(
            ReinitializeCaptureCoreAsync,
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Ошибка переинициализации"
            }
        );

    private async Task ReinitializeCaptureCoreAsync()
    {
        if (_isReinitializing)
        {
            Log(LogLevel.Warning,
                LogPrefix,
                "Reinitialization already in progress");
            return;
        }

        try
        {
            _isReinitializing = true;
            _controller.IsTransitioning = true;

            await StopCaptureAsync(true);
            await Task.Delay(500);

            var device = GetDefaultAudioDevice();
            if (device is null) return;

            _lastDeviceId = device.ID;

            lock (_stateLock)
            {
                DisposeCaptureState(_state);
                _state = null;
            }

            await InitializeUIComponentsAsync();
            await StartCaptureAsync();
        }
        finally
        {
            _controller.IsTransitioning = false;
            _isReinitializing = false;
        }
    }

    private Task InitializeUIComponentsAsync() =>
        _controller.Dispatcher.InvokeAsync(
            InitializeUIComponentsCore
        ).Task;

    private void InitializeUIComponentsCore()
    {
        _controller.Analyzer = CreateAnalyzer();

        if (_controller.SpectrumCanvas is { ActualWidth: > 0, ActualHeight: > 0 } canvas)
        {
            _controller.Renderer = new Renderer(
                _controller.SpectrumStyles,
                _controller,
                _controller.Analyzer,
                canvas,
                _rendererFactory
            );
            _controller.SynchronizeVisualization();
        }
    }

    private SpectrumAnalyzer CreateAnalyzer()
    {
        var analyzer = new SpectrumAnalyzer(
            new FftProcessor { WindowType = _controller.WindowType },
            new SpectrumConverter(_controller.GainParameters),
            SynchronizationContext.Current
        );

        analyzer.ScaleType = _controller.ScaleType;
        analyzer.UpdateSettings(
            _controller.WindowType,
            _controller.ScaleType
        );

        return analyzer;
    }

    private SpectrumAnalyzer InitializeAnalyzer()
    {
        return _controller.Dispatcher.Invoke(() =>
        {
            var analyzer = CreateAnalyzer();
            if (_controller.SpectrumCanvas is { ActualWidth: > 0, ActualHeight: > 0 } canvas)
            {
                _controller.Renderer = new Renderer(
                    _controller.SpectrumStyles,
                    _controller,
                    analyzer,
                    canvas,
                    _rendererFactory
                );
                _controller.SynchronizeVisualization();
            }
            return analyzer;
        });
    }

    public async Task StartCaptureAsync()
    {
#if DEBUG
        Log(LogLevel.Debug,
            LogPrefix,
            "StartCaptureAsync called");
#endif

        ThrowIfDisposed();

        if (_isCaptureStopping || IsRecording || _isReinitializing || _controller.IsTransitioning)
        {
            Log(LogLevel.Warning,
                LogPrefix,
                "Cannot start capture: operation in progress or already recording");
            return;
        }

        if (!await _stateChangeLock.WaitAsync(STATE_LOCK_TIMEOUT_MS))
        {
            Log(LogLevel.Warning,
                LogPrefix,
                "Could not acquire state change lock for starting capture");
            return;
        }

        try
        {
            if (_isDisposed || IsRecording || _isReinitializing || _controller.IsTransitioning)
            {
                Log(LogLevel.Warning,
                    LogPrefix,
                    "Cannot start capture after lock: state changed");
                return;
            }

            var device = GetDefaultAudioDevice();
            if (device is null)
            {
                Log(LogLevel.Error,
                    LogPrefix,
                    "No audio device available");
                return;
            }

            CancellationToken token;

            _lastDeviceId = device.ID;
            InitializeCaptureCore();

            if (_state is null)
            {
                Log(LogLevel.Error,
                    LogPrefix,
                    "Failed to initialize capture state");
                return;
            }

            token = _state.CTS.Token;

            _ = Task.Run(() => MonitorCaptureAsync(token));

#if DEBUG
            Log(LogLevel.Debug,
                LogPrefix,
                "Audio capture started successfully");
#endif
        }
        finally
        {
            _stateChangeLock.Release();
        }
    }

    private void InitializeCaptureCore()
    {
        try
        {
            lock (_stateLock)
            {
                DisposeCaptureState(_state);
                _state = null;
            }

            var device = GetDefaultAudioDevice();
            if (device is null) return;

            var capture = new WasapiLoopbackCapture(device)
                          ?? throw new InvalidOperationException(
                              "Failed to initialize capture device"
                          );

            var analyzer = InitializeAnalyzer();

            CaptureState newState = new(
                analyzer,
                capture,
                new CancellationTokenSource()
            );

            newState.Capture.DataAvailable += OnDataAvailable;
            newState.Capture.RecordingStopped += OnRecordingStopped;

            lock (_stateLock)
            {
                _state = newState;
            }

            UpdateStatus(true);
            newState.Capture.StartRecording();

            Log(LogLevel.Information,
                LogPrefix,
                $"Audio capture started on device: {device.FriendlyName}");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error initializing capture: {ex.Message}");

            lock (_stateLock)
            {
                DisposeCaptureState(_state);
                _state = null;
            }

            UpdateStatus(false);
            throw;
        }
    }

    public async Task StopCaptureAsync(bool updateUI = true)
    {
        ThrowIfDisposed();

        if (_isCaptureStopping)
        {
            Log(LogLevel.Warning, LogPrefix, "Stop capture already in progress");
            return;
        }

        CaptureState? stateToDispose = null;

        try
        {
            _isCaptureStopping = true;

            if (!IsRecording && _state is null)
            {
                Log(LogLevel.Debug, LogPrefix, "Stop capture ignored: Not recording");
                return;
            }

            lock (_stateLock)
            {
                if (_state is null)
                {
                    Log(LogLevel.Warning, LogPrefix, "No capture state to dispose");
                    return;
                }

                _state.CTS.Cancel();
                stateToDispose = _state;
                _state = null;
            }

            if (stateToDispose is not null)
            {
                bool stopSuccessful = false;

                try
                {
                    using var timeoutCts = new CancellationTokenSource(STOP_OPERATION_TIMEOUT_MS);

                    try
                    {
                        await Task.Run(() =>
                        {
                            try
                            {
                                stateToDispose.Capture.StopRecording();
                                stopSuccessful = true;
                            }
                            catch (Exception ex)
                            {
                                Log(LogLevel.Error, LogPrefix, $"Error stopping recording: {ex.Message}");
                            }
                        }).WaitAsync(timeoutCts.Token);
                    }
                    catch (TimeoutException)
                    {
                        Log(LogLevel.Warning, LogPrefix, "Stop capture operation timed out");
                    }
                    catch (OperationCanceledException)
                    {
                        Log(LogLevel.Warning, LogPrefix, "Stop capture operation was canceled");
                    }

                    if ((stopSuccessful || timeoutCts.IsCancellationRequested) &&
                        !_controller.IsTransitioning)
                    {
                        try
                        {
                            stateToDispose.Analyzer.SafeReset();
                        }
                        catch (Exception ex)
                        {
                            Log(LogLevel.Error, LogPrefix, $"Error resetting analyzer: {ex.Message}");
                        }
                    }
                }
                finally
                {
                    DisposeCaptureState(stateToDispose);
                }
            }

            if (updateUI)
            {
                UpdateStatus(false);
            }
        }
        finally
        {
            _isCaptureStopping = false;
        }
    }

    public async Task ToggleCaptureAsync() =>
        await (IsRecording ? StopCaptureAsync() : StartCaptureAsync());

    private MMDevice? GetDefaultAudioDevice()
    {
        try
        {
            SafeDispose(
                _currentDevice,
                nameof(_currentDevice),
                new ErrorHandlingOptions
                {
                    Source = LogPrefix,
                    ErrorMessage = "Error disposing current device"
                }
            );

            _currentDevice = _deviceEnumerator.GetDefaultAudioEndpoint(
                DataFlow.Render,
                Role.Multimedia
            );
            return _currentDevice;
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error getting default audio device: {ex.Message}"
            );
            return null;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        Safe(
            () => ProcessDataAvailable(e),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error processing audio data"
            }
        );
    }

    private void ProcessDataAvailable(WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        SpectrumAnalyzer? analyzer;
        WasapiLoopbackCapture? capture;

        lock (_stateLock)
        {
            if (_state is null) return;
            analyzer = _state.Analyzer;
            capture = _state.Capture;
        }

        if (analyzer is null || analyzer.IsDisposed || capture is null) return;

        var waveFormat = capture.WaveFormat
                         ?? throw new InvalidOperationException(
                             "WaveFormat is null"
                         );

        int channels = waveFormat.Channels;
        int frameCount = e.BytesRecorded / 4 / channels;
        float[] monoSamples = new float[frameCount];

        ConvertToMono(
            e.Buffer,
            channels,
            frameCount,
            monoSamples,
            e.BytesRecorded
        );

        _ = analyzer.AddSamplesAsync(
            monoSamples,
            waveFormat.SampleRate
        );
    }

    private static void ConvertToMono(
        byte[] buffer,
        int channels,
        int frameCount,
        float[] monoSamples,
        int bytesRecorded
    )
    {
        var span = buffer.AsSpan(0, bytesRecorded);
        for (int frame = 0; frame < frameCount; frame++)
        {
            float sum = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
                int idx = (frame * channels + ch) * 4;
                sum += BitConverter.ToSingle(span.Slice(idx, 4));
            }
            monoSamples[frame] = sum / channels;
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Recording stopped with error: {e.Exception.Message}"
            );

            if (!_isDisposed && IsRecording && !_isReinitializing &&
                !_controller.IsTransitioning && !_isCaptureStopping)
            {
                _ = Task.Run(ReinitializeCaptureAsync);
            }
            return;
        }

        Log(LogLevel.Information,
            LogPrefix,
            "Recording stopped normally"
        );
    }

    private Task MonitorCaptureAsync(CancellationToken token)
    {
        return SafeAsync(
            async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(
                            _deviceCheckIntervalMs,
                            token
                        );

                        if (!IsRecording || _controller.IsTransitioning || _isCaptureStopping)
                        {
                            continue;
                        }

                        var device = GetDefaultAudioDevice();
                        if (device is null || device.ID != _lastDeviceId)
                        {
                            OnDeviceChanged();
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    Log(LogLevel.Information,
                        LogPrefix,
                        "Device monitoring task cancelled normally");
                }
                catch (OperationCanceledException)
                {
                    Log(LogLevel.Information,
                        LogPrefix,
                        "Device monitoring operation cancelled normally");
                }
            },
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error in device monitoring",
                IgnoreExceptions =
                [
                    typeof(TaskCanceledException),
                    typeof(OperationCanceledException)
                ]
            }
        );
    }

    private void UpdateStatus(bool isRecording)
    {
        _controller.Dispatcher.Invoke(() =>
        {
            IsRecording = isRecording;
            _controller.IsRecording = isRecording;
            _controller.OnPropertyChanged(
                nameof(_controller.IsRecording),
                nameof(_controller.CanStartCapture)
            );
        });
    }

    private void DisposeCaptureState(CaptureState? state)
    {
        if (state is null) return;

        try
        {
            try
            {
                state.Capture.DataAvailable -= OnDataAvailable;
                state.Capture.RecordingStopped -= OnRecordingStopped;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error,
                    LogPrefix,
                    $"Error unsubscribing events: {ex.Message}");
            }

            SafeDispose(
                state.Capture,
                nameof(state.Capture),
                new ErrorHandlingOptions
                {
                    Source = LogPrefix,
                    ErrorMessage = "Error disposing capture"
                }
            );

            SafeDispose(
                state.CTS,
                nameof(state.CTS),
                new ErrorHandlingOptions
                {
                    Source = LogPrefix,
                    ErrorMessage = "Error disposing CTS"
                }
            );

            try
            {
                if (state.Analyzer is IAsyncDisposable asyncDisposable)
                {
                    _ = asyncDisposable.DisposeAsync();
                }
                else
                {
                    SafeDispose(
                        state.Analyzer,
                        nameof(state.Analyzer),
                        new ErrorHandlingOptions
                        {
                            Source = LogPrefix,
                            ErrorMessage = "Error disposing analyzer"
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error,
                    LogPrefix,
                    $"Error during analyzer disposal: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error during capture state disposal: {ex.Message}");
        }
    }

    protected override void DisposeManaged()
    {
        try
        {
            if (IsRecording)
            {
                StopCaptureAsync(updateUI: false).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error stopping capture during dispose: {ex.Message}");
        }

        try
        {
            _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationHandler);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error unregistering notification callback: {ex.Message}");
        }

        SafeDispose(
            _currentDevice,
            nameof(_currentDevice),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error disposing current device"
            }
        );

        SafeDispose(
            _deviceEnumerator,
            nameof(_deviceEnumerator),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error disposing device enumerator"
            }
        );

        _stateChangeLock.Dispose();
    }

    protected override async ValueTask DisposeAsyncManagedResources()
    {
        try
        {
            if (IsRecording)
            {
                await StopCaptureAsync(updateUI: false);
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error stopping capture during async dispose: {ex.Message}");
        }

        try
        {
            _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationHandler);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error unregistering notification callback: {ex.Message}");
        }

        SafeDispose(
            _currentDevice,
            nameof(_currentDevice),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error disposing current device"
            }
        );

        SafeDispose(
            _deviceEnumerator,
            nameof(_deviceEnumerator),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error disposing device enumerator"
            }
        );

        _stateChangeLock.Dispose();
    }

    private class AudioEndpointNotificationHandler : IMMNotificationClient
    {
        private readonly Action _deviceChangeCallback;

        public AudioEndpointNotificationHandler(Action deviceChangeCallback)
        {
            ArgumentNullException.ThrowIfNull(deviceChangeCallback);
            _deviceChangeCallback = deviceChangeCallback;
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) =>
            _deviceChangeCallback();

        public void OnDeviceAdded(string pwstrDeviceId) =>
            _deviceChangeCallback();

        public void OnDeviceRemoved(string deviceId) =>
            _deviceChangeCallback();

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
            {
                _deviceChangeCallback();
            }
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}