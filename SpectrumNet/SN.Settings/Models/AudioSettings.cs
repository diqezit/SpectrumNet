#nullable enable

using SpectrumNet;

namespace SpectrumNet.SN.Settings.Models;

public class AudioSettings
{
    public float MinDbLevel { get; set; } = DefaultSettings.UIMinDbLevel;
    public float MaxDbLevel { get; set; } = DefaultSettings.UIMaxDbLevel;
    public float AmplificationFactor { get; set; } = DefaultSettings.UIAmplificationFactor;
}
