#nullable enable

namespace SpectrumNet.SN.Spectrum;

public sealed class FftProcessor : AsyncDisposableBase, IFftProcessor
{
    private const string LogPrefix = nameof(FftProcessor);
    private readonly ISmartLogger _logger = Instance;

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
        set => _logger.Safe(() => HandleSetWindowType(value),
                        LogPrefix,
                        "Error setting window type");
    }

    private void HandleSetWindowType(FftWindowType value)
    {
        if (_windowType == value)
            return;

        if (!_windows.TryGetValue(value, out var window) || window is null)
            throw new InvalidOperationException($"Unsupported window type: {value}");

        _windowType = value;
        _window = window;
        ResetFftState();
    }

    public ValueTask AddSamplesAsync(
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken cancellationToken = default) =>
        _logger.SafeResult(() => HandleAddSamplesAsync(samples, sampleRate, cancellationToken),
                        ValueTask.CompletedTask,
                        LogPrefix,
                        "Error adding samples");

    private ValueTask HandleAddSamplesAsync(
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (sampleRate <= 0)
            throw new ArgumentException("Sample rate must be positive", nameof(sampleRate));

        if (samples.Length == 0)
            return ValueTask.CompletedTask;

        return WriteToChannelAsync(samples, sampleRate, cancellationToken);
    }

    public void ResetFftState() =>
        _logger.Safe(() =>
        {
            ThrowIfDisposed();
            _sampleCount = 0;
            Array.Clear(_buffer, 0, _fftSize);
        }, LogPrefix, "Error resetting FFT state");

    protected override void DisposeManaged() =>
        _logger.Safe(() =>
        {
            CleanupResources();
            WaitForPendingTasksAsync(FromSeconds(5))
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            SuppressFinalize(this);
        }, LogPrefix, "Error during managed disposal");

    protected override async ValueTask DisposeAsyncManagedResources() =>
        await _logger.SafeAsync(async () =>
        {
            CleanupResources();
            await WaitForPendingTasksAsync(FromSeconds(5));
            SuppressFinalize(this);
        }, LogPrefix, "Error during async managed disposal");

    private void CleanupResources() =>
        _logger.Safe(() =>
        {
            _cts.Cancel();
            _channel.Writer.Complete();
        }, LogPrefix, "Error cleaning up resources");

    private static void ValidateFftSize(int fftSize)
    {
        if (!BitOperations.IsPow2(fftSize) || fftSize <= 0)
            throw new ArgumentException("FFT size must be a positive power of 2", nameof(fftSize));
    }

    private void InitializeWindows(int fftSize) =>
        _logger.Safe(() =>
        {
            foreach (FftWindowType type in Enum.GetValues(typeof(FftWindowType)))
                _windows[type] = WindowGenerator.Generate(fftSize, type);
        }, LogPrefix, "Error initializing windows");

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

    private async Task ProcessAsync() =>
        await _logger.SafeAsync(async () =>
        {
            try
            {
                await foreach (var (samples, rate, token) in _channel.Reader.ReadAllAsync(_cts.Token))
                {
                    if (token.IsCancellationRequested)
                        continue;

                    if (samples.Length > 0)
                        await Task.Run(() => ProcessBatch(samples, rate, token), token);
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore operation canceled exceptions
            }
        },
        LogPrefix,
        "Error in FFT processing loop");

    private void ProcessBatch(
        ReadOnlyMemory<float> samples,
        int rate,
        CancellationToken cancellationToken) =>
        _logger.Safe(() => HandleProcessBatch(samples, rate, cancellationToken),
                  LogPrefix,
                  "Error processing batch");

    private void HandleProcessBatch(
        ReadOnlyMemory<float> samples,
        int rate,
        CancellationToken cancellationToken)
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
    }

    private void PerformFftAndNotify(int rate, CancellationToken cancellationToken) =>
        _logger.Safe(() =>
        {
            Complex[] fftBuffer = new Complex[_fftSize];
            Array.Copy(_buffer, fftBuffer, _fftSize);
            ScheduleFftCalculation(fftBuffer, rate, cancellationToken);
        }, LogPrefix, "Error preparing FFT");

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
                _logger.Error(LogPrefix, $"FFT calculation failed: {ex}");
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
            try
            {
                _pendingTasks.Add(task);
            }
            finally
            {
                _pendingTasksSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogPrefix, $"Error adding task to tracking: {ex.Message}");
        }
    }

    private async Task RemoveTaskFromTracking()
    {
        try
        {
            await _pendingTasksSemaphore.WaitAsync();
            try
            {
                _pendingTasks.TryTake(out _);
            }
            finally
            {
                _pendingTasksSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogPrefix, $"Error removing task from tracking: {ex.Message}");
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
                _logger.Log(LogLevel.Warning, LogPrefix,
                    $"Waiting for pending tasks interrupted: {ex.Message}");
            }
        }
    }

    private async Task<Task[]> GetAllPendingTasks()
    {
        try
        {
            await _pendingTasksSemaphore.WaitAsync();
            try
            {
                return [.. _pendingTasks];
            }
            finally
            {
                _pendingTasksSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogPrefix, $"Error getting pending tasks: {ex.Message}");
            return [];
        }
    }

    private void NotifyFftResultIfNeeded(
        Complex[] fftBuffer,
        int rate,
        CancellationToken cancellationToken) =>
        _logger.Safe(() =>
        {
            if (!cancellationToken.IsCancellationRequested && FftCalculated != null)
                FftCalculated(this, new FftEventArgs(fftBuffer, rate));
        }, LogPrefix, "Error notifying FFT result");

    private void ProcessChunk(ReadOnlyMemory<float> chunk) =>
        _logger.Safe(() => HandleProcessChunk(chunk), LogPrefix, "Error processing chunk");

    private void HandleProcessChunk(ReadOnlyMemory<float> chunk)
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
    }

    private void ProcessLargeChunkInParallel(ReadOnlyMemory<float> chunk, int offset, int len) =>
        _logger.Safe(() =>
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
        }, LogPrefix, "Error processing large chunk in parallel");
}