#nullable enable

using SpectrumNet.Service;
using SpectrumNet.Service.Enums;

namespace SpectrumNet;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            // Включаем аппаратное ускорение для SKGLElement
            RenderOptions.ProcessRenderMode = RenderMode.Default;

            SmartLogger.Initialize();

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var logger = SmartLogger.LoggerFactory.CreateLogger<App>();
                logger.LogCritical(args.ExceptionObject as Exception, "Unhandled exception in application");
            };

            base.OnStartup(e);
            SpectrumNet.CommonResources.InitialiseResources();

            // Установка BrushesProvider для PaletteNameToBrushConverter
            if (Resources["PaletteNameToBrushConverter"] is PaletteNameToBrushConverter converter)
            {
                converter.BrushesProvider = SpectrumBrushes.Instance;
            }

            Current.Exit += (_, args) => SmartLogger.Log(LogLevel.Information, "App",
                "Application is shutting down normally", forceLog: true);
        }
        catch (Exception)
        {
            // При ошибке инициализации логирования
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SmartLogger.Shutdown(e.ApplicationExitCode);
        SpectrumBrushes.Instance.Dispose();
        base.OnExit(e);
    }
}