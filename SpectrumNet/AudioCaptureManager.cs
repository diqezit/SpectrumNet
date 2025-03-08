#nullable enable
namespace SpectrumNet;

public sealed class AudioCaptureManager : IDisposable
{
    #region Constants and Fields
    private const string LogPrefix = "[AudioCaptureManager] ";
    private const string _readyStatus = "Ready";
    private const string _recordingStatus = "Recording...";
    private const int _defaultDeviceCheckIntervalMs = 500;

    private readonly IAudioVisualizationController _controller;
    private readonly object _lock = new();
    private readonly MMDeviceEnumerator _deviceEnumerator;
    private readonly AudioEndpointNotificationHandler _notificationHandler;
    private readonly int _deviceCheckIntervalMs;

    private MMDevice? _currentDevice;
    private string _lastDeviceId = string.Empty;
    private CaptureState? _state;
    private bool _isDisposed;
    private bool _isReinitializing;
    #endregion

    #region Properties
    public bool IsRecording { get; private set; }
    public bool IsDeviceAvailable => GetDefaultAudioDevice() is not null;
    #endregion

    #region Types
    private sealed record CaptureState(
        SpectrumAnalyzer Analyzer,
        WasapiLoopbackCapture Capture,
        CancellationTokenSource CTS);
    #endregion

    #region Constructor
    public AudioCaptureManager(IAudioVisualizationController controller)
    {
        ArgumentNullException.ThrowIfNull(controller, nameof(controller));

        _controller = controller;
        _deviceCheckIntervalMs = _defaultDeviceCheckIntervalMs;
        _deviceEnumerator = new MMDeviceEnumerator()
            ?? throw new InvalidOperationException("Failed to create MMDeviceEnumerator");
        _notificationHandler = new AudioEndpointNotificationHandler(OnDeviceChanged)
            ?? throw new InvalidOperationException("Failed to create notification handler");

        var device = GetDefaultAudioDevice();
        if (device is not null)
        {
            _lastDeviceId = device.ID ?? string.Empty;
            SmartLogger.Log(LogLevel.Information, LogPrefix,
            $"Initial audio device detected: '{_lastDeviceId}'");
        }

        RegisterDeviceNotifications();
    }
    #endregion

    #region Device Management
    private void RegisterDeviceNotifications()
    {
        try
        {
            _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationHandler);
            SmartLogger.Log(LogLevel.Information, LogPrefix,
                "Audio device notification handler registered successfully", forceLog: true);
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix,
                $"Failed to register endpoint notification callback: {ex.Message}\nStackTrace: {ex.StackTrace}");
        }
    }

    private void OnDeviceChanged()
    {
        try
        {
            lock (_lock)
            {
                if (_isDisposed || _isReinitializing || _controller.IsTransitioning) return;

                var device = GetDefaultAudioDevice();
                if (device is null)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "No default audio device available");
                    if (_state is not null && IsRecording)
                        TryStopCapture("Error stopping capture during device change");

                    UpdateStatus(false, "No audio device");
                    return;
                }

                if (device.ID != _lastDeviceId)
                {
                    SmartLogger.Log(LogLevel.Information, LogPrefix,
                        $"Audio device changed from '{_lastDeviceId}' to '{device.ID}'", forceLog: true);

                    _lastDeviceId = device.ID
                        ?? throw new InvalidOperationException("Device ID is null");

                    if (IsRecording && !_isReinitializing)
                    {
                        Task.Run(async () => await ReinitializeCaptureAsync());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix,
                $"Unhandled exception in OnDeviceChanged: {ex.Message}\nStackTrace: {ex.StackTrace}");
        }

        void TryStopCapture(string errorMsg)
        {
            try
            {
                StopCaptureAsync(true).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"{errorMsg}: {ex.Message}");
            }
        }
    }

    private MMDevice? GetDefaultAudioDevice()
    {
        try
        {
            _currentDevice?.Dispose();

            _currentDevice = _deviceEnumerator.GetDefaultAudioEndpoint(
                DataFlow.Render,
                Role.Multimedia
            );

            if (_currentDevice is null)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "No default audio device found");
            }

            return _currentDevice;
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"GetDefaultAudioDevice error: {ex.Message}");
            return null;
        }
    }

    private async Task MonitorCaptureAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(_deviceCheckIntervalMs, token);

                if (!IsRecording || _controller.IsTransitioning)
                    continue;

                var device = GetDefaultAudioDevice();
                if (device is null || device.ID != _lastDeviceId)
                    OnDeviceChanged();
            }
        }
        catch (TaskCanceledException)
        {
            // Expected when token is canceled 
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error in device monitoring: {ex.Message}");
        }
    }
    #endregion

    #region Capture Reinitialization
    public async Task ReinitializeCaptureAsync()
    {
        try
        {
            _isReinitializing = true;
            SmartLogger.Log(LogLevel.Information, LogPrefix,
                "Starting capture reinitialization", forceLog: true);

            await StopCaptureAsync(true);
            await Task.Delay(500);

            var device = GetDefaultAudioDevice();
            if (device is null)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix,
                    "No default audio device available after reinitialization");
                UpdateStatus(false, "No audio device");
                return;
            }

            _lastDeviceId = device.ID
                ?? throw new InvalidOperationException("Device ID is null");

            DisposeCaptureState(_state);
            _state = null;

            var analyzerHashCode = string.Empty;

            await _controller.Dispatcher.InvokeAsync(() =>
            {
                SmartLogger.Log(LogLevel.Debug, LogPrefix,
                    "Reinitializing renderer and analyzer on UI thread");

                if (_controller.Renderer is not null)
                {
                    SmartLogger.Log(LogLevel.Debug, LogPrefix, "Disposing old renderer");
                    _controller.Renderer.Dispose();
                    _controller.Renderer = null;
                }

                if (_controller.Analyzer is not null)
                {
                    SmartLogger.Log(LogLevel.Debug, LogPrefix, "Disposing old analyzer");
                    _controller.Analyzer.Dispose();
                }

                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Creating new analyzer");
                var newAnalyzer = new SpectrumAnalyzer(
                    new FftProcessor { WindowType = _controller.WindowType },
                    new SpectrumConverter(_controller.GainParameters),
                    SynchronizationContext.Current);

                newAnalyzer.ScaleType = _controller.ScaleType;
                _controller.Analyzer = newAnalyzer;

                analyzerHashCode = newAnalyzer.GetHashCode().ToString("X8");
                SmartLogger.Log(LogLevel.Debug, LogPrefix,
                    $"Created analyzer with hash: {analyzerHashCode}");

                bool canvasValid = _controller.SpectrumCanvas is not null &&
                                   _controller.SpectrumCanvas.ActualWidth > 0 &&
                                   _controller.SpectrumCanvas.ActualHeight > 0;

                if (canvasValid)
                {
                    SmartLogger.Log(LogLevel.Debug, LogPrefix,
                        $"Creating new renderer with canvas dimensions: " +
                        $"{_controller.SpectrumCanvas.ActualWidth}x{_controller.SpectrumCanvas.ActualHeight}");
                    CreateRenderer(newAnalyzer);
                }
                else
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix,
                        "Canvas dimensions invalid, scheduling delayed renderer creation");

                    _controller.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        Task.Delay(200).ContinueWith(_ =>
                        {
                            _controller.Dispatcher.Invoke(() =>
                            {
                                if (_controller.SpectrumCanvas is not null &&
                                    _controller.SpectrumCanvas.ActualWidth > 0 &&
                                    _controller.SpectrumCanvas.ActualHeight > 0)
                                {
                                    SmartLogger.Log(LogLevel.Debug, LogPrefix,
                                        $"Delayed renderer creation with canvas: " +
                                        $"{_controller.SpectrumCanvas.ActualWidth}x{_controller.SpectrumCanvas.ActualHeight}");

                                    CreateRenderer(_controller.Analyzer);
                                }
                                else
                                {
                                    SmartLogger.Log(LogLevel.Error, LogPrefix,
                                        "Canvas still invalid after delay, cannot create renderer");
                                }
                            });
                        });
                    }));
                }
            });

            await StartCaptureAsync();
            SmartLogger.Log(LogLevel.Information, LogPrefix,
                $"Capture successfully reinitialized with analyzer {analyzerHashCode}", forceLog: true);
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix,
                $"Error during capture reinitialization: {ex.Message}");
            UpdateStatus(false, "Error reconnecting");
        }
        finally
        {
            _isReinitializing = false;
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Reinitialization completed");
        }

        void CreateRenderer(SpectrumAnalyzer analyzer)
        {
            try
            {
                if (analyzer is null)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix,
                        "Cannot create renderer - analyzer is null");
                    return;
                }

                _controller.Renderer = new Renderer(
                    _controller.SpectrumStyles ?? new SpectrumBrushes(),
                    _controller,
                    analyzer,
                    _controller.SpectrumCanvas);

                SmartLogger.Log(LogLevel.Debug, LogPrefix,
                    $"Renderer created successfully with analyzer {analyzer.GetHashCode():X8}");

                _controller.Renderer.SynchronizeWithController();
                SmartLogger.Log(LogLevel.Information, LogPrefix,
                    "Renderer created and synchronized successfully");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix,
                    $"Failed to create renderer: {ex.Message}");
            }
        }
    }
    #endregion

    #region Capture Control
    public async Task StartCaptureAsync()
    {
        try
        {
            lock (_lock)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(AudioCaptureManager));

                if (IsRecording) return;

                var device = GetDefaultAudioDevice();
                if (device is null)
                {
                    UpdateStatus(false, "No audio device");
                    return;
                }

                DisposeCaptureState(_state);
                _state = null;

                var analyzer = _controller.Analyzer;
                if (analyzer is null)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix,
                        "Controller's analyzer is null, cannot start capture");
                    UpdateStatus(false, "Analyzer error");
                    return;
                }

                SmartLogger.Log(LogLevel.Debug, LogPrefix,
                    $"Using existing analyzer: {analyzer.GetHashCode():X8}");

                var capture = new WasapiLoopbackCapture(device);
                _state = new CaptureState(analyzer, capture, new CancellationTokenSource());

                _state.Capture.DataAvailable += OnDataAvailable;
                _state.Capture.RecordingStopped += OnRecordingStopped;

                _state.Capture.StartRecording();
                UpdateStatus(true);

                SmartLogger.Log(LogLevel.Information, LogPrefix,
                    $"Capture started on device: {device.FriendlyName} with existing analyzer");
            }

            // Запускаем мониторинг вне блокировки
            await MonitorCaptureAsync(_state.CTS.Token);
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"StartCapture error: {ex.Message}");
            UpdateStatus(false, "Start failed");
        }
    }

    public async Task StopCaptureAsync(bool updateUI = true)
    {
        CaptureState? stateToDispose;
        lock (_lock)
        {
            stateToDispose = _state;
            _state = null;
        }

        if (stateToDispose is not null)
        {
            try
            {
                stateToDispose.CTS.Cancel();
                await Task.Run(() =>
                {
                    stateToDispose.Capture.StopRecording();
                    stateToDispose.Capture.Dispose();
                    stateToDispose.Analyzer.SafeReset();
                });
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"StopCapture error: {ex.Message}");
            }
        }

        if (updateUI) UpdateStatus(false);
    }

    private void InitializeCapture()
    {
        DisposeCaptureState(_state);
        _state = null;

        var device = GetDefaultAudioDevice();
        if (device is null)
        {
            UpdateStatus(false, "No audio device");
            return;
        }

        try
        {
            // Используем существующий анализатор из контроллера вместо создания нового
            var analyzer = _controller.Analyzer;
            if (analyzer is null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix,
                    "Controller's analyzer is null, cannot start capture");
                UpdateStatus(false, "Analyzer error");
                return;
            }

            SmartLogger.Log(LogLevel.Debug, LogPrefix,
                $"Using analyzer from controller: {analyzer.GetHashCode():X8}");

            var capture = new WasapiLoopbackCapture(device);
            _state = new CaptureState(analyzer, capture, new CancellationTokenSource());

            _state.Capture.DataAvailable += OnDataAvailable;
            _state.Capture.RecordingStopped += OnRecordingStopped;

            UpdateStatus(true);
            _state.Capture.StartRecording();

            SmartLogger.Log(LogLevel.Information, LogPrefix,
                $"Audio capture started on device: {device.FriendlyName} using existing analyzer",
                forceLog: true);
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix,
                $"Capture initialization failed: {ex.Message}");
            UpdateStatus(false, "Capture init failed");
        }
    }
    #endregion

    #region Event Handlers
    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix,
                $"Recording stopped with error: {e.Exception.Message}");

            if (!_isDisposed && IsRecording && !_isReinitializing && !_controller.IsTransitioning)
                Task.Run(ReinitializeCaptureAsync);

            return;
        }

        SmartLogger.Log(LogLevel.Information, LogPrefix,
            "Recording stopped normally", forceLog: true);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _state?.Analyzer is null)
            return;

        var waveFormat = _state.Capture.WaveFormat
            ?? throw new InvalidOperationException("WaveFormat is null");

        int channels = waveFormat.Channels;
        int frameCount = e.BytesRecorded / 4 / channels;
        float[] monoSamples = new float[frameCount];

        try
        {
            Span<byte> bufferSpan = new(e.Buffer, 0, e.BytesRecorded);
            for (int frame = 0; frame < frameCount; frame++)
            {
                float sum = 0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    int sampleIndex = (frame * channels + ch) * 4;
                    sum += BitConverter.ToSingle(bufferSpan.Slice(sampleIndex, 4).ToArray(), 0);
                }
                monoSamples[frame] = sum / channels;
            }

            SmartLogger.Log(LogLevel.Debug, LogPrefix,
                $"Adding {monoSamples.Length} samples to analyzer {_state.Analyzer.GetHashCode():X8}");

            _ = _state.Analyzer.AddSamplesAsync(monoSamples, waveFormat.SampleRate);
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix,
                $"Error processing audio data: {ex.Message}");
        }
    }
    #endregion

    #region Helper Methods
    private SpectrumAnalyzer InitializeAnalyzer()
    {
        return _controller.Dispatcher.Invoke(() =>
        {
            var currentContext = SynchronizationContext.Current;
            SmartLogger.Log(LogLevel.Debug, LogPrefix,
                $"Initializing analyzer with SynchronizationContext: " +
                $"{(currentContext is not null ? "Present" : "Null")}");

            var analyzer = new SpectrumAnalyzer(
                new FftProcessor { WindowType = _controller.WindowType },
                new SpectrumConverter(_controller.GainParameters),
                currentContext
            );

            analyzer.SetWindowType(_controller.WindowType);
            analyzer.SetScaleType(_controller.ScaleType);

            // Подписываемся на событие для отладки
            analyzer.SpectralDataReady += (s, e) =>
                SmartLogger.Log(LogLevel.Debug, LogPrefix,
                    $"SpectralDataReady fired, data length: {e.Data.Spectrum.Length}");

            if (_controller.Renderer is not null)
            {
                _controller.Renderer.UpdateRenderDimensions(
                    (int)_controller.SpectrumCanvas.ActualWidth,
                    (int)_controller.SpectrumCanvas.ActualHeight
                );
            }

            return analyzer;
        });
    }

    private void UpdateStatus(bool isRecording, string? customStatus = null)
    {
        _controller.Dispatcher.Invoke(() =>
        {
            IsRecording = isRecording;
            _controller.IsRecording = isRecording;
            _controller.StatusText = customStatus ?? (isRecording ? _recordingStatus : _readyStatus);

            _controller.OnPropertyChanged(
                nameof(IAudioVisualizationController.IsRecording),
                nameof(IAudioVisualizationController.CanStartCapture),
                nameof(IAudioVisualizationController.StatusText)
            );
        });
    }

    private void DisposeCaptureState(CaptureState? state)
    {
        if (state is null) return;

        state.Capture.DataAvailable -= OnDataAvailable;
        state.Capture.RecordingStopped -= OnRecordingStopped;
        state.Capture?.Dispose();
        state.CTS?.Dispose();
        state.Analyzer?.Dispose();
    }
    #endregion

    #region IDisposable Implementation
    public void Dispose()
    {
        if (_isDisposed) return;

        lock (_lock)
        {
            if (_isDisposed) return;

            StopCaptureAsync().GetAwaiter().GetResult();
            _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationHandler);
            _currentDevice?.Dispose();
            _deviceEnumerator.Dispose();
            _isDisposed = true;
        }
    }
    #endregion

    #region Nested Types
    private sealed class AudioEndpointNotificationHandler : IMMNotificationClient
    {
        private readonly Action _deviceChangeCallback;

        public AudioEndpointNotificationHandler(Action deviceChangeCallback) =>
            _deviceChangeCallback = deviceChangeCallback
                ?? throw new ArgumentNullException(nameof(deviceChangeCallback));

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) =>
            _deviceChangeCallback();

        public void OnDeviceAdded(string pwstrDeviceId) =>
            _deviceChangeCallback();

        public void OnDeviceRemoved(string deviceId) =>
            _deviceChangeCallback();

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
                _deviceChangeCallback();
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
    #endregion
}