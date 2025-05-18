// Controllers/SpectrumCore/SpectrumAnalyzer.cs
#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

public sealed class SpectrumAnalyzer : AsyncDisposableBase,
    ISpectralDataProvider,
    IComponent
{
    private const string LOG_SOURCE = nameof(SpectrumAnalyzer);

    private readonly IFftProcessor _fftProcessor;
    private readonly ISpectrumConverter _converter;
    private readonly SynchronizationContext? _context;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<(Complex[] Fft, int SampleRate)>
        _processingChannel;

    private Complex[]? _lastFftResult;
    private int _lastSampleRate;
    private SpectralData? _lastData;
    private SpectrumScale _scaleType = SpectrumScale.Linear;
    private volatile bool _processorDisposed;

    public event EventHandler<SpectralDataEventArgs>?
        SpectralDataReady;
    public event EventHandler? Disposed;

    public ISite? Site { get; set; }
    public bool IsDisposed => _isDisposed;

    public SpectrumAnalyzer(
        IFftProcessor fftProcessor,
        ISpectrumConverter converter,
        SynchronizationContext? context = null,
        int channelCapacity = Constants.DEFAULT_CHANNEL_CAPACITY)
    {
        ArgumentNullException.ThrowIfNull(fftProcessor);
        ArgumentNullException.ThrowIfNull(converter);

        _fftProcessor = fftProcessor;
        _converter = converter;
        _context = context;
        _processingChannel = Channel.CreateBounded<
            (Complex[] Fft, int SampleRate)>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

        Task.Run(ProcessLoopAsync);
        _fftProcessor.FftCalculated += HandleFftEvent;
    }

    public SpectrumScale ScaleType
    {
        get => _scaleType;
        set => SafeExecute(
            () => {
                bool changed = UpdateScaleType(value);
                if (changed)
                {
                    ReprocessLastData();
                }
            },
            nameof(ScaleType),
            "Error setting scale"
        );
    }

    public void UpdateSettings(
        FftWindowType windowType,
        SpectrumScale scaleType) =>
        SafeExecute(
            () => {
                bool changed = UpdateWindow(windowType) | UpdateScaleType(scaleType);
                if (changed)
                {
                    ReprocessLastData();
                }
            },
            nameof(UpdateSettings),
            "Error updating settings"
        );

    public void ReprocessLastData()
    {
        lock (_lock)
        {
            if (_lastFftResult != null && _lastFftResult.Length > 0 && _lastSampleRate > 0)
            {
                var fftCopy = new Complex[_lastFftResult.Length];
                Array.Copy(_lastFftResult, fftCopy, _lastFftResult.Length);

                if (!_processingChannel.Writer.TryWrite((fftCopy, _lastSampleRate)))
                {
                    Log(LogLevel.Warning,
                        LOG_SOURCE,
                        "Could not enqueue last FFT data for reprocessing");
                }
                else
                {
                    Log(LogLevel.Debug,
                        LOG_SOURCE,
                        "Reprocessing last FFT data with new settings");
                }
            }
        }
    }

    public SpectralData? GetCurrentSpectrum()
    {
        ThrowIfDisposed();
        lock (_lock) return _lastData;
    }

    public async Task AddSamplesAsync(
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken cancellationToken = default)
    {
        if (samples.Length == 0 || _processorDisposed)
            return;

        ThrowIfDisposed();
        await ForwardSamplesAsync(
            samples,
            sampleRate,
            cancellationToken
        ).ConfigureAwait(false);
    }

    public void SafeReset() => ResetSpectrum();

    protected override void DisposeManaged() =>
        DisposeCore(async: false)
            .GetAwaiter()
            .GetResult();

    protected override async ValueTask
        DisposeAsyncManagedResources() =>
        await DisposeCore(async: true)
            .ConfigureAwait(false);

    private bool UpdateScaleType(SpectrumScale value)
    {
        lock (_lock)
        {
            if (_scaleType == value) return false;
            _scaleType = value;
            return true;
        }
    }

    private bool UpdateWindow(FftWindowType type)
    {
        try
        {
            if (_processorDisposed ||
                _fftProcessor.WindowType == type)
                return false;

            _fftProcessor.WindowType = type;
            _fftProcessor.ResetFftState();
            return true;
        }
        catch (ObjectDisposedException)
        {
            MarkProcessorDisposed("window");
            return false;
        }
    }

    private void MarkProcessorDisposed(string context)
    {
        _processorDisposed = true;
        Log(LogLevel.Warning,
            LOG_SOURCE,
            $"Processor disposed: {context}"
        );
    }

    private async Task ForwardSamplesAsync(
        ReadOnlyMemory<float> samples,
        int rate,
        CancellationToken token)
    {
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                token,
                _cts.Token
            );

        try
        {
            await _fftProcessor.AddSamplesAsync(
                samples,
                rate,
                linkedCts.Token
            ).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            MarkProcessorDisposed("samples");
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException)
        {
            Log(LogLevel.Error,
                LOG_SOURCE,
                $"Error adding samples: {ex}"
            );
            throw;
        }
    }

    private Task ProcessLoopAsync() => SafeExecuteAsync(
        ProcessCoreAsync,
        nameof(ProcessLoopAsync),
        "Processing loop error",
        [
            typeof(OperationCanceledException),
            typeof(ObjectDisposedException)
        ]
    );

    private async Task ProcessCoreAsync()
    {
        await foreach (
            var (fft, rate) in
            _processingChannel.Reader
                .ReadAllAsync(_cts.Token)
        )
        {
            if (_cts.IsCancellationRequested)
                break;

            await HandleFftDataAsync(fft, rate)
                .ConfigureAwait(false);
        }
    }

    private async Task HandleFftDataAsync(
        Complex[] fft,
        int rate)
    {
        SpectrumScale scale;
        lock (_lock)
        {
            if (_isDisposed)
                return;

            scale = _scaleType;

            if (_lastFftResult == null || _lastFftResult.Length != fft.Length)
            {
                _lastFftResult = new Complex[fft.Length];
            }
            Array.Copy(fft, _lastFftResult, fft.Length);
            _lastSampleRate = rate;
        }

        float[] spectrum = await ComputeSpectrumAsync(
            fft,
            rate,
            scale
        ).ConfigureAwait(false);

        if (_isDisposed)
            return;

        var data = new SpectralData(
            spectrum,
            DateTime.UtcNow
        );

        StoreAndNotify(data);
    }

    private Task<float[]> ComputeSpectrumAsync(
        Complex[] fft,
        int rate,
        SpectrumScale scale) =>
        Task.Run(
            () => _converter.ConvertToSpectrum(
                fft,
                rate,
                scale,
                _cts.Token
            ),
            _cts.Token
        );

    private void HandleFftEvent(
        object? sender,
        FftEventArgs e
    ) => SafeExecute(
        () => EnqueueFft(e),
        nameof(HandleFftEvent),
        "FFT event error"
    );

    private void EnqueueFft(FftEventArgs e)
    {
        if (_isDisposed ||
            e.Result.Length == 0 ||
            _cts.IsCancellationRequested)
            return;

        var copy = new Complex[e.Result.Length];
        Array.Copy(e.Result, copy, e.Result.Length);

        if (!
            _processingChannel
                .Writer
                .TryWrite((copy, e.SampleRate))
        )
            Log(LogLevel.Warning,
                LOG_SOURCE,
                "Channel full"
            );
    }

    private void StoreAndNotify(SpectralData data)
    {
        lock (_lock)
            _lastData = data;

        PostEvent(() =>
            SpectralDataReady?.Invoke(
                this,
                new SpectralDataEventArgs(data)
            )
        );
    }

    private void ResetSpectrum() => SafeExecute(
        () => NotifyEmpty(),
        nameof(ResetSpectrum),
        "Error resetting"
    );

    private void NotifyEmpty()
    {
        _lastData = null;
        var emptyData = new SpectralData([], UtcNow);
        PostEvent(() =>
            SpectralDataReady?.Invoke(
                this,
                new SpectralDataEventArgs(emptyData)
            )
        );
    }

    private void PostEvent(Action action)
    {
        try
        {
            if (_context != null)
                _context.Post(_ => action(), null);
            else
                action();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LOG_SOURCE,
                $"PostEvent error: {ex.Message}"
            );
        }
    }

    private async Task DisposeCore(bool async)
    {
        if (async)
            await SafeExecuteAsync(
                CleanupAsync,
                nameof(DisposeCore),
                "Async cleanup error"
            ).ConfigureAwait(false);
        else
            SafeExecute(
                Cleanup,
                nameof(DisposeCore),
                "Cleanup error"
            );

        PostEvent(() =>
            Disposed?.Invoke(this, EventArgs.Empty)
        );
    }

    private void Cleanup()
    {
        _fftProcessor.FftCalculated -= HandleFftEvent;
        _cts.Cancel();
        _processingChannel.Writer.Complete();
        _processorDisposed = true;

        if (_fftProcessor is IDisposable disp)
            disp.Dispose();

        _cts.Dispose();
    }

    private async Task CleanupAsync()
    {
        _fftProcessor.FftCalculated -= HandleFftEvent;
        _cts.Cancel();
        _processingChannel.Writer.Complete();
        _processorDisposed = true;

        if (_fftProcessor is IAsyncDisposable adisp)
            await adisp.DisposeAsync().ConfigureAwait(false);
        else if (_fftProcessor is IDisposable disp)
            disp.Dispose();

        _cts.Dispose();
    }

    private static void SafeExecute(
        Action action,
        string methodName,
        string errorMessage
    ) => Safe(
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
        Type[]? ignoreExceptions = null
    ) => SafeAsync(
        asyncAction,
        new ErrorHandlingOptions
        {
            Source = $"{LOG_SOURCE}.{methodName}",
            ErrorMessage = errorMessage,
            IgnoreExceptions = ignoreExceptions
        }
    );
}