#nullable enable

namespace SpectrumNet;

public partial class App : Application
{
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
                    Fatal(nameof(App),
                          "Unhandled exception in application",
                          ex);
                }
                else
                {
                    Fatal(nameof(App),
                          $"Unhandled non-Exception object in application: {args.ExceptionObject}");
                }
            };

            base.OnStartup(e);
            CommonResources.InitialiseResources();
            SettingsWindow.Instance.LoadSettings();

            if (Resources["PaletteNameToBrushConverter"] is PaletteNameToBrushConverter conv)
                conv.BrushesProvider = SpectrumBrushes.Instance;

            Current.Exit += (_, _) =>
                Log(LogLevel.Information,
                    nameof(App),
                    "Application is shutting down normally",
                    forceLog: true);
        }
        catch (Exception ex)
        {
            try
            {
                Fatal(nameof(App),
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
                try { Shutdown(e.ApplicationExitCode); }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during SmartLogger.Shutdown: {ex.Message}");
                }

                try { SpectrumBrushes.Instance.Dispose(); }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error disposing SpectrumBrushes: {ex.Message}");
                }
            });

            if (!shutdown.Wait(3000))
                Log(LogLevel.Warning,
                    nameof(App),
                    "Shutdown process taking too long, forcing exit");
        }
        catch (Exception ex)
        {
            try
            {
                Log(LogLevel.Error,
                    nameof(App),
                    $"Error during shutdown: {ex.Message}");
            }
            catch { }
        }
    }
}