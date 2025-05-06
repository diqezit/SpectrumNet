#nullable enable

namespace SpectrumNet.Controllers.AudioCore;

public sealed class AudioCapture : AsyncDisposableBase
{
    private const string LogPrefix = "AudioCapture";
    private const int DefaultDeviceCheckIntervalMs = 500;

    private readonly object
        _lock = new(),
        _stateLock = new();

    private readonly IMainController _controller;
    private readonly MMDeviceEnumerator _deviceEnumerator;
    private readonly AudioEndpointNotificationHandler _notificationHandler;
    private readonly int _deviceCheckIntervalMs;

    private MMDevice? _currentDevice;
    private string _lastDeviceId = string.Empty;
    private CaptureState? _state;
    private bool _isReinitializing;

    public bool IsRecording { get; private set; }
    public bool IsDeviceAvailable => GetDefaultAudioDevice() != null;

    private record CaptureState(
        SpectrumAnalyzer Analyzer,
        WasapiLoopbackCapture Capture,
        CancellationTokenSource CTS
    );

    public AudioCapture(
        IMainController controller,
        int deviceCheckIntervalMs = DefaultDeviceCheckIntervalMs)
    {
        ArgumentNullException.ThrowIfNull(controller);

        _controller = controller;
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
            "Audio device notification handler registered successfully",
            forceLog: true
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
            if (_isDisposed || _isReinitializing || _controller.IsTransitioning)
            {
                return;
            }

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
                    $"Audio device changed from '{_lastDeviceId}' to '{device.ID}'",
                    forceLog: true
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
        _controller.IsTransitioning = true;
        try
        {
            await StopCaptureAsync(true);
            await Task.Delay(500);

            var device = GetDefaultAudioDevice();
            if (device is null) return;

            _lastDeviceId = device.ID;
            DisposeCaptureState(_state);
            _state = null;

            await InitializeUIComponentsAsync();
            await StartCaptureAsync();
        }
        finally
        {
            _controller.IsTransitioning = false;
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
                canvas
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
                    canvas
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
            "AudioCapture",
            "StartCaptureAsync called");
#endif

        ThrowIfDisposed();

        if (_isReinitializing || _controller.IsTransitioning || IsRecording) return;

        var device = GetDefaultAudioDevice();
        if (device is null) return;

        CancellationToken token;
        lock (_lock)
        {
            if (_isDisposed) return;

            _lastDeviceId = device.ID;
            InitializeCaptureCore();
            token = _state!.CTS.Token;
        }

        await MonitorCaptureAsync(token);

#if DEBUG
        Log(LogLevel.Debug,
            "AudioCapture",
            "Audio capture started successfully");
#endif
    }

    private void InitializeCaptureCore()
    {
        DisposeCaptureState(_state);
        _state = null;

        var device = GetDefaultAudioDevice();
        if (device is null) return;

        var capture = new WasapiLoopbackCapture(device)
                      ?? throw new InvalidOperationException(
                          "Failed to initialize capture device"
                      );

        var analyzer = InitializeAnalyzer();

        lock (_stateLock)
        {
            _state = new CaptureState(
                analyzer,
                capture,
                new CancellationTokenSource()
            );
        }

        _state.Capture.DataAvailable += OnDataAvailable;
        _state.Capture.RecordingStopped += OnRecordingStopped;

        UpdateStatus(true);
        _state.Capture.StartRecording();

        Log(LogLevel.Information,
            LogPrefix,
            $"Audio capture started on device: {device.FriendlyName}",
            forceLog: true
        );
    }

    public async Task StopCaptureAsync(bool updateUI = true)
    {
        ThrowIfDisposed();

        if (!IsRecording && _state is null) return;

        CaptureState? stateToDispose;
        lock (_stateLock)
        {
            _state?.CTS.Cancel();
            stateToDispose = _state;
            _state = null;
        }

        if (stateToDispose is not null)
        {
            using var timeoutCts = new CancellationTokenSource(FromSeconds(3));

            await SafeAsync(
                async () =>
                {
                    var stopTask = Task.Run(() => StopCaptureCore(stateToDispose));

                    if (await Task.WhenAny(
                        stopTask,
                        Task.Delay(2000, timeoutCts.Token)) != stopTask)
                    {
                        Log(LogLevel.Warning,
                            LogPrefix,
                            "StopCapture operation timed out");
                    }
                },
                new ErrorHandlingOptions
                {
                    Source = LogPrefix,
                    ErrorMessage = "Stop capture error"
                }
            );
        }

        if (updateUI) UpdateStatus(false);
    }

    private void StopCaptureCore(CaptureState state)
    {
        try
        {
            state.Capture.StopRecording();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error stopping recording: {ex.Message}");
        }

        if (!_controller.IsTransitioning)
        {
            try
            {
                state.Analyzer.SafeReset();
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error,
                    LogPrefix,
                    $"Error resetting analyzer: {ex.Message}");
            }
        }

        DisposeCaptureState(state);
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
        lock (_stateLock)
        {
            analyzer = _state?.Analyzer;
            if (analyzer is null || analyzer.IsDisposed) return;
        }

        var waveFormat = _state!.Capture.WaveFormat
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
            if (!_isDisposed && IsRecording && !_isReinitializing && !_controller
                .IsTransitioning)
            {
                _ = Task.Run(ReinitializeCaptureAsync);
            }
            return;
        }

        Log(LogLevel.Information,
            LogPrefix,
            "Recording stopped normally",
            forceLog: true
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
                        if (!IsRecording || _controller.IsTransitioning)
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
                        "Device monitoring task cancelled normally",
                        forceLog: false);
                }
                catch (OperationCanceledException)
                {
                    Log(LogLevel.Information,
                        LogPrefix,
                        "Device monitoring operation cancelled normally",
                        forceLog: false);
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

    protected override void DisposeManaged()
    {
        try
        {
            StopCaptureAsync().GetAwaiter().GetResult();
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
    }

    protected override async ValueTask DisposeAsyncManagedResources()
    {
        try
        {
            await StopCaptureAsync();
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