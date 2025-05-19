// Controllers/AudioCore/AudioDeviceManager.cs
#nullable enable

namespace SpectrumNet.Controllers.AudioCore;

public sealed class AudioDeviceManager : AsyncDisposableBase, IAudioDeviceManager
{
    private const string LogPrefix = nameof(AudioDeviceManager);
    private readonly ISmartLogger _logger = Instance;
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

    public MMDevice? GetDefaultAudioDevice() =>
        _logger.SafeResult<MMDevice?>(
            () => HandleGetDefaultAudioDevice(),
            null,
            LogPrefix,
            "Error getting default audio device"
        );

    private MMDevice? HandleGetDefaultAudioDevice()
    {
        DisposeCurrentDevice();
        return AcquireNewDevice();
    }

    private void DisposeCurrentDevice() =>
        _logger.Safe(
            () => {
                if (_currentDevice != null)
                {
                    _currentDevice.Dispose();
                    _currentDevice = null;
                }
            },
            LogPrefix,
            "Error disposing current device"
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
        _logger.Safe(
            () => _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationHandler),
            LogPrefix,
            "Error registering device notifications"
        );

    public void UnregisterDeviceNotifications() =>
        _logger.Safe(
            () => _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationHandler),
            LogPrefix,
            "Error unregistering device notifications"
        );

    private void OnDeviceChanged() =>
        _logger.Safe(() => DeviceChanged(), LogPrefix, "Error in device change callback");

    protected override void DisposeManaged() =>
        _logger.Safe(() => HandleDispose(), LogPrefix, "Error during dispose");

    private void HandleDispose()
    {
        UnregisterDeviceNotifications();

        if (_currentDevice != null)
        {
            _currentDevice.Dispose();
            _currentDevice = null;
        }

        _deviceEnumerator?.Dispose();
    }

    protected override ValueTask DisposeAsyncManagedResources()
    {
        HandleDispose();
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