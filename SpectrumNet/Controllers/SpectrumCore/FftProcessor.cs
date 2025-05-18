#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

public sealed class FftProcessor : AsyncDisposableBase, IFftProcessor
{
    private const string LOG_SOURCE = nameof(FftProcessor);

    private readonly int _fftSize;
    private readonly Complex[] _buffer;
    private readonly Channel<(ReadOnlyMemory<float> Samples, int SampleRate, CancellationToken Token)> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<FftWindowType, float[]> _windows = [];
    private readonly int _vecSize = Vector<float>.Count;
    private readonly ParallelOptions _parallelOpts;
    private readonly ConcurrentBag<Task> _pendingTasks = [];
    private readonly SemaphoreSlim _pendingTasksSemaphore = new(1, 1);

    private float[] _window;
    private int _sampleCount;
    private FftWindowType _windowType = FftWindowType.Hann;

    public event EventHandler<FftEventArgs>? FftCalculated;

    public FftProcessor(
        int fftSize = DEFAULT_FFT_SIZE,
        int channelCapacity = DEFAULT_CHANNEL_CAPACITY)
    {
        ValidateFftSize(fftSize);

        _fftSize = fftSize;
        _buffer = new Complex[fftSize];

        InitializeWindows(fftSize);
        _window = _windows[_windowType];

        _channel = CreateProcessingChannel(channelCapacity);

        _parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = ProcessorCount };

        Task.Run(ProcessAsync);
    }

    public FftWindowType WindowType
    {
        get => _windowType;
        set
        {
            if (_windowType == value)
                return;

            if (!_windows.TryGetValue(value, out var window) || window is null)
                throw new InvalidOperationException($"Unsupported window type: {value}");

            _windowType = value;
            _window = window;
            ResetFftState();
        }
    }

    public ValueTask AddSamplesAsync(
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (sampleRate <= 0)
            throw new ArgumentException("Sample rate must be positive", nameof(sampleRate));

        if (samples.Length == 0)
            return ValueTask.CompletedTask;

        return WriteToChannelAsync(samples, sampleRate, cancellationToken);
    }

    public void ResetFftState()
    {
        ThrowIfDisposed();
        _sampleCount = 0;
        Array.Clear(_buffer, 0, _fftSize);
    }

    protected override void DisposeManaged()
    {
        CleanupResources();
        WaitForPendingTasksAsync(FromSeconds(5))
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        Dispose();
    }

    protected override async ValueTask DisposeAsyncManagedResources()
    {
        CleanupResources();
        await WaitForPendingTasksAsync(FromSeconds(5));
        Dispose();
    }

    private void CleanupResources()
    {
        _cts.Cancel();
        _channel.Writer.Complete();
        Dispose();
    }

    private static void ValidateFftSize(int fftSize)
    {
        if (!BitOperations.IsPow2(fftSize) || fftSize <= 0)
            throw new ArgumentException("FFT size must be a positive power of 2", nameof(fftSize));
    }

    private void InitializeWindows(int fftSize)
    {
        foreach (FftWindowType type in Enum.GetValues(typeof(FftWindowType)))
            _windows[type] = WindowGenerator.Generate(fftSize, type);
    }

    private static Channel<(ReadOnlyMemory<float>, int, CancellationToken)> CreateProcessingChannel(int capacity)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        };
        return Channel.CreateBounded<(ReadOnlyMemory<float>, int, CancellationToken)>(options);
    }

    private ValueTask WriteToChannelAsync(
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _cts.Token);

        return _channel.Writer.TryWrite((samples, sampleRate, linkedCts.Token))
            ? ValueTask.CompletedTask
            : new ValueTask(
                _channel.Writer.WriteAsync(
                    (samples, sampleRate, linkedCts.Token),
                    linkedCts.Token).AsTask());
    }

    private async Task ProcessAsync()
    {
        await SafeAsync(async () =>
        {
            await foreach (var (samples, rate, token) in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                if (token.IsCancellationRequested)
                    continue;

                if (samples.Length > 0)
                    await Task.Run(() => ProcessBatch(samples, rate, token), token);
            }
        },
        LOG_SOURCE,
        "Error in FFT processing loop",
        ignoreExceptions: [typeof(OperationCanceledException)]);
    }

    private void ProcessBatch(
        ReadOnlyMemory<float> samples,
        int rate,
        CancellationToken cancellationToken)
    {
        Safe(() =>
        {
            int pos = 0;
            while (pos < samples.Length && !cancellationToken.IsCancellationRequested)
            {
                int count = Min(_fftSize - _sampleCount, samples.Length - pos);
                if (count <= 0)
                    break;

                ProcessChunk(samples.Slice(pos, count));
                pos += count;
                _sampleCount += count;

                if (_sampleCount >= _fftSize && !cancellationToken.IsCancellationRequested)
                {
                    PerformFftAndNotify(rate, cancellationToken);
                    ResetFftState();
                }
            }
        },
        LOG_SOURCE,
        "Error processing batch");
    }

    private void PerformFftAndNotify(int rate, CancellationToken cancellationToken)
    {
        Safe(() =>
        {
            Complex[] fftBuffer = new Complex[_fftSize];
            Array.Copy(_buffer, fftBuffer, _fftSize);
            ScheduleFftCalculation(fftBuffer, rate, cancellationToken);
        },
        LOG_SOURCE,
        "Error preparing FFT");
    }

    private void ScheduleFftCalculation(
        Complex[] fftBuffer,
        int rate,
        CancellationToken cancellationToken)
    {
        var task = Task.Run(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                FastFourierTransformHelper.PerformFFT(fftBuffer, _fftSize);
                NotifyFftResultIfNeeded(fftBuffer, rate, cancellationToken);
            }
            catch (Exception ex)
            {
                Error(LOG_SOURCE, $"FFT calculation failed: {ex}");
            }
        }, cancellationToken);

        TrackTask(task);
    }

    private async void TrackTask(Task task)
    {
        await AddTaskToTracking(task);

        try
        {
            await task;
        }
        catch
        {
            // Исключения обрабатываются внутри задачи
        }
        finally
        {
            await RemoveTaskFromTracking();
        }
    }

    private async Task AddTaskToTracking(Task task)
    {
        try
        {
            await _pendingTasksSemaphore.WaitAsync();
            _pendingTasks.Add(task);
        }
        finally
        {
            _pendingTasksSemaphore.Release();
        }
    }

    private async Task RemoveTaskFromTracking()
    {
        try
        {
            await _pendingTasksSemaphore.WaitAsync();
            _pendingTasks.TryTake(out _);
        }
        finally
        {
            _pendingTasksSemaphore.Release();
        }
    }

    public async Task WaitForPendingTasksAsync(TimeSpan timeout)
    {
        Task[] tasks = await GetAllPendingTasks();

        if (tasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(tasks).WaitAsync(timeout);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, LOG_SOURCE,
                    $"Waiting for pending tasks interrupted: {ex.Message}");
            }
        }
    }

    private async Task<Task[]> GetAllPendingTasks()
    {
        try
        {
            await _pendingTasksSemaphore.WaitAsync();
            return [.. _pendingTasks];
        }
        finally
        {
            _pendingTasksSemaphore.Release();
        }
    }

    private void NotifyFftResultIfNeeded(
        Complex[] fftBuffer,
        int rate,
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested && FftCalculated != null)
            FftCalculated(this, new FftEventArgs(fftBuffer, rate));
    }

    private void ProcessChunk(ReadOnlyMemory<float> chunk)
    {
        Safe(() =>
        {
            int offset = _sampleCount;
            int len = chunk.Length;

            if (len > BATCH_SIZE)
            {
                ProcessLargeChunkInParallel(chunk, offset, len);
            }
            else
            {
                FastFourierTransformHelper.ApplyWindowInPlaceVectorized(
                    _buffer, chunk, _window, offset, _vecSize);
            }
        },
        LOG_SOURCE,
        "Error processing chunk");
    }

    private void ProcessLargeChunkInParallel(ReadOnlyMemory<float> chunk, int offset, int len)
    {
        int chunkSize = Max(BATCH_SIZE, len / ProcessorCount);
        Parallel.For(0, (len + chunkSize - 1) / chunkSize, _parallelOpts, i =>
        {
            if (_parallelOpts.CancellationToken.IsCancellationRequested)
                return;

            int start = i * chunkSize;
            int end = Min(start + chunkSize, len);

            FastFourierTransformHelper.ApplyWindowInPlaceVectorized(
                _buffer, chunk[start..end], _window, offset + start, _vecSize);
        });
    }
}