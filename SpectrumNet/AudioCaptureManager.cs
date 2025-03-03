#nullable enable
namespace SpectrumNet
{
    public sealed class AudioCaptureManager : IDisposable
    {
        const string LogPrefix = "[AudioCaptureManager] ";
        readonly MainWindow _mainWindow;
        readonly object _lock = new();
        record CaptureState(SpectrumAnalyzer Analyzer, WasapiLoopbackCapture Capture, CancellationTokenSource CTS);
        CaptureState? _state;
        bool _isDisposed;
        readonly MMDeviceEnumerator _deviceEnumerator;
        MMDevice? _currentDevice;
        bool _isReinitializing;
        readonly int _deviceCheckIntervalMs = 500;
        string _lastDeviceId = string.Empty;

        public bool IsRecording { get; private set; }
        public bool IsDeviceAvailable => GetDefaultAudioDevice() != null;
        readonly AudioEndpointNotificationHandler _notificationHandler;

        public AudioCaptureManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
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
                    if (_isDisposed || _isReinitializing || _mainWindow.IsTransitioning)
                        return;

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
                if (IsRecording && !_mainWindow.IsTransitioning)
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

        async Task ReinitializeCaptureAsync()
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

                    await _mainWindow.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            _mainWindow._renderer?.Dispose();
                            _mainWindow._renderer = null;
                            _mainWindow._analyzer?.Dispose();
                            _mainWindow._analyzer = new SpectrumAnalyzer(
                                new FftProcessor { WindowType = _mainWindow.SelectedFftWindowType },
                                new SpectrumConverter(_mainWindow._gainParameters),
                                SynchronizationContext.Current);
                            _mainWindow._analyzer.ScaleType = _mainWindow.SelectedScaleType;
                            if (_mainWindow.RenderElement is { ActualWidth: > 0, ActualHeight: > 0 })
                            {
                                _mainWindow._renderer = new Renderer(
                                    _mainWindow._spectrumStyles ?? new SpectrumBrushes(),
                                    _mainWindow,
                                    _mainWindow._analyzer,
                                    _mainWindow.RenderElement);
                                _mainWindow._renderer.SynchronizeWithMainWindow();
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

        public Task StopCaptureAsync(bool updateUI = true)
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
                    stateToDispose.Capture.StopRecording();

                    if (!_mainWindow.IsTransitioning)
                    {
                        try
                        {
                            stateToDispose.Analyzer.ResetSpectrum();
                        }
                        catch (Exception ex)
                        {
                            SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Error resetting spectrum: {ex.Message}");
                        }
                    }

                    DisposeCaptureState(stateToDispose);
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error disposing capture state: {ex}");
                }
            }

            if (updateUI && !_mainWindow.IsTransitioning)
                UpdateStatus(false);

            return Task.CompletedTask;
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
                SmartLogger.Log(_mainWindow.IsTransitioning ? LogLevel.Debug : LogLevel.Error, LogPrefix,
                    $"Recording stopped with error{(_mainWindow.IsTransitioning ? " during transition" : "")}: {e.Exception}");
                if (!_isDisposed && IsRecording && !_isReinitializing && !_mainWindow.IsTransitioning)
                    Task.Run(ReinitializeCaptureAsync);
                return;
            }
            SmartLogger.Log(LogLevel.Information, LogPrefix, "Recording stopped normally", forceLog: true);
        }

        SpectrumAnalyzer InitializeAnalyzer() =>
            _mainWindow.Dispatcher.Invoke(() =>
            {
                var analyzer = new SpectrumAnalyzer(
                    new FftProcessor { WindowType = _mainWindow.SelectedFftWindowType },
                    new SpectrumConverter(_mainWindow._gainParameters),
                    SynchronizationContext.Current);
                analyzer.ScaleType = _mainWindow.SelectedScaleType;
                if (_mainWindow.RenderElement is { ActualWidth: > 0, ActualHeight: > 0 })
                {
                    _mainWindow._renderer?.Dispose();
                    _mainWindow._renderer = new Renderer(
                        _mainWindow._spectrumStyles ?? new SpectrumBrushes(),
                        _mainWindow,
                        analyzer,
                        _mainWindow.RenderElement);
                    _mainWindow._renderer.SynchronizeWithMainWindow();
                }
                return analyzer;
            });

        void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || _state?.Analyzer == null)
                return;

            var samples = new float[e.BytesRecorded / 4];
            try { Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded); }
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
                    if (!IsRecording || _mainWindow.IsTransitioning)
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
            _mainWindow.Dispatcher.Invoke(() =>
            {
                IsRecording = isRecording;
                _mainWindow.IsRecording = isRecording;
                if (!_mainWindow.IsTransitioning)
                    _mainWindow.StatusText = customStatus ?? (isRecording ? MwConstants.RecordingStatus : MwConstants.ReadyStatus);
                _mainWindow.OnPropertyChanged(nameof(_mainWindow.IsRecording), nameof(_mainWindow.CanStartCapture), nameof(_mainWindow.StatusText));
            });

        private void DisposeCaptureState(CaptureState? state)
        {
            if (state == null)
                return;

            try
            {
                state.Capture.DataAvailable -= OnDataAvailable;
                state.Capture.RecordingStopped -= OnRecordingStopped;

                try { state.Capture.Dispose(); }
                catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error disposing capture: {ex.Message}"); }

                try { state.CTS.Dispose(); }
                catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error disposing CTS: {ex.Message}"); }

                try { state.Analyzer.Dispose(); }
                catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error disposing analyzer: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error in DisposeCaptureState: {ex}");
            }
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