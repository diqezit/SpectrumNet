#nullable enable

namespace SpectrumNet
{
    public interface ISettings
    {
        // Существующие настройки рендереров
        public int MaxParticles { get; set; }
        float SpawnThresholdOverlay { get; set; }
        float SpawnThresholdNormal { get; set; }
        float ParticleVelocityMin { get; set; }
        float ParticleVelocityMax { get; set; }
        float ParticleSizeOverlay { get; set; }
        float ParticleSizeNormal { get; set; }
        float ParticleLife { get; set; }
        float ParticleLifeDecay { get; set; }
        float VelocityMultiplier { get; set; }
        float AlphaDecayExponent { get; set; }
        float SpawnProbability { get; set; }
        float OverlayOffsetMultiplier { get; set; }
        float OverlayHeightMultiplier { get; set; }
        float MaxZDepth { get; set; }
        float MinZDepth { get; set; }

        // Настройки для RaindropsRenderer
        int MaxRaindrops { get; set; }
        float BaseFallSpeed { get; set; }
        float RaindropSize { get; set; }
        float SplashParticleSize { get; set; }
        float SplashUpwardForce { get; set; }
        float SpeedVariation { get; set; }
        float IntensitySpeedMultiplier { get; set; }
        float TimeScaleFactor { get; set; }
        float MaxTimeStep { get; set; }
        float MinTimeStep { get; set; }

        // UI настройки
        double WindowLeft { get; set; }
        double WindowTop { get; set; }
        double WindowWidth { get; set; }
        double WindowHeight { get; set; }
        WindowState WindowState { get; set; }
        bool IsControlPanelVisible { get; set; }
        RenderStyle SelectedRenderStyle { get; set; }
        FftWindowType SelectedFftWindowType { get; set; }
        SpectrumScale SelectedScaleType { get; set; }
        RenderQuality SelectedRenderQuality { get; set; }
        int UIBarCount { get; set; }
        double UIBarSpacing { get; set; }
        string SelectedPalette { get; set; }
        bool IsOverlayTopmost { get; set; }
        bool ShowPerformanceInfo { get; set; }
        float UIMinDbLevel { get; set; }
        float UIMaxDbLevel { get; set; }
        float UIAmplificationFactor { get; set; }
        bool IsDarkTheme { get; set; }

        // Настройки для AsciiDonutRenderer
        int DonutSegments { get; set; }
        float DonutRadius { get; set; }
        float DonutTubeRadius { get; set; }
        float DonutScale { get; set; }
        float DonutDepthOffset { get; set; }
        float DonutDepthScaleFactor { get; set; }
        float DonutCharOffsetX { get; set; }
        float DonutCharOffsetY { get; set; }
        float DonutFontSize { get; set; }
        float DonutBaseRotationIntensity { get; set; }
        float DonutSpectrumIntensityScale { get; set; }
        float DonutSmoothingFactorSpectrum { get; set; }
        float DonutSmoothingFactorRotation { get; set; }
        float DonutMaxRotationAngleChange { get; set; }
        float DonutRotationSpeedX { get; set; }
        float DonutRotationSpeedY { get; set; }
        float DonutRotationSpeedZ { get; set; }
        float DonutBarCountScaleFactorDonutScale { get; set; }
        float DonutBarCountScaleFactorAlpha { get; set; }
        float DonutBaseAlphaIntensity { get; set; }
        float DonutMaxSpectrumAlphaScale { get; set; }
        float DonutMinAlphaValue { get; set; }
        float DonutAlphaRange { get; set; }
        float DonutRotationIntensityMin { get; set; }
        float DonutRotationIntensityMax { get; set; }
        float DonutRotationIntensitySmoothingFactor { get; set; }
        int DonutLowQualitySkipFactor { get; set; }
        int DonutMediumQualitySkipFactor { get; set; }
        int DonutHighQualitySkipFactor { get; set; }
        string DonutAsciiChars { get; set; }

        void ResetToDefaults();
        event PropertyChangedEventHandler? PropertyChanged;
    }

    public class DefaultSettings
    {
        // Application settings constants
        public const string APP_FOLDER = "SpectrumNet";
        public const string SETTINGS_FILE = "settings.json";

        // Particle renderer settings
        public const int MaxParticles = 2500;
        public const float
            SpawnThresholdOverlay = 0.03f,
            SpawnThresholdNormal = 0.08f,
            ParticleVelocityMin = 0.5f,
            ParticleVelocityMax = 2.0f,
            ParticleSizeOverlay = 2.5f,
            ParticleSizeNormal = 2.0f,
            ParticleLife = 3.0f,
            ParticleLifeDecay = 0.01f,
            VelocityMultiplier = 0.8f,
            AlphaDecayExponent = 1.3f,
            SpawnProbability = 0.08f,
            OverlayOffsetMultiplier = 1.0f,
            OverlayHeightMultiplier = 0.7f,
            MaxZDepth = 1000f,
            MinZDepth = 100f;

        // RaindropsRenderer settings
        public const int MaxRaindrops = 1000;
        public const float
            BaseFallSpeed = 12f,
            RaindropSize = 3f,
            SplashParticleSize = 2f,
            SplashUpwardForce = 8f,
            SpeedVariation = 3f,
            IntensitySpeedMultiplier = 4f,
            TimeScaleFactor = 60.0f,
            MaxTimeStep = 0.1f,
            MinTimeStep = 0.001f;

        // UI default settings
        public const double
            WindowLeft = 100,
            WindowTop = 100,
            WindowWidth = 800,
            WindowHeight = 600,
            UIBarSpacing = 4;
        public static readonly WindowState WindowState = WindowState.Normal;
        public const bool
            IsControlPanelVisible = true,
            IsOverlayTopmost = true,
            IsDarkTheme = true,
            ShowPerformanceInfo = true;
        public static readonly RenderStyle SelectedRenderStyle = RenderStyle.Bars;
        public static readonly FftWindowType SelectedFftWindowType = FftWindowType.Hann;
        public static readonly SpectrumScale SelectedScaleType = SpectrumScale.Linear;
        public static readonly RenderQuality SelectedRenderQuality = RenderQuality.Medium;

        // UI bar display settings
        public const int UIBarCount = 60;

        // Default palette
        public const string SelectedPalette = "Solid";

        // UI amplification settings
        public const float
            UIMinDbLevel = -130f,
            UIMaxDbLevel = -20f,
            UIAmplificationFactor = 2.0f;

        // AsciiDonutRenderer settings
        public static class DonutRenderer
        {
            // Donut geometry
            public const int Segments = 40;
            public const float DonutRadius = 2.2f;
            public const float TubeRadius = 0.9f;
            public const float DonutScale = 1.9f;

            // Rendering parameters
            public const float DepthOffset = 7.5f;
            public const float DepthScaleFactor = 1.3f;
            public const float CharOffsetX = 3.8f;
            public const float CharOffsetY = 3.8f;
            public const float FontSize = 12f;

            // Rotation and animation
            public const float BaseRotationIntensity = 0.45f;
            public const float SpectrumIntensityScale = 1.3f;
            public const float SmoothingFactorSpectrum = 0.25f;
            public const float SmoothingFactorRotation = 0.88f;
            public const float MaxRotationAngleChange = 0.0025f;
            public const float RotationSpeedX = 0.011f / 4f;
            public const float RotationSpeedY = 0.019f / 4f;
            public const float RotationSpeedZ = 0.014f / 4f;

            // Scaling and alpha
            public const float BarCountScaleFactorDonutScale = 0.12f;
            public const float BarCountScaleFactorAlpha = 0.22f;
            public const float BaseAlphaIntensity = 0.55f;
            public const float MaxSpectrumAlphaScale = 0.45f;
            public const float MinAlphaValue = 0.22f;
            public const float AlphaRange = 0.65f;

            // Rotation intensity limits
            public const float RotationIntensityMin = 0.1f;
            public const float RotationIntensityMax = 2.0f;
            public const float RotationIntensitySmoothingFactor = 0.1f;

            // Quality settings
            public const int LowQualitySkipFactor = 2;
            public const int MediumQualitySkipFactor = 1;
            public const int HighQualitySkipFactor = 0;

            // ASCII characters
            public static readonly string AsciiChars = " .:=*#%@█▓";
            public static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(0.6f, 0.6f, -1.0f));
        }
    }

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
        public void ResetToDefaults() => SmartLogger.Safe(() =>
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

            SmartLogger.Log(LogLevel.Information, LogPrefix, "Settings have been reset to defaults");
        },
        new SmartLogger.ErrorHandlingOptions
        {
            Source = nameof(ResetToDefaults),
            ErrorMessage = "Ошибка сброса настроек к значениям по умолчанию"
        });
    }
}