// Controllers/AudioCore/AudioCapture.cs
#nullable enable

namespace SpectrumNet.Controllers.AudioCore;

public sealed class AudioCapture : AsyncDisposableBase
{
    private const string LogPrefix = nameof(AudioCapture);
    private readonly IMainController _controller;
    private readonly ICaptureService _captureService;
    private readonly ISmartLogger _logger = Instance;

    public bool IsRecording => _captureService.IsRecording;

    public AudioCapture(IMainController controller, IRendererFactory rendererFactory)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(rendererFactory);

        _controller = controller;
        var deviceManager = new AudioDeviceManager();
        _captureService = new CaptureService(controller, deviceManager, rendererFactory);
    }

    public SpectrumAnalyzer? GetAnalyzer() => _captureService.GetAnalyzer();

    public Task StartCaptureAsync() => _captureService.StartCaptureAsync();

    public Task StopCaptureAsync() => _captureService.StopCaptureAsync();

    public Task ReinitializeCaptureAsync() => _captureService.ReinitializeCaptureAsync();

    protected override void DisposeManaged() =>
        _logger.Safe(() => DisposeCaptureServiceSync(), LogPrefix, "Error disposing capture service synchronously");

    protected override async ValueTask DisposeAsyncManagedResources() =>
        await _logger.SafeAsync(async () => await DisposeCaptureServiceAsync(), LogPrefix, "Error disposing capture service asynchronously");

    private void DisposeCaptureServiceSync()
    {
        _logger.Log(LogLevel.Information, LogPrefix, "AudioCapture disposing");
        TryStopAndDisposeCaptureServiceSync();
        _logger.Log(LogLevel.Information, LogPrefix, "AudioCapture disposed successfully");
    }

    private void TryStopAndDisposeCaptureServiceSync()
    {
        try
        {
            if (_captureService is IDisposable disposable)
            {
                _captureService.StopCaptureAsync().GetAwaiter().GetResult();
                disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(LogPrefix, $"Error disposing capture service: {ex.Message}");
        }
    }

    private async Task DisposeCaptureServiceAsync()
    {
        _logger.Log(LogLevel.Information, LogPrefix, "AudioCapture async disposing");
        await TryStopAndDisposeCaptureServiceAsync();
        _logger.Log(LogLevel.Information, LogPrefix, "AudioCapture async disposed successfully");
    }

    private async Task TryStopAndDisposeCaptureServiceAsync()
    {
        try
        {
            await _captureService.StopCaptureAsync();
            await _captureService.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(LogPrefix, $"Error async disposing capture service: {ex.Message}");
        }
    }
}