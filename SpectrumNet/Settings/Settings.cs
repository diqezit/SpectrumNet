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
        float UIMinDbLevel { get; set; }
        float UIMaxDbLevel { get; set; }
        float UIAmplificationFactor { get; set; }
        bool IsDarkTheme { get; set; }

        void ResetToDefaults();
        event PropertyChangedEventHandler? PropertyChanged;
    }

    public class DefaultSettings
    {
        // Константы для файлов настроек
        public const string APP_FOLDER = "SpectrumNet";
        public const string SETTINGS_FILE = "settings.json";

        // Существующие настройки рендереров
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

        // Настройки для RaindropsRenderer
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

        // UI настройки по умолчанию
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
            IsDarkTheme = true;
        public static readonly RenderStyle SelectedRenderStyle = RenderStyle.Bars;
        public static readonly FftWindowType SelectedFftWindowType = FftWindowType.Hann;
        public static readonly SpectrumScale SelectedScaleType = SpectrumScale.Linear;
        public static readonly RenderQuality SelectedRenderQuality = RenderQuality.Medium;


        // Параметры настроек отображения полос UI
        public const int UIBarCount = 60;


        // Название палитры по умолчанию
        public const string SelectedPalette = "Solid";


        // Параметры усиления UI
        public const float
            UIMinDbLevel = -130f,
            UIMaxDbLevel = -20f,
            UIAmplificationFactor = 2.0f;
    }

    public class Settings : ISettings, INotifyPropertyChanged
    {
        #region Константы
        private const string LogPrefix = "[Settings] ";
        #endregion

        #region Поля для рендереров
        private int _maxParticles;
        private float
            _spawnThresholdOverlay, _spawnThresholdNormal,
            _particleVelocityMin, _particleVelocityMax,
            _particleSizeOverlay, _particleSizeNormal,
            _particleLife, _particleLifeDecay,
            _velocityMultiplier, _alphaDecayExponent,
            _spawnProbability,
            _overlayOffsetMultiplier, _overlayHeightMultiplier,
            _maxZDepth, _minZDepth;

        // Поля для RaindropsRenderer
        private int _maxRaindrops;
        private float
            _baseFallSpeed,
            _raindropSize,
            _splashParticleSize,
            _splashUpwardForce,
            _speedVariation,
            _intensitySpeedMultiplier,
            _timeScaleFactor,
            _maxTimeStep, _minTimeStep;

        // Поля для UI настроек
        private double
            _windowLeft, _windowTop, _windowWidth, _windowHeight,
            _uiBarSpacing;
        private WindowState _windowState;
        private bool
            _isControlPanelVisible, _isOverlayTopmost, _isDarkTheme;
        private int _uiBarCount;
        private RenderStyle _selectedRenderStyle = DefaultSettings.SelectedRenderStyle;
        private FftWindowType _selectedFftWindowType = DefaultSettings.SelectedFftWindowType;
        private SpectrumScale _selectedScaleType = DefaultSettings.SelectedScaleType;
        private RenderQuality _selectedRenderQuality = DefaultSettings.SelectedRenderQuality;
        private float
            _uiMinDbLevel, _uiMaxDbLevel, _uiAmplificationFactor;
        private string _selectedPalette = DefaultSettings.SelectedPalette;

        [JsonIgnore]
        private PropertyChangedEventHandler? _propertyChanged;

        #endregion

        #region Singleton и конструктор
        private static readonly Lazy<Settings> _instance = new(() => new Settings());
        public static Settings Instance => _instance.Value;

        public Settings()
        {
            ResetToDefaults();
        }
        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { _propertyChanged += value; }
            remove { _propertyChanged -= value; }
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        #region Свойства для рендереров
        public float VelocityMultiplier
        {
            get => _velocityMultiplier;
            set => SetProperty(ref _velocityMultiplier, value);
        }

        public float AlphaDecayExponent
        {
            get => _alphaDecayExponent;
            set => SetProperty(ref _alphaDecayExponent, value);
        }

        public float SpawnProbability
        {
            get => _spawnProbability;
            set => SetProperty(ref _spawnProbability, value);
        }

        public float OverlayOffsetMultiplier
        {
            get => _overlayOffsetMultiplier;
            set => SetProperty(ref _overlayOffsetMultiplier, value);
        }

        public float OverlayHeightMultiplier
        {
            get => _overlayHeightMultiplier;
            set => SetProperty(ref _overlayHeightMultiplier, value);
        }

        public int MaxParticles
        {
            get => _maxParticles;
            set => SetProperty(ref _maxParticles, value);
        }

        public float SpawnThresholdOverlay
        {
            get => _spawnThresholdOverlay;
            set => SetProperty(ref _spawnThresholdOverlay, value);
        }

        public float SpawnThresholdNormal
        {
            get => _spawnThresholdNormal;
            set => SetProperty(ref _spawnThresholdNormal, value);
        }

        public float ParticleVelocityMin
        {
            get => _particleVelocityMin;
            set => SetProperty(ref _particleVelocityMin, value);
        }

        public float ParticleVelocityMax
        {
            get => _particleVelocityMax;
            set => SetProperty(ref _particleVelocityMax, value);
        }

        public float ParticleSizeOverlay
        {
            get => _particleSizeOverlay;
            set => SetProperty(ref _particleSizeOverlay, value);
        }

        public float ParticleSizeNormal
        {
            get => _particleSizeNormal;
            set => SetProperty(ref _particleSizeNormal, value);
        }

        public float ParticleLife
        {
            get => _particleLife;
            set => SetProperty(ref _particleLife, value);
        }

        public float ParticleLifeDecay
        {
            get => _particleLifeDecay;
            set => SetProperty(ref _particleLifeDecay, value);
        }

        public float MaxZDepth
        {
            get => _maxZDepth;
            set => SetProperty(ref _maxZDepth, value);
        }

        public float MinZDepth
        {
            get => _minZDepth;
            set => SetProperty(ref _minZDepth, value);
        }

        // Свойства для RaindropsRenderer
        public int MaxRaindrops
        {
            get => _maxRaindrops;
            set => SetProperty(ref _maxRaindrops, value);
        }

        public float BaseFallSpeed
        {
            get => _baseFallSpeed;
            set => SetProperty(ref _baseFallSpeed, value);
        }

        public float RaindropSize
        {
            get => _raindropSize;
            set => SetProperty(ref _raindropSize, value);
        }

        public float SplashParticleSize
        {
            get => _splashParticleSize;
            set => SetProperty(ref _splashParticleSize, value);
        }

        public float SplashUpwardForce
        {
            get => _splashUpwardForce;
            set => SetProperty(ref _splashUpwardForce, value);
        }

        public float SpeedVariation
        {
            get => _speedVariation;
            set => SetProperty(ref _speedVariation, value);
        }

        public float IntensitySpeedMultiplier
        {
            get => _intensitySpeedMultiplier;
            set => SetProperty(ref _intensitySpeedMultiplier, value);
        }

        public float TimeScaleFactor
        {
            get => _timeScaleFactor;
            set => SetProperty(ref _timeScaleFactor, value);
        }

        public float MaxTimeStep
        {
            get => _maxTimeStep;
            set => SetProperty(ref _maxTimeStep, value);
        }

        public float MinTimeStep
        {
            get => _minTimeStep;
            set => SetProperty(ref _minTimeStep, value);
        }
        #endregion

        #region Свойства для UI настроек
        public double WindowLeft
        {
            get => _windowLeft;
            set => SetProperty(ref _windowLeft, value);
        }

        public double WindowTop
        {
            get => _windowTop;
            set => SetProperty(ref _windowTop, value);
        }

        public double WindowWidth
        {
            get => _windowWidth;
            set => SetProperty(ref _windowWidth, value);
        }

        public double WindowHeight
        {
            get => _windowHeight;
            set => SetProperty(ref _windowHeight, value);
        }

        public WindowState WindowState
        {
            get => _windowState;
            set => SetProperty(ref _windowState, value);
        }

        public bool IsControlPanelVisible
        {
            get => _isControlPanelVisible;
            set => SetProperty(ref _isControlPanelVisible, value);
        }

        public RenderStyle SelectedRenderStyle
        {
            get => _selectedRenderStyle;
            set => SetProperty(ref _selectedRenderStyle, value);
        }

        public FftWindowType SelectedFftWindowType
        {
            get => _selectedFftWindowType;
            set => SetProperty(ref _selectedFftWindowType, value);
        }

        public SpectrumScale SelectedScaleType
        {
            get => _selectedScaleType;
            set => SetProperty(ref _selectedScaleType, value);
        }

        public RenderQuality SelectedRenderQuality
        {
            get => _selectedRenderQuality;
            set => SetProperty(ref _selectedRenderQuality, value);
        }

        public int UIBarCount
        {
            get => _uiBarCount;
            set => SetProperty(ref _uiBarCount, value);
        }

        public double UIBarSpacing
        {
            get => _uiBarSpacing;
            set => SetProperty(ref _uiBarSpacing, value);
        }

        public string SelectedPalette
        {
            get => _selectedPalette;
            set => SetProperty(ref _selectedPalette, value);
        }

        public bool IsOverlayTopmost
        {
            get => _isOverlayTopmost;
            set => SetProperty(ref _isOverlayTopmost, value);
        }

        public float UIMinDbLevel
        {
            get => _uiMinDbLevel;
            set => SetProperty(ref _uiMinDbLevel, value);
        }

        public float UIMaxDbLevel
        {
            get => _uiMaxDbLevel;
            set => SetProperty(ref _uiMaxDbLevel, value);
        }

        public float UIAmplificationFactor
        {
            get => _uiAmplificationFactor;
            set => SetProperty(ref _uiAmplificationFactor, value);
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set => SetProperty(ref _isDarkTheme, value);
        }

        #endregion

        #region Базовые методы
        /// <summary>
        /// Сбрасывает все настройки на значения по умолчанию
        /// </summary>
        public void ResetToDefaults()
        {
            // Существующие настройки рендереров
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

            // Настройки для RaindropsRenderer
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

            // UI настройки по умолчанию
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
            UIMinDbLevel = DefaultSettings.UIMinDbLevel;
            UIMaxDbLevel = DefaultSettings.UIMaxDbLevel;
            UIAmplificationFactor = DefaultSettings.UIAmplificationFactor;

            SmartLogger.Log(LogLevel.Information, LogPrefix, "Settings have been reset to defaults");
        }
        #endregion
    }
}