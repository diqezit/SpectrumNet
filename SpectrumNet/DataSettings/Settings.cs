#nullable enable

namespace SpectrumNet.DataSettings;

public class Settings : ISettings, INotifyPropertyChanged
{
    private const string LogPrefix = "Settings";

    // Поля для рендереров
    private int _maxParticles;
    private float _spawnThresholdOverlay, _spawnThresholdNormal,
                  _particleVelocityMin, _particleVelocityMax,
                  _particleSizeOverlay, _particleSizeNormal,
                  _particleLife, _particleLifeDecay,
                  _velocityMultiplier, _alphaDecayExponent,
                  _spawnProbability,
                  _overlayOffsetMultiplier, _overlayHeightMultiplier,
                  _maxZDepth, _minZDepth;

    // Поля для AsciiDonutRenderer
    private int _donutSegments, _donutLowQualitySkipFactor, _donutMediumQualitySkipFactor, _donutHighQualitySkipFactor;
    private float _donutRadius, _donutTubeRadius, _donutScale, _donutDepthOffset, _donutDepthScaleFactor,
                  _donutCharOffsetX, _donutCharOffsetY, _donutFontSize, _donutBaseRotationIntensity,
                  _donutSpectrumIntensityScale, _donutSmoothingFactorSpectrum, _donutSmoothingFactorRotation,
                  _donutMaxRotationAngleChange, _donutRotationSpeedX, _donutRotationSpeedY, _donutRotationSpeedZ,
                  _donutBarCountScaleFactorDonutScale, _donutBarCountScaleFactorAlpha, _donutBaseAlphaIntensity,
                  _donutMaxSpectrumAlphaScale, _donutMinAlphaValue, _donutAlphaRange,
                  _donutRotationIntensityMin, _donutRotationIntensityMax, _donutRotationIntensitySmoothingFactor;
    private string _donutAsciiChars = string.Empty;

    // Поля для RaindropsRenderer
    private int _maxRaindrops;
    private float _baseFallSpeed, _raindropSize, _splashParticleSize, _splashUpwardForce, _speedVariation,
                  _intensitySpeedMultiplier, _timeScaleFactor, _maxTimeStep, _minTimeStep;

    // Поля для UI настроек
    private double _windowLeft, _windowTop, _windowWidth, _windowHeight, _uiBarSpacing;
    private WindowState _windowState;
    private bool _isControlPanelVisible, _isOverlayTopmost, _isDarkTheme, _showPerformanceInfo;
    private int _uiBarCount;
    private RenderStyle _selectedRenderStyle = DefaultSettings.SelectedRenderStyle;
    private FftWindowType _selectedFftWindowType = DefaultSettings.SelectedFftWindowType;
    private SpectrumScale _selectedScaleType = DefaultSettings.SelectedScaleType;
    private RenderQuality _selectedRenderQuality = DefaultSettings.SelectedRenderQuality;
    private float _uiMinDbLevel, _uiMaxDbLevel, _uiAmplificationFactor;
    private string _selectedPalette = DefaultSettings.SelectedPalette;

    [JsonIgnore]
    private PropertyChangedEventHandler? _propertyChanged;

    // Синглтон
    private static readonly Lazy<Settings> _instance = new(() => new Settings());
    public static Settings Instance => _instance.Value;

    public Settings() => ResetToDefaults();

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected virtual void OnPropertyChanged(string propertyName) =>
        _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // свойство для хранения списка избранных рендеров

    private ObservableCollection<RenderStyle> _favoriteRenderers = new ObservableCollection<RenderStyle>();

    public ObservableCollection<RenderStyle> FavoriteRenderers { get => _favoriteRenderers; set => SetProperty(ref _favoriteRenderers, value); }


    // Свойства для рендереров (с использованием выражений-методов)
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

    // Свойства для RaindropsRenderer
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

    // Свойства для AsciiDonutRenderer
    public int DonutSegments { get => _donutSegments; set => SetProperty(ref _donutSegments, value); }
    public float DonutRadius { get => _donutRadius; set => SetProperty(ref _donutRadius, value); }
    public float DonutTubeRadius { get => _donutTubeRadius; set => SetProperty(ref _donutTubeRadius, value); }
    public float DonutScale { get => _donutScale; set => SetProperty(ref _donutScale, value); }
    public float DonutDepthOffset { get => _donutDepthOffset; set => SetProperty(ref _donutDepthOffset, value); }
    public float DonutDepthScaleFactor { get => _donutDepthScaleFactor; set => SetProperty(ref _donutDepthScaleFactor, value); }
    public float DonutCharOffsetX { get => _donutCharOffsetX; set => SetProperty(ref _donutCharOffsetX, value); }
    public float DonutCharOffsetY { get => _donutCharOffsetY; set => SetProperty(ref _donutCharOffsetY, value); }
    public float DonutFontSize { get => _donutFontSize; set => SetProperty(ref _donutFontSize, value); }
    public float DonutBaseRotationIntensity { get => _donutBaseRotationIntensity; set => SetProperty(ref _donutBaseRotationIntensity, value); }
    public float DonutSpectrumIntensityScale { get => _donutSpectrumIntensityScale; set => SetProperty(ref _donutSpectrumIntensityScale, value); }
    public float DonutSmoothingFactorSpectrum { get => _donutSmoothingFactorSpectrum; set => SetProperty(ref _donutSmoothingFactorSpectrum, value); }
    public float DonutSmoothingFactorRotation { get => _donutSmoothingFactorRotation; set => SetProperty(ref _donutSmoothingFactorRotation, value); }
    public float DonutMaxRotationAngleChange { get => _donutMaxRotationAngleChange; set => SetProperty(ref _donutMaxRotationAngleChange, value); }
    public float DonutRotationSpeedX { get => _donutRotationSpeedX; set => SetProperty(ref _donutRotationSpeedX, value); }
    public float DonutRotationSpeedY { get => _donutRotationSpeedY; set => SetProperty(ref _donutRotationSpeedY, value); }
    public float DonutRotationSpeedZ { get => _donutRotationSpeedZ; set => SetProperty(ref _donutRotationSpeedZ, value); }
    public float DonutBarCountScaleFactorDonutScale { get => _donutBarCountScaleFactorDonutScale; set => SetProperty(ref _donutBarCountScaleFactorDonutScale, value); }
    public float DonutBarCountScaleFactorAlpha { get => _donutBarCountScaleFactorAlpha; set => SetProperty(ref _donutBarCountScaleFactorAlpha, value); }
    public float DonutBaseAlphaIntensity { get => _donutBaseAlphaIntensity; set => SetProperty(ref _donutBaseAlphaIntensity, value); }
    public float DonutMaxSpectrumAlphaScale { get => _donutMaxSpectrumAlphaScale; set => SetProperty(ref _donutMaxSpectrumAlphaScale, value); }
    public float DonutMinAlphaValue { get => _donutMinAlphaValue; set => SetProperty(ref _donutMinAlphaValue, value); }
    public float DonutAlphaRange { get => _donutAlphaRange; set => SetProperty(ref _donutAlphaRange, value); }
    public float DonutRotationIntensityMin { get => _donutRotationIntensityMin; set => SetProperty(ref _donutRotationIntensityMin, value); }
    public float DonutRotationIntensityMax { get => _donutRotationIntensityMax; set => SetProperty(ref _donutRotationIntensityMax, value); }
    public float DonutRotationIntensitySmoothingFactor { get => _donutRotationIntensitySmoothingFactor; set => SetProperty(ref _donutRotationIntensitySmoothingFactor, value); }
    public int DonutLowQualitySkipFactor { get => _donutLowQualitySkipFactor; set => SetProperty(ref _donutLowQualitySkipFactor, value); }
    public int DonutMediumQualitySkipFactor { get => _donutMediumQualitySkipFactor; set => SetProperty(ref _donutMediumQualitySkipFactor, value); }
    public int DonutHighQualitySkipFactor { get => _donutHighQualitySkipFactor; set => SetProperty(ref _donutHighQualitySkipFactor, value); }
    public string DonutAsciiChars { get => _donutAsciiChars; set => SetProperty(ref _donutAsciiChars, value); }

    // Свойства для UI настроек
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

    // Сброс настроек к значениям по умолчанию с использованием SmartLogger.Safe для обработки ошибок
    public void ResetToDefaults() => Safe(() =>
    {
        // Particle renderer settings
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

        // Raindrops renderer settings
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

        // ASCII Donut renderer settings
        DonutSegments = DefaultSettings.DonutRenderer.Segments;
        DonutRadius = DefaultSettings.DonutRenderer.DonutRadius;
        DonutTubeRadius = DefaultSettings.DonutRenderer.TubeRadius;
        DonutScale = DefaultSettings.DonutRenderer.DonutScale;
        DonutDepthOffset = DefaultSettings.DonutRenderer.DepthOffset;
        DonutDepthScaleFactor = DefaultSettings.DonutRenderer.DepthScaleFactor;
        DonutCharOffsetX = DefaultSettings.DonutRenderer.CharOffsetX;
        DonutCharOffsetY = DefaultSettings.DonutRenderer.CharOffsetY;
        DonutFontSize = DefaultSettings.DonutRenderer.FontSize;
        DonutBaseRotationIntensity = DefaultSettings.DonutRenderer.BaseRotationIntensity;
        DonutSpectrumIntensityScale = DefaultSettings.DonutRenderer.SpectrumIntensityScale;
        DonutSmoothingFactorSpectrum = DefaultSettings.DonutRenderer.SmoothingFactorSpectrum;
        DonutSmoothingFactorRotation = DefaultSettings.DonutRenderer.SmoothingFactorRotation;
        DonutMaxRotationAngleChange = DefaultSettings.DonutRenderer.MaxRotationAngleChange;
        DonutRotationSpeedX = DefaultSettings.DonutRenderer.RotationSpeedX;
        DonutRotationSpeedY = DefaultSettings.DonutRenderer.RotationSpeedY;
        DonutRotationSpeedZ = DefaultSettings.DonutRenderer.RotationSpeedZ;
        DonutBarCountScaleFactorDonutScale = DefaultSettings.DonutRenderer.BarCountScaleFactorDonutScale;
        DonutBarCountScaleFactorAlpha = DefaultSettings.DonutRenderer.BarCountScaleFactorAlpha;
        DonutBaseAlphaIntensity = DefaultSettings.DonutRenderer.BaseAlphaIntensity;
        DonutMaxSpectrumAlphaScale = DefaultSettings.DonutRenderer.MaxSpectrumAlphaScale;
        DonutMinAlphaValue = DefaultSettings.DonutRenderer.MinAlphaValue;
        DonutAlphaRange = DefaultSettings.DonutRenderer.AlphaRange;
        DonutRotationIntensityMin = DefaultSettings.DonutRenderer.RotationIntensityMin;
        DonutRotationIntensityMax = DefaultSettings.DonutRenderer.RotationIntensityMax;
        DonutRotationIntensitySmoothingFactor = DefaultSettings.DonutRenderer.RotationIntensitySmoothingFactor;
        DonutLowQualitySkipFactor = DefaultSettings.DonutRenderer.LowQualitySkipFactor;
        DonutMediumQualitySkipFactor = DefaultSettings.DonutRenderer.MediumQualitySkipFactor;
        DonutHighQualitySkipFactor = DefaultSettings.DonutRenderer.HighQualitySkipFactor;
        DonutAsciiChars = DefaultSettings.DonutRenderer.AsciiChars;

        // UI настройки
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

        Log(LogLevel.Information, LogPrefix, "Settings have been reset to defaults");
    },
    new ErrorHandlingOptions
    {
        Source = nameof(ResetToDefaults),
        ErrorMessage = "Ошибка сброса настроек к значениям по умолчанию"
    });
}