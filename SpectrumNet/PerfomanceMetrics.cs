public static class PerfomanceMetrics
{
    private const int MaxFrames = 120;
    private const float CpuSmoothing = 0.2f;
    private static readonly object Lock = new();
    private static readonly int ProcessorCount = Environment.ProcessorCount;
    private static readonly double[] FrameTimes = new double[MaxFrames];
    private static readonly Stopwatch Timer = Stopwatch.StartNew();

    private static int FrameIndex;
    private static float FpsCache;
    private static TimeSpan LastCpuTime = TimeSpan.Zero;
    private static double LastTotalTime;
    private static DateTime LastCpuUpdate = DateTime.UtcNow;
    private static double CpuUsage;

    public static void DrawPerformanceInfo(SKCanvas canvas, SKImageInfo info)
    {
        try
        {
            float fps = CalculateFps();
            UpdateCpuUsage();

            using var paint = new SKPaint
            {
                TextSize = 12,
                IsAntialias = true,
                Color = GetTextColor()
            };

            string infoText = $"RAM: {GetResourceUsage(ResourceType.Ram):F1} MB | " +
                              $"CPU: {CpuUsage:F1}% | FPS: {fps:F0}";
            canvas.DrawText(infoText, 10, 20, paint);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PerformanceMetrics] Error drawing performance info");
        }
    }

    private static float CalculateFps()
    {
        lock (Lock)
        {
            double now = Timer.Elapsed.TotalSeconds;
            FrameTimes[FrameIndex++ % MaxFrames] = now;

            if (FrameIndex < MaxFrames) return FpsCache;

            double delta = now - FrameTimes[(FrameIndex - MaxFrames) % MaxFrames];
            return delta > 0 ? FpsCache = FpsCache * 0.9f + (float)(MaxFrames / delta) * 0.1f : FpsCache;
        }
    }

    private static void UpdateCpuUsage()
    {
        if ((DateTime.UtcNow - LastCpuUpdate).TotalSeconds < 0.1) return;

        lock (Lock)
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                var cpuTime = process.TotalProcessorTime;
                double elapsed = Timer.Elapsed.TotalSeconds;

                if (LastCpuTime != TimeSpan.Zero)
                {
                    double cpuDelta = (cpuTime - LastCpuTime).TotalSeconds;
                    double timeDelta = elapsed - LastTotalTime;

                    if (timeDelta > 0)
                        CpuUsage = CpuUsage * (1 - CpuSmoothing) +
                                   (cpuDelta / (timeDelta * ProcessorCount) * 100) * CpuSmoothing;
                }

                LastCpuTime = cpuTime;
                LastTotalTime = elapsed;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PerformanceMetrics] Error updating CPU usage");
            }

            LastCpuUpdate = DateTime.UtcNow;
        }
    }

    private static double GetResourceUsage(ResourceType type)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return type == ResourceType.Ram
                ? process.PagedMemorySize64 / (1024.0 * 1024.0)
                : Math.Max(process.PagedMemorySize64 / (1024.0 * 1024.0), GC.GetTotalMemory(false) / (1024.0 * 1024.0));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PerformanceMetrics] Error calculating resource usage");
            return 0;
        }
    }

    private static SKColor GetTextColor()
    {
        bool isDarkTheme = ThemeManager.Instance.IsDarkTheme;
        return isDarkTheme ? SKColors.White : SKColors.Black;
    }

    private enum ResourceType { Ram, ManagedRam }
}