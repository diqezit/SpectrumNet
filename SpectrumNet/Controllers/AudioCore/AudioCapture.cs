#nullable enable

namespace SpectrumNet.Controllers.AudioCore;

public sealed class AudioCapture : AsyncDisposableBase
{
    private const string LogPrefix = nameof(AudioCapture);
    private readonly IMainController _controller;
    private readonly ICaptureService _captureService;

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
        DisposeCaptureService(synchronous: true);

    protected override async ValueTask DisposeAsyncManagedResources() =>
        await DisposeCaptureServiceAsync();

    private void DisposeCaptureService(bool synchronous)
    {
        Log(LogLevel.Information, LogPrefix,
            synchronous ? "AudioCapture disposing" : "AudioCapture async disposing");

        try
        {
            if (synchronous)
            {
                if (_captureService is IDisposable disposable)
                {
                    _captureService.StopCaptureAsync().GetAwaiter().GetResult();
                    disposable.Dispose();
                }
            }

            Log(LogLevel.Information, LogPrefix,
                synchronous ? "AudioCapture disposed successfully" : "AudioCapture async disposed successfully");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogPrefix, $"Error disposing capture service: {ex.Message}");
        }
    }

    private async Task DisposeCaptureServiceAsync()
    {
        Log(LogLevel.Information, LogPrefix, "AudioCapture async disposing");

        try
        {
            await _captureService.StopCaptureAsync();
            await _captureService.DisposeAsync();

            Log(LogLevel.Information, LogPrefix, "AudioCapture async disposed successfully");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogPrefix, $"Error async disposing capture service: {ex.Message}");
        }
    }
}