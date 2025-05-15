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
    private readonly float[] _cosCache, _sinCache;
    private readonly int _vecSize = Vector<float>.Count;
    private readonly ParallelOptions _parallelOpts;
    private readonly ThreadLocal<Complex[]> _threadLocalBuffer;
    private readonly ArrayPool<Complex> _complexArrayPool = ArrayPool<Complex>.Shared;
    private readonly ArrayPool<float> _floatArrayPool = ArrayPool<float>.Shared;
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
        (_cosCache, _sinCache) = TrigonometricTables.Get(fftSize);

        InitializeWindows(fftSize);
        _window = _windows[_windowType];

        _channel = CreateProcessingChannel(channelCapacity);

        _parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = ProcessorCount };
        _threadLocalBuffer = new ThreadLocal<Complex[]>(() => new Complex[fftSize]);

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
        WaitForPendingTasksAsync(TimeSpan.FromSeconds(5))
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        _pendingTasksSemaphore.Dispose();
    }

    protected override async ValueTask DisposeAsyncManagedResources()
    {
        CleanupResources();
        await WaitForPendingTasksAsync(TimeSpan.FromSeconds(5));
        _pendingTasksSemaphore.Dispose();
    }

    private void CleanupResources()
    {
        _cts.Cancel();
        _channel.Writer.Complete();
        _threadLocalBuffer.Dispose();
        _cts.Dispose();
    }

    private static void ValidateFftSize(int fftSize)
    {
        if (!BitOperations.IsPow2(fftSize) || fftSize <= 0)
            throw new ArgumentException("FFT size must be a positive power of 2", nameof(fftSize));
    }

    private void InitializeWindows(int fftSize)
    {
        foreach (FftWindowType type in Enum.GetValues(typeof(FftWindowType)))
            _windows[type] = GenerateWindow(fftSize, type);
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

    private float[] GenerateWindow(int size, FftWindowType type) =>
        SafeResult(() =>
        {
            float[] w = new float[size];
            Action<int> setWindow = GetWindowGenerator(w, size, type);

            Parallel.For(0, size, setWindow);
            return w;
        },
        new float[size],
        LOG_SOURCE,
        "Error generating window");

    private Action<int> GetWindowGenerator(float[] w, int size, FftWindowType type)
    {
        return type switch
        {
            FftWindowType.Hann =>
                i => w[i] = 0.5f * (1f - _cosCache[i]),

            FftWindowType.Hamming =>
                i => w[i] = 0.54f - 0.46f * _cosCache[i],

            FftWindowType.Blackman =>
                i => w[i] = 0.42f - 0.5f * _cosCache[i] +
                    0.08f * MathF.Cos(TWO_PI * 2 * i / (size - 1)),

            FftWindowType.Bartlett =>
                i => w[i] = 2f / (size - 1) * ((size - 1) / 2f -
                    MathF.Abs(i - (size - 1) / 2f)),

            FftWindowType.Kaiser =>
                i => w[i] = BesselI0(KAISER_BETA *
                    MathF.Sqrt(1 - MathF.Pow(2f * i / (size - 1) - 1, 2))) /
                    BesselI0(KAISER_BETA),

            _ => throw new NotSupportedException($"Unsupported window type: {type}")
        };
    }

    private static float BesselI0(float x)
    {
        float sum = 1f, term = x * x / 4f;
        for (int k = 1; term > BESSEL_EPSILON; k++)
        {
            sum += term;
            term *= x * x / (4f * k * k);
        }
        return sum;
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
            Complex[] fftBuffer = _complexArrayPool.Rent(_fftSize);
            try
            {
                Array.Copy(_buffer, fftBuffer, _fftSize);
                ScheduleFftCalculation(fftBuffer, rate, cancellationToken);
            }
            catch (Exception ex)
            {
                _complexArrayPool.Return(fftBuffer);
                Log(LogLevel.Error, LOG_SOURCE, $"Error preparing FFT: {ex.Message}");
            }
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

                PerformFftCalculation(fftBuffer);
                NotifyFftResultIfNeeded(fftBuffer, rate, cancellationToken);
            }
            catch (Exception ex)
            {
                Error(LOG_SOURCE, $"FFT calculation failed: {ex}");
            }
            finally
            {
                _complexArrayPool.Return(fftBuffer);
            }
        }, cancellationToken);

        TrackTask(task);
    }

    private void PerformFftCalculation(Complex[] fftBuffer) => 
        FastFourierTransform.FFT(true, (int)Log2(_fftSize), fftBuffer);

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
                ApplyWindow(chunk, offset);
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

            ApplyWindow(chunk[start..end], offset + start);
        });
    }

    private void ApplyWindow(ReadOnlyMemory<float> data, int offset)
    {
        int len = data.Length;
        int vecEnd = len - len % _vecSize;

        ProcessVectorizedData(data, offset, vecEnd);
        ProcessRemainingData(data, offset, vecEnd, len);
    }

    private void ProcessVectorizedData(ReadOnlyMemory<float> data, int offset, int vecEnd)
    {
        Span<float> temp = stackalloc float[_vecSize];

        for (int i = 0; i < vecEnd; i += _vecSize)
        {
            data.Span.Slice(i, _vecSize).CopyTo(temp);
            Vector<float> s = new(temp);
            Vector<float> w = new(_window, offset + i);
            (s * w).CopyTo(temp);

            for (int j = 0; j < _vecSize; j++)
                _buffer[offset + i + j] = new Complex { X = temp[j] };
        }
    }

    private void ProcessRemainingData(ReadOnlyMemory<float> data, int offset, int vecEnd, int len)
    {
        for (int i = vecEnd; i < len; i++)
            _buffer[offset + i] = new Complex { X = data.Span[i] * _window[offset + i] };
    }
}