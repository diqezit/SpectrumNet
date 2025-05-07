#nullable enable

namespace SpectrumNet.DataSettings.Interfaces;

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
    bool LimitFpsTo60 { get; set; }

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