namespace SpectrumNet
{
    public interface ISettings
    {
        // Существующие настройки
        public int MaxParticles { get; set; } // Изменено с float на int
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


        // Новые свойства
        float MaxZDepth { get; set; }
        float MinZDepth { get; set; }

        void ResetToDefaults();
        event PropertyChangedEventHandler? PropertyChanged;
    }

    public class DefaultSettings
    {
        // Существующие настройки
        public const int MaxParticles = 2500;
        public const float SpawnThresholdOverlay = 0.03f;
        public const float SpawnThresholdNormal = 0.08f;
        public const float ParticleVelocityMin = 0.5f;
        public const float ParticleVelocityMax = 2.0f;
        public const float ParticleSizeOverlay = 2.5f;
        public const float ParticleSizeNormal = 2.0f;
        public const float ParticleLife = 3.0f;
        public const float ParticleLifeDecay = 0.01f;
        public const float VelocityMultiplier = 0.8f;
        public const float AlphaDecayExponent = 1.3f;
        public const float SpawnProbability = 0.08f;
        public const float OverlayOffsetMultiplier = 1.0f;
        public const float OverlayHeightMultiplier = 0.7f;

        // Новые настройки
        public const float MaxZDepth = 1000f;
        public const float MinZDepth = 100f;
    }

    public class Settings : ISettings, INotifyPropertyChanged
    {

        // Существующие поля
        private int _maxParticles;
        private float _spawnThresholdOverlay;
        private float _spawnThresholdNormal;
        private float _particleVelocityMin;
        private float _particleVelocityMax;
        private float _particleSizeOverlay;
        private float _particleSizeNormal;
        private float _particleLife;
        private float _particleLifeDecay;
        private float _velocityMultiplier;
        private float _alphaDecayExponent;
        private float _spawnProbability;
        private float _overlayOffsetMultiplier;
        private float _overlayHeightMultiplier;

        // Новые поля
        private float _maxZDepth;
        private float _minZDepth;


        private static readonly Lazy<Settings> _instance = new(() => new Settings());
        public static Settings Instance => _instance.Value;

        public Settings()
        {
            ResetToDefaults();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void ResetToDefaults()
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

            Log.Information("Settings have been reset to defaults");
        }

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

        // Реализация новых свойств
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
    }
}