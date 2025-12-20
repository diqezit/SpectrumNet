namespace SpectrumNet.SN.Settings;

[AttributeUsage(AttributeTargets.Property)]
public sealed class SliderAttribute(
    int order = int.MaxValue,
    double min = double.NaN,
    double max = double.NaN,
    string? description = null) : Attribute
{
    public int Order { get; } = order;
    public string? Description { get; } = description;
    public double Min { get; } = min;
    public double Max { get; } = max;
    public double AutoMaxMultiplier { get; init; } = 3.0;
}

public record ParticlesConfig
{
    [Slider(0, 100, 5000, "Max particles.")]
    public int MaxParticles { get; init; } = 600;

    [Slider(1, 0.5, 10.0, "Lifetime (sec).")]
    public float ParticleLife { get; init; } = 3.0f;

    [Slider(2, 0.005, 0.1, "Decay rate.")]
    public float ParticleLifeDecay { get; init; } = 0.016f;
}

public record RaindropsConfig
{
    [Slider(0, 50, 1000)]
    public int MaxRaindrops { get; init; } = 200;

    [Slider(1, 1, 30)]
    public float BaseFallSpeed { get; init; } = 12f;
}

public record WindowConfig
{
    public double Left { get; init; } = 100;
    public double Top { get; init; } = 100;
    public double Width { get; init; } = 800;
    public double Height { get; init; } = 600;
    public WindowState State { get; init; } = WindowState.Normal;
    public bool IsControlPanelVisible { get; init; } = true;
}

public record VisualizationConfig
{
    public RenderStyle SelectedRenderStyle { get; init; } = RenderStyle.Bars;
    public FftWindowType SelectedFftWindowType { get; init; } = FftWindowType.Hann;
    public SpectrumScale SelectedScaleType { get; init; } = SpectrumScale.Linear;
    public StereoMode SelectedStereoMode { get; init; } = StereoMode.Mid;
    public RenderQuality SelectedRenderQuality { get; init; } = RenderQuality.Medium;
    public int BarCount { get; init; } = 60;
    public double BarSpacing { get; init; } = 4;
    public string SelectedPalette { get; init; } = "Solid";
    public bool ShowPerformanceInfo { get; init; } = true;
}

public record AudioConfig
{
    public float MinDbLevel { get; init; } = -130f;
    public float MaxDbLevel { get; init; } = -20f;
    public float AmplificationFactor { get; init; } = 2.0f;
}

public record GeneralConfig
{
    public bool IsOverlayTopmost { get; init; } = true;
    public bool IsDarkTheme { get; init; } = true;
    public bool LimitFpsTo60 { get; init; }
    public ImmutableArray<RenderStyle> FavoriteRenderers { get; init; } = [];
}

public record KeyBindingsConfig
{
    public ImmutableSortedDictionary<string, Key> Bindings { get; init; } =
        new Dictionary<string, Key>
        {
            ["ClosePopup"] = Key.Escape,
            ["DecreaseBarCount"] = Key.Left,
            ["DecreaseBarSpacing"] = Key.Down,
            ["IncreaseBarCount"] = Key.Right,
            ["IncreaseBarSpacing"] = Key.Up,
            ["NextRenderer"] = Key.X,
            ["PreviousRenderer"] = Key.Z,
            ["QualityHigh"] = Key.E,
            ["QualityLow"] = Key.Q,
            ["QualityMedium"] = Key.W,
            ["ToggleControlPanel"] = Key.P,
            ["ToggleOverlay"] = Key.O,
            ["ToggleRecording"] = Key.Space,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    public Key GetKey(string action) =>
        Bindings.GetValueOrDefault(action, Key.None);

    public KeyBindingsConfig SetKey(string action, Key key) =>
        this with { Bindings = Bindings.SetItem(action, key) };
}

public sealed record AppSettingsConfig
{
    public AudioConfig Audio { get; init; } = new();
    public GeneralConfig General { get; init; } = new();
    public KeyBindingsConfig KeyBindings { get; init; } = new();
    public ParticlesConfig Particles { get; init; } = new();
    public RaindropsConfig Raindrops { get; init; } = new();
    public VisualizationConfig Visualization { get; init; } = new();
    public WindowConfig Window { get; init; } = new();
}

public interface ISettingsService
{
    AppSettingsConfig Current { get; }
    IGainParametersProvider GainParameters { get; }
    event Action<AppSettingsConfig>? Changed;

    void Apply(AppSettingsConfig config);
    void Load();
    void Reset();

    void UpdateVisualization(Func<VisualizationConfig, VisualizationConfig> updater);
    void UpdateAudio(Func<AudioConfig, AudioConfig> updater);
    void UpdateWindow(Func<WindowConfig, WindowConfig> updater);
    void UpdateGeneral(Func<GeneralConfig, GeneralConfig> updater);
    void UpdateParticles(Func<ParticlesConfig, ParticlesConfig> updater);
    void UpdateRaindrops(Func<RaindropsConfig, RaindropsConfig> updater);
    void UpdateKeyBindings(Func<KeyBindingsConfig, KeyBindingsConfig> updater);
}

public sealed class SettingsService : ISettingsService
{
    private const string LP = nameof(SettingsService);
    private const string FileName = "settings.json";
    private const string AppFolder = "SpectrumNet";

    private static readonly JsonSerializerSettings JsonCfg = new()
    {
        Formatting = Formatting.Indented,
        Converters = { new StringEnumConverter() }
    };

    private static readonly Lazy<SettingsService> _lazy = new(() => new SettingsService());
    public static SettingsService Instance => _lazy.Value;

    private readonly ISmartLogger _log = SmartLogger.Instance;
    private readonly GainParameters _gain;

    public AppSettingsConfig Current { get; private set; } = new();
    public IGainParametersProvider GainParameters => _gain;

    public event Action<AppSettingsConfig>? Changed;

    private SettingsService()
    {
        _gain = new GainParameters(null);
        Load();
    }

    public void Apply(AppSettingsConfig cfg)
    {
        Current = cfg;
        SyncGain();
        Save();
        Changed?.Invoke(Current);
    }

    public void Load()
    {
        string path = GetPath();
        if (!File.Exists(path)) { Reset(); return; }

        try
        {
            string json = File.ReadAllText(path);
            Apply(JsonConvert.DeserializeObject<AppSettingsConfig>(json, JsonCfg) ?? new());
        }
        catch (Exception ex)
        {
            _log.Log(LogLevel.Error, LP, $"Load failed: {ex.Message}");
            Reset();
        }
    }

    public void Reset() => Apply(new());

    public void UpdateVisualization(Func<VisualizationConfig, VisualizationConfig> u) =>
        Apply(Current with { Visualization = u(Current.Visualization) });

    public void UpdateAudio(Func<AudioConfig, AudioConfig> u) =>
        Apply(Current with { Audio = u(Current.Audio) });

    public void UpdateWindow(Func<WindowConfig, WindowConfig> u) =>
        Apply(Current with { Window = u(Current.Window) });

    public void UpdateGeneral(Func<GeneralConfig, GeneralConfig> u) =>
        Apply(Current with { General = u(Current.General) });

    public void UpdateParticles(Func<ParticlesConfig, ParticlesConfig> u) =>
        Apply(Current with { Particles = u(Current.Particles) });

    public void UpdateRaindrops(Func<RaindropsConfig, RaindropsConfig> u) =>
        Apply(Current with { Raindrops = u(Current.Raindrops) });

    public void UpdateKeyBindings(Func<KeyBindingsConfig, KeyBindingsConfig> u) =>
        Apply(Current with { KeyBindings = u(Current.KeyBindings) });

    private void Save()
    {
        try
        {
            string path = GetPath(ensureDir: true);
            File.WriteAllText(path, JsonConvert.SerializeObject(Current, JsonCfg));
        }
        catch (Exception ex)
        {
            _log.Log(LogLevel.Error, LP, $"Save failed: {ex.Message}");
        }
    }

    private void SyncGain()
    {
        _gain.AmplificationFactor = Current.Audio.AmplificationFactor;
        _gain.MaxDbValue = Current.Audio.MaxDbLevel;
        _gain.MinDbValue = Current.Audio.MinDbLevel;
    }

    private static string GetPath(bool ensureDir = false)
    {
        string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
        if (File.Exists(local)) return local;

        string folder = Path.Combine(GetFolderPath(SpecialFolder.ApplicationData), AppFolder);
        if (ensureDir) Directory.CreateDirectory(folder);

        return Path.Combine(folder, FileName);
    }
}
