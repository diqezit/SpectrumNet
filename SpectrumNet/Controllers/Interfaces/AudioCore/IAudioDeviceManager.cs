#nullable enable

namespace SpectrumNet.Controllers.Interfaces.AudioCore;

public interface IAudioDeviceManager : IAsyncDisposable
{
    event Action DeviceChanged;
    MMDevice? GetDefaultAudioDevice();
    void RegisterDeviceNotifications();
    void UnregisterDeviceNotifications();
}