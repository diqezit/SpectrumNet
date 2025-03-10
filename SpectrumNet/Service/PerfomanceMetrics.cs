namespace SpectrumNet;

using System;
using System.Diagnostics;

public readonly record struct PerformanceMetrics(double FrameTime, double Fps, double CpuUsage, double RamUsageMb);

public static class PerformanceMetricsManager
{
    private static readonly int _processorCount = Environment.ProcessorCount;
    private static readonly double[] _frameTimes = new double[60]; 
    private static readonly Stopwatch _timer = Stopwatch.StartNew();

    private static int _frameIndex;
    private static float _currentFps = 60f;
    private static TimeSpan _lastCpuTime = TimeSpan.Zero;
    private static double _lastTotalTime;
    private static DateTime _lastCpuUpdate = DateTime.UtcNow;
    private static double _cpuUsage;

    public static event EventHandler<PerformanceMetrics>? PerformanceUpdated;

    public static float CurrentFps => _currentFps;
    public static double CurrentCpuUsage => _cpuUsage;

    public static PerformanceMetrics UpdateMetrics()
    {
        float fps = CalculateFps();
        UpdateCpuUsage();
        double ramUsage = GetRamUsage();

        var metrics = new PerformanceMetrics(
            _timer.Elapsed.TotalMilliseconds / Math.Max(1, _frameIndex),
            fps,
            _cpuUsage,
            ramUsage);

        PerformanceUpdated?.Invoke(null, metrics);

        return metrics;
    }

    private static float CalculateFps()
    {
        double now = _timer.Elapsed.TotalSeconds;
        int currentIndex = _frameIndex++;
        _frameTimes[currentIndex % _frameTimes.Length] = now;

        if (currentIndex < _frameTimes.Length)
        {
            return _currentFps;
        }

        int firstFrameIndex = (currentIndex - _frameTimes.Length + 1) % _frameTimes.Length;
        double firstFrameTime = _frameTimes[firstFrameIndex];
        double delta = now - firstFrameTime;

        if (delta <= 0.001)
        {
            return _currentFps;
        }

        float newFps = (float)(_frameTimes.Length / delta);

        if (newFps > 0 && newFps < 1000.0)
        {
            _currentFps = _currentFps * 0.9f + newFps * 0.1f;
        }

        return _currentFps;
    }

    private static void UpdateCpuUsage()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCpuUpdate) < TimeSpan.FromMilliseconds(100))
            return;

        try
        {
            using var process = Process.GetCurrentProcess();
            var cpuTime = process.TotalProcessorTime;
            double elapsed = _timer.Elapsed.TotalSeconds;

            if (_lastCpuTime != TimeSpan.Zero)
            {
                double cpuDelta = (cpuTime - _lastCpuTime).TotalSeconds;
                double timeDelta = elapsed - _lastTotalTime;

                if (timeDelta > 0)
                {
                    double instantUsage = cpuDelta / (timeDelta * _processorCount) * 100;
                    _cpuUsage = _cpuUsage * 0.8 + instantUsage * 0.2;
                }
            }

            _lastCpuTime = cpuTime;
            _lastTotalTime = elapsed;
            _lastCpuUpdate = now;
        }
        catch
        {
            // Silently handle errors
        }
    }

    private static double GetRamUsage()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.PagedMemorySize64 / (1024.0 * 1024.0); 
        }
        catch
        {
            return 0;
        }
    }

    public static void DrawPerformanceInfo(Viewport viewport, bool showPerformanceInfo)
    {
        if (!showPerformanceInfo || viewport.Width <= 0 || viewport.Height <= 0)
            return;

        // Here you would implement the actual rendering logic
        // For example:
        // Draw text at positions relative to the viewport dimensions:
        // RenderText($"FPS: {CurrentFps:F1}", viewport.X + 10, viewport.Y + 10, color);
        // RenderText($"CPU: {CurrentCpuUsage:F1}%", viewport.X + 10, viewport.Y + 30, color);
        // RenderText($"RAM: {GetRamUsage():F1} MB", viewport.X + 10, viewport.Y + 50, color);
    }
}