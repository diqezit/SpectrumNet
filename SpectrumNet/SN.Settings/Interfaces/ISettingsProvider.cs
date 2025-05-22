#nullable enable

namespace SpectrumNet.SN.Settings.Interfaces;

public interface ISettingsProvider
{
    ISettings Settings { get; }
    IGainParametersProvider GainParameters { get; }
}