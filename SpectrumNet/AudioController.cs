namespace SpectrumNet
{
    public sealed class AudioController : IDisposable
    {
        private const int MonitorDelay = 16;
        private SpectrumAnalyzer? _analyzer;
        private readonly MainWindow _mainWindow;
        private readonly CaptureOperations _captureOps;
        private WasapiLoopbackCapture? _capture;
        private CancellationTokenSource? _cts;

        public AudioController(SpectrumAnalyzer analyzer, MainWindow mainWindow, CaptureOperations captureOps)
        {
            _analyzer = analyzer;
            _mainWindow = mainWindow;
            _captureOps = captureOps;
        }

        public async Task StartCaptureAsync()
        {
            await StopCaptureAsync();
            _cts = new CancellationTokenSource();
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;
            UpdateStatus(true, "Запись...");
            _capture.StartRecording();
            await MonitorCaptureAsync(_cts.Token);
        }

        public async Task StopCaptureAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _capture?.StopRecording();
                _capture?.Dispose();
                _analyzer?.Dispose();
                _cts = null;
                _capture = null;
                _analyzer = null;
            }
            UpdateStatus(false, "Готово");
        }

        private async Task MonitorCaptureAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                    await Task.Delay(MonitorDelay, token);
            }
            catch (OperationCanceledException) { }
        }

        private async void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded > 0 && _capture != null && _analyzer != null)
            {
                float[] samples = new float[e.BytesRecorded / 4];
                Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
                await _analyzer.AddSamplesAsync(samples, _capture.WaveFormat.SampleRate);
            }
        }

        private void UpdateStatus(bool isRecording, string statusText)
        {
            _captureOps.SetRecordingStatus(isRecording, _mainWindow.Dispatcher);
            _captureOps.SetStatusText(statusText);
        }

        public void Dispose() => StopCaptureAsync().GetAwaiter().GetResult();
    }
}