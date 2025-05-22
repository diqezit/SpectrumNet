#nullable enable

using Constants = SpectrumNet.SN.Spectrum.Utils.Constants;

namespace SpectrumNet.SN.Spectrum;

public sealed class SpectrumAnalyzer : AsyncDisposableBase,
    ISpectralDataProvider,
    IComponent
{
    private const string LogPrefix = nameof(SpectrumAnalyzer);
    private readonly ISmartLogger _logger = Instance;

    private readonly IFftProcessor _fftProcessor;
    private readonly ISpectrumConverter _converter;
    private readonly SynchronizationContext? _context;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<(Complex[] Fft, int SampleRate)> _processingChannel;

    private Complex[]? _lastFftResult;
    private int _lastSampleRate;
    private SpectralData? _lastSpectralData;
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
        int channelCapacity = Constants.DEFAULT_CHANNEL_CAPACITY)
    {
        ArgumentNullException.ThrowIfNull(fftProcessor);
        ArgumentNullException.ThrowIfNull(converter);

        _fftProcessor = fftProcessor;
        _converter = converter;
        _context = context;
        _processingChannel = Channel.CreateBounded<(Complex[] Fft, int SampleRate)>(
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
        set => _logger.Safe(() => HandleSetScaleType(value),
                                LogPrefix,
                                "Error setting scale");
    }

    private void HandleSetScaleType(SpectrumScale value)
    {
        bool changed = UpdateScaleType(value);
        if (changed)
        {
            ReprocessLastData();
        }
    }

    public void UpdateSettings(
        FftWindowType windowType,
        SpectrumScale scaleType) =>
        _logger.Safe(() => HandleUpdateSettings(windowType, scaleType),
                    LogPrefix,
                    "Error updating settings");

    private void HandleUpdateSettings(FftWindowType windowType, SpectrumScale scaleType)
    {
        bool windowChanged = UpdateWindow(windowType);
        bool scaleChanged = UpdateScaleType(scaleType);

        if (windowChanged || scaleChanged)
            ReprocessLastData();
    }

    public void ReprocessLastData() =>
        _logger.Safe(() => HandleReprocessLastData(),
                    LogPrefix,
                    "Error reprocessing data");

    private void HandleReprocessLastData()
    {
        lock (_lock)
        {
            if (_lastFftResult != null && _lastFftResult.Length > 0 && _lastSampleRate > 0)
            {
                var fftCopy = new Complex[_lastFftResult.Length];
                Array.Copy(_lastFftResult, fftCopy, _lastFftResult.Length);

                if (!_processingChannel.Writer.TryWrite((fftCopy, _lastSampleRate)))
                {
                    _logger.Log(LogLevel.Warning,
                        LogPrefix,
                        "Could not enqueue last FFT data for reprocessing");
                }
                else
                {
                    _logger.Log(LogLevel.Debug,
                        LogPrefix,
                        "Reprocessing last FFT data with new settings");
                }
            }
        }
    }

    public SpectralData? GetCurrentSpectrum() =>
        _logger.SafeResult(() =>
        {
            ThrowIfDisposed();
            lock (_lock) return _lastSpectralData;
        },
        null,
        LogPrefix,
        "Error getting current spectrum");

    public async Task AddSamplesAsync(
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken cancellationToken = default) =>
        await _logger.SafeAsync(async () => await HandleAddSamples(samples, sampleRate, cancellationToken),
                                LogPrefix,
                                "Error adding samples");

    private async Task HandleAddSamples(
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken cancellationToken)
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

    public void SafeReset() =>
        _logger.Safe(() => ResetSpectrum(),
                    LogPrefix,
                    "Error resetting spectrum analyzer");

    protected override void DisposeManaged() =>
        _logger.Safe(() =>
        {
            DisposeCore(async: false)
                .GetAwaiter()
                .GetResult();
        }, LogPrefix, "Error during managed disposal");

    protected override async ValueTask DisposeAsyncManagedResources() =>
        await _logger.SafeAsync(async () =>
            await DisposeCore(async: true).ConfigureAwait(false),
            LogPrefix,
            "Error during async managed disposal");

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
        _logger.Log(LogLevel.Warning,
            LogPrefix,
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
            _logger.Log(LogLevel.Error,
                LogPrefix,
                $"Error adding samples: {ex}"
            );
            throw;
        }
    }

    private Task ProcessLoopAsync() =>
        _logger.SafeAsync(
            async () => await ProcessCoreAsync(),
            LogPrefix,
            "Processing loop error");

    private async Task ProcessCoreAsync()
    {
        try
        {
            await foreach (var (fft, rate) in _processingChannel.Reader.ReadAllAsync(_cts.Token))
            {
                if (_cts.IsCancellationRequested)
                    break;

                await HandleFftDataAsync(fft, rate).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Log(LogLevel.Information,
                LogPrefix,
                "Processing loop cancelled normally");
            return;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error,
                LogPrefix,
                $"Processing loop error: {ex.Message}");
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

        var spectralData = new SpectralData(
            spectrum,
            UtcNow
        );

        StoreAndNotify(spectralData);
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
        FftEventArgs e) =>
        _logger.Safe(() => EnqueueFft(e),
                    LogPrefix,
                    "FFT event error");

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
            _logger.Log(LogLevel.Warning,
                LogPrefix,
                "Channel full"
            );
    }

    private void StoreAndNotify(SpectralData spectralData)
    {
        lock (_lock)
            _lastSpectralData = spectralData;

        PostEvent(() =>
            SpectralDataReady?.Invoke(
                this,
                new SpectralDataEventArgs(spectralData)
            )
        );
    }

    private void ResetSpectrum() =>
        _logger.Safe(() => NotifyEmpty(),
                    LogPrefix,
                    "Error resetting spectrum");

    private void NotifyEmpty()
    {
        _lastSpectralData = null;
        var emptySpectralData = new SpectralData([], UtcNow);
        PostEvent(() =>
            SpectralDataReady?.Invoke(
                this,
                new SpectralDataEventArgs(emptySpectralData)
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
            _logger.Log(LogLevel.Error,
                LogPrefix,
                $"PostEvent error: {ex.Message}"
            );
        }
    }

    private async Task DisposeCore(bool async)
    {
        if (async)
            await _logger.SafeAsync(
                async () => await CleanupAsync(),
                LogPrefix,
                "Async cleanup error"
            ).ConfigureAwait(false);
        else
            _logger.Safe(
                () => Cleanup(),
                LogPrefix,
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
}