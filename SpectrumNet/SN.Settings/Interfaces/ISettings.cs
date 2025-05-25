#nullable enable

namespace SpectrumNet.SN.Settings.Interfaces;

public interface ISettings : INotifyPropertyChanged
{
    ParticleSettings Particles { get; set; }
    RaindropSettings Raindrops { get; set; }
    WindowSettings Window { get; set; }
    VisualizationSettings Visualization { get; set; }
    AudioSettings Audio { get; set; }
    GeneralSettings General { get; set; }

    void LoadSettings(string? filePath = null);
    void SaveSettings(string? filePath = null);
    void ResetToDefaults();

    event EventHandler<string> SettingsChanged;
}