#nullable enable

namespace SpectrumNet.SN.Settings;

public class ParticleSettings
{
    public int MaxParticles { get; set; } = DefaultSettings.MaxParticles;
    public float SpawnThresholdOverlay { get; set; } = DefaultSettings.SpawnThresholdOverlay;
    public float SpawnThresholdNormal { get; set; } = DefaultSettings.SpawnThresholdNormal;
    public float ParticleVelocityMin { get; set; } = DefaultSettings.ParticleVelocityMin;
    public float ParticleVelocityMax { get; set; } = DefaultSettings.ParticleVelocityMax;
    public float ParticleSizeOverlay { get; set; } = DefaultSettings.ParticleSizeOverlay;
    public float ParticleSizeNormal { get; set; } = DefaultSettings.ParticleSizeNormal;
    public float ParticleLife { get; set; } = DefaultSettings.ParticleLife;
    public float ParticleLifeDecay { get; set; } = DefaultSettings.ParticleLifeDecay;
    public float VelocityMultiplier { get; set; } = DefaultSettings.VelocityMultiplier;
    public float AlphaDecayExponent { get; set; } = DefaultSettings.AlphaDecayExponent;
    public float SpawnProbability { get; set; } = DefaultSettings.SpawnProbability;
    public float OverlayOffsetMultiplier { get; set; } = DefaultSettings.OverlayOffsetMultiplier;
    public float OverlayHeightMultiplier { get; set; } = DefaultSettings.OverlayHeightMultiplier;
    public float MaxZDepth { get; set; } = DefaultSettings.MaxZDepth;
    public float MinZDepth { get; set; } = DefaultSettings.MinZDepth;
}

public class RaindropSettings
{
    public int MaxRaindrops { get; set; } = DefaultSettings.MaxRaindrops;
    public float BaseFallSpeed { get; set; } = DefaultSettings.BaseFallSpeed;
    public float RaindropSize { get; set; } = DefaultSettings.RaindropSize;
    public float SplashParticleSize { get; set; } = DefaultSettings.SplashParticleSize;
    public float SplashUpwardForce { get; set; } = DefaultSettings.SplashUpwardForce;
    public float SpeedVariation { get; set; } = DefaultSettings.SpeedVariation;
    public float IntensitySpeedMultiplier { get; set; } = DefaultSettings.IntensitySpeedMultiplier;
    public float TimeScaleFactor { get; set; } = DefaultSettings.TimeScaleFactor;
    public float MaxTimeStep { get; set; } = DefaultSettings.MaxTimeStep;
    public float MinTimeStep { get; set; } = DefaultSettings.MinTimeStep;
}

public class WindowSettings
{
    public double Left { get; set; } = DefaultSettings.WindowLeft;
    public double Top { get; set; } = DefaultSettings.WindowTop;
    public double Width { get; set; } = DefaultSettings.WindowWidth;
    public double Height { get; set; } = DefaultSettings.WindowHeight;
    public WindowState State { get; set; } = DefaultSettings.WindowState;
    public bool IsControlPanelVisible { get; set; } = DefaultSettings.IsControlPanelVisible;
}

public class VisualizationSettings
{
    public RenderStyle SelectedRenderStyle { get; set; } = DefaultSettings.SelectedRenderStyle;
    public FftWindowType SelectedFftWindowType { get; set; } = DefaultSettings.SelectedFftWindowType;
    public SpectrumScale SelectedScaleType { get; set; } = DefaultSettings.SelectedScaleType;
    public RenderQuality SelectedRenderQuality { get; set; } = DefaultSettings.SelectedRenderQuality;
    public int BarCount { get; set; } = DefaultSettings.UIBarCount;
    public double BarSpacing { get; set; } = DefaultSettings.UIBarSpacing;
    public string SelectedPalette { get; set; } = DefaultSettings.SelectedPalette;
    public bool ShowPerformanceInfo { get; set; } = DefaultSettings.ShowPerformanceInfo;
}

public class AudioSettings
{
    public float MinDbLevel { get; set; } = DefaultSettings.UIMinDbLevel;
    public float MaxDbLevel { get; set; } = DefaultSettings.UIMaxDbLevel;
    public float AmplificationFactor { get; set; } = DefaultSettings.UIAmplificationFactor;
}

public class GeneralSettings
{
    public bool IsOverlayTopmost { get; set; } = DefaultSettings.IsOverlayTopmost;
    public bool IsDarkTheme { get; set; } = DefaultSettings.IsDarkTheme;
    public bool LimitFpsTo60 { get; set; } = DefaultSettings.LimitFpsTo60;
    public ObservableCollection<RenderStyle> FavoriteRenderers { get; set; } = [];
}