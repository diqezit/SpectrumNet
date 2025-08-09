#nullable enable

using SpectrumNet;

namespace SpectrumNet.SN.Settings.Models;

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
