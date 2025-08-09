#nullable enable

namespace SpectrumNet;

public partial class App : Application
{
    private const string LogPrefix = nameof(App);
    private readonly ISmartLogger _logger = Instance;
    private const int SHUTDOWN_TIMEOUT_MS = 5000;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            RenderOptions.ProcessRenderMode = RenderMode.Default;
            Initialize();
            SetupExceptionHandling();
            InitializeApplication();
            RegisterApplicationExitHandler();
        }
        catch (Exception ex)
        {
            LogCriticalStartupError(ex);
            Shutdown(-1);
        }
    }

    private void SetupExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                _logger.Error(LogPrefix, "Unhandled exception in application", ex);
            }
            else
            {
                _logger.Fatal(LogPrefix, $"Unhandled non-Exception object in application: {args.ExceptionObject}");
            }
        };
    }

    private void InitializeApplication()
    {
        base.OnStartup(null);
        CommonResources.InitialiseResources();
        SettingsWindow.Instance.LoadSettings();
        InitializeBrushProvider();
    }

    private void InitializeBrushProvider()
    {
        if (Resources["PaletteNameToBrushConverter"] is PaletteNameToBrushConverter conv)
            conv.BrushesProvider = SpectrumBrushes.Instance;
    }

    private void RegisterApplicationExitHandler()
    {
        Current.Exit += (_, _) =>
            _logger.Log(LogLevel.Information,
                LogPrefix,
                "Application is shutting down normally",
                forceLog: true);
    }

    private void LogCriticalStartupError(Exception ex) =>
        _logger.Error(LogPrefix, "Critical error during startup", ex);

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        PerformShutdownCleanup();
    }

    private void PerformShutdownCleanup()
    {
        try
        {
            var shutdownTask = Task.Run(CleanupResources);

            if (!shutdownTask.Wait(SHUTDOWN_TIMEOUT_MS))
            {
                LogShutdownTimeout();
            }
        }
        catch (Exception ex)
        {
            LogShutdownError(ex);
        }
    }

    private void CleanupResources()
    {
        try
        {
            DisposeSpectrumBrushes();
            LogResourceCleanupCompleted();
        }
        catch (Exception ex)
        {
            LogResourceCleanupError(ex);
        }
    }

    private void DisposeSpectrumBrushes()
    {
        if (SpectrumBrushes.Instance != null)
        {
            _logger.Log(LogLevel.Information,
                LogPrefix,
                "Disposing SpectrumBrushes instance",
                forceLog: true);

            SpectrumBrushes.Instance.Dispose();
        }
    }

    private void LogResourceCleanupCompleted()
    {
        _logger.Log(LogLevel.Information,
            LogPrefix,
            "Resource cleanup completed",
            forceLog: true);
    }

    private void LogResourceCleanupError(Exception ex)
    {
        _logger.Log(LogLevel.Error,
            LogPrefix,
            $"Error during shutdown resource cleanup: {ex.Message}",
            forceLog: true);
    }

    private void LogShutdownTimeout()
    {
        _logger.Log(LogLevel.Warning,
            LogPrefix,
            "Shutdown resources cleanup taking too long, continuing with exit",
            forceLog: true);
    }

    private void LogShutdownError(Exception ex)
    {
        _logger.Log(LogLevel.Error,
            LogPrefix,
            $"Error during shutdown: {ex.Message}",
            forceLog: true);
    }
}