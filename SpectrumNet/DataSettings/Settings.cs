#nullable enable

namespace SpectrumNet.DataSettings;

public class Settings : ISettings
{
    private const string LogPrefix = nameof(Settings);
    private readonly ISmartLogger _logger = SmartLogger.Instance;

    private int _maxParticles;
    private float _spawnThresholdOverlay, _spawnThresholdNormal,
                  _particleVelocityMin, _particleVelocityMax,
                  _particleSizeOverlay, _particleSizeNormal,
                  _particleLife, _particleLifeDecay,
                  _velocityMultiplier, _alphaDecayExponent,
                  _spawnProbability,
                  _overlayOffsetMultiplier, _overlayHeightMultiplier,
                  _maxZDepth, _minZDepth;

    private int _maxRaindrops;
    private float _baseFallSpeed, _raindropSize, _splashParticleSize, _splashUpwardForce, _speedVariation,
                  _intensitySpeedMultiplier, _timeScaleFactor, _maxTimeStep, _minTimeStep;

    private double _windowLeft, _windowTop, _windowWidth, _windowHeight, _uiBarSpacing;
    private WindowState _windowState;
    private bool _isControlPanelVisible, _isOverlayTopmost, _isDarkTheme, _showPerformanceInfo, _limitFpsTo60;
    private int _uiBarCount;
    private RenderStyle _selectedRenderStyle = DefaultSettings.SelectedRenderStyle;
    private FftWindowType _selectedFftWindowType = DefaultSettings.SelectedFftWindowType;
    private SpectrumScale _selectedScaleType = DefaultSettings.SelectedScaleType;
    private RenderQuality _selectedRenderQuality = DefaultSettings.SelectedRenderQuality;
    private float _uiMinDbLevel, _uiMaxDbLevel, _uiAmplificationFactor;
    private string _selectedPalette = DefaultSettings.SelectedPalette;

    [JsonIgnore]
    private PropertyChangedEventHandler? _propertyChanged;

    private static readonly Lazy<Settings> _instance = new(() => new());
    public static Settings Instance => _instance.Value;

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    public event EventHandler<string>? SettingsChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected virtual void OnPropertyChanged(string propertyName) =>
        _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private ObservableCollection<RenderStyle> _favoriteRenderers = [];

    public ObservableCollection<RenderStyle> FavoriteRenderers { get => _favoriteRenderers; set => SetProperty(ref _favoriteRenderers, value); }

    public float VelocityMultiplier { get => _velocityMultiplier; set => SetProperty(ref _velocityMultiplier, value); }
    public float AlphaDecayExponent { get => _alphaDecayExponent; set => SetProperty(ref _alphaDecayExponent, value); }
    public float SpawnProbability { get => _spawnProbability; set => SetProperty(ref _spawnProbability, value); }
    public float OverlayOffsetMultiplier { get => _overlayOffsetMultiplier; set => SetProperty(ref _overlayOffsetMultiplier, value); }
    public float OverlayHeightMultiplier { get => _overlayHeightMultiplier; set => SetProperty(ref _overlayHeightMultiplier, value); }
    public int MaxParticles { get => _maxParticles; set => SetProperty(ref _maxParticles, value); }
    public float SpawnThresholdOverlay { get => _spawnThresholdOverlay; set => SetProperty(ref _spawnThresholdOverlay, value); }
    public float SpawnThresholdNormal { get => _spawnThresholdNormal; set => SetProperty(ref _spawnThresholdNormal, value); }
    public float ParticleVelocityMin { get => _particleVelocityMin; set => SetProperty(ref _particleVelocityMin, value); }
    public float ParticleVelocityMax { get => _particleVelocityMax; set => SetProperty(ref _particleVelocityMax, value); }
    public float ParticleSizeOverlay { get => _particleSizeOverlay; set => SetProperty(ref _particleSizeOverlay, value); }
    public float ParticleSizeNormal { get => _particleSizeNormal; set => SetProperty(ref _particleSizeNormal, value); }
    public float ParticleLife { get => _particleLife; set => SetProperty(ref _particleLife, value); }
    public float ParticleLifeDecay { get => _particleLifeDecay; set => SetProperty(ref _particleLifeDecay, value); }
    public float MaxZDepth { get => _maxZDepth; set => SetProperty(ref _maxZDepth, value); }
    public float MinZDepth { get => _minZDepth; set => SetProperty(ref _minZDepth, value); }

    public int MaxRaindrops { get => _maxRaindrops; set => SetProperty(ref _maxRaindrops, value); }
    public float BaseFallSpeed { get => _baseFallSpeed; set => SetProperty(ref _baseFallSpeed, value); }
    public float RaindropSize { get => _raindropSize; set => SetProperty(ref _raindropSize, value); }
    public float SplashParticleSize { get => _splashParticleSize; set => SetProperty(ref _splashParticleSize, value); }
    public float SplashUpwardForce { get => _splashUpwardForce; set => SetProperty(ref _splashUpwardForce, value); }
    public float SpeedVariation { get => _speedVariation; set => SetProperty(ref _speedVariation, value); }
    public float IntensitySpeedMultiplier { get => _intensitySpeedMultiplier; set => SetProperty(ref _intensitySpeedMultiplier, value); }
    public float TimeScaleFactor { get => _timeScaleFactor; set => SetProperty(ref _timeScaleFactor, value); }
    public float MaxTimeStep { get => _maxTimeStep; set => SetProperty(ref _maxTimeStep, value); }
    public float MinTimeStep { get => _minTimeStep; set => SetProperty(ref _minTimeStep, value); }

    public double WindowLeft { get => _windowLeft; set => SetProperty(ref _windowLeft, value); }
    public double WindowTop { get => _windowTop; set => SetProperty(ref _windowTop, value); }
    public double WindowWidth { get => _windowWidth; set => SetProperty(ref _windowWidth, value); }
    public double WindowHeight { get => _windowHeight; set => SetProperty(ref _windowHeight, value); }
    public WindowState WindowState { get => _windowState; set => SetProperty(ref _windowState, value); }
    public bool IsControlPanelVisible { get => _isControlPanelVisible; set => SetProperty(ref _isControlPanelVisible, value); }
    public RenderStyle SelectedRenderStyle { get => _selectedRenderStyle; set => SetProperty(ref _selectedRenderStyle, value); }
    public FftWindowType SelectedFftWindowType { get => _selectedFftWindowType; set => SetProperty(ref _selectedFftWindowType, value); }
    public SpectrumScale SelectedScaleType { get => _selectedScaleType; set => SetProperty(ref _selectedScaleType, value); }
    public RenderQuality SelectedRenderQuality { get => _selectedRenderQuality; set => SetProperty(ref _selectedRenderQuality, value); }
    public int UIBarCount { get => _uiBarCount; set => SetProperty(ref _uiBarCount, value); }
    public double UIBarSpacing { get => _uiBarSpacing; set => SetProperty(ref _uiBarSpacing, value); }
    public string SelectedPalette { get => _selectedPalette; set => SetProperty(ref _selectedPalette, value); }
    public bool IsOverlayTopmost { get => _isOverlayTopmost; set => SetProperty(ref _isOverlayTopmost, value); }
    public bool ShowPerformanceInfo { get => _showPerformanceInfo; set => SetProperty(ref _showPerformanceInfo, value); }
    public float UIMinDbLevel { get => _uiMinDbLevel; set => SetProperty(ref _uiMinDbLevel, value); }
    public float UIMaxDbLevel { get => _uiMaxDbLevel; set => SetProperty(ref _uiMaxDbLevel, value); }
    public float UIAmplificationFactor { get => _uiAmplificationFactor; set => SetProperty(ref _uiAmplificationFactor, value); }
    public bool IsDarkTheme { get => _isDarkTheme; set => SetProperty(ref _isDarkTheme, value); }
    public bool LimitFpsTo60 { get => _limitFpsTo60; set => SetProperty(ref _limitFpsTo60, value); }

    public void LoadSettings(string? filePath = null) =>
        _logger.Safe(() =>
        {
            string settingsPath = filePath ?? GetSettingsFilePath();

            if (!File.Exists(settingsPath))
            {
                _logger.Log(LogLevel.Information, LogPrefix, "Settings file not found, using defaults");
                return;
            }

            try
            {
                var content = File.ReadAllText(settingsPath);
                var loadedSettings = JsonConvert.DeserializeObject<Settings>(content);

                if (loadedSettings == null)
                {
                    _logger.Log(LogLevel.Warning, LogPrefix, "Failed to deserialize settings. Using defaults.");
                    return;
                }

                ApplySettings(loadedSettings);
                _logger.Log(LogLevel.Information, LogPrefix, $"Settings loaded from {settingsPath}");
                SettingsChanged?.Invoke(this, "LoadSettings");
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogPrefix, $"Error loading settings: {ex.Message}. Using defaults.");
            }
        },
        LogPrefix,
        "Error loading settings");

    public void SaveSettings(string? filePath = null) =>
        _logger.Safe(() =>
        {
            string settingsPath = filePath ?? EnsureSettingsDirectory();

            var json = JsonConvert.SerializeObject(
                this,
                new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    ContractResolver = new DefaultContractResolver
                    {
                        NamingStrategy = new CamelCaseNamingStrategy()
                    }
                });

            File.WriteAllText(settingsPath, json);
            File.WriteAllText("settings.json", json);

            _logger.Log(LogLevel.Information, LogPrefix, $"Settings saved to {settingsPath}");
            SettingsChanged?.Invoke(this, "SaveSettings");
        },
        LogPrefix,
        "Error saving settings");

    public void ResetToDefaults() =>
        _logger.Safe(() =>
        {
            MaxParticles = DefaultSettings.MaxParticles;
            SpawnThresholdOverlay = DefaultSettings.SpawnThresholdOverlay;
            SpawnThresholdNormal = DefaultSettings.SpawnThresholdNormal;
            ParticleVelocityMin = DefaultSettings.ParticleVelocityMin;
            ParticleVelocityMax = DefaultSettings.ParticleVelocityMax;
            ParticleSizeOverlay = DefaultSettings.ParticleSizeOverlay;
            ParticleSizeNormal = DefaultSettings.ParticleSizeNormal;
            ParticleLife = DefaultSettings.ParticleLife;
            ParticleLifeDecay = DefaultSettings.ParticleLifeDecay;
            VelocityMultiplier = DefaultSettings.VelocityMultiplier;
            AlphaDecayExponent = DefaultSettings.AlphaDecayExponent;
            SpawnProbability = DefaultSettings.SpawnProbability;
            OverlayOffsetMultiplier = DefaultSettings.OverlayOffsetMultiplier;
            OverlayHeightMultiplier = DefaultSettings.OverlayHeightMultiplier;
            MaxZDepth = DefaultSettings.MaxZDepth;
            MinZDepth = DefaultSettings.MinZDepth;

            MaxRaindrops = DefaultSettings.MaxRaindrops;
            BaseFallSpeed = DefaultSettings.BaseFallSpeed;
            RaindropSize = DefaultSettings.RaindropSize;
            SplashParticleSize = DefaultSettings.SplashParticleSize;
            SplashUpwardForce = DefaultSettings.SplashUpwardForce;
            SpeedVariation = DefaultSettings.SpeedVariation;
            IntensitySpeedMultiplier = DefaultSettings.IntensitySpeedMultiplier;
            TimeScaleFactor = DefaultSettings.TimeScaleFactor;
            MaxTimeStep = DefaultSettings.MaxTimeStep;
            MinTimeStep = DefaultSettings.MinTimeStep;

            WindowLeft = DefaultSettings.WindowLeft;
            WindowTop = DefaultSettings.WindowTop;
            WindowWidth = DefaultSettings.WindowWidth;
            WindowHeight = DefaultSettings.WindowHeight;
            WindowState = DefaultSettings.WindowState;
            IsControlPanelVisible = DefaultSettings.IsControlPanelVisible;
            SelectedRenderStyle = DefaultSettings.SelectedRenderStyle;
            SelectedFftWindowType = DefaultSettings.SelectedFftWindowType;
            SelectedScaleType = DefaultSettings.SelectedScaleType;
            SelectedRenderQuality = DefaultSettings.SelectedRenderQuality;
            UIBarCount = DefaultSettings.UIBarCount;
            UIBarSpacing = DefaultSettings.UIBarSpacing;
            SelectedPalette = DefaultSettings.SelectedPalette;
            IsOverlayTopmost = DefaultSettings.IsOverlayTopmost;
            ShowPerformanceInfo = DefaultSettings.ShowPerformanceInfo;
            UIMinDbLevel = DefaultSettings.UIMinDbLevel;
            UIMaxDbLevel = DefaultSettings.UIMaxDbLevel;
            UIAmplificationFactor = DefaultSettings.UIAmplificationFactor;
            IsDarkTheme = DefaultSettings.IsDarkTheme;
            LimitFpsTo60 = DefaultSettings.LimitFpsTo60;

            _logger.Log(LogLevel.Information, LogPrefix, "Settings have been reset to defaults");
            SettingsChanged?.Invoke(this, "ResetToDefaults");
        },
        LogPrefix,
        "Error resetting settings to defaults");

    private void ApplySettings(Settings source)
    {
        var properties = source.GetType()
            .GetProperties()
            .Where(p => p.CanRead &&
                  p.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).Length == 0);

        foreach (var prop in properties)
        {
            var targetProp = GetType().GetProperty(prop.Name);
            if (targetProp?.CanWrite == true)
                targetProp.SetValue(this, prop.GetValue(source));
        }
    }

    private static string GetSettingsFilePath()
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string settingsPath = Path.Combine(
            appDataPath,
            DefaultSettings.APP_FOLDER,
            DefaultSettings.SETTINGS_FILE);

        if (!File.Exists(settingsPath) && File.Exists("settings.json"))
            settingsPath = "settings.json";

        return settingsPath;
    }

    private static string EnsureSettingsDirectory()
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(appDataPath, DefaultSettings.APP_FOLDER);

        if (!Directory.Exists(appFolder))
            Directory.CreateDirectory(appFolder);

        return Path.Combine(appFolder, DefaultSettings.SETTINGS_FILE);
    }
}