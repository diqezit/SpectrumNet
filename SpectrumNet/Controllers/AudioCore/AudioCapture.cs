#nullable enable

namespace SpectrumNet.Controllers.AudioCore;

public sealed class AudioCapture : AsyncDisposableBase
{
    private const string LogPrefix = nameof(AudioCapture);

    private const int 
        DefaultDeviceCheckIntervalMs = 500,
        OPERATION_TIMEOUT_MS = 3000,
        STATE_LOCK_TIMEOUT_MS = 3000;

    private readonly object _stateLock = new();
    private readonly IMainController _controller;
    private readonly MMDeviceEnumerator _deviceEnumerator;
    private readonly AudioEndpointNotificationHandler _notificationHandler;
    private readonly int _deviceCheckIntervalMs;
    private readonly IRendererFactory _rendererFactory;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    private MMDevice? _currentDevice;
    private string _lastDeviceId = string.Empty;
    private CaptureState? _state;

    private volatile bool 
        _isReinitializing,
        _isCaptureStopping;

    public bool IsRecording { get; private set; }

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
        _deviceEnumerator = new MMDeviceEnumerator();
        _deviceCheckIntervalMs = deviceCheckIntervalMs;
        _notificationHandler = new AudioEndpointNotificationHandler(OnDeviceChanged);

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

    private void RegisterDeviceNotificationsImpl() =>
        _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationHandler);

    private void OnDeviceChanged() =>
        Safe(
            OnDeviceChangedCore,
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Unhandled exception in OnDeviceChanged"
            }
        );

    private void OnDeviceChangedCore()
    {
        if (_isDisposed ||
            _isReinitializing ||
            _controller.IsTransitioning ||
            _isCaptureStopping)
            return;

        var device = GetDefaultAudioDevice();
        if (device is null)
        {
            Log(LogLevel.Warning,
                LogPrefix,
                "No default audio device available");
            StopCaptureIfNeeded();
            return;
        }

        if (device.ID != _lastDeviceId)
        {
            Log(LogLevel.Information,
                LogPrefix,
                $"Audio device changed from '{_lastDeviceId}' to '{device.ID}'");
            _lastDeviceId = device.ID;

            if (IsRecording && !_isReinitializing)
                _ = Task.Run(ReinitializeCaptureAsync);
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
        _controller.Dispatcher.InvokeAsync(InitializeUIComponentsCore).Task;

    private void InitializeUIComponentsCore()
    {
        if (_isDisposed) return;

        _controller.Analyzer = CreateAnalyzer();

        if (_controller.SpectrumCanvas is SkiaSharp.Views.WPF.SKElement
            {
                ActualWidth: > 0,
                ActualHeight: > 0
            } canvas)
            CreateAndSetRenderer(canvas);
    }

    private void CreateAndSetRenderer(SkiaSharp.Views.WPF.SKElement canvas)
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

    private SpectrumAnalyzer CreateAnalyzer()
    {
        var fftProcessor = new FftProcessor { WindowType = _controller.WindowType };
        var spectrumConverter = new SpectrumConverter(_controller.GainParameters);

        var analyzer = new SpectrumAnalyzer(
            fftProcessor,
            spectrumConverter,
            SynchronizationContext.Current
        );

        analyzer.ScaleType = _controller.ScaleType;
        analyzer.UpdateSettings(
            _controller.WindowType,
            _controller.ScaleType
        );

        return analyzer;
    }

    private SpectrumAnalyzer InitializeAnalyzer() =>
        _controller.Dispatcher.Invoke(() =>
        {
            var analyzer = CreateAnalyzer();
            if (_controller.SpectrumCanvas is SkiaSharp.Views.WPF.SKElement
                {
                    ActualWidth: > 0,
                    ActualHeight: > 0
                } canvas)
                CreateAndSetRenderer(canvas);

            return analyzer;
        });

    public async Task StartCaptureAsync()
    {
        ThrowIfDisposed();

        if (IsCaptureInProgress())
        {
            Log(LogLevel.Warning,
                LogPrefix,
                "Cannot start capture: operation in progress or already recording");
            return;
        }

        if (!await _operationLock.WaitAsync(STATE_LOCK_TIMEOUT_MS))
        {
            Log(LogLevel.Warning,
                LogPrefix,
                "Could not acquire operation lock for starting capture");
            return;
        }

        try
        {
            if (_isDisposed ||
                IsRecording ||
                _isReinitializing ||
                _controller.IsTransitioning)
            {
                Log(LogLevel.Warning,
                    LogPrefix,
                    "Cannot start capture after lock: state changed");
                return;
            }

            await InitializeAndStartCapture();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private bool IsCaptureInProgress() =>
        _isCaptureStopping ||
        IsRecording ||
        _isReinitializing ||
        _controller.IsTransitioning;

    private Task InitializeAndStartCapture()
    {
        var device = GetDefaultAudioDevice();
        if (device is null)
        {
            Log(LogLevel.Error,
                LogPrefix,
                "No audio device available");
            return Task.CompletedTask;
        }

        _lastDeviceId = device.ID;

        try
        {
            InitializeCapture(device);

            if (_state is null)
            {
                Log(LogLevel.Error,
                    LogPrefix,
                    "Failed to initialize capture state");
                return Task.CompletedTask;
            }

            var token = _state.CTS.Token;
            _ = Task.Run(() => MonitorCaptureAsync(token));

            Log(LogLevel.Debug,
                LogPrefix,
                "Audio capture started successfully");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error initializing capture: {ex.Message}");

            if (_state != null)
            {
                DisposeCaptureState(_state);
                _state = null;
            }

            UpdateStatus(false);
            throw;
        }
    }

    private void InitializeCapture(MMDevice device)
    {
        if (_isDisposed) return;

        lock (_stateLock)
        {
            DisposeCaptureState(_state);
            _state = null;
        }

        var capture = new WasapiLoopbackCapture(device);
        var analyzer = InitializeAnalyzer();

        var newState = new CaptureState(
            analyzer,
            capture,
            new CancellationTokenSource()
        );

        newState.Capture.DataAvailable += OnDataAvailable;
        newState.Capture.RecordingStopped += OnRecordingStopped;

        lock (_stateLock)
            _state = newState;

        UpdateStatus(true);
        newState.Capture.StartRecording();

        Log(LogLevel.Information,
            LogPrefix,
            $"Audio capture started on device: {device.FriendlyName}");
    }

    public async Task StopCaptureAsync(bool updateUI = true)
    {
        ThrowIfDisposed();

        if (_isCaptureStopping)
        {
            Log(LogLevel.Warning,
                LogPrefix,
                "Stop capture already in progress");
            return;
        }

        try
        {
            _isCaptureStopping = true;

            if (!IsRecording && _state is null)
            {
                Log(LogLevel.Debug,
                    LogPrefix,
                    "Stop capture ignored: Not recording");
                return;
            }

            CaptureState? stateToDispose = AcquireCaptureStateForDisposal();
            if (stateToDispose is not null)
                await StopRecordingAndWait(stateToDispose);

            if (updateUI)
                UpdateStatus(false);
        }
        finally
        {
            _isCaptureStopping = false;
        }
    }

    private CaptureState? AcquireCaptureStateForDisposal()
    {
        lock (_stateLock)
        {
            if (_state is null)
            {
                Log(LogLevel.Warning,
                    LogPrefix,
                    "No capture state to dispose");
                return null;
            }

            var stateToDispose = _state;
            stateToDispose.CTS.Cancel();
            _state = null;
            return stateToDispose;
        }
    }

    private async Task StopRecordingAndWait(CaptureState stateToDispose)
    {
        bool stopSuccessful = false;

        try
        {
            using var timeoutCts = new CancellationTokenSource(OPERATION_TIMEOUT_MS);

            try
            {
                await TryStopRecording(
                    stateToDispose,
                    timeoutCts.Token
                );
                stopSuccessful = true;
            }
            catch (TimeoutException)
            {
                Log(LogLevel.Warning,
                    LogPrefix,
                    "Stop capture operation timed out");
            }
            catch (OperationCanceledException)
            {
                Log(LogLevel.Warning,
                    LogPrefix,
                    "Stop capture operation was canceled");
            }

            if ((stopSuccessful || timeoutCts.IsCancellationRequested) &&
                !_controller.IsTransitioning)
                TryResetAnalyzer(stateToDispose);
        }
        finally
        {
            DisposeCaptureState(stateToDispose);
        }
    }

    private static async Task TryStopRecording(
        CaptureState stateToDispose,
        CancellationToken token) =>
        await Task.Run(() =>
        {
            try
            {
                stateToDispose.Capture.StopRecording();
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error,
                    LogPrefix,
                    $"Error stopping recording: {ex.Message}");
            }
        }, token).WaitAsync(token);

    private static void TryResetAnalyzer(CaptureState stateToDispose)
    {
        try
        {
            stateToDispose.Analyzer.SafeReset();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error resetting analyzer: {ex.Message}");
        }
    }

    public Task ToggleCaptureAsync() =>
        IsRecording ? StopCaptureAsync() : StartCaptureAsync();

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
                $"Error getting default audio device: {ex.Message}");
            return null;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e) =>
        Safe(
            () => ProcessDataAvailable(e),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error processing audio data"
            }
        );

    private void ProcessDataAvailable(WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        var (analyzer, capture) = GetAnalyzerAndCapture();
        if (analyzer is null ||
            analyzer.IsDisposed ||
            capture is null ||
            capture.WaveFormat is null)
            return;

        var waveFormat = capture.WaveFormat;
        int channels = waveFormat.Channels;
        int frameCount = e.BytesRecorded / 4 / channels;

        if (frameCount <= 0) return;

        ProcessAudioSamples(
            e,
            frameCount,
            channels,
            waveFormat,
            analyzer
        );
    }

    private (SpectrumAnalyzer? analyzer, WasapiLoopbackCapture? capture) GetAnalyzerAndCapture()
    {
        lock (_stateLock)
        {
            if (_state is null) return (null, null);
            return (_state.Analyzer, _state.Capture);
        }
    }

    private static void ProcessAudioSamples(
        WaveInEventArgs e,
        int frameCount,
        int channels,
        WaveFormat waveFormat,
        SpectrumAnalyzer analyzer)
    {
        float[] monoSamples = new float[frameCount];
        ConvertToMono(
            e.Buffer,
            channels,
            frameCount,
            monoSamples,
            e.BytesRecorded
        );

        try
        {
            _ = analyzer.AddSamplesAsync(
                monoSamples,
                waveFormat.SampleRate
            );
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error adding samples to analyzer: {ex.Message}");
        }
    }

    private static void ConvertToMono(
        byte[] buffer,
        int channels,
        int frameCount,
        float[] monoSamples,
        int bytesRecorded)
    {
        if (buffer == null ||
            monoSamples == null ||
            channels <= 0 ||
            frameCount <= 0 ||
            bytesRecorded <= 0)
            return;

        var span = buffer.AsSpan(0, bytesRecorded);

        for (int frame = 0; frame < frameCount; frame++)
        {
            float sum = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
                int idx = (frame * channels + ch) * 4;
                if (idx + 4 <= span.Length)
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
                $"Recording stopped with error: {e.Exception.Message}");

            if (!_isDisposed &&
                IsRecording &&
                !_isReinitializing &&
                !_controller.IsTransitioning &&
                !_isCaptureStopping)
                _ = Task.Run(ReinitializeCaptureAsync);

            return;
        }

        Log(LogLevel.Information,
            LogPrefix,
            "Recording stopped normally");
    }

    private Task MonitorCaptureAsync(CancellationToken token) =>
        SafeAsync(
            async () => await MonitorDevicesLoop(token),
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

    private async Task MonitorDevicesLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(
                    _deviceCheckIntervalMs,
                    token
                );

                if (!IsRecording ||
                    _controller.IsTransitioning ||
                    _isCaptureStopping)
                    continue;

                var device = GetDefaultAudioDevice();
                if (device is null || device.ID != _lastDeviceId)
                    OnDeviceChanged();
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
    }

    private void UpdateStatus(bool isRecording) =>
        _controller.Dispatcher.Invoke(() =>
        {
            IsRecording = isRecording;
            _controller.IsRecording = isRecording;
            _controller.OnPropertyChanged(
                nameof(_controller.IsRecording),
                nameof(_controller.CanStartCapture)
            );
        });

    private void DisposeCaptureState(CaptureState? state)
    {
        if (state is null) return;

        UnsubscribeFromEvents(state);
        DisposeStateComponents(state);
    }

    private void UnsubscribeFromEvents(CaptureState state) =>
        Safe(() =>
        {
            if (state.Capture != null)
            {
                state.Capture.DataAvailable -= OnDataAvailable;
                state.Capture.RecordingStopped -= OnRecordingStopped;
            }
        },
        new ErrorHandlingOptions
        {
            Source = LogPrefix,
            ErrorMessage = "Error unsubscribing events"
        });

    private void DisposeStateComponents(CaptureState state)
    {
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

        DisposeAnalyzer(state);
    }

    private static void DisposeAnalyzer(CaptureState state) =>
        Safe(() =>
        {
            if (state.Analyzer is IAsyncDisposable asyncDisposable)
            {
                var task = asyncDisposable.DisposeAsync();
                _ = task.ConfigureAwait(false);
            }
            else if (state.Analyzer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        },
        new ErrorHandlingOptions
        {
            Source = LogPrefix,
            ErrorMessage = "Error disposing analyzer"
        });

    protected override void DisposeManaged()
    {
        TryStopCaptureOnDispose();
        UnregisterNotifications();
        DisposeManagedResources();
    }

    private void TryStopCaptureOnDispose()
    {
        try
        {
            if (IsRecording)
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(
                        TimeSpan.FromSeconds(5)
                    );
                    var task = StopCaptureAsync(updateUI: false);
                    if (!task.Wait(5000))
                        Log(LogLevel.Warning,
                            LogPrefix,
                            "Timeout waiting for StopCaptureAsync during dispose");
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Error,
                        LogPrefix,
                        $"Error stopping capture during dispose: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error stopping capture during dispose: {ex.Message}");
        }
    }

    private void UnregisterNotifications()
    {
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
    }

    private void DisposeManagedResources()
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

        SafeDispose(
            _deviceEnumerator,
            nameof(_deviceEnumerator),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error disposing device enumerator"
            }
        );

        _operationLock.Dispose();
    }

    protected override async ValueTask DisposeAsyncManagedResources()
    {
        await TryStopCaptureAsyncOnDispose();
        UnregisterNotifications();
        DisposeManagedResources();
    }

    private async Task TryStopCaptureAsyncOnDispose()
    {
        try
        {
            if (IsRecording)
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(
                        TimeSpan.FromSeconds(5)
                    );
                    await StopCaptureAsync(updateUI: false)
                        .WaitAsync(
                            TimeSpan.FromSeconds(5),
                            timeoutCts.Token
                        );
                }
                catch (OperationCanceledException)
                {
                    Log(LogLevel.Warning,
                        LogPrefix,
                        "Timeout waiting for StopCaptureAsync during async dispose");
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Error,
                        LogPrefix,
                        $"Error stopping capture during async dispose: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error stopping capture during async dispose: {ex.Message}");
        }
    }

    private class AudioEndpointNotificationHandler : IMMNotificationClient
    {
        private readonly Action _deviceChangeCallback;

        public AudioEndpointNotificationHandler(Action deviceChangeCallback) =>
            _deviceChangeCallback = deviceChangeCallback ??
                throw new ArgumentNullException(nameof(deviceChangeCallback));

        public void OnDeviceStateChanged(
            string deviceId,
            DeviceState newState) =>
            _deviceChangeCallback();

        public void OnDeviceAdded(string pwstrDeviceId) =>
            _deviceChangeCallback();

        public void OnDeviceRemoved(string deviceId) =>
            _deviceChangeCallback();

        public void OnDefaultDeviceChanged(
            DataFlow flow,
            Role role,
            string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
                _deviceChangeCallback();
        }

        public void OnPropertyValueChanged(
            string pwstrDeviceId,
            PropertyKey key)
        { }
    }
}