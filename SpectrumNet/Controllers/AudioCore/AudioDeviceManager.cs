#nullable enable

namespace SpectrumNet.Controllers.AudioCore;

public sealed class AudioDeviceManager : AsyncDisposableBase, IAudioDeviceManager
{
    private const string LogPrefix = nameof(AudioDeviceManager);
    private readonly MMDeviceEnumerator _deviceEnumerator;
    private readonly AudioEndpointNotificationHandler _notificationHandler;
    private MMDevice? _currentDevice;
    private string _lastDeviceId = string.Empty;

    public event Action DeviceChanged = () => { };

    public AudioDeviceManager()
    {
        _deviceEnumerator = new MMDeviceEnumerator();
        _notificationHandler = new AudioEndpointNotificationHandler(OnDeviceChanged);
    }

    public MMDevice? GetDefaultAudioDevice()
    {
        try
        {
            DisposeCurrentDevice();
            return AcquireNewDevice();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogPrefix, $"Error getting default audio device: {ex.Message}");
            return null;
        }
    }

    private void DisposeCurrentDevice() =>
        SafeDispose(
            _currentDevice,
            nameof(_currentDevice),
            new ErrorHandlingOptions { Source = LogPrefix }
        );

    private MMDevice? AcquireNewDevice()
    {
        _currentDevice = _deviceEnumerator.GetDefaultAudioEndpoint(
            DataFlow.Render,
            Role.Multimedia
        );
        if (_currentDevice != null && _currentDevice.ID != _lastDeviceId)
            _lastDeviceId = _currentDevice.ID;
        return _currentDevice;
    }

    public void RegisterDeviceNotifications() =>
        Safe(
            () => _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationHandler),
            new ErrorHandlingOptions { Source = LogPrefix }
        );

    public void UnregisterDeviceNotifications() =>
        Safe(
            () => _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationHandler),
            new ErrorHandlingOptions { Source = LogPrefix }
        );

    private void OnDeviceChanged() =>
        Safe(() => DeviceChanged(), new ErrorHandlingOptions { Source = LogPrefix });

    protected override void DisposeManaged()
    {
        UnregisterDeviceNotifications();
        SafeDispose(_currentDevice, nameof(_currentDevice));
        SafeDispose(_deviceEnumerator, nameof(_deviceEnumerator));
    }

    protected override ValueTask DisposeAsyncManagedResources()
    {
        UnregisterDeviceNotifications();
        SafeDispose(_currentDevice, nameof(_currentDevice));
        SafeDispose(_deviceEnumerator, nameof(_deviceEnumerator));
        return ValueTask.CompletedTask;
    }

    private class AudioEndpointNotificationHandler
        (Action deviceChangeCallback) : IMMNotificationClient
    {
        private readonly Action _deviceChangeCallback = deviceChangeCallback ??
                throw new ArgumentNullException(nameof(deviceChangeCallback));

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) =>
            _deviceChangeCallback();

        public void OnDeviceAdded(string pwstrDeviceId) =>
            _deviceChangeCallback();

        public void OnDeviceRemoved(string deviceId) =>
            _deviceChangeCallback();

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
                _deviceChangeCallback();
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}