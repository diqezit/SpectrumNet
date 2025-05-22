// Controllers/AudioCore/CaptureService.cs
#nullable enable

namespace SpectrumNet.SN.Sound;

public sealed class CaptureService : AsyncDisposableBase, ICaptureService
{
    private const string LogPrefix = nameof(CaptureService);

    private const int
        OPERATION_TIMEOUT_MS = 3000,
        STATE_LOCK_TIMEOUT_MS = 3000,
        DEVICE_CHECK_INTERVAL_MS = 500;

    private readonly object _stateLock = new();
    private readonly IMainController _controller;
    private readonly IAudioDeviceManager _deviceManager;
    private readonly IRendererFactory _rendererFactory;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly ISmartLogger _logger = Instance;

    private CaptureState? _state;
    private bool _isCaptureStopping;
    private bool _isReinitializing;

    private record CaptureState(
        SpectrumAnalyzer Analyzer,
        WasapiLoopbackCapture Capture,
        CancellationTokenSource CTS
    );

    public bool IsRecording { get; private set; }
    public bool IsInitializing => _isReinitializing;

    public CaptureService(
        IMainController controller,
        IAudioDeviceManager deviceManager,
        IRendererFactory rendererFactory)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
        _rendererFactory = rendererFactory ?? throw new ArgumentNullException(nameof(rendererFactory));

        InitializeDeviceMonitoring();
    }

    private void InitializeDeviceMonitoring()
    {
        _deviceManager.RegisterDeviceNotifications();
        _deviceManager.DeviceChanged += OnDeviceChanged;
    }

    public SpectrumAnalyzer? GetAnalyzer() => _state?.Analyzer;

    private void OnDeviceChanged() =>
        _logger.Safe(HandleDeviceChanged, LogPrefix, "Unhandled exception in OnDeviceChanged");

    private void HandleDeviceChanged()
    {
        if (ShouldSkipDeviceChange())
            return;

        var device = _deviceManager.GetDefaultAudioDevice();
        if (device is null)
        {
            _logger.Log(LogLevel.Warning, LogPrefix, "No default audio device available");
            StopCaptureIfNeeded();
            return;
        }

        if (IsRecording && !_isReinitializing)
            _ = Task.Run(ReinitializeCaptureAsync);
    }

    private bool ShouldSkipDeviceChange() =>
        _isDisposed || _isReinitializing || _controller.IsTransitioning || _isCaptureStopping;

    private void StopCaptureIfNeeded() =>
        _logger.Safe(() => StopCaptureAsync(true).GetAwaiter().GetResult(),
            LogPrefix, "Error stopping capture during device change");

    public async Task ReinitializeCaptureAsync()
    {
        if (_isReinitializing)
        {
            _logger.Log(LogLevel.Warning, LogPrefix, "Reinitialization already in progress");
            return;
        }

        try
        {
            await PrepareForReinitialization();
            await ReinitializeCapture();
        }
        finally
        {
            CompleteReinitialization();
        }
    }

    private async Task PrepareForReinitialization()
    {
        _isReinitializing = true;
        _controller.IsTransitioning = true;
        await StopCaptureAsync(true);
        await Task.Delay(500);
    }

    private async Task ReinitializeCapture()
    {
        var device = _deviceManager.GetDefaultAudioDevice();
        if (device is null) return;

        lock (_stateLock)
        {
            DisposeCaptureState(_state);
            _state = null;
        }

        await InitializeUIComponentsAsync();
        await StartCaptureAsync();
    }

    private void CompleteReinitialization()
    {
        _controller.IsTransitioning = false;
        _isReinitializing = false;
    }

    private Task InitializeUIComponentsAsync() =>
        _controller.Dispatcher.InvokeAsync(InitializeUIComponentsCore).Task;

    private void InitializeUIComponentsCore()
    {
        if (_isDisposed) return;

        _controller.Analyzer = CreateAnalyzer();

        if (_controller.SpectrumCanvas is SKElement canvas &&
            canvas.ActualWidth > 0 && canvas.ActualHeight > 0)
        {
            CreateAndSetRenderer(canvas);
        }
    }

    private void CreateAndSetRenderer(SKElement canvas)
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

        ConfigureAnalyzer(analyzer);
        return analyzer;
    }

    private void ConfigureAnalyzer(SpectrumAnalyzer analyzer)
    {
        analyzer.ScaleType = _controller.ScaleType;
        analyzer.UpdateSettings(
            _controller.WindowType,
            _controller.ScaleType
        );
    }

    private SpectrumAnalyzer InitializeAnalyzer() =>
        _controller.Dispatcher.Invoke(() =>
        {
            var fftProcessor = new FftProcessor { WindowType = _controller.WindowType };
            var spectrumConverter = new SpectrumConverter(SettingsProvider.Instance.GainParameters);

            var analyzer = new SpectrumAnalyzer(
                fftProcessor,
                spectrumConverter,
                SynchronizationContext.Current
            );

            analyzer.ScaleType = _controller.ScaleType;
            analyzer.UpdateSettings(_controller.WindowType, _controller.ScaleType);

            if (_controller.SpectrumCanvas is SKElement canvas &&
                canvas.ActualWidth > 0 && canvas.ActualHeight > 0)
            {
                CreateAndSetRenderer(canvas);
            }

            return analyzer;
        });

    public async Task StartCaptureAsync()
    {
        ThrowIfDisposed();

        if (IsCaptureInProgress())
        {
            _logger.Log(LogLevel.Warning, LogPrefix, "Cannot start capture: operation in progress");
            return;
        }

        if (!await AcquireOperationLock())
            return;

        try
        {
            if (ShouldAbortStartCapture())
                return;

            await _logger.SafeAsync(async () => await InitializeAndStartCaptureAsync(),
                LogPrefix, "Error initializing and starting capture");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private bool IsCaptureInProgress() =>
        _isCaptureStopping || IsRecording || _isReinitializing || _controller.IsTransitioning;

    private async Task<bool> AcquireOperationLock()
    {
        if (!await _operationLock.WaitAsync(STATE_LOCK_TIMEOUT_MS))
        {
            _logger.Log(LogLevel.Warning, LogPrefix, "Could not acquire operation lock for starting capture");
            return false;
        }
        return true;
    }

    private bool ShouldAbortStartCapture() =>
        _isDisposed || IsRecording || _isReinitializing || _controller.IsTransitioning;

    private async Task InitializeAndStartCaptureAsync()
    {
        var device = _deviceManager.GetDefaultAudioDevice();
        if (device is null)
        {
            _logger.Log(LogLevel.Error, LogPrefix, "No audio device available");
            return;
        }

        try
        {
            InitializeCapture(device);

            if (_state is null)
            {
                _logger.Log(LogLevel.Error, LogPrefix, "Failed to initialize capture state");
                return;
            }

            await StartDeviceMonitoringAsync(_state.CTS.Token);
            _logger.Log(LogLevel.Debug, LogPrefix, "Audio capture started successfully");
        }
        catch (Exception ex)
        {
            HandleCaptureInitializationError(ex);
            throw;
        }
    }

    private async Task StartDeviceMonitoringAsync(CancellationToken token) =>
        await Task.Run(() => MonitorCaptureAsync(token), token);

    private void HandleCaptureInitializationError(Exception ex)
    {
        _logger.Log(LogLevel.Error, LogPrefix, $"Error initializing capture: {ex.Message}");

        if (_state != null)
        {
            DisposeCaptureState(_state);
            _state = null;
        }

        UpdateStatus(false);
    }

    private void InitializeCapture(MMDevice device)
    {
        if (_isDisposed) return;

        ClearExistingCaptureState();
        var newState = CreateCaptureState(device);
        RegisterEventHandlers(newState);

        lock (_stateLock)
            _state = newState;

        UpdateStatus(true);
        newState.Capture.StartRecording();

        _logger.Log(LogLevel.Information, LogPrefix, $"Audio capture started on device: {device.FriendlyName}");
    }

    private void ClearExistingCaptureState()
    {
        lock (_stateLock)
        {
            DisposeCaptureState(_state);
            _state = null;
        }
    }

    private CaptureState CreateCaptureState(MMDevice device)
    {
        var capture = new WasapiLoopbackCapture(device);
        var analyzer = InitializeAnalyzer();

        return new CaptureState(
            analyzer,
            capture,
            new CancellationTokenSource()
        );
    }

    private void RegisterEventHandlers(CaptureState state)
    {
        state.Capture.DataAvailable += OnDataAvailable;
        state.Capture.RecordingStopped += OnRecordingStopped;
    }

    public async Task StopCaptureAsync(bool force = false)
    {
        ThrowIfDisposed();

        if (_isCaptureStopping)
        {
            _logger.Log(LogLevel.Warning, LogPrefix, "Stop capture already in progress");
            return;
        }

        try
        {
            _isCaptureStopping = true;

            if (!IsRecording && _state is null)
            {
                _logger.Log(LogLevel.Debug, LogPrefix, "Stop capture ignored: Not recording");
                return;
            }

            await _logger.SafeAsync(async () => await StopCaptureCore(force),
                LogPrefix, "Error in stop capture core operation");
        }
        finally
        {
            _isCaptureStopping = false;
        }
    }

    private async Task StopCaptureCore(bool force)
    {
        CaptureState? stateToDispose = AcquireCaptureStateForDisposal();
        if (stateToDispose is not null)
            await StopRecordingAndWait(stateToDispose);

        if (!force)
            UpdateStatus(false);
    }

    private CaptureState? AcquireCaptureStateForDisposal()
    {
        lock (_stateLock)
        {
            if (_state is null)
            {
                _logger.Log(LogLevel.Warning, LogPrefix, "No capture state to dispose");
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
        try
        {
            using var timeoutCts = new CancellationTokenSource(OPERATION_TIMEOUT_MS);
            bool stopSuccessful = await AttemptStopRecording(stateToDispose, timeoutCts);
            if ((stopSuccessful || timeoutCts.IsCancellationRequested) &&
                !_controller.IsTransitioning)
            {
                TryResetAnalyzer(stateToDispose);
            }
        }
        finally
        {
            DisposeCaptureState(stateToDispose);
        }
    }

    private static async Task<bool> AttemptStopRecording(CaptureState stateToDispose, CancellationTokenSource timeoutCts)
    {
        try
        {
            await TryStopRecording(stateToDispose, timeoutCts.Token);
            return true;
        }
        catch (TimeoutException)
        {
            Instance.Log(LogLevel.Warning, LogPrefix, "Stop capture operation timed out");
            return false;
        }
        catch (OperationCanceledException)
        {
            Instance.Log(LogLevel.Warning, LogPrefix, "Stop capture operation was canceled");
            return false;
        }
    }

    private static async Task TryStopRecording(CaptureState stateToDispose, CancellationToken token) =>
        await Task.Run(() =>
        {
            try
            {
                stateToDispose.Capture.StopRecording();
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Error, LogPrefix, $"Error stopping recording: {ex.Message}");
            }
        }, token).WaitAsync(token);

    private void TryResetAnalyzer(CaptureState stateToDispose) =>
        _logger.Safe(() => stateToDispose.Analyzer.SafeReset(),
            LogPrefix, "Error resetting analyzer");

    private void OnDataAvailable(object? sender, WaveInEventArgs e) =>
        _logger.Safe(() => ProcessDataAvailable(e), LogPrefix, "Error processing audio data");

    private void ProcessDataAvailable(WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        var (analyzer, capture) = GetAnalyzerAndCapture();
        if (!AreComponentsValid(analyzer, capture))
            return;

        int frameCount = CalculateFrameCount(e.BytesRecorded, capture!.WaveFormat!.Channels);
        if (frameCount <= 0) return;

        ProcessAudioSamples(
            e,
            frameCount,
            capture.WaveFormat.Channels,
            capture.WaveFormat,
            analyzer!
        );
    }

    private static bool AreComponentsValid(SpectrumAnalyzer? analyzer, WasapiLoopbackCapture? capture) =>
        analyzer != null && !analyzer.IsDisposed && capture != null && capture.WaveFormat != null;

    private static int CalculateFrameCount(int bytesRecorded, int channels) =>
        bytesRecorded / 4 / channels;

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
        ConvertToMono(e.Buffer, channels, frameCount, monoSamples, e.BytesRecorded);

        try
        {
            _ = analyzer.AddSamplesAsync(monoSamples, waveFormat.SampleRate);
        }
        catch (Exception ex)
        {
            Instance.Log(LogLevel.Error, LogPrefix, $"Error adding samples to analyzer: {ex.Message}");
        }
    }

    private static void ConvertToMono(
        byte[] buffer,
        int channels,
        int frameCount,
        float[] monoSamples,
        int bytesRecorded)
    {
        if (buffer == null || monoSamples == null || channels <= 0 ||
            frameCount <= 0 || bytesRecorded <= 0)
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

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) =>
        _logger.Safe(() => HandleRecordingStopped(e), LogPrefix, "Error handling recording stopped event");

    private void HandleRecordingStopped(StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            HandleRecordingStoppedWithError(e.Exception);
            return;
        }

        _logger.Log(LogLevel.Information, LogPrefix, "Recording stopped normally");
    }

    private void HandleRecordingStoppedWithError(Exception ex)
    {
        _logger.Log(LogLevel.Error, LogPrefix, $"Recording stopped with error: {ex.Message}");

        if (!_isDisposed && IsRecording && !_isReinitializing &&
            !_controller.IsTransitioning && !_isCaptureStopping)
        {
            _ = Task.Run(ReinitializeCaptureAsync);
        }
    }

    private Task MonitorCaptureAsync(CancellationToken token) =>
        _logger.SafeAsync(async () => await MonitorDevicesLoop(token),
            LogPrefix, "Error in device monitoring");

    private async Task MonitorDevicesLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(DEVICE_CHECK_INTERVAL_MS, token);

                if (ShouldSkipDeviceCheck())
                    continue;

                CheckDefaultDevice();
            }
        }
        catch (TaskCanceledException)
        {
            _logger.Log(LogLevel.Information, LogPrefix, "Device monitoring task cancelled normally");
        }
        catch (OperationCanceledException)
        {
            _logger.Log(LogLevel.Information, LogPrefix, "Device monitoring operation cancelled normally");
        }
    }

    private bool ShouldSkipDeviceCheck() =>
        !IsRecording || _controller.IsTransitioning || _isCaptureStopping;

    private void CheckDefaultDevice()
    {
        var device = _deviceManager.GetDefaultAudioDevice();
        if (device is null)
            OnDeviceChanged();
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
        _logger.Safe(() =>
        {
            if (state.Capture != null)
            {
                state.Capture.DataAvailable -= OnDataAvailable;
                state.Capture.RecordingStopped -= OnRecordingStopped;
            }
        }, LogPrefix, "Error unsubscribing events");

    private void DisposeStateComponents(CaptureState state)
    {
        DisposeCapture(state);
        DisposeCts(state);
        DisposeAnalyzer(state);
    }

    private void DisposeCapture(CaptureState state) =>
        _logger.Safe(() =>
        {
            state.Capture?.Dispose();
        }, LogPrefix, "Error disposing capture");

    private void DisposeCts(CaptureState state) =>
        _logger.Safe(() =>
        {
            state.CTS?.Dispose();
        }, LogPrefix, "Error disposing CTS");

    private void DisposeAnalyzer(CaptureState state) =>
        _logger.Safe(async () =>
        {
            if (state.Analyzer is IAsyncDisposable asyncDisposable)
            {
                var task = asyncDisposable.DisposeAsync();
                await task.ConfigureAwait(false);
            }
            else if (state.Analyzer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }, LogPrefix, "Error disposing analyzer");

    protected override void DisposeManaged() =>
        _logger.Safe(() =>
        {
            UnsubscribeDeviceManager();
            StopCapture();
            CleanupResources();
        }, LogPrefix, "Error during managed disposal");

    private void UnsubscribeDeviceManager() =>
        _deviceManager.DeviceChanged -= OnDeviceChanged;

    private void StopCapture() =>
        TryStopCaptureOnDispose();

    private void CleanupResources() =>
        _operationLock.Dispose();

    private void TryStopCaptureOnDispose() =>
        _logger.Safe(() =>
        {
            if (IsRecording)
            {
                using var timeoutCts = new CancellationTokenSource(FromSeconds(5));
                var task = StopCaptureAsync(true);

                if (!task.Wait(5000))
                    _logger.Log(LogLevel.Warning, LogPrefix, "Timeout waiting for StopCaptureAsync during dispose");
            }
        }, LogPrefix, "Error stopping capture during dispose");

    protected override async ValueTask DisposeAsyncManagedResources() =>
        await _logger.SafeAsync(async () =>
        {
            UnsubscribeDeviceManager();
            await TryStopCaptureAsyncOnDispose();
            CleanupResources();
        }, LogPrefix, "Error during async managed disposal");

    private async Task TryStopCaptureAsyncOnDispose() =>
        await _logger.SafeAsync(async () =>
        {
            if (IsRecording)
            {
                using var timeoutCts = new CancellationTokenSource(FromSeconds(5));

                try
                {
                    await StopCaptureAsync(true)
                        .WaitAsync(FromSeconds(5), timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.Log(LogLevel.Warning, LogPrefix,
                        "Timeout waiting for StopCaptureAsync during async dispose");
                }
            }
        }, LogPrefix, "Error stopping capture during async dispose");
}