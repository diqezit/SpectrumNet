﻿namespace SpectrumNet;

public readonly record struct PerformanceMetrics(double FrameTime, double Fps);

public static class PerformanceMetricsManager
{
    private static class Constants
    {
        public const int MaxFrames = 120;
        public const float CpuSmoothing = 0.2f;
        public const float FpsSmoothing = 0.1f;
        public const string LogPrefix = "[PerformanceMetrics] ";
        public const double HighCpuThreshold = 80.0;
        public const double MediumCpuThreshold = 60.0;
        public const double HighMemoryThreshold = 1000.0;
        public const double MediumMemoryThreshold = 500.0;
        public const int LogFrequency = 300;
        public const int HistoryLength = 60;
        public const float DefaultFps = 60f;
        public const float MinGoodFps = 50f;
        public const float MinFairFps = 30f;
        public const double MinTimeDelta = 0.001;
        public const double MaxRealisticFps = 1000.0;
    }

    public enum PerformanceLevel
    {
        Excellent,
        Good,
        Fair,
        Poor
    }

    private record struct PerformanceSnapshot(
        float Fps,
        double CpuUsage,
        double RamUsageMb,
        DateTime Timestamp,
        PerformanceLevel Level);

    private static readonly object _syncLock = new();
    private static readonly int _processorCount = Environment.ProcessorCount;
    private static readonly double[] _frameTimes = new double[Constants.MaxFrames];
    private static readonly Stopwatch _timer = Stopwatch.StartNew();
    private static readonly TimeSpan _cpuUpdateInterval = TimeSpan.FromMilliseconds(100);
    private static readonly Queue<PerformanceSnapshot> _performanceHistory = new(Constants.HistoryLength);

    private static int _frameIndex;
    private static float _fpsCache = Constants.DefaultFps;
    private static TimeSpan _lastCpuTime = TimeSpan.Zero;
    private static double _lastTotalTime;
    private static DateTime _lastCpuUpdate = DateTime.UtcNow;
    private static double _cpuUsage;
    private static PerformanceLevel _currentLevel = PerformanceLevel.Good;
    private static DateTime _lastSnapshotTime = DateTime.UtcNow;
    private static readonly TimeSpan _snapshotInterval = TimeSpan.FromSeconds(1);
    private static bool _isInitialized;

    // Текстурный шрифт для OpenGL
    private static TextRenderer _textRenderer;

    public static event EventHandler<PerformanceMetrics>? PerformanceUpdated;
    public static event EventHandler<PerformanceLevel>? PerformanceLevelChanged;

    public static PerformanceLevel CurrentPerformanceLevel => _currentLevel;
    public static float CurrentFps => _fpsCache;
    public static double CurrentCpuUsage => _cpuUsage;
    public static TimeSpan UpTime => _timer.Elapsed;

    static PerformanceMetricsManager()
    {
        Initialize();
    }

    public static void Initialize()
    {
        if (_isInitialized) return;

        lock (_syncLock)
        {
            if (_isInitialized) return;

            _isInitialized = true;
            _timer.Start();

            // Инициализация рендерера текста
            _textRenderer = new TextRenderer();

            AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();

            SmartLogger.Log(LogLevel.Information, Constants.LogPrefix, "Performance monitoring initialized", forceLog: true);
        }
    }

    public static PerformanceMetrics UpdateMetrics()
    {
        float fps = CalculateFps();
        UpdateCpuUsage();
        double ramUsage = GetResourceUsage(ResourceType.Ram);

        if (fps <= 0) fps = _fpsCache > 0 ? _fpsCache : Constants.DefaultFps;

        var metrics = new PerformanceMetrics(
            _timer.Elapsed.TotalMilliseconds / Math.Max(1, _frameIndex),
            fps);

        UpdatePerformanceHistory(fps, _cpuUsage, ramUsage);

        PerformanceUpdated?.Invoke(null, metrics);
        return metrics;
    }

    public static void DrawPerformanceInfo(Viewport viewport, bool showPerformanceInfo)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return;

        if (!showPerformanceInfo)
            return;

        try
        {
            float fps = CalculateFps();
            UpdateCpuUsage();
            double ramUsage = GetResourceUsage(ResourceType.Ram);

            if (fps <= 0) fps = _fpsCache > 0 ? _fpsCache : Constants.DefaultFps;

            Color textColor = GetPerformanceColor(_currentLevel);

            // Настройка проекции для текста
            Matrix4 projection = Matrix4.CreateOrthographicOffCenter(
                0, viewport.Width, viewport.Height, 0, -1, 1);

            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadMatrix(ref projection);

            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadIdentity();

            string infoText = FormattableString.Invariant(
                $"RAM: {ramUsage:F1} MB | CPU: {_cpuUsage:F1}% | FPS: {fps:F0} | {_currentLevel}");

            // Отрисовка текста с использованием TextRenderer
            _textRenderer.RenderText(infoText, 10, 20, textColor);

            if (_frameIndex % 60 == 0)
            {
                SmartLogger.Log(LogLevel.Debug, Constants.LogPrefix, $"Current FPS: {fps:F1} | Level: {_currentLevel}", forceLog: false);
            }
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, Constants.LogPrefix, $"Error drawing performance info: {ex.Message}", forceLog: true);
        }
    }

    public static (DateTime Time, float Fps, double CpuUsage, double RamUsage)[] GetPerformanceHistory()
    {
        lock (_syncLock)
        {
            return _performanceHistory
                .Select(s => (s.Timestamp, s.Fps, s.CpuUsage, s.RamUsageMb))
                .ToArray();
        }
    }

    public static void ResetStatistics()
    {
        lock (_syncLock)
        {
            _performanceHistory.Clear();
            _frameIndex = 0;
            _fpsCache = Constants.DefaultFps;
            _cpuUsage = 0;
            _lastCpuTime = TimeSpan.Zero;
            _lastTotalTime = 0;
            _timer.Restart();

            SmartLogger.Log(LogLevel.Information, Constants.LogPrefix, "Performance statistics reset", forceLog: true);
        }
    }

    private static float CalculateFps()
    {
        lock (_syncLock)
        {
            double now = _timer.Elapsed.TotalSeconds;
            int currentIndex = _frameIndex++;
            _frameTimes[currentIndex % Constants.MaxFrames] = now;

            if (currentIndex < Constants.MaxFrames)
            {
                return _fpsCache > 0 ? _fpsCache : Constants.DefaultFps;
            }

            int firstFrameIndex = (currentIndex - Constants.MaxFrames + 1) % Constants.MaxFrames;
            double firstFrameTime = _frameTimes[firstFrameIndex];
            double delta = now - firstFrameTime;

            if (delta <= Constants.MinTimeDelta)
            {
                SmartLogger.Log(LogLevel.Warning, Constants.LogPrefix, $"Very small time delta detected: {delta}", forceLog: false);
                return _fpsCache > 0 ? _fpsCache : Constants.DefaultFps;
            }

            float newFps = (float)(Constants.MaxFrames / delta);

            if (newFps > 0 && newFps < Constants.MaxRealisticFps)
            {
                _fpsCache = _fpsCache * (1 - Constants.FpsSmoothing) + newFps * Constants.FpsSmoothing;
            }

            if (currentIndex % Constants.LogFrequency == 0)
            {
                SmartLogger.Log(LogLevel.Debug, Constants.LogPrefix,
                    $"FPS calculation: frames={Constants.MaxFrames}, delta={delta:F3}s, raw={newFps:F1}, smoothed={_fpsCache:F1}",
                    forceLog: false);
            }

            return _fpsCache;
        }
    }

    private static void UpdateCpuUsage()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCpuUpdate) < _cpuUpdateInterval)
            return;

        lock (_syncLock)
        {
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
                        _cpuUsage = _cpuUsage * (1 - Constants.CpuSmoothing) + instantUsage * Constants.CpuSmoothing;

                        if (_cpuUsage > Constants.HighCpuThreshold)
                        {
                            SmartLogger.Log(LogLevel.Warning, Constants.LogPrefix, $"High CPU usage: {_cpuUsage:F1}%", forceLog: false);
                        }
                    }
                    else
                    {
                        SmartLogger.Log(LogLevel.Warning, Constants.LogPrefix, "Invalid time delta in CPU calculation", forceLog: false);
                    }
                }

                _lastCpuTime = cpuTime;
                _lastTotalTime = elapsed;
                _lastCpuUpdate = now;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LogPrefix, $"Error updating CPU usage: {ex.Message}", forceLog: true);
            }
        }
    }

    private static double GetResourceUsage(ResourceType type)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            double usage = type switch
            {
                ResourceType.Ram => process.PagedMemorySize64 / (1024.0 * 1024.0),
                ResourceType.ManagedRam => Math.Max(
                    process.PagedMemorySize64 / (1024.0 * 1024.0),
                    GC.GetTotalMemory(false) / (1024.0 * 1024.0)),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown resource type")
            };

            if (usage > Constants.HighMemoryThreshold)
            {
                SmartLogger.Log(LogLevel.Warning, Constants.LogPrefix, $"High memory usage: {usage:F1} MB", forceLog: false);
            }

            return usage;
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, Constants.LogPrefix, $"Error calculating resource usage: {ex.Message}", forceLog: true);
            return 0;
        }
    }

    private static Color GetTextColor()
    {
        try
        {
            bool isDarkTheme = ThemeManager.Instance?.IsDarkTheme ?? false;
            return isDarkTheme ? Color.White : Color.Black;
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, Constants.LogPrefix, $"Error determining text color: {ex.Message}", forceLog: true);
            return Color.White;
        }
    }

    private static Color GetPerformanceColor(PerformanceLevel level)
    {
        return level switch
        {
            PerformanceLevel.Excellent => Color.LimeGreen,
            PerformanceLevel.Good => Color.DodgerBlue,
            PerformanceLevel.Fair => Color.Orange,
            PerformanceLevel.Poor => Color.Red,
            _ => GetTextColor()
        };
    }

    private static void UpdatePerformanceHistory(float fps, double cpuUsage, double ramUsage)
    {
        var now = DateTime.UtcNow;

        if ((now - _lastSnapshotTime) < _snapshotInterval)
            return;

        lock (_syncLock)
        {
            PerformanceLevel newLevel = CalculatePerformanceLevel(fps, cpuUsage, ramUsage);

            var snapshot = new PerformanceSnapshot(fps, cpuUsage, ramUsage, now, newLevel);

            if (_performanceHistory.Count >= Constants.HistoryLength)
                _performanceHistory.Dequeue();

            _performanceHistory.Enqueue(snapshot);

            if (newLevel != _currentLevel)
            {
                _currentLevel = newLevel;
                SmartLogger.Log(LogLevel.Information, Constants.LogPrefix,
                    $"Performance level changed to {newLevel}: FPS={fps:F1}, CPU={cpuUsage:F1}%, RAM={ramUsage:F1}MB",
                    forceLog: false);

                PerformanceLevelChanged?.Invoke(null, newLevel);
            }

            _lastSnapshotTime = now;
        }
    }

    private static PerformanceLevel CalculatePerformanceLevel(float fps, double cpuUsage, double ramUsage)
    {
        bool hasHighCpu = cpuUsage > Constants.HighCpuThreshold;
        bool hasMediumCpu = cpuUsage > Constants.MediumCpuThreshold;
        bool hasHighMemory = ramUsage > Constants.HighMemoryThreshold;
        bool hasMediumMemory = ramUsage > Constants.MediumMemoryThreshold;

        if (fps >= Constants.MinGoodFps && !hasHighCpu && !hasHighMemory)
        {
            return !hasMediumCpu && !hasMediumMemory
                ? PerformanceLevel.Excellent
                : PerformanceLevel.Good;
        }
        else if (fps >= Constants.MinFairFps && !hasHighCpu)
        {
            return PerformanceLevel.Fair;
        }
        else
        {
            return PerformanceLevel.Poor;
        }
    }

    private static void Cleanup()
    {
        try
        {
            PerformanceUpdated = null;
            PerformanceLevelChanged = null;

            lock (_syncLock)
            {
                _performanceHistory.Clear();
            }

            // Освобождение ресурсов текстового рендерера
            _textRenderer?.Dispose();

            SmartLogger.Log(LogLevel.Information, Constants.LogPrefix, "Performance monitoring resources cleaned up", forceLog: true);
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, Constants.LogPrefix, $"Error during cleanup: {ex.Message}", forceLog: true);
        }
    }

    private enum ResourceType
    {
        Ram,
        ManagedRam
    }
}