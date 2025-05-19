#nullable enable

namespace SpectrumNet;

public partial class App : Application
{
    private const string LogPrefix = nameof(App);
    private readonly ISmartLogger _logger = Instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            RenderOptions.ProcessRenderMode = RenderMode.Default;
            Initialize();

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    _logger.Error(LogPrefix,
                          "Unhandled exception in application",
                          ex);
                }
                else
                {
                    _logger.Fatal(LogPrefix,
                          $"Unhandled non-Exception object in application: {args.ExceptionObject}");
                }
            };

            base.OnStartup(e);
            CommonResources.InitialiseResources();
            SettingsWindow.Instance.LoadSettings();

            if (Resources["PaletteNameToBrushConverter"] is PaletteNameToBrushConverter conv)
                conv.BrushesProvider = SpectrumBrushes.Instance;

            Current.Exit += (_, _) =>
                _logger.Log(LogLevel.Information,
                    LogPrefix,
                    "Application is shutting down normally",
                    forceLog: true);
        }
        catch (Exception ex)
        {
            try
            {
                _logger.Error(LogPrefix,
                      "Critical error during startup",
                      ex);
            }
            catch { }
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);

        try
        {
            var shutdown = Task.Run(() =>
            {
                _logger.Safe(() => Shutdown(e.ApplicationExitCode),
                    LogPrefix,
                    "Error during SmartLogger.Shutdown");

                _logger.Safe(() => SpectrumBrushes.Instance.Dispose(),
                    LogPrefix,
                    "Error disposing SpectrumBrushes");
            });

            if (!shutdown.Wait(3000))
                _logger.Log(LogLevel.Warning,
                    LogPrefix,
                    "Shutdown process taking too long, forcing exit");
        }
        catch (Exception ex)
        {
            try
            {
                _logger.Log(LogLevel.Error,
                    LogPrefix,
                    $"Error during shutdown: {ex.Message}");
            }
            catch { }
        }
    }
}