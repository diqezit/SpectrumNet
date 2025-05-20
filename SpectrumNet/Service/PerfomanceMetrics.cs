#nullable enable

namespace SpectrumNet.Service;

public readonly record struct PerformanceMetrics(
    double FrameTimeMs,
    double Fps
);

public enum PerformanceLevel { Excellent, Good, Fair, Poor }

public sealed class PerformanceMetricsManager : IPerformanceMetricsManager
{
    private static readonly object _syncLock = new();
    private static readonly int _processorCount = ProcessorCount > 0 ? ProcessorCount : 1;

    private const double
        HIGH_CPU_THRESHOLD_PERCENT = 80.0,
        MEDIUM_CPU_THRESHOLD_PERCENT = 60.0,
        HIGH_MEMORY_THRESHOLD_MB = 1000.0,
        MEDIUM_MEMORY_THRESHOLD_MB = 500.0,
        MIN_TIME_DELTA_SECONDS_FOR_FPS = 0.001,
        MIN_TIME_DELTA_SECONDS_FOR_CPU = 0.01,
        CPU_USAGE_SMOOTHING_FACTOR = 0.2;

    private const int
        MAX_FRAMES_FOR_FPS_CALCULATION = 120;

    private const float
        DEFAULT_FPS = 60.0f,
        MIN_GOOD_FPS = 50.0f,
        MIN_FAIR_FPS = 30.0f;

    private static readonly TimeSpan METRICS_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(250);

    private static readonly double[] _frameTimes = new double[MAX_FRAMES_FOR_FPS_CALCULATION];
    private static readonly Stopwatch _timer = new();

    private static TimeSpan _lastProcessCpuTime = TimeSpan.Zero;
    private static double _lastWallClockTimeSeconds;

    private static int _frameIndex;
    private static float _currentFps = DEFAULT_FPS;
    private static double _currentCpuUsagePercent;
    private static double _currentRamUsageMb;
    private static PerformanceLevel _currentPerformanceLevel = PerformanceLevel.Good;

    private static bool _isInitialized;
    private static CancellationTokenSource? _cancellationTokenSource;
    private static Task? _updateTask;

    private static readonly Lazy<PerformanceMetricsManager> _instance = 
        new(() => new PerformanceMetricsManager());

    public static PerformanceMetricsManager Instance => _instance.Value;

    public event EventHandler<PerformanceMetrics>? PerformanceMetricsUpdated;
    public event EventHandler<PerformanceLevel>? PerformanceLevelChanged;

    public float GetCurrentFps() 
    { 
        lock (_syncLock) return _currentFps;
    }

    public double GetCurrentCpuUsagePercent()
    {
        lock (_syncLock) return _currentCpuUsagePercent;
    }

    public double GetCurrentRamUsageMb()
    {
        lock (_syncLock) return _currentRamUsageMb;
    }

    public PerformanceLevel GetCurrentPerformanceLevel()
    {
        lock (_syncLock) return _currentPerformanceLevel;
    }

    public void Initialize()
    {
        if (_isInitialized) return;

        lock (_syncLock)
        {
            if (_isInitialized) return;

            _timer.Start();
            _lastWallClockTimeSeconds = _timer.Elapsed.TotalSeconds;

            try
            {
                using var currentProcess = Process.GetCurrentProcess();
                _lastProcessCpuTime = currentProcess.TotalProcessorTime;
            }
            catch (PlatformNotSupportedException)
            {
                _lastProcessCpuTime = TimeSpan.Zero;
            }
            catch (Exception)
            {
                _lastProcessCpuTime = TimeSpan.Zero;
            }

            AppDomain.CurrentDomain.ProcessExit += HandleApplicationExit;

            _cancellationTokenSource = new CancellationTokenSource();
            _updateTask = Task.Run(() => UpdateMetricsLoopAsync(_cancellationTokenSource.Token));

            _isInitialized = true;
        }
    }

    public void RecordFrameTime()
    {
        if (!_isInitialized)
        {
            Initialize();
            if (!_isInitialized) return;
        }

        lock (_syncLock)
        {
            double currentTimeSeconds = _timer.Elapsed.TotalSeconds;
            _frameTimes[_frameIndex % MAX_FRAMES_FOR_FPS_CALCULATION] = currentTimeSeconds;
            _frameIndex++;
        }
    }

    public void Cleanup()
    {
        lock (_syncLock)
        {
            if (!_isInitialized) return;

            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }

            if (_updateTask != null && !_updateTask.IsCompleted)
            {
                try
                {
                    _updateTask.Wait(TimeSpan.FromSeconds(1));
                }
                catch (AggregateException ae)
                {
                    ae.Handle(ex => ex is OperationCanceledException);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception)
                {
                }
            }
            _updateTask = null;

            AppDomain.CurrentDomain.ProcessExit -= HandleApplicationExit;

            _timer.Stop();

            PerformanceMetricsUpdated = null;
            PerformanceLevelChanged = null;

            _isInitialized = false;
        }
    }

    private static void HandleApplicationExit(object? sender, EventArgs e) => Instance.Cleanup();

    private static async Task UpdateMetricsLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(METRICS_UPDATE_INTERVAL, token);
                ProcessAndNotifyMetrics();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
        }
    }

    private static void ProcessAndNotifyMetrics()
    {
        float fps;
        double cpuUsagePercent;
        double ramUsageMb;
        PerformanceLevel newLevel;

        lock (_syncLock)
        {
            fps = CalculateFpsInternal();
            cpuUsagePercent = CalculateCpuUsagePercentInternal();
            ramUsageMb = CalculateCurrentRamUsageMbInternal();

            _currentFps = fps;
            _currentCpuUsagePercent = cpuUsagePercent;
            _currentRamUsageMb = ramUsageMb;

            newLevel = DeterminePerformanceLevelInternal(fps, cpuUsagePercent, ramUsageMb);
        }

        NotifyPerformanceMetricsUpdated(fps);
        NotifyPerformanceLevelChanged(newLevel);
    }

    private static void NotifyPerformanceMetricsUpdated(float fps)
    {
        double frameTimeMs = (fps > 0.001f) ? (1000.0 / fps) : 0.0;
        Instance.PerformanceMetricsUpdated?.Invoke(null, new PerformanceMetrics(frameTimeMs, fps));
    }

    private static void NotifyPerformanceLevelChanged(PerformanceLevel newLevel)
    {
        bool levelActuallyChanged = false;

        lock (_syncLock)
        {
            if (newLevel != _currentPerformanceLevel)
            {
                _currentPerformanceLevel = newLevel;
                levelActuallyChanged = true;
            }
        }

        if (levelActuallyChanged)
        {
            Instance.PerformanceLevelChanged?.Invoke(null, newLevel);
        }
    }

    private static float CalculateFpsInternal()
    {
        int framesAvailable = _frameIndex;
        int framesToConsider = Math.Min(framesAvailable, MAX_FRAMES_FOR_FPS_CALCULATION);

        if (framesToConsider < 2)
        {
            return _currentFps;
        }

        int newestFrameBufferIndex = (framesAvailable - 1 + MAX_FRAMES_FOR_FPS_CALCULATION)
            % MAX_FRAMES_FOR_FPS_CALCULATION;

        int oldestFrameBufferIndex = (framesAvailable - framesToConsider + MAX_FRAMES_FOR_FPS_CALCULATION)
            % MAX_FRAMES_FOR_FPS_CALCULATION;

        double newestTime = _frameTimes[newestFrameBufferIndex];
        double oldestTime = _frameTimes[oldestFrameBufferIndex];
        double deltaTimeSeconds = newestTime - oldestTime;

        if (deltaTimeSeconds <= MIN_TIME_DELTA_SECONDS_FOR_FPS)
        {
            return _currentFps;
        }
        return (float)((framesToConsider - 1) / deltaTimeSeconds);
    }

    private static double CalculateCpuUsagePercentInternal()
    {
        if (_lastProcessCpuTime == TimeSpan.Zero)
        {
            return _currentCpuUsagePercent;
        }

        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            TimeSpan currentProcessCpuTime = currentProcess.TotalProcessorTime;
            double currentWallClockTimeSeconds = _timer.Elapsed.TotalSeconds;

            double cpuTimeDeltaSeconds = (currentProcessCpuTime - _lastProcessCpuTime).TotalSeconds;
            double wallClockDeltaSeconds = currentWallClockTimeSeconds - _lastWallClockTimeSeconds;

            _lastProcessCpuTime = currentProcessCpuTime;
            _lastWallClockTimeSeconds = currentWallClockTimeSeconds;

            if (wallClockDeltaSeconds <= MIN_TIME_DELTA_SECONDS_FOR_CPU || _processorCount <= 0)
            {
                return _currentCpuUsagePercent;
            }

            double instantaneousUsage = (cpuTimeDeltaSeconds / wallClockDeltaSeconds) / _processorCount * 100.0;
            double smoothedUsage = _currentCpuUsagePercent * (1.0 - CPU_USAGE_SMOOTHING_FACTOR) +
                                   instantaneousUsage * CPU_USAGE_SMOOTHING_FACTOR;

            return Math.Clamp(smoothedUsage, 0.0, 100.0);
        }
        catch (PlatformNotSupportedException)
        {
            _lastProcessCpuTime = TimeSpan.Zero;
            return _currentCpuUsagePercent;
        }
        catch (InvalidOperationException)
        {
            _lastProcessCpuTime = TimeSpan.Zero;
            return _currentCpuUsagePercent;
        }
        catch (Exception)
        {
            _lastProcessCpuTime = TimeSpan.Zero;
            return _currentCpuUsagePercent;
        }
    }

    private static double CalculateCurrentRamUsageMbInternal()
    {
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            return currentProcess.WorkingSet64 / (1024.0 * 1024.0);
        }
        catch (Exception)
        {
            return _currentRamUsageMb;
        }
    }

    private static PerformanceLevel DeterminePerformanceLevelInternal(
        float fps,
        double cpuUsagePercent,
        double ramUsageMb)
    {
        bool isCpuHigh = cpuUsagePercent > HIGH_CPU_THRESHOLD_PERCENT;
        bool isCpuMedium = !isCpuHigh && cpuUsagePercent > MEDIUM_CPU_THRESHOLD_PERCENT;
        bool isMemoryHigh = ramUsageMb > HIGH_MEMORY_THRESHOLD_MB;
        bool isMemoryMedium = !isMemoryHigh && ramUsageMb > MEDIUM_MEMORY_THRESHOLD_MB;

        if (fps >= MIN_GOOD_FPS && !isCpuHigh && !isCpuMedium && !isMemoryHigh && !isMemoryMedium)
            return PerformanceLevel.Excellent;

        if (fps >= MIN_GOOD_FPS && !isCpuHigh && !isMemoryHigh)
            return PerformanceLevel.Good;

        if (fps >= MIN_FAIR_FPS && !isCpuHigh)
            return PerformanceLevel.Fair;

        return PerformanceLevel.Poor;
    }
}