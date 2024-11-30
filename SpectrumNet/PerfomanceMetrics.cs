#nullable enable
namespace SpectrumNet;

public static class PerfomanceMetrics
{
    private static readonly object _lock = new();
    private static readonly Queue<long> _frameTimestamps = new(MAX_FRAMES);
    private static readonly int MAX_FRAMES = 120, _processorCount = Environment.ProcessorCount;
    private static float _previousFps, _previousCpuUsage;
    private static long _previousCpuTime;
    private static TimeSpan _previousTotalTime;
    private static readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private static DateTime _lastCpuUpdate = DateTime.UtcNow;
    private static double _currentCpuUsage;
    private const float SMOOTHING_FACTOR_CPU = 0.1f;
    private const double MIN_TIME_DELTA = 1e-6;

    public static void DrawPerformanceInfo(SKCanvas canvas, SKImageInfo info)
    {
        try
        {
            var fps = GetFPS();
            if ((DateTime.UtcNow - _lastCpuUpdate).TotalSeconds >= 0.25)
            {
                _currentCpuUsage = GetCpuUsage();
                _lastCpuUpdate = DateTime.UtcNow;
            }
            using var paint = new SKPaint { TextSize = 10, IsAntialias = true, Color = fps < 30 ? SKColors.Red : SKColors.White };
            canvas.DrawText($"RAM: {GetResourceUsage(ResourceType.Ram):F1} MB | CPU: {_currentCpuUsage:F1}% | FPS: {fps:F0}", 5, 15, paint);
        }
        catch (Exception ex) { Log.Error(ex, "[PerfomanceMetrics] Ошибка при рисовании информации о производительности"); }
    }

    private static float GetFPS()
    {
        lock (_lock)
        {
            var currentTime = _stopwatch.ElapsedTicks;
            _frameTimestamps.Enqueue(currentTime);
            while (_frameTimestamps.TryPeek(out var oldestTime) && oldestTime < currentTime - (2 * Stopwatch.Frequency))
                _frameTimestamps.Dequeue();
            if (_frameTimestamps.Count > MAX_FRAMES) _frameTimestamps.Dequeue();
            if (_frameTimestamps.Count >= 2)
                _previousFps = (float)(_frameTimestamps.Count / ((_frameTimestamps.Last() - _frameTimestamps.Peek()) / (double)Stopwatch.Frequency));
            return _previousFps;
        }
    }

    private static double GetCpuUsage()
    {
        lock (_lock)
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                var currentCpuTime = process.TotalProcessorTime;
                var currentTotalTime = _stopwatch.Elapsed;
                if (_previousCpuTime == 0)
                {
                    _previousCpuTime = currentCpuTime.Ticks;
                    _previousTotalTime = currentTotalTime;
                    return _previousCpuUsage;
                }
                var cpuTimeDelta = currentCpuTime.Ticks - _previousCpuTime;
                var totalTimeDelta = currentTotalTime.TotalSeconds - _previousTotalTime.TotalSeconds;
                _previousCpuTime = currentCpuTime.Ticks;
                _previousTotalTime = currentTotalTime;
                return totalTimeDelta <= MIN_TIME_DELTA ? _previousCpuUsage :
                    _previousCpuUsage = (float)(_previousCpuUsage * (1 - SMOOTHING_FACTOR_CPU) +
                    ((cpuTimeDelta / (double)TimeSpan.TicksPerSecond) / (totalTimeDelta * _processorCount)) * 100 * SMOOTHING_FACTOR_CPU);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PerfomanceMetrics] Ошибка при получении использования CPU");
                return _previousCpuUsage;
            }
        }
    }

    private static double GetResourceUsage(ResourceType type) => type switch
    {
        ResourceType.Ram => Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0),
        _ => throw new ArgumentException($"[PerfomanceMetrics] Неподдерживаемый тип ресурса: {type}", nameof(type))
    };

    private enum ResourceType { Ram }
}