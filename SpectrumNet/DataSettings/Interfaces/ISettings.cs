#nullable enable

namespace SpectrumNet.DataSettings.Interfaces;

public interface ISettings : INotifyPropertyChanged
{
    int MaxParticles { get; set; }
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
    ObservableCollection<RenderStyle> FavoriteRenderers { get; set; }

    void LoadSettings(string? filePath = null);
    void SaveSettings(string? filePath = null);
    void ResetToDefaults();

    event EventHandler<string> SettingsChanged;
}