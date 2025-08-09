#nullable enable

using SpectrumNet;

namespace SpectrumNet.SN.Settings.Constants;

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
        ShowPerformanceInfo = true,
        LimitFpsTo60 = false;
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
}