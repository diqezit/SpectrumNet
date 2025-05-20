#nullable enable

namespace SpectrumNet.DataSettings.Interfaces;

public interface ISettingsProvider
{
    ISettings Settings { get; }
    IGainParametersProvider GainParameters { get; }
}