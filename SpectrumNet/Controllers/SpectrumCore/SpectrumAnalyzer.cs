#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

public sealed class SpectrumAnalyzer 
    : AsyncDisposableBase, 
    ISpectralDataProvider, 
    IComponent
{
    private const string LogSource = nameof(SpectrumAnalyzer);
    private readonly IFftProcessor _fftProcessor;
    private readonly ISpectrumConverter _converter;
    private readonly SynchronizationContext? _context;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<(Complex[] Fft, int SampleRate)> _processingChannel;
    private readonly ArrayPool<float> _floatArrayPool = ArrayPool<float>.Shared;
    private SpectralData? _lastData;
    private SpectrumScale _scaleType = SpectrumScale.Linear;

    public event EventHandler<SpectralDataEventArgs>? SpectralDataReady;
    public event EventHandler? Disposed;
    public ISite? Site { get; set; }
    public bool IsDisposed => _isDisposed;

    public SpectrumAnalyzer(
        IFftProcessor fftProcessor,
        ISpectrumConverter converter,
        SynchronizationContext? context = null,
        int channelCapacity = Constants.DefaultChannelCapacity
    )
    {
        _fftProcessor = fftProcessor ??
            throw new ArgumentNullException(nameof(fftProcessor));

        _converter = converter ??
            throw new ArgumentNullException(nameof(converter));

        _context = context;
        var options = new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        };
        _processingChannel = Channel.CreateBounded<(Complex[] Fft, int SampleRate)>(options);
        Task.Run(ProcessFftResultsAsync);
        _fftProcessor.FftCalculated += OnFftCalculated;
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

    public void SetWindowType(FftWindowType windowType) =>
        SafeExecute(() =>
        {
            lock (_lock)
            {
                if (_fftProcessor.WindowType == windowType)
                    return;
                _fftProcessor.WindowType = windowType;
                _fftProcessor.ResetFftState();
                ResetSpectrum();
            }
        },
        new ErrorHandlingOptions
        {
            Source = LogSource,
            ErrorMessage = $"Error setting window type: {windowType}"
        });

    public void SetScaleType(SpectrumScale scaleType) => ScaleType = scaleType;

    public void UpdateSettings(FftWindowType windowType, SpectrumScale scaleType) =>
        SafeExecute(() =>
        {
            lock (_lock)
            {
                bool changed = false;
                if (_fftProcessor.WindowType != windowType)
                {
                    _fftProcessor.WindowType = windowType;
                    _fftProcessor.ResetFftState();
                    changed = true;
                }
                if (_scaleType != scaleType)
                {
                    _scaleType = scaleType;
                    changed = true;
                }
                if (changed)
                    ResetSpectrum();
            }
        },
        new ErrorHandlingOptions
        {
            Source = LogSource,
            ErrorMessage = "Error updating settings"
        });

    public SpectralData? GetCurrentSpectrum()
    {
        ThrowIfDisposed();
        return _lastData;
    }

    public async Task AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (samples.Length == 0)
            return;

        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

        try
        {
            await _fftProcessor.AddSamplesAsync(samples, sampleRate,
                linkedCts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log(LogLevel.Error, LogSource,
                $"Error adding samples: {ex}");
            throw;
        }
    }

    public void SafeReset() => ResetSpectrum();

    protected override void DisposeManaged()
    {
        _fftProcessor.FftCalculated -= OnFftCalculated;
        _cts.Cancel();
        _processingChannel.Writer.Complete();

        try
        {
            if (_fftProcessor is IDisposable disposable)
                disposable.Dispose();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogSource, $"Error disposing processor: {ex.Message}");
        }

        _cts.Dispose();

        try
        {
            if (_context != null)
                _context.Post(_ => Disposed?.Invoke(this, EventArgs.Empty), null);
            else
                Disposed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogSource, $"Error invoking Disposed event: {ex.Message}");
        }
    }

    protected override async ValueTask DisposeAsyncManagedResources()
    {
        _fftProcessor.FftCalculated -= OnFftCalculated;
        _cts.Cancel();
        _processingChannel.Writer.Complete();

        try
        {
            await _fftProcessor.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogSource, $"Error disposing processor asynchronously: {ex.Message}");
        }

        _cts.Dispose();

        try
        {
            if (_context != null)
                _context.Post(_ => Disposed?.Invoke(this, EventArgs.Empty), null);
            else
                Disposed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogSource, $"Error invoking Disposed event: {ex.Message}");
        }
    }

    private async Task ProcessFftResultsAsync() =>
        await SafeExecuteAsync(async () =>
        {
            await foreach (var (fft, sampleRate) in
                _processingChannel.Reader.ReadAllAsync(_cts.Token))
            {
                if (_cts.IsCancellationRequested)
                    break;
                try
                {
                    var currentScale = _scaleType;
                    var spectrum = await Task.Run(() =>
                    {
                        _cts.Token.ThrowIfCancellationRequested();
                        return _converter.ConvertToSpectrum(
                            fft, sampleRate, currentScale, _cts.Token);
                    }, _cts.Token);
                    var data = new SpectralData(spectrum, UtcNow);
                    lock (_lock)
                        _lastData = data;
                    if (_context != null)
                        _context.Post(_ => SpectralDataReady?.Invoke(
                            this, new SpectralDataEventArgs(data)), null);
                    else
                        SpectralDataReady?.Invoke(
                            this, new SpectralDataEventArgs(data));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Error, LogSource,
                        $"Error processing FFT result: {ex}");
                }
            }
        },
        new ErrorHandlingOptions
        {
            Source = LogSource,
            ErrorMessage = "Error in FFT results processing loop",
            IgnoreExceptions = new[] { typeof(OperationCanceledException) }
        });

    private void ResetSpectrum() =>
        SafeExecute(() =>
        {
            _lastData = null;
            var emptyData = new SpectralData(Array.Empty<float>(), UtcNow);
            if (_context != null)
                _context.Post(_ => SpectralDataReady?.Invoke(
                    this, new SpectralDataEventArgs(emptyData)), null);
            else
                SpectralDataReady?.Invoke(
                    this, new SpectralDataEventArgs(emptyData));
        },
        new ErrorHandlingOptions
        {
            Source = LogSource,
            ErrorMessage = "Error resetting spectrum"
        });

    private void OnFftCalculated(object? sender, FftEventArgs e) =>
        SafeExecute(() =>
        {
            if (_isDisposed || e.Result.Length == 0 || _cts.IsCancellationRequested)
                return;
            if (!_processingChannel.Writer.TryWrite((e.Result, e.SampleRate)))
                Log(LogLevel.Warning, LogSource,
                    "Processing channel is full, dropping FFT result");
        },
        new ErrorHandlingOptions
        {
            Source = LogSource,
            ErrorMessage = "Error handling FFT calculation event"
        });
}