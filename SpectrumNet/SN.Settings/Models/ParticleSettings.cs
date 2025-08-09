#nullable enable

using SpectrumNet;

namespace SpectrumNet.SN.Settings.Models;

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
