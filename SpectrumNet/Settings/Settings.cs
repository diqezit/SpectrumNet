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

        // Новые свойства
        float VelocityMultiplier { get; set; }
        float AlphaDecayExponent { get; set; }
        float SpawnProbability { get; set; }
        float OverlayOffsetMultiplier { get; set; }
        float OverlayHeightMultiplier { get; set; }

        void ResetToDefaults();
        event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Class containing default settings for rendering (particle render at this time) in the app.
    /// These settings define various parameters such as the number of particles,
    /// spawn thresholds, movement parameters, visual parameters,
    /// particle lifecycle parameters, and spawn and overlay parameters.
    /// </summary>
    public class DefaultSettings
    {
        /// <summary>
        /// Maximum number of particles allowed on the canvas in time.
        /// </summary>
        public const int MaxParticles = 2500;

        /// <summary>
        /// Spawn threshold for particles when the overlay is active.
        /// </summary>
        public const float SpawnThresholdOverlay = 0.02f;

        /// <summary>
        /// Spawn threshold for particles when the overlay is not active.
        /// </summary>
        public const float SpawnThresholdNormal = 0.06f;

        /// <summary>
        /// Minimum velocity for particles.
        /// </summary>
        public const float ParticleVelocityMin = 0.4f;

        /// <summary>
        /// Maximum velocity for particles.
        /// </summary>
        public const float ParticleVelocityMax = 1.8f;

        /// <summary>
        /// Multiplier for particle velocity.
        /// </summary>
        public const float VelocityMultiplier = 0.85f;

        /// <summary>
        /// Size of particles when the overlay is active.
        /// </summary>
        public const float ParticleSizeOverlay = 3.0f;

        /// <summary>
        /// Size of particles when the overlay is not active.
        /// </summary>
        public const float ParticleSizeNormal = 2.2f;

        /// <summary>
        /// Initial life of particles.
        /// </summary>
        public const float ParticleLife = 4.0f;

        /// <summary>
        /// Decay rate of particle life.
        /// </summary>
        public const float ParticleLifeDecay = 0.008f;

        /// <summary>
        /// Exponent for alpha decay of particles.
        /// </summary>
        public const float AlphaDecayExponent = 1.5f;

        /// <summary>
        /// Probability of spawning a new particle.
        /// </summary>
        public const float SpawnProbability = 0.1f;

        /// <summary>
        /// Multiplier for the overlay offset. Configure for 1920x1080.
        /// </summary>
        public const float OverlayOffsetMultiplier = 1.0f;

        /// <summary>
        /// Multiplier for the overlay height. Configure for 1920x1080.
        /// </summary>
        public const float OverlayHeightMultiplier = 0.7f;
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

        // Новые поля
        private float _velocityMultiplier;
        private float _alphaDecayExponent;
        private float _spawnProbability;
        private float _overlayOffsetMultiplier;
        private float _overlayHeightMultiplier;

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

            Log.Information("Settings have been reset to defaults");
        }

        // Реализация новых свойств
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
    }
}