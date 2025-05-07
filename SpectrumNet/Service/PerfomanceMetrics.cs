#nullable enable

namespace SpectrumNet.Service;

public readonly record struct PerformanceMetrics(
    double FrameTime,
    double Fps);

public enum PerformanceLevel { Excellent, Good, Fair, Poor }

public static class PerformanceMetricsManager
{
    private static readonly object _syncLock = new();
    private static readonly int _processorCount = Environment.ProcessorCount;
    private static readonly double[] _frameTimes = new double[Constants.MAX_FRAMES];
    private static readonly Stopwatch _timer = Stopwatch.StartNew();
    private static readonly TimeSpan _cpuUpdateInterval = TimeSpan.FromMilliseconds(100);
    private static readonly Queue<PerformanceSnapshot> _performanceHistory = new(Constants.HISTORY_LENGTH);
    private static readonly TimeSpan _snapshotInterval = TimeSpan.FromSeconds(1);

    private static int _frameIndex;
    private static float _fpsCache = Constants.DEFAULT_FPS;
    private static TimeSpan _lastCpuTime = TimeSpan.Zero;
    private static double _lastTotalTime;
    private static double _cpuUsage;
    private static double _ramUsage;
    private static PerformanceLevel _currentLevel = PerformanceLevel.Good;
    private static DateTime _lastSnapshotTime = DateTime.UtcNow;
    private static bool _isInitialized;
    private static CancellationTokenSource _cts = new();

    public static event EventHandler<PerformanceMetrics>? PerformanceUpdated;
    public static event EventHandler<PerformanceLevel>? PerformanceLevelChanged;
    public static PerformanceLevel CurrentPerformanceLevel => _currentLevel;
    public static float CurrentFps => _fpsCache;
    public static double CurrentCpuUsage => _cpuUsage;
    public static TimeSpan UpTime => _timer.Elapsed;

    private record struct PerformanceSnapshot(
        float Fps,
        double CpuUsage,
        double RamUsageMb,
        DateTime Timestamp,
        PerformanceLevel Level);

    private enum ResourceType { Ram, ManagedRam }

    private record Constants
    {
        public const double
            HIGH_CPU_THRESHOLD = 80.0,
            MEDIUM_CPU_THRESHOLD = 60.0,
            HIGH_MEMORY_THRESHOLD = 1000.0,
            MEDIUM_MEMORY_THRESHOLD = 500.0,
            MIN_TIME_DELTA = 0.001,
            MAX_REALISTIC_FPS = 1000.0;

        public const float
            DEFAULT_FPS = 60f,
            MIN_GOOD_FPS = 50f,
            MIN_FAIR_FPS = 30f,
            CPU_SMOOTHING = 0.2f,
            FPS_SMOOTHING = 0.1f;

        public const int
            MAX_FRAMES = 120,
            LOG_FREQUENCY = 300,
            HISTORY_LENGTH = 60;

        public const string LOG_PREFIX = "PerformanceMetrics";
    }

    static PerformanceMetricsManager() => Initialize();

    public static void Initialize()
    {
        if (_isInitialized) return;

        lock (_syncLock)
        {
            if (_isInitialized) return;
            _isInitialized = true;
            _timer.Start();
            AppDomain.CurrentDomain.ProcessExit += HandleApplicationExit;
            Log(LogLevel.Information,
                Constants.LOG_PREFIX,
                "Performance monitoring initialized",
                forceLog: true);

            _cts = new CancellationTokenSource();
            Task.Run(() => UpdateMetricsAsync(_cts.Token));
        }
    }

    private static void HandleApplicationExit(object? sender, EventArgs e) =>
        Cleanup();

    public static void RecordFrameTime()
    {
        lock (_syncLock)
        {
            double now = _timer.Elapsed.TotalSeconds;
            int currentIndex = _frameIndex++;
            _frameTimes[currentIndex % Constants.MAX_FRAMES] = now;
        }
    }

    private static async Task UpdateMetricsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(100, token);

                lock (_syncLock)
                {
                    float fps = CalculateFpsInternal();
                    double cpuUsage = CalculateCpuUsage();
                    _ramUsage = GetResourceUsage(ResourceType.Ram);

                    _fpsCache = fps;
                    _cpuUsage = cpuUsage;

                    UpdatePerformanceHistory(fps, cpuUsage, _ramUsage);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, Constants.LOG_PREFIX, $"Error in background metrics update: {ex.Message}", forceLog: true);
            }
        }
    }

    private static float CalculateFpsInternal()
    {
        if (_frameIndex < Constants.MAX_FRAMES)
            return Constants.DEFAULT_FPS;

        int oldestIndex = (_frameIndex - Constants.MAX_FRAMES) % Constants.MAX_FRAMES;
        int newestIndex = (_frameIndex - 1) % Constants.MAX_FRAMES;

        double oldestTime = _frameTimes[oldestIndex];
        double newestTime = _frameTimes[newestIndex];

        double delta = newestTime - oldestTime;

        if (delta <= Constants.MIN_TIME_DELTA)
            return Constants.DEFAULT_FPS;

        return (float)(Constants.MAX_FRAMES / delta);
    }

    private static double CalculateCpuUsage()
    {
        using var process = Process.GetCurrentProcess();
        var cpuTime = process.TotalProcessorTime;
        double elapsed = _timer.Elapsed.TotalSeconds;

        double cpuUsage = 0;

        if (_lastCpuTime != TimeSpan.Zero)
        {
            double cpuDelta = (cpuTime - _lastCpuTime).TotalSeconds;
            double timeDelta = elapsed - _lastTotalTime;

            if (timeDelta > 0)
            {
                double instantUsage = cpuDelta / (timeDelta * _processorCount) * 100;
                cpuUsage = _cpuUsage * (1 - Constants.CPU_SMOOTHING) + instantUsage * Constants.CPU_SMOOTHING;
            }
        }

        _lastCpuTime = cpuTime;
        _lastTotalTime = elapsed;

        return cpuUsage;
    }

    public static void DrawPerformanceInfo(
        SKCanvas? canvas,
        SKImageInfo info,
        bool showPerformanceInfo)
    {
        if (!CanDrawPerformanceInfo(canvas, info, showPerformanceInfo)) return;

        Safe(() =>
        {
            lock (_syncLock)
            {
                float fps = _fpsCache;
                double cpuUsage = _cpuUsage;
                double ramUsage = _ramUsage;

                string fpsLimiterInfo = IsFpsLimiterEnabled() ? " [60 FPS Lock]" : "";

                DrawMetricsText(canvas!, info, fps, ramUsage, fpsLimiterInfo);
            }
        },
        new ErrorHandlingOptions
        {
            Source = Constants.LOG_PREFIX,
            ErrorMessage = "Error drawing performance info"
        });
    }

    private static bool IsFpsLimiterEnabled()
    {
        var controller = TryGetController();
        if (controller != null)
        {
            return controller.LimitFpsTo60;
        }

        try
        {
            return Settings.Instance.LimitFpsTo60;
        }
        catch
        {
            Log(LogLevel.Warning, Constants.LOG_PREFIX,
                "Failed to get FPS limiter status from settings",
                forceLog: true);
            return false;
        }
    }

    private static IMainController? TryGetController()
    {
        try
        {
            if (Application.Current?.MainWindow?.DataContext is IMainController mainController)
            {
                return mainController;
            }

            if (Application.Current?.MainWindow != null)
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window.DataContext is IMainController controller)
                    {
                        return controller;
                    }
                }
            }

            Log(LogLevel.Warning, Constants.LOG_PREFIX,
                "Cannot determine FPS limiter status - controller not found in any window",
                forceLog: true);
            return null;
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX,
                $"Error trying to get controller: {ex.Message}",
                forceLog: true);
            return null;
        }
    }

    private static bool CanDrawPerformanceInfo(
        SKCanvas? canvas,
        SKImageInfo info,
        bool showPerformanceInfo) =>
        canvas != null && info.Width > 0 && info.Height > 0 && showPerformanceInfo;

    private static void DrawMetricsText(
        SKCanvas canvas,
        SKImageInfo info,
        float fps,
        double ramUsage,
        string fpsLimiterInfo)
    {
        using var font = new SKFont { Size = 12, Edging = SKFontEdging.SubpixelAntialias };
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = GetPerformanceColor(_currentLevel)
        };

        string infoText = string.Create(CultureInfo.InvariantCulture,
            $"RAM: {ramUsage:F1} MB | CPU: {_cpuUsage:F1}% | FPS: {fps:F0}{fpsLimiterInfo} | {_currentLevel}");

        canvas.DrawText(infoText, 10, 20, SKTextAlign.Left, font, paint);
    }

    private static double GetResourceUsage(ResourceType type)
    {
        using var process = Process.GetCurrentProcess();
        double usage = CalculateResourceUsage(type, process);

        if (usage > Constants.HIGH_MEMORY_THRESHOLD)
        {
            Log(LogLevel.Warning,
                Constants.LOG_PREFIX,
                $"High memory usage: {usage:F1} MB",
                forceLog: false);
        }

        return usage;
    }

    private static double CalculateResourceUsage(ResourceType type, Process process) =>
        type switch
        {
            ResourceType.Ram => process.PagedMemorySize64 / (1024.0 * 1024.0),
            ResourceType.ManagedRam => Max(
                process.PagedMemorySize64 / (1024.0 * 1024.0),
                GetTotalMemory(false) / (1024.0 * 1024.0)),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown resource type")
        };

    private static SKColor GetTextColor()
    {
        try
        {
            bool isDarkTheme = ThemeManager.Instance?.IsDarkTheme ?? false;
            return isDarkTheme ? SKColors.White : SKColors.Black;
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                Constants.LOG_PREFIX,
                $"Error determining text color: {ex.Message}",
                forceLog: true);
            return SKColors.White;
        }
    }

    private static SKColor GetPerformanceColor(PerformanceLevel level) =>
        level switch
        {
            PerformanceLevel.Excellent => SKColors.LimeGreen,
            PerformanceLevel.Good => SKColors.DodgerBlue,
            PerformanceLevel.Fair => SKColors.Orange,
            PerformanceLevel.Poor => SKColors.Red,
            _ => GetTextColor()
        };

    private static void UpdatePerformanceHistory(float fps, double cpuUsage, double ramUsage)
    {
        var now = DateTime.UtcNow;
        if (now - _lastSnapshotTime < _snapshotInterval) return;

        lock (_syncLock)
        {
            PerformanceLevel newLevel = CalculatePerformanceLevel(fps, cpuUsage, ramUsage);
            var snapshot = CreatePerformanceSnapshot(fps, cpuUsage, ramUsage, now, newLevel);

            UpdateHistoryQueue(snapshot);
            UpdatePerformanceLevel(newLevel, fps, cpuUsage, ramUsage);
            _lastSnapshotTime = now;
        }
    }

    private static PerformanceSnapshot CreatePerformanceSnapshot(
        float fps,
        double cpuUsage,
        double ramUsage,
        DateTime timestamp,
        PerformanceLevel level) =>
        new(fps, cpuUsage, ramUsage, timestamp, level);

    private static void UpdateHistoryQueue(PerformanceSnapshot snapshot)
    {
        if (_performanceHistory.Count >= Constants.HISTORY_LENGTH)
        {
            _performanceHistory.Dequeue();
        }

        _performanceHistory.Enqueue(snapshot);
    }

    private static void UpdatePerformanceLevel(
        PerformanceLevel newLevel,
        float fps,
        double cpuUsage,
        double ramUsage)
    {
        if (newLevel != _currentLevel)
        {
            _currentLevel = newLevel;
            Log(LogLevel.Information,
                Constants.LOG_PREFIX,
                $"Performance level changed to {newLevel}: " +
                $"FPS={fps:F1}, CPU={cpuUsage:F1}%, RAM={ramUsage:F1}MB",
                forceLog: false);
            PerformanceLevelChanged?.Invoke(null, newLevel);
        }
    }

    private static PerformanceLevel CalculatePerformanceLevel(
        float fps,
        double cpuUsage,
        double ramUsage)
    {
        bool hasHighCpu = cpuUsage > Constants.HIGH_CPU_THRESHOLD;
        bool hasMediumCpu = cpuUsage > Constants.MEDIUM_CPU_THRESHOLD;
        bool hasHighMemory = ramUsage > Constants.HIGH_MEMORY_THRESHOLD;
        bool hasMediumMemory = ramUsage > Constants.MEDIUM_MEMORY_THRESHOLD;

        return (fps, hasHighCpu, hasMediumCpu, hasHighMemory, hasMediumMemory) switch
        {
            ( >= Constants.MIN_GOOD_FPS, false, false, false, false) => PerformanceLevel.Excellent,
            ( >= Constants.MIN_GOOD_FPS, false, _, false, _) => PerformanceLevel.Good,
            ( >= Constants.MIN_FAIR_FPS, false, _, _, _) => PerformanceLevel.Fair,
            _ => PerformanceLevel.Poor
        };
    }

    private static void Cleanup()
    {
        Safe(() =>
        {
            _cts.Cancel();
            _cts.Dispose();
            ClearEventHandlers();
            ClearPerformanceHistory();
            Log(LogLevel.Information,
                Constants.LOG_PREFIX,
                "Performance monitoring resources cleaned up",
                forceLog: true);
        },
        new ErrorHandlingOptions
        {
            Source = Constants.LOG_PREFIX,
            ErrorMessage = "Error during cleanup"
        });
    }

    private static void ClearEventHandlers()
    {
        PerformanceUpdated = null;
        PerformanceLevelChanged = null;
    }

    private static void ClearPerformanceHistory()
    {
        lock (_syncLock)
        {
            _performanceHistory.Clear();
        }
    }
}