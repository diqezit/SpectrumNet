#nullable enable

namespace SpectrumNet
{
    public sealed class AudioCaptureManager : IDisposable
    {
        const string LogPrefix = "[AudioCaptureManager] ";
        readonly IAudioVisualizationController _controller;
        readonly object _lock = new();
        record CaptureState(SpectrumAnalyzer Analyzer, WasapiLoopbackCapture Capture, CancellationTokenSource CTS);
        CaptureState? _state;
        bool _isDisposed;
        readonly MMDeviceEnumerator _deviceEnumerator;
        MMDevice? _currentDevice;
        bool _isReinitializing;
        readonly int _deviceCheckIntervalMs = 500;
        string _lastDeviceId = string.Empty;
        readonly AudioEndpointNotificationHandler _notificationHandler;

        public bool IsRecording { get; private set; }
        public bool IsDeviceAvailable => GetDefaultAudioDevice() != null;

        public AudioCaptureManager(IAudioVisualizationController controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _deviceEnumerator = new MMDeviceEnumerator();
            _notificationHandler = new AudioEndpointNotificationHandler(OnDeviceChanged);
            RegisterDeviceNotifications();
        }

        void RegisterDeviceNotifications()
        {
            try
            {
                _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationHandler);
                SmartLogger.Log(LogLevel.Information, LogPrefix, "Audio device notification handler registered successfully", forceLog: true);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to register endpoint notification callback: {ex}");
            }
        }

        void OnDeviceChanged()
        {
            try
            {
                lock (_lock)
                {
                    if (_isDisposed || _isReinitializing || _controller.IsTransitioning)
                    {
                        if (_controller.IsTransitioning)
                        {
                            Task.Run(async () =>
                            {
                                while (_controller.IsTransitioning)
                                    await Task.Delay(100);
                                OnDeviceChanged();
                            });
                        }
                        return;
                    }

                    var device = GetDefaultAudioDevice();
                    if (device == null)
                    {
                        SmartLogger.Log(LogLevel.Warning, LogPrefix, "No default audio device available");
                        if (_state != null && IsRecording)
                            TryStopCapture("Error stopping capture during device change");
                        UpdateStatus(false, "No audio device");
                        return;
                    }

                    if (device.ID != _lastDeviceId)
                    {
                        SmartLogger.Log(LogLevel.Information, LogPrefix, $"Audio device changed from '{_lastDeviceId}' to '{device.ID}'", forceLog: true);
                        _lastDeviceId = device.ID;
                        if (IsRecording && !_isReinitializing)
                        {
                            SmartLogger.Log(LogLevel.Information, LogPrefix, "Reinitializing audio capture due to device change", forceLog: true);
                            Task.Run(async () =>
                            {
                                try { await ReinitializeCaptureAsync(); }
                                catch (Exception ex)
                                {
                                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Reinitialize task failed: {ex}");
                                    UpdateStatus(false, "Device change error");
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Unhandled exception in OnDeviceChanged: {ex}");
                if (IsRecording && !_controller.IsTransitioning)
                {
                    TryStopCapture("Device error - restarting");
                    UpdateStatus(false, "Device error - restarting");
                }
            }

            void TryStopCapture(string errorMsg)
            {
                try { StopCaptureAsync(true).GetAwaiter().GetResult(); }
                catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error stopping capture during device change: {ex}"); }
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
                _lastDeviceId = device.ID;
                if (IsRecording)
                {
                    DisposeCaptureState(_state);
                    _state = null;
                    await _controller.Dispatcher.InvokeAsync(() =>
                    {
                        try
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
                        }
                        catch (Exception ex)
                        {
                            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error during UI component reinitialization: {ex}");
                            throw;
                        }
                    });
                    await StartCaptureAsync();
                    SmartLogger.Log(LogLevel.Information, LogPrefix, "Capture successfully reinitialized", forceLog: true);
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error during capture reinitialization: {ex}");
                UpdateStatus(false, "Error reconnecting");
            }
            finally { _isReinitializing = false; }
        }

        public async Task StartCaptureAsync()
        {
            CancellationToken token;
            lock (_lock)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(AudioCaptureManager));
                var device = GetDefaultAudioDevice();
                if (device == null)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "Cannot start capture: No audio device available");
                    UpdateStatus(false, "No audio device");
                    return;
                }
                _lastDeviceId = device.ID;
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

                        if (!_controller.IsTransitioning)
                        {
                            try
                            {
                                stateToDispose.Analyzer.ResetSpectrum();
                            }
                            catch (Exception ex)
                            {
                                SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Spectrum reset error: {ex}");
                            }
                        }
                        DisposeCaptureState(stateToDispose);
                    });
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Stop capture error: {ex}");
                }
            }

            if (updateUI)
                UpdateStatus(false);
        }

        MMDevice? GetDefaultAudioDevice()
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

        void InitializeCapture()
        {
            DisposeCaptureState(_state);
            _state = null;

            try
            {
                var device = GetDefaultAudioDevice();
                if (device == null)
                {
                    UpdateStatus(false, "No audio device");
                    return;
                }
                var capture = new WasapiLoopbackCapture(device);
                var analyzer = InitializeAnalyzer();
                _state = new CaptureState(analyzer, capture, new CancellationTokenSource());
                _state.Capture.DataAvailable += OnDataAvailable;
                _state.Capture.RecordingStopped += OnRecordingStopped;
                UpdateStatus(true);
                _state.Capture.StartRecording();
                SmartLogger.Log(LogLevel.Information, LogPrefix, $"Audio capture started on device: {device.FriendlyName}", forceLog: true);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to initialize capture: {ex}");
                UpdateStatus(false, "Capture error");
                throw;
            }
        }

        void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                SmartLogger.Log(_controller.IsTransitioning ? LogLevel.Debug : LogLevel.Error, LogPrefix,
                    $"Recording stopped with error{(_controller.IsTransitioning ? " during transition" : "")}: {e.Exception}");
                if (!_isDisposed && IsRecording && !_isReinitializing && !_controller.IsTransitioning)
                    Task.Run(ReinitializeCaptureAsync);
                return;
            }
            SmartLogger.Log(LogLevel.Information, LogPrefix, "Recording stopped normally", forceLog: true);
        }

        SpectrumAnalyzer InitializeAnalyzer() =>
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
                    _controller.Renderer.ShouldShowPlaceholder = !_controller.IsRecording;
                    _controller.Renderer.RequestRender();
                }
                return analyzer;
            });

        void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || _state?.Analyzer == null)
                return;

            var samples = new float[e.BytesRecorded / 4];
            try
            {
                Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing audio data: {ex}");
                return;
            }
            _ = _state.Analyzer.AddSamplesAsync(samples, _state.Capture.WaveFormat.SampleRate);
        }

        async Task MonitorCaptureAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(_deviceCheckIntervalMs, token);
                    if (!IsRecording || _controller.IsTransitioning)
                        continue;
                    var device = GetDefaultAudioDevice();
                    if (device == null || device.ID != _lastDeviceId)
                        OnDeviceChanged();
                }
            }
            catch (TaskCanceledException)
            {
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Monitor task canceled");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error in device monitoring: {ex}");
            }
        }

        void UpdateStatus(bool isRecording, string? customStatus = null) =>
            _controller.Dispatcher.Invoke(() =>
            {
                IsRecording = isRecording;
                _controller.IsRecording = isRecording;
                if (!_controller.IsTransitioning)
                    _controller.StatusText = customStatus ?? (isRecording ? MwConstants.RecordingStatus : MwConstants.ReadyStatus);
                _controller.OnPropertyChanged(nameof(_controller.IsRecording), nameof(_controller.CanStartCapture), nameof(_controller.StatusText));
            });

        void DisposeCaptureState(CaptureState? state)
        {
            if (state == null)
                return;
            try
            {
                state.Capture.DataAvailable -= OnDataAvailable;
                state.Capture.RecordingStopped -= OnRecordingStopped;
                TryDispose(state.Capture, "capture");
                TryDispose(state.CTS, "CTS");
                TryDispose(state.Analyzer, "analyzer");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error in DisposeCaptureState: {ex}");
            }
        }

        void TryDispose(IDisposable? disposable, string name)
        {
            try { disposable?.Dispose(); }
            catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error disposing {name}: {ex.Message}"); }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            lock (_lock)
            {
                if (_isDisposed)
                    return;
                try
                {
                    StopCaptureAsync().GetAwaiter().GetResult();
                    try { _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationHandler); }
                    catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error unregistering device notification: {ex}"); }
                    _currentDevice?.Dispose();
                    _deviceEnumerator.Dispose();
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error during disposal: {ex}");
                }
                _isDisposed = true;
            }
        }

        class AudioEndpointNotificationHandler : IMMNotificationClient
        {
            readonly Action _deviceChangeCallback;
            public AudioEndpointNotificationHandler(Action deviceChangeCallback) => _deviceChangeCallback = deviceChangeCallback;
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