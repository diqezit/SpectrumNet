#nullable enable

namespace SpectrumNet.DataSettings;

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