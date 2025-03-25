#nullable enable

using static SpectrumNet.SmartLogger;

namespace SpectrumNet
{
    public sealed class AudioCapture : IDisposable
    {
        private const string LogPrefix = "AudioCapture";
        private const int DefaultDeviceCheckIntervalMs = 500;

        private readonly IAudioVisualizationController _controller;
        private readonly object _lock = new();
        private readonly MMDeviceEnumerator _deviceEnumerator;
        private readonly AudioEndpointNotificationHandler _notificationHandler;
        private readonly int _deviceCheckIntervalMs;
        private readonly object _stateLock = new();

        private MMDevice? _currentDevice;
        private string _lastDeviceId = string.Empty;
        private CaptureState? _state;
        private bool _isDisposed, _isReinitializing;

        public bool IsRecording { get; private set; }
        public bool IsDeviceAvailable => GetDefaultAudioDevice() != null;

        private record CaptureState(
            SpectrumAnalyzer Analyzer,
            WasapiLoopbackCapture Capture,
            CancellationTokenSource CTS
        );

        public AudioCapture(IAudioVisualizationController controller, int deviceCheckIntervalMs = DefaultDeviceCheckIntervalMs)
        {
            ArgumentNullException.ThrowIfNull(controller);

            _controller = controller;
            _deviceEnumerator = new MMDeviceEnumerator() ??
                throw new InvalidOperationException("Failed to create MMDeviceEnumerator");
            _notificationHandler = new AudioEndpointNotificationHandler(OnDeviceChanged);
            _deviceCheckIntervalMs = deviceCheckIntervalMs;

#if DEBUG
            Log(LogLevel.Debug, LogPrefix, "Creating AudioCapture instance");
#endif
            RegisterDeviceNotifications();
        }

        public SpectrumAnalyzer? GetAnalyzer() => _state?.Analyzer;

        private void RegisterDeviceNotifications() => Safe(
            () => {
                _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationHandler);
#if DEBUG
                Log(LogLevel.Debug, LogPrefix, "Registered device notification callback");
#endif
                Log(LogLevel.Information, LogPrefix, "Audio device notification handler registered successfully", forceLog: true);
            },
            new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Failed to register endpoint notification callback" }
        );

        private void OnDeviceChanged() => Safe(
            () => {
                lock (_lock)
                {
                    if (ShouldSkipDeviceChange())
                        return;

                    var device = GetDefaultAudioDevice();
                    if (device is null)
                    {
                        Log(LogLevel.Warning, LogPrefix, "No default audio device available");
                        StopCaptureIfNeeded();
                        return;
                    }

                    if (device.ID != _lastDeviceId)
                    {
                        LogDeviceChange(_lastDeviceId, device.ID);
                        _lastDeviceId = device.ID ?? throw new InvalidOperationException("Device ID is null");

                        if (IsRecording && !_isReinitializing)
                            Task.Run(ReinitializeCaptureAsync);
                    }
                }
            },
            new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Unhandled exception in OnDeviceChanged" }
        );

        private bool ShouldSkipDeviceChange() => _isDisposed || _isReinitializing || _controller.IsTransitioning;

        private void LogDeviceChange(string oldDeviceId, string? newDeviceId) =>
            Log(LogLevel.Information, LogPrefix, $"Audio device changed from '{oldDeviceId}' to '{newDeviceId}'", forceLog: true);

        private void StopCaptureIfNeeded()
        {
            if (_state is not null && IsRecording)
            {
                Safe(
                    () => StopCaptureAsync(true).GetAwaiter().GetResult(),
                    new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error stopping capture during device change" }
                );
            }
        }

        public async Task ReinitializeCaptureAsync() => await SafeAsync(
            async () =>
            {
#if DEBUG
                Log(LogLevel.Debug, LogPrefix, "Starting capture reinitialization");
#endif
                _controller.IsTransitioning = true;
                try
                {
                    await StopCaptureAsync(true);
                    await Task.Delay(500);

                    var device = GetDefaultAudioDevice();
                    if (device is null) return;

                    _lastDeviceId = device.ID ?? throw new InvalidOperationException("Device ID is null");
                    DisposeCaptureState(_state);
                    _state = null;

                    await InitializeUIComponentsAsync();
                    await StartCaptureAsync();
                }
                finally
                {
                    _controller.IsTransitioning = false;
#if DEBUG
                    Log(LogLevel.Debug, LogPrefix, "Completed capture reinitialization");
#endif
                }
            },
            new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Ошибка переинициализации" }
        );

        private async Task InitializeUIComponentsAsync() => await _controller.Dispatcher.InvokeAsync(() => {
#if DEBUG
            Log(LogLevel.Debug, LogPrefix, "Initializing UI components");
#endif
            _controller.Analyzer = CreateAnalyzer();

            if (_controller.SpectrumCanvas is { ActualWidth: > 0, ActualHeight: > 0 } canvas)
            {
                _controller.Renderer = CreateRenderer(_controller.Analyzer);
                _controller.SynchronizeVisualization();
            }
        });

        private SpectrumAnalyzer CreateAnalyzer()
        {
#if DEBUG
            Log(LogLevel.Debug, LogPrefix, string.Format("Creating analyzer with WindowType: {0}, ScaleType: {1}",
                _controller.WindowType, _controller.ScaleType));
#endif
            var analyzer = new SpectrumAnalyzer(
                new FftProcessor { WindowType = _controller.WindowType },
                new SpectrumConverter(_controller.GainParameters),
                SynchronizationContext.Current
            );

            analyzer.ScaleType = _controller.ScaleType;
            analyzer.UpdateSettings(_controller.WindowType, _controller.ScaleType);

            return analyzer;
        }

        private Renderer CreateRenderer(SpectrumAnalyzer analyzer) => new(
            _controller.SpectrumStyles,
            _controller,
            analyzer,
            _controller.SpectrumCanvas
        );

        public async Task StartCaptureAsync()
        {
            if (ShouldPreventCapture("start"))
                return;

            if (IsRecording)
            {
                Log(LogLevel.Information, LogPrefix, "Capture already started");
                return;
            }

            var device = GetDefaultAudioDevice();
            if (device is null)
            {
                Log(LogLevel.Warning, LogPrefix, "Cannot start capture: No audio device available");
                return;
            }

            CancellationToken token;
            lock (_lock)
            {
                if (_isDisposed)
                {
                    Log(LogLevel.Warning, LogPrefix, "Cannot start capture: Manager was disposed");
                    return;
                }

                _lastDeviceId = device.ID ?? throw new InvalidOperationException("Device ID is null");

#if DEBUG
                Log(LogLevel.Debug, LogPrefix, string.Format("Initializing capture for device: {0}", device.FriendlyName));
#endif
                InitializeCapture();
                token = _state!.CTS.Token;
            }

            await MonitorCaptureAsync(token);
        }

        private bool ShouldPreventCapture(string operation)
        {
            if (_isDisposed)
            {
                Log(LogLevel.Warning, LogPrefix, $"Cannot {operation} capture: Manager is disposed");
                return true;
            }

            if (_isReinitializing || _controller.IsTransitioning)
            {
                Log(LogLevel.Warning, LogPrefix, $"Cannot {operation} capture: Manager is reinitializing or transitioning");
                return true;
            }

            return false;
        }

        public async Task StopCaptureAsync(bool updateUI = true)
        {
            if (_isDisposed)
            {
                Log(LogLevel.Warning, LogPrefix, "Cannot stop capture: Manager is disposed");
                return;
            }

            if (!IsRecording && _state is null)
            {
                Log(LogLevel.Information, LogPrefix, "Capture already stopped");
                return;
            }

#if DEBUG
            Log(LogLevel.Debug, LogPrefix, "Stopping capture, updateUI: {0}", updateUI);
#endif

            CaptureState? stateToDispose = null;

            lock (_stateLock)
            {
                if (_state is not null)
                {
                    _state.CTS.Cancel();
                    stateToDispose = _state;
                    _state = null;
                }
            }

            if (stateToDispose is not null)
            {
                await SafeAsync(
                    async () => {
                        await Task.Run(() => {
                            stateToDispose.Capture.StopRecording();

                            if (!_controller.IsTransitioning)
                                stateToDispose.Analyzer.SafeReset();

                            DisposeCaptureState(stateToDispose);
                        });
                    },
                    new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Stop capture error" }
                );
            }

            if (updateUI)
                UpdateStatus(false);
        }

        public async Task ToggleCaptureAsync()
        {
            if (_isDisposed)
            {
                Log(LogLevel.Warning, LogPrefix, "Cannot toggle capture: Manager is disposed");
                return;
            }

#if DEBUG
            Log(LogLevel.Debug, LogPrefix, string.Format("Toggling capture, current state: {0}", IsRecording ? "Recording" : "Stopped"));
#endif
            await (IsRecording ? StopCaptureAsync() : StartCaptureAsync());
        }

        private MMDevice? GetDefaultAudioDevice()
        {
            try
            {
                SafeDispose(_currentDevice, "CurrentDevice",
                    new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error disposing current device" }
                );

                _currentDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
#if DEBUG
                if (_currentDevice is not null)
                {
                    Log(LogLevel.Debug, LogPrefix, string.Format("Got default audio device: {0}", _currentDevice.FriendlyName));
                }
#endif
                return _currentDevice;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, LogPrefix, $"Error getting default audio device: {ex.Message}");
                return null;
            }
        }

        private void InitializeCapture()
        {
            DisposeCaptureState(_state);
            _state = null;

            var device = GetDefaultAudioDevice();
            if (device is null)
                return;

            var capture = new WasapiLoopbackCapture(device)
                ?? throw new InvalidOperationException("Failed to initialize capture device");

            var analyzer = InitializeAnalyzer();

            lock (_stateLock)
            {
                _state = new CaptureState(analyzer, capture, new CancellationTokenSource());
            }

            _state.Capture.DataAvailable += OnDataAvailable;
            _state.Capture.RecordingStopped += OnRecordingStopped;

            UpdateStatus(true);
            _state.Capture.StartRecording();

            Log(LogLevel.Information, LogPrefix,
                $"Audio capture started on device: {device.FriendlyName}", forceLog: true);
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception is not null)
            {
                Log(LogLevel.Error, LogPrefix, $"Recording stopped with error: {e.Exception.Message}");

                if (!_isDisposed && IsRecording && !_isReinitializing && !_controller.IsTransitioning)
                    Task.Run(ReinitializeCaptureAsync);

                return;
            }

            Log(LogLevel.Information, LogPrefix, "Recording stopped normally", forceLog: true);
        }

        private SpectrumAnalyzer InitializeAnalyzer() => _controller.Dispatcher.Invoke(() => {
            var analyzer = CreateAnalyzer();

            if (_controller.SpectrumCanvas is { ActualWidth: > 0, ActualHeight: > 0 } canvas)
            {
                _controller.Renderer = CreateRenderer(analyzer);
                _controller.SynchronizeVisualization();
            }

            return analyzer;
        });

        private void OnDataAvailable(object? sender, WaveInEventArgs e) => Safe(
            () => {
                if (e.BytesRecorded <= 0)
                    return;

                SpectrumAnalyzer? analyzer;
                lock (_stateLock)
                {
                    analyzer = _state?.Analyzer;
                    if (analyzer is null || analyzer.IsDisposed)
                        return;
                }

                var waveFormat = _state!.Capture.WaveFormat
                    ?? throw new InvalidOperationException("WaveFormat is null");

                int channels = waveFormat.Channels;
                int frameCount = e.BytesRecorded / 4 / channels;
                float[] monoSamples = new float[frameCount];

#if DEBUG
                if (frameCount > 0 && frameCount % 1000 == 0)
                {
                    Log(LogLevel.Debug, LogPrefix, string.Format("Processing audio data: {0} bytes, {1} frames, {2} channels",
                        e.BytesRecorded, frameCount, channels));
                }
#endif

                ProcessAudioData(e.Buffer, e.BytesRecorded, channels, frameCount, monoSamples);
                _ = analyzer.AddSamplesAsync(monoSamples, waveFormat.SampleRate);
            },
            new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error processing audio data" }
        );

        private static void ProcessAudioData(byte[] buffer, int bytesRecorded, int channels, int frameCount, float[] monoSamples)
        {
            Span<byte> bufferSpan = buffer.AsSpan(0, bytesRecorded);

            for (int frame = 0; frame < frameCount; frame++)
            {
                float sum = 0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    int sampleIndex = (frame * channels + ch) * 4;
                    sum += BitConverter.ToSingle(bufferSpan.Slice(sampleIndex, 4));
                }
                monoSamples[frame] = sum / channels;
            }
        }

        private async Task MonitorCaptureAsync(CancellationToken token) => await SafeAsync(
            async () => {
                try
                {
#if DEBUG
                    Log(LogLevel.Debug, LogPrefix, string.Format("Starting device monitoring with interval: {0}ms", _deviceCheckIntervalMs));
#endif
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
                    // Expected exception on cancellation
#if DEBUG
                    Log(LogLevel.Debug, LogPrefix, "Device monitoring canceled");
#endif
                }
            },
            new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error in device monitoring" }
        );

        private void UpdateStatus(bool isRecording) => _controller.Dispatcher.Invoke(() => {
#if DEBUG
            Log(LogLevel.Debug, LogPrefix, string.Format("Updating recording status to: {0}", isRecording ? "Recording" : "Stopped"));
#endif
            IsRecording = isRecording;
            _controller.IsRecording = isRecording;
            _controller.OnPropertyChanged(
                nameof(_controller.IsRecording),
                nameof(_controller.CanStartCapture)
            );
        });

        private void DisposeCaptureState(CaptureState? state)
        {
            if (state is null)
                return;

#if DEBUG
            Log(LogLevel.Debug, LogPrefix, "Disposing capture state");
#endif
            state.Capture.DataAvailable -= OnDataAvailable;
            state.Capture.RecordingStopped -= OnRecordingStopped;

            SafeDispose(state.Capture, "Capture",
                new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error disposing capture" }
            );

            SafeDispose(state.CTS, "CTS",
                new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error disposing CTS" }
            );

            SafeDispose(state.Analyzer, "Analyzer",
                new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error disposing analyzer" }
            );
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            lock (_lock)
            {
                if (_isDisposed)
                    return;

#if DEBUG
                Log(LogLevel.Debug, LogPrefix, "Disposing AudioCapture");
#endif
                DisposeResources();
                _isDisposed = true;
            }
        }

        private void DisposeResources()
        {
            Safe(
                () => StopCaptureAsync().GetAwaiter().GetResult(),
                new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error stopping capture during dispose" }
            );

            Safe(
                () => _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationHandler),
                new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error unregistering notification callback" }
            );

            SafeDispose(_currentDevice, "CurrentDevice",
                new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error disposing current device" }
            );

            SafeDispose(_deviceEnumerator, "DeviceEnumerator",
                new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error disposing device enumerator" }
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

            public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _deviceChangeCallback();
            public void OnDeviceAdded(string pwstrDeviceId) => _deviceChangeCallback();
            public void OnDeviceRemoved(string deviceId) => _deviceChangeCallback();

            public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
            {
                if (flow == DataFlow.Render && role == Role.Multimedia)
                    _deviceChangeCallback();
            }

            public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
        }
    }
}