namespace SpectrumNet;

public partial class App : Application
{
    private const int Timeout = 5000;

    private IServiceProvider? _svc;
    private ISmartLogger? _log;
    private ISettingsService? _cfg;

    public static IServiceProvider Services => ((App)Current)._svc!;

    public static T Get<T>() where T : class =>
        Services.GetRequiredService<T>();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            Configure();

            _log = _svc!.GetRequiredService<ISmartLogger>();
            _cfg = _svc!.GetRequiredService<ISettingsService>();

            SetupExc();
            CommonResources.InitialiseResources();

            _cfg.Load();
            InitTheme();
            InitBrush();
            CreateMain();
        }
        catch (Exception ex)
        {
            _log?.Error(nameof(App), "Startup error", ex);
            Shutdown(-1);
        }
    }

    private void Configure()
    {
        var sc = new ServiceCollection();

        sc.AddSingleton<ISmartLogger>(_ => Instance);
        sc.AddSingleton<ISettingsService>(_ => SettingsService.Instance);
        sc.AddSingleton<IThemes>(_ => Theme.Instance);
        sc.AddSingleton<ITransparencyManager>(_ => TransparencyManager.Instance);
        sc.AddSingleton<IBrushProvider>(_ => SpectrumBrushes.Instance);

        sc.AddSingleton<IRendererFactory>(p =>
        {
            RendererFactory r = RendererFactory.Instance;
            r.Initialize(
                p.GetRequiredService<ISmartLogger>(),
                p.GetRequiredService<ITransparencyManager>(),
                RenderQuality.Medium);
            return r;
        });

        sc.AddSingleton<IPerformanceMetricsManager>(_ =>
        {
            PerformanceMetricsManager m = PerformanceMetricsManager.Instance;
            m.Initialize();
            return m;
        });

        sc.AddSingleton<MainWindow>();
        sc.AddSingleton(CreateCtrl);

        sc.AddTransient(p =>
            new ControlPanelWindow(p.GetRequiredService<AppController>()));

        sc.AddTransient<SettingsWindow>();

        _svc = sc.BuildServiceProvider();
    }

    private AppController CreateCtrl(IServiceProvider p)
    {
        MainWindow mw = p.GetRequiredService<MainWindow>();

        return new AppController(
            mw,
            mw.SpectrumCanvas,
            p.GetRequiredService<ISettingsService>(),
            p.GetRequiredService<ISmartLogger>(),
            p.GetRequiredService<IRendererFactory>(),
            p.GetRequiredService<IBrushProvider>(),
            p.GetRequiredService<IThemes>(),
            p.GetRequiredService<ITransparencyManager>(),
            p.GetRequiredService<IPerformanceMetricsManager>());
    }

    private void CreateMain()
    {
        MainWindow mw = _svc!.GetRequiredService<MainWindow>();
        AppController c = _svc!.GetRequiredService<AppController>();

        mw.Initialize(c);
        mw.Show();
    }

    private void InitTheme()
    {
        if (_svc == null || _cfg == null) return;

        IThemes t = _svc.GetRequiredService<IThemes>();

        if (t.IsDarkTheme != _cfg.Current.General.IsDarkTheme)
            t.SetTheme(_cfg.Current.General.IsDarkTheme);
    }

    private void InitBrush()
    {
        if (_svc == null) return;

        if (Resources["PaletteNameToBrushConverter"] is PaletteNameToBrushConverter c)
            c.BrushesProvider =
                _svc.GetRequiredService<IBrushProvider>() as SpectrumBrushes;
    }

    private void SetupExc()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
        {
            if (a.ExceptionObject is Exception ex)
                _log?.Error(nameof(App), "Unhandled", ex);
            else
                _log?.Fatal(nameof(App), $"Unhandled: {a.ExceptionObject}");
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DoShutdown();
        base.OnExit(e);
    }

    private void DoShutdown()
    {
        try
        {
            _cfg?.Apply(_cfg.Current);

            var t = Task.Run(DisposeAsync);

            if (!t.Wait(Timeout))
                _log?.Log(
                    LogLevel.Warning,
                    nameof(App),
                    "Timeout",
                    forceLog: true);
        }
        catch (Exception ex)
        {
            _log?.Log(
                LogLevel.Error,
                nameof(App),
                ex.Message,
                forceLog: true);
        }
    }

    private async Task DisposeAsync()
    {
        try
        {
            if (_svc is IAsyncDisposable ad)
                await ad.DisposeAsync().ConfigureAwait(false);
            else
                (_svc as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            _log?.Log(
                LogLevel.Error,
                nameof(App),
                ex.Message,
                forceLog: true);
        }
    }
}
