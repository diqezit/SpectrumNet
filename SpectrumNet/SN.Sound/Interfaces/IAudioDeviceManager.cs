#nullable enable

namespace SpectrumNet.SN.Sound.Interfaces;

public interface IAudioDeviceManager : IAsyncDisposable
{
    event Action DeviceChanged;
    MMDevice? GetDefaultAudioDevice();
    void RegisterDeviceNotifications();
    void UnregisterDeviceNotifications();
}