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
            Source = LOG_SOURCE,
            ErrorMessage = $"Error setting window type: {windowType}"
        });

    public void SetScaleType(SpectrumScale scaleType) => ScaleType = scaleType;

    public void UpdateSettings(FftWindowType windowType, SpectrumScale scaleType) =>
        SafeExecute(() =>
        {
            lock (_lock)
            {
                bool changed = UpdateProcessorSettings(windowType, scaleType);

                if (changed)
                    ResetSpectrum();
            }
        },
        new ErrorHandlingOptions
        {
            Source = LOG_SOURCE,
            ErrorMessage = "Error updating settings"
        });

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

        if (samples.Length == 0)
            return;

        await ForwardSamplesToProcessorAsync(samples, sampleRate, cancellationToken);
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

    private static void ValidateDependencies(IFftProcessor fftProcessor, ISpectrumConverter converter)
    {
        ArgumentNullException.ThrowIfNull(fftProcessor);

        ArgumentNullException.ThrowIfNull(converter);
    }

    private static Channel<(Complex[] Fft, int SampleRate)> CreateProcessingChannel(int channelCapacity)
    {
        var options = new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        };

        return Channel.CreateBounded<(Complex[] Fft, int SampleRate)>(options);
    }

    private void InitializeProcessing()
    {
        Task.Run(ProcessFftResultsAsync);
        _fftProcessor.FftCalculated += OnFftCalculated;
    }

    private bool UpdateProcessorSettings(FftWindowType windowType, SpectrumScale scaleType)
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

        return changed;
    }

    private async Task ForwardSamplesToProcessorAsync(
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _cts.Token);

        try
        {
            await _fftProcessor.AddSamplesAsync(samples, sampleRate, linkedCts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log(LogLevel.Error, LOG_SOURCE, $"Error adding samples: {ex}");
            throw;
        }
    }

    private void CleanupResources()
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
            Log(LogLevel.Error, LOG_SOURCE, $"Error disposing processor: {ex.Message}");
        }

        _cts.Dispose();
    }

    private async Task CleanupResourcesAsync()
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
            Log(LogLevel.Error, LOG_SOURCE, $"Error disposing processor asynchronously: {ex.Message}");
        }

        _cts.Dispose();
    }

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
            Log(LogLevel.Error, LOG_SOURCE, $"Error invoking Disposed event: {ex.Message}");
        }
    }

    private async Task ProcessFftResultsAsync() =>
        await SafeExecuteAsync(async () =>
        {
            await foreach (var (fft, sampleRate) in _processingChannel.Reader.ReadAllAsync(_cts.Token))
            {
                if (_cts.IsCancellationRequested)
                    break;

                await ProcessFftDataAsync(fft, sampleRate);
            }
        },
        new ErrorHandlingOptions
        {
            Source = LOG_SOURCE,
            ErrorMessage = "Error in FFT results processing loop",
            IgnoreExceptions = [typeof(OperationCanceledException)]
        });

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
            Log(LogLevel.Error, LOG_SOURCE, $"Error processing FFT result: {ex}");
        }
    }

    private async Task<float[]> ComputeSpectrumAsync(
        Complex[] fft,
        int sampleRate,
        SpectrumScale scale)
    {
        return await Task.Run(() =>
        {
            _cts.Token.ThrowIfCancellationRequested();
            return _converter.ConvertToSpectrum(fft, sampleRate, scale, _cts.Token);
        }, _cts.Token);
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
        SafeExecute(() =>
        {
            _lastData = null;
            var emptyData = new SpectralData([], UtcNow);
            NotifyDataReady(emptyData);
        },
        new ErrorHandlingOptions
        {
            Source = LOG_SOURCE,
            ErrorMessage = "Error resetting spectrum"
        });

    private void OnFftCalculated(object? sender, FftEventArgs e) =>
        SafeExecute(() =>
        {
            if (_isDisposed || e.Result.Length == 0 || _cts.IsCancellationRequested)
                return;

            if (!TryEnqueueFftResult(e.Result, e.SampleRate))
                Log(LogLevel.Warning, LOG_SOURCE, "Processing channel is full, dropping FFT result");
        },
        new ErrorHandlingOptions
        {
            Source = LOG_SOURCE,
            ErrorMessage = "Error handling FFT calculation event"
        });

    private bool TryEnqueueFftResult(Complex[] fftResult, int sampleRate)
    {
        return _processingChannel.Writer.TryWrite((fftResult, sampleRate));
    }
}