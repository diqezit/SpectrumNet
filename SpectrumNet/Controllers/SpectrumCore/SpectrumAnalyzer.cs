#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

public sealed class SpectrumAnalyzer
    : AsyncDisposableBase,
    ISpectralDataProvider,
    IComponent
{
    private const string LOG_SOURCE = nameof(SpectrumAnalyzer);

    private readonly IFftProcessor _fftProcessor;
    private readonly ISpectrumConverter _converter;
    private readonly SynchronizationContext? _context;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<(Complex[] Fft, int SampleRate)> _processingChannel;

    private SpectralData? _lastData;
    private SpectrumScale _scaleType = SpectrumScale.Linear;

    private volatile bool _processorDisposed;

    public event EventHandler<SpectralDataEventArgs>? SpectralDataReady;
    public event EventHandler? Disposed;
    public ISite? Site { get; set; }
    public bool IsDisposed => _isDisposed;

    public SpectrumAnalyzer(
        IFftProcessor fftProcessor,
        ISpectrumConverter converter,
        SynchronizationContext? context = null,
        int channelCapacity = DEFAULT_CHANNEL_CAPACITY)
    {
        ValidateDependencies(fftProcessor, converter);

        _fftProcessor = fftProcessor;
        _converter = converter;
        _context = context;

        _processingChannel = CreateProcessingChannel(channelCapacity);

        InitializeProcessing();
    }

    public SpectrumScale ScaleType
    {
        get => _scaleType;
        set
        {
            lock (_lock)
            {
                if (_scaleType == value)
                    return;

                _scaleType = value;
                ResetSpectrum();
            }
        }
    }

    public void UpdateSettings(FftWindowType windowType, SpectrumScale scaleType) =>
        SafeExecute(
            () => TryUpdateSettings(windowType, scaleType),
            "UpdateSettings",
            "Error updating settings"
        );

    public SpectralData? GetCurrentSpectrum()
    {
        ThrowIfDisposed();
        return _lastData;
    }

    public async Task AddSamplesAsync(
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (samples.Length == 0 || _processorDisposed)
            return;

        await ForwardSamplesToProcessorAsync(
            samples,
            sampleRate,
            cancellationToken);
    }

    public void SafeReset() => ResetSpectrum();

    protected override void DisposeManaged()
    {
        CleanupResources();
        RaiseDisposedEvent();
    }

    protected override async ValueTask DisposeAsyncManagedResources()
    {
        await CleanupResourcesAsync();
        RaiseDisposedEvent();
    }

    private void TryUpdateSettings(
        FftWindowType windowType,
        SpectrumScale scaleType)
    {
        lock (_lock)
        {
            if (_isDisposed || _processorDisposed)
                return;

            bool changed = false;

            changed |= TryUpdateWindowType(windowType);
            changed |= UpdateScaleType(scaleType);

            if (changed)
                ResetSpectrum();
        }
    }

    private bool TryUpdateWindowType(FftWindowType windowType)
    {
        try
        {
            if (_processorDisposed || _fftProcessor.WindowType == windowType)
                return false;

            _fftProcessor.WindowType = windowType;
            _fftProcessor.ResetFftState();
            return true;
        }
        catch (ObjectDisposedException)
        {
            HandleProcessorDisposed("cannot update window type");
            return false;
        }
    }

    private bool UpdateScaleType(SpectrumScale scaleType)
    {
        if (_scaleType == scaleType)
            return false;

        _scaleType = scaleType;
        return true;
    }

    private void HandleProcessorDisposed(string context)
    {
        _processorDisposed = true;
        Log(LogLevel.Warning,
            LOG_SOURCE,
            $"Processor is disposed, {context}");
    }

    private static void ValidateDependencies(
        IFftProcessor fftProcessor,
        ISpectrumConverter converter)
    {
        ArgumentNullException.ThrowIfNull(fftProcessor);
        ArgumentNullException.ThrowIfNull(converter);
    }

    private static Channel<(Complex[] Fft, int SampleRate)> CreateProcessingChannel(int channelCapacity) =>
        Channel.CreateBounded<(Complex[] Fft, int SampleRate)>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

    private void InitializeProcessing()
    {
        Task.Run(ProcessFftResultsAsync);
        _fftProcessor.FftCalculated += OnFftCalculated;
    }

    private void CleanupResources()
    {
        UnsubscribeFromEvents();
        CancelProcessing();
        DisposeProcessor();
    }

    private async Task CleanupResourcesAsync()
    {
        UnsubscribeFromEvents();
        CancelProcessing();
        await DisposeProcessorAsync();
    }

    private void UnsubscribeFromEvents() =>
        _fftProcessor.FftCalculated -= OnFftCalculated;

    private void CancelProcessing()
    {
        _cts.Cancel();
        _processingChannel.Writer.Complete();
        _processorDisposed = true;
    }

    private void DisposeProcessor()
    {
        try
        {
            if (_fftProcessor is IDisposable disposable)
                disposable.Dispose();
        }
        catch (Exception ex)
        {
            LogProcessorDisposalError(ex.Message);
        }

        _cts.Dispose();
    }

    private async Task DisposeProcessorAsync()
    {
        try
        {
            await _fftProcessor.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogProcessorDisposalError($"asynchronously: {ex.Message}");
        }

        _cts.Dispose();
    }

    private static void LogProcessorDisposalError(string details) =>
        Log(LogLevel.Error,
            LOG_SOURCE,
            $"Error disposing processor {details}");

    private async Task ForwardSamplesToProcessorAsync(
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _cts.Token);

        try
        {
            if (!_processorDisposed)
                await _fftProcessor.AddSamplesAsync(samples, sampleRate, linkedCts.Token);
        }
        catch (ObjectDisposedException)
        {
            HandleProcessorDisposed("cannot add samples");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log(LogLevel.Error,
                LOG_SOURCE,
                $"Error adding samples: {ex}");
            throw;
        }
    }

    private async Task ProcessFftResultsAsync() =>
        await SafeExecuteAsync(
            async () => await ProcessFftResultsCore(),
            "ProcessFftResultsAsync",
            "Error in FFT results processing loop",
            [typeof(OperationCanceledException)]
        );

    private async Task ProcessFftResultsCore()
    {
        await foreach (var (fft, sampleRate) in _processingChannel.Reader.ReadAllAsync(_cts.Token))
        {
            if (_cts.IsCancellationRequested)
                break;

            await ProcessFftDataAsync(fft, sampleRate);
        }
    }

    private async Task ProcessFftDataAsync(Complex[] fft, int sampleRate)
    {
        try
        {
            var currentScale = _scaleType;
            var spectrum = await ComputeSpectrumAsync(fft, sampleRate, currentScale);

            var data = new SpectralData(spectrum, UtcNow);

            UpdateAndNotifyWithNewData(data);
        }
        catch (OperationCanceledException)
        {
            // Operation was canceled, just exit
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LOG_SOURCE,
                $"Error processing FFT result: {ex}");
        }
    }

    private async Task<float[]> ComputeSpectrumAsync(
        Complex[] fft,
        int sampleRate,
        SpectrumScale scale) =>
        await Task.Run(
            () => {
                _cts.Token.ThrowIfCancellationRequested();
                return _converter.ConvertToSpectrum(fft, sampleRate, scale, _cts.Token);
            },
            _cts.Token
        );

    private void OnFftCalculated(object? sender, FftEventArgs e) =>
        SafeExecute(
            () => ProcessFftCalculatedEvent(e),
            "OnFftCalculated",
            "Error handling FFT calculation event"
        );

    private void ProcessFftCalculatedEvent(FftEventArgs e)
    {
        if (_isDisposed || e.Result.Length == 0 || _cts.IsCancellationRequested)
            return;

        if (!TryEnqueueFftResult(e.Result, e.SampleRate))
            Log(LogLevel.Warning,
                LOG_SOURCE,
                "Processing channel is full, dropping FFT result");
    }

    private bool TryEnqueueFftResult(Complex[] fftResult, int sampleRate) =>
        _processingChannel.Writer.TryWrite((fftResult, sampleRate));

    private void RaiseDisposedEvent()
    {
        try
        {
            if (_context != null)
                _context.Post(_ => Disposed?.Invoke(this, EventArgs.Empty), null);
            else
                Disposed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LOG_SOURCE,
                $"Error invoking Disposed event: {ex.Message}");
        }
    }

    private void UpdateAndNotifyWithNewData(SpectralData data)
    {
        lock (_lock)
            _lastData = data;

        NotifyDataReady(data);
    }

    private void NotifyDataReady(SpectralData data)
    {
        if (_context != null)
            _context.Post(_ => SpectralDataReady?.Invoke(
                this, new SpectralDataEventArgs(data)), null);
        else
            SpectralDataReady?.Invoke(this, new SpectralDataEventArgs(data));
    }

    private void ResetSpectrum() =>
        SafeExecute(
            () => {
                _lastData = null;
                var emptyData = new SpectralData([], UtcNow);
                NotifyDataReady(emptyData);
            },
            "ResetSpectrum",
            "Error resetting spectrum"
        );

    private static void SafeExecute(
        Action action,
        string methodName,
        string errorMessage) =>
        Safe(
            action,
            new ErrorHandlingOptions
            {
                Source = $"{LOG_SOURCE}.{methodName}",
                ErrorMessage = errorMessage
            }
        );

    private static Task SafeExecuteAsync(
        Func<Task> asyncAction,
        string methodName,
        string errorMessage,
        Type[]? ignoreExceptions = null) =>
        SafeAsync(
            asyncAction,
            new ErrorHandlingOptions
            {
                Source = $"{LOG_SOURCE}.{methodName}",
                ErrorMessage = errorMessage,
                IgnoreExceptions = ignoreExceptions
            }
        );
}