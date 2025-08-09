#nullable enable

namespace SpectrumNet.SN.Visualization.Performance;

public readonly record struct PerformanceMetrics(double FrameTimeMs, double Fps);
public enum PerformanceLevel { Excellent, Good, Fair, Poor }

public sealed class PerformanceMetricsManager : IPerformanceMetricsManager, IDisposable
{
    private const int MaxFrames = 120;
    private const double CpuSmoothFactor = 0.2,
                         HighCpuPct = 80.0, MedCpuPct = 60.0,
                         HighMemMb = 1000.0, MedMemMb = 500.0,
                         MinDeltaFpsSec = 0.001, MinDeltaCpuSec = 0.01;
    private const float DefaultFps = 60f, MinGoodFps = 50f, MinFairFps = 30f;
    private static readonly TimeSpan UpdateInterval = FromMilliseconds(250);

    private static readonly Lazy<PerformanceMetricsManager> _lazy
        = new(() => new PerformanceMetricsManager());

    public static PerformanceMetricsManager Instance => _lazy.Value;
    public event EventHandler<PerformanceMetrics>? PerformanceMetricsUpdated;
    public event EventHandler<PerformanceLevel>? PerformanceLevelChanged;

    private readonly object _lock = new();
    private readonly double[] _frameTimes = new double[MaxFrames];
    private readonly Stopwatch _stopwatch = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    private TimeSpan _lastProcCpuTime;
    private double _lastWallTime;
    private int _frameCount;
    private float _fps = DefaultFps;
    private double _cpuPct, _ramMb;
    private PerformanceLevel _level = PerformanceLevel.Good;

    private PerformanceMetricsManager() { }

    public void Initialize()
    {
        if (_cts != null) return;
        lock (_lock)
        {
            if (_cts != null) return;
            _stopwatch.Start();
            _lastWallTime = _stopwatch.Elapsed.TotalSeconds;
            _lastProcCpuTime = TryGetProcess()?.TotalProcessorTime ?? Zero;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_cts.Token), _cts.Token);
        }
    }

    public void RecordFrameTime()
    {
        if (_cts == null) Initialize();
        lock (_lock)
        {
            var t = _stopwatch.Elapsed.TotalSeconds;
            _frameTimes[_frameCount % MaxFrames] = t;
            _frameCount++;
        }
    }

    public float GetCurrentFps() => Safe(() => _fps);
    public double GetCurrentCpuUsagePercent() => Safe(() => _cpuPct);
    public double GetCurrentRamUsageMb() => Safe(() => _ramMb);
    public PerformanceLevel GetCurrentPerformanceLevel() => Safe(() => _level);

    public void Cleanup() => Dispose();

    public void Dispose()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            try { _loop?.Wait(FromSeconds(1)); } catch { }
            _cts?.Dispose(); _cts = null;
            AppDomain.CurrentDomain.ProcessExit -= (_, _) => Dispose();
            _stopwatch.Stop();
            PerformanceMetricsUpdated = null;
            PerformanceLevelChanged = null;
        }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(UpdateInterval, token);
                UpdateMetrics();
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(FromSeconds(5), token); }
        }
    }

    private void UpdateMetrics()
    {
        float fps;
        double cpu, ram;
        PerformanceLevel newLevel;

        lock (_lock)
        {
            fps = CalculateFps();
            cpu = CalculateCpu();
            ram = GetRamUsage();
            newLevel = DetermineLevel(fps, cpu, ram);

            _fps = fps;
            _cpuPct = cpu;
            _ramMb = ram;
        }

        PerformanceMetricsUpdated?.Invoke(this, new PerformanceMetrics(fps > 0 ? 1000.0 / fps : 0, fps));
        if (newLevel != _level)
        {
            _level = newLevel;
            PerformanceLevelChanged?.Invoke(this, newLevel);
        }
    }

    private T Safe<T>(Func<T> fn) { lock (_lock) return fn(); }

    private float CalculateFps()
    {
        int count = Min(_frameCount, MaxFrames);
        if (count < 2) return _fps;

        int newest = (_frameCount - 1) % MaxFrames;
        int oldest = (_frameCount - count) % MaxFrames;
        double delta = _frameTimes[newest] - _frameTimes[oldest];
        return delta <= MinDeltaFpsSec ? _fps : (float)((count - 1) / delta);
    }

    private double CalculateCpu()
    {
        if (_lastProcCpuTime == Zero) return _cpuPct;
        var proc = TryGetProcess();
        var nowCpu = proc?.TotalProcessorTime ?? _lastProcCpuTime;
        var nowWall = _stopwatch.Elapsed.TotalSeconds;
        var cpuDelta = (nowCpu - _lastProcCpuTime).TotalSeconds;
        var wallDelta = nowWall - _lastWallTime;
        _lastProcCpuTime = nowCpu;
        _lastWallTime = nowWall;
        if (wallDelta <= MinDeltaCpuSec) return _cpuPct;
        double inst = cpuDelta / wallDelta / Max(ProcessorCount, 1) * 100;
        return Clamp(_cpuPct * (1 - CpuSmoothFactor) + inst * CpuSmoothFactor, 0, 100);
    }

    private double GetRamUsage()
    {
        try { return TryGetProcess()?.WorkingSet64 / 1024.0 / 1024.0 ?? _ramMb; }
        catch { return _ramMb; }
    }

    private static Process? TryGetProcess()
    {
        try { return Process.GetCurrentProcess(); }
        catch { return null; }
    }

    private static PerformanceLevel DetermineLevel(float fps, double cpu, double ram)
    {
        bool hiCpu = cpu > HighCpuPct, medCpu = cpu > MedCpuPct && !hiCpu;
        bool hiMem = ram > HighMemMb, medMem = ram > MedMemMb && !hiMem;
        return fps >= MinGoodFps && !hiCpu && !medCpu && !hiMem && !medMem ? PerformanceLevel.Excellent
             : fps >= MinGoodFps && !hiCpu && !hiMem ? PerformanceLevel.Good
             : fps >= MinFairFps && !hiCpu ? PerformanceLevel.Fair
             : PerformanceLevel.Poor;
    }
}