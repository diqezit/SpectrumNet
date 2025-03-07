#nullable enable
namespace SpectrumNet
{
    public sealed class AudioCaptureManager : IDisposable
    {
        private const string LogPrefix = "[AudioCaptureManager] ";
        private const string _readyStatus = "Ready";
        private const string _recordingStatus = "Recording...";

        private readonly IAudioVisualizationController _controller;
        private readonly object _lock = new();
        private readonly MMDeviceEnumerator _deviceEnumerator;
        private MMDevice? _currentDevice;
        private readonly AudioEndpointNotificationHandler _notificationHandler;
        private readonly int _deviceCheckIntervalMs = 500;
        private string _lastDeviceId = string.Empty;
        private CaptureState? _state;
        private bool _isDisposed;
        private bool _isReinitializing;

        public bool IsRecording { get; private set; }
        public bool IsDeviceAvailable => GetDefaultAudioDevice() != null;

        private record CaptureState(
            SpectrumAnalyzer Analyzer,
            WasapiLoopbackCapture Capture,
            CancellationTokenSource CTS);

        public AudioCaptureManager(IAudioVisualizationController controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _deviceEnumerator = new MMDeviceEnumerator() ??
                                  throw new InvalidOperationException("Failed to create MMDeviceEnumerator");
            _notificationHandler = new AudioEndpointNotificationHandler(OnDeviceChanged) ??
                                       throw new InvalidOperationException("Failed to create notification handler");
            RegisterDeviceNotifications();
        }

        private void RegisterDeviceNotifications()
        {
            try
            {
                _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationHandler);
                SmartLogger.Log(LogLevel.Information, LogPrefix, "Audio device notification handler registered successfully", forceLog: true);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to register endpoint notification callback: {ex.Message}\nStackTrace: {ex.StackTrace}");
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
                    if (device == null)
                    {
                        SmartLogger.Log(LogLevel.Warning, LogPrefix, "No default audio device available");
                        if (_state != null && IsRecording) TryStopCapture("Error stopping capture during device change");
                        UpdateStatus(false, "No audio device");
                        return;
                    }

                    if (device.ID != _lastDeviceId)
                    {
                        SmartLogger.Log(LogLevel.Information, LogPrefix, $"Audio device changed from '{_lastDeviceId}' to '{device.ID}'", forceLog: true);
                        _lastDeviceId = device.ID ?? throw new InvalidOperationException("Device ID is null");
                        if (IsRecording && !_isReinitializing)
                        {
                            Task.Run(async () => await ReinitializeCaptureAsync());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Unhandled exception in OnDeviceChanged: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }

            void TryStopCapture(string errorMsg)
            {
                try { StopCaptureAsync(true).GetAwaiter().GetResult(); }
                catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"{errorMsg}: {ex.Message}"); }
            }
        }

        public async Task ReinitializeCaptureAsync()
        {
            try
            {
                _isReinitializing = true;
                SmartLogger.Log(LogLevel.Information, LogPrefix, "Starting capture reinitialization", forceLog: true);
                await StopCaptureAsync(true);
                await Task.Delay(500);

                var device = GetDefaultAudioDevice();
                if (device == null)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "No default audio device available after reinitialization");
                    UpdateStatus(false, "No audio device");
                    return;
                }

                _lastDeviceId = device.ID ?? throw new InvalidOperationException("Device ID is null");
                DisposeCaptureState(_state);
                _state = null;

                await _controller.Dispatcher.InvokeAsync(() =>
                {
                    _controller.Renderer?.Dispose();
                    _controller.Renderer = null;
                    _controller.Analyzer?.Dispose();
                    _controller.Analyzer = new SpectrumAnalyzer(
                        new FftProcessor { WindowType = _controller.WindowType },
                        new SpectrumConverter(_controller.GainParameters),
                        SynchronizationContext.Current);
                    _controller.Analyzer.ScaleType = _controller.ScaleType;

                    if (_controller.SpectrumCanvas is { ActualWidth: > 0, ActualHeight: > 0 })
                    {
                        _controller.Renderer = new Renderer(
                            _controller.SpectrumStyles ?? new SpectrumBrushes(),
                            _controller,
                            _controller.Analyzer,
                            _controller.SpectrumCanvas);
                        _controller.Renderer.SynchronizeWithController();
                    }
                });

                await StartCaptureAsync();
                SmartLogger.Log(LogLevel.Information, LogPrefix, "Capture successfully reinitialized", forceLog: true);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error during capture reinitialization: {ex.Message}");
                UpdateStatus(false, "Error reconnecting");
            }
            finally { _isReinitializing = false; }
        }

        public async Task StartCaptureAsync()
        {
            CancellationToken token;
            lock (_lock)
            {
                if (_isDisposed) throw new ObjectDisposedException(nameof(AudioCaptureManager));
                var device = GetDefaultAudioDevice();
                if (device == null)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "Cannot start capture: No audio device available");
                    UpdateStatus(false, "No audio device");
                    return;
                }

                _lastDeviceId = device.ID ?? throw new InvalidOperationException("Device ID is null");
                InitializeCapture();
                token = _state!.CTS.Token;
            }
            await MonitorCaptureAsync(token);
        }

        public async Task StopCaptureAsync(bool updateUI = true)
        {
            CaptureState? stateToDispose = null;
            lock (_lock)
            {
                if (_state != null)
                {
                    _state.CTS.Cancel();
                    stateToDispose = _state;
                    _state = null;
                }
            }

            if (stateToDispose != null)
            {
                try
                {
                    await Task.Run(() =>
                    {
                        stateToDispose.Capture.StopRecording();
                        if (!_controller.IsTransitioning) stateToDispose.Analyzer.SafeReset();
                        DisposeCaptureState(stateToDispose);
                    });
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Stop capture error: {ex.Message}");
                }
            }

            if (updateUI) UpdateStatus(false);
        }

        private MMDevice? GetDefaultAudioDevice()
        {
            try
            {
                _currentDevice?.Dispose();
                _currentDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return _currentDevice;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error getting default audio device: {ex.Message}");
                return null;
            }
        }

        private void InitializeCapture()
        {
            DisposeCaptureState(_state);
            _state = null;

            var device = GetDefaultAudioDevice();
            if (device == null)
            {
                UpdateStatus(false, "No audio device");
                return;
            }

            var capture = new WasapiLoopbackCapture(device) ??
                          throw new InvalidOperationException("Failed to initialize capture device");
            var analyzer = InitializeAnalyzer();
            _state = new CaptureState(analyzer, capture, new CancellationTokenSource());
            _state.Capture.DataAvailable += OnDataAvailable;
            _state.Capture.RecordingStopped += OnRecordingStopped;
            UpdateStatus(true);
            _state.Capture.StartRecording();
            SmartLogger.Log(LogLevel.Information, LogPrefix, $"Audio capture started on device: {device.FriendlyName}", forceLog: true);
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Recording stopped with error: {e.Exception.Message}");
                if (!_isDisposed && IsRecording && !_isReinitializing && !_controller.IsTransitioning) Task.Run(ReinitializeCaptureAsync);
                return;
            }
            SmartLogger.Log(LogLevel.Information, LogPrefix, "Recording stopped normally", forceLog: true);
        }

        private SpectrumAnalyzer InitializeAnalyzer() =>
            _controller.Dispatcher.Invoke(() =>
            {
                var analyzer = new SpectrumAnalyzer(
                    new FftProcessor { WindowType = _controller.WindowType },
                    new SpectrumConverter(_controller.GainParameters),
                    SynchronizationContext.Current);
                analyzer.UpdateSettings(_controller.WindowType, _controller.ScaleType);

                if (_controller.SpectrumCanvas is { ActualWidth: > 0, ActualHeight: > 0 })
                {
                    _controller.Renderer?.Dispose();
                    _controller.Renderer = new Renderer(
                        _controller.SpectrumStyles ?? new SpectrumBrushes(),
                        _controller,
                        analyzer,
                        _controller.SpectrumCanvas);
                    _controller.Renderer.SynchronizeWithController();
                }
                return analyzer;
            });

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || _state?.Analyzer == null) return;
            var waveFormat = _state.Capture.WaveFormat ?? throw new InvalidOperationException("WaveFormat is null");
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
                _ = _state.Analyzer.AddSamplesAsync(monoSamples, waveFormat.SampleRate);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing audio data: {ex.Message}");
            }
        }

        private async Task MonitorCaptureAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(_deviceCheckIntervalMs, token);
                    if (!IsRecording || _controller.IsTransitioning) continue;
                    var device = GetDefaultAudioDevice();
                    if (device == null || device.ID != _lastDeviceId) OnDeviceChanged();
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error in device monitoring: {ex.Message}");
            }
        }

        private void UpdateStatus(bool isRecording, string? customStatus = null) =>
            _controller.Dispatcher.Invoke(() =>
            {
                IsRecording = isRecording;
                _controller.IsRecording = isRecording;
                _controller.StatusText = customStatus ?? (isRecording ? _recordingStatus : _readyStatus);
                _controller.OnPropertyChanged(
                    nameof(_controller.IsRecording),
                    nameof(_controller.CanStartCapture),
                    nameof(_controller.StatusText));
            });

        private void DisposeCaptureState(CaptureState? state)
        {
            if (state == null) return;
            state.Capture.DataAvailable -= OnDataAvailable;
            state.Capture.RecordingStopped -= OnRecordingStopped;
            state.Capture?.Dispose();
            state.CTS?.Dispose();
            state.Analyzer?.Dispose();
        }

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

        private class AudioEndpointNotificationHandler : IMMNotificationClient
        {
            private readonly Action _deviceChangeCallback;
            public AudioEndpointNotificationHandler(Action deviceChangeCallback) =>
                _deviceChangeCallback = deviceChangeCallback ?? throw new ArgumentNullException(nameof(deviceChangeCallback));
            public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _deviceChangeCallback();
            public void OnDeviceAdded(string pwstrDeviceId) => _deviceChangeCallback();
            public void OnDeviceRemoved(string deviceId) => _deviceChangeCallback();
            public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
            {
                if (flow == DataFlow.Render && role == Role.Multimedia) _deviceChangeCallback();
            }
            public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
        }
    }
}