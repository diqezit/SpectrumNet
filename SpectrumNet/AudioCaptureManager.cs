#nullable enable
namespace SpectrumNet
{
    public sealed class AudioCaptureManager : IDisposable
    {
        private const string LogPrefix = "[AudioCaptureManager] ";
        private readonly MainWindow _mainWindow;
        private readonly object _lock = new();
        private record CaptureState(SpectrumAnalyzer Analyzer, WasapiLoopbackCapture Capture, CancellationTokenSource CTS);
        private CaptureState? _state;
        private bool _isDisposed;
        private readonly MMDeviceEnumerator _deviceEnumerator;
        private MMDevice? _currentDevice;
        private bool _isReinitializing;
        private readonly int _deviceCheckIntervalMs = 500;
        private string _lastDeviceId = string.Empty;

        public bool IsRecording { get; private set; }
        public bool IsDeviceAvailable => GetDefaultAudioDevice() != null;

        private readonly AudioEndpointNotificationHandler _notificationHandler;

        public AudioCaptureManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _deviceEnumerator = new MMDeviceEnumerator();
            _notificationHandler = new AudioEndpointNotificationHandler(OnDeviceChanged);
            RegisterDeviceNotifications();
        }

        private void RegisterDeviceNotifications()
        {
            try
            {
                _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationHandler);
                Log.Information($"{LogPrefix}Audio device notification handler registered successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"{LogPrefix}Failed to register endpoint notification callback: {ex}");
            }
        }

        private void OnDeviceChanged()
        {
            try
            {
                lock (_lock)
                {
                    if (_isDisposed || _isReinitializing)
                        return;
                    var device = GetDefaultAudioDevice();
                    if (device == null)
                    {
                        Log.Warning($"{LogPrefix}No default audio device available");
                        if (_state != null && IsRecording)
                        {
                            try { StopCaptureAsync(true).GetAwaiter().GetResult(); }
                            catch (Exception ex) { Log.Error($"{LogPrefix}Error stopping capture during device change: {ex}"); }
                        }
                        UpdateStatus(false, "No audio device");
                        return;
                    }
                    if (device.ID != _lastDeviceId)
                    {
                        Log.Information($"{LogPrefix}Audio device changed from '{_lastDeviceId}' to '{device.ID}'");
                        _lastDeviceId = device.ID;
                        if (IsRecording && !_isReinitializing)
                        {
                            Log.Information($"{LogPrefix}Reinitializing audio capture due to device change");
                            Task.Run(async () =>
                            {
                                try { await ReinitializeCaptureAsync(); }
                                catch (Exception ex)
                                {
                                    Log.Error($"{LogPrefix}Reinitialize task failed: {ex}");
                                    UpdateStatus(false, "Device change error");
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{LogPrefix}Unhandled exception in OnDeviceChanged: {ex}");
                try
                {
                    if (IsRecording)
                    {
                        StopCaptureAsync(true).GetAwaiter().GetResult();
                        UpdateStatus(false, "Device error - restarting");
                    }
                }
                catch { }
            }
        }

        private async Task ReinitializeCaptureAsync()
        {
            try
            {
                _isReinitializing = true;
                Log.Information($"{LogPrefix}Starting capture reinitialization");
                await StopCaptureAsync(true);
                await Task.Delay(500);
                var device = GetDefaultAudioDevice();
                if (device == null)
                {
                    Log.Warning($"{LogPrefix}No default audio device available after reinitialization");
                    UpdateStatus(false, "No audio device");
                    return;
                }
                _lastDeviceId = device.ID;
                if (IsRecording)
                {
                    DisposeState();
                    try
                    {
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
                                if (_mainWindow.RenderElement is { ActualWidth: > 0, ActualHeight: > 0 })
                                {
                                    _mainWindow._renderer = new Renderer(
                                        _mainWindow._spectrumStyles ?? new SpectrumBrushes(),
                                        _mainWindow,
                                        _mainWindow._analyzer,
                                        _mainWindow.RenderElement);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"{LogPrefix}Error during UI component reinitialization: {ex}");
                                throw;
                            }
                        });
                        await StartCaptureAsync();
                        Log.Information($"{LogPrefix}Capture successfully reinitialized");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"{LogPrefix}Error during component reinitialization: {ex}");
                        UpdateStatus(false, "Error reconnecting");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{LogPrefix}Error during capture reinitialization: {ex}");
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
                    Log.Warning($"{LogPrefix}Cannot start capture: No audio device available");
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
            lock (_lock)
            {
                if (_state != null)
                {
                    _state.CTS.Cancel();
                    _state.Capture.StopRecording();
                    DisposeState();
                }
            }
            if (updateUI)
                UpdateStatus(false);
            return Task.CompletedTask;
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
                Log.Error($"{LogPrefix}Error getting default audio device: {ex.Message}");
                return null;
            }
        }

        private void InitializeCapture()
        {
            DisposeState();
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
                Log.Information($"{LogPrefix}Audio capture started on device: {device.FriendlyName}");
            }
            catch (Exception ex)
            {
                Log.Error($"{LogPrefix}Failed to initialize capture: {ex}");
                UpdateStatus(false, "Capture error");
                throw;
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Log.Error($"{LogPrefix}Recording stopped with error: {e.Exception}");
                if (!_isDisposed && IsRecording && !_isReinitializing)
                    Task.Run(ReinitializeCaptureAsync);
                return;
            }
            Log.Information($"{LogPrefix}Recording stopped normally");
        }

        private SpectrumAnalyzer InitializeAnalyzer()
        {
            return _mainWindow.Dispatcher.Invoke(() =>
            {
                var analyzer = new SpectrumAnalyzer(new FftProcessor(),
                                                     new SpectrumConverter(_mainWindow._gainParameters),
                                                     SynchronizationContext.Current);
                if (_mainWindow.RenderElement is { ActualWidth: > 0, ActualHeight: > 0 })
                {
                    _mainWindow._renderer?.Dispose();
                    _mainWindow._renderer = new Renderer(_mainWindow._spectrumStyles ?? new SpectrumBrushes(),
                                                         _mainWindow, analyzer, _mainWindow.RenderElement);
                }
                return analyzer;
            });
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || _state?.Analyzer == null)
                return;
            var samples = new float[e.BytesRecorded / 4];
            try { Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded); }
            catch (Exception ex)
            {
                Log.Error($"{LogPrefix}Error processing audio data: {ex}");
                return;
            }
            _ = _state.Analyzer.AddSamplesAsync(samples, _state.Capture.WaveFormat.SampleRate);
        }

        private async Task MonitorCaptureAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(_deviceCheckIntervalMs, token);
                    if (!IsRecording) continue;
                    var device = GetDefaultAudioDevice();
                    if (device == null || device.ID != _lastDeviceId)
                        OnDeviceChanged();
                }
            }
            catch (TaskCanceledException)
            {
                Log.Debug($"{LogPrefix}Monitor task canceled");
            }
            catch (Exception ex)
            {
                Log.Error($"{LogPrefix}Error in device monitoring: {ex}");
            }
        }

        private void UpdateStatus(bool isRecording, string? customStatus = null)
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                IsRecording = isRecording;
                _mainWindow.IsRecording = isRecording;
                _mainWindow.StatusText = customStatus ?? (isRecording ? MwConstants.RecordingStatus : MwConstants.ReadyStatus);
                _mainWindow.OnPropertyChanged(nameof(_mainWindow.IsRecording), nameof(_mainWindow.CanStartCapture), nameof(_mainWindow.StatusText));
            });
        }

        private void DisposeState()
        {
            if (_state == null)
                return;
            try
            {
                if (_state.Capture != null)
                {
                    _state.Capture.DataAvailable -= OnDataAvailable;
                    _state.Capture.RecordingStopped -= OnRecordingStopped;
                    try { _state.Capture.Dispose(); }
                    catch (Exception ex) { Log.Error($"{LogPrefix}Error disposing capture: {ex.Message}"); }
                }
                try { _state.CTS.Dispose(); }
                catch (Exception ex) { Log.Error($"{LogPrefix}Error disposing CTS: {ex.Message}"); }
                try { _state.Analyzer.Dispose(); }
                catch (Exception ex) { Log.Error($"{LogPrefix}Error disposing analyzer: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                Log.Error($"{LogPrefix}Error in DisposeState: {ex}");
            }
            finally { _state = null; }
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
                    catch (Exception ex) { Log.Error($"{LogPrefix}Error unregistering device notification: {ex}"); }
                    _currentDevice?.Dispose();
                    _deviceEnumerator.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error($"{LogPrefix}Error during disposal: {ex}");
                }
                _isDisposed = true;
            }
        }

        private class AudioEndpointNotificationHandler : IMMNotificationClient
        {
            private readonly Action _deviceChangeCallback;
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