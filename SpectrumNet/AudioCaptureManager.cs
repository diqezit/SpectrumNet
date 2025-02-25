#nullable enable

namespace SpectrumNet
{
    public sealed class AudioCaptureManager : IDisposable
    {
        private readonly MainWindow _mainWindow;
        private readonly object _lock = new();
        private record CaptureState(SpectrumAnalyzer Analyzer, WasapiLoopbackCapture Capture, CancellationTokenSource CTS);

        private CaptureState? _state;
        private bool _isDisposed;

        public bool IsRecording { get; private set; }

        public AudioCaptureManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        }

        public async Task StartCaptureAsync()
        {
            lock (_lock)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(AudioCaptureManager));
                InitializeCapture();
            }
            await MonitorCaptureAsync(_state!.CTS.Token);
        }

        public Task StopCaptureAsync()
        {
            lock (_lock)
            {
                if (_state != null)
                {
                    _state.CTS.Cancel();
                    _state.Capture.StopRecording();
                    DisposeState();
                }
            }
            UpdateStatus(false);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            lock (_lock)
            {
                if (_isDisposed) return;
                try
                {
                    StopCaptureAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Error($"Ошибка при завершении: {ex}");
                }
                _isDisposed = true;
            }
        }

        private void InitializeCapture()
        {
            DisposeState();
            var capture = new WasapiLoopbackCapture();
            var analyzer = InitializeAnalyzer();
            _state = new CaptureState(analyzer, capture, new CancellationTokenSource());
            _state.Capture.DataAvailable += OnDataAvailable;
            UpdateStatus(true);
            _state.Capture.StartRecording();
        }

        private SpectrumAnalyzer InitializeAnalyzer()
        {
            return _mainWindow.Dispatcher.Invoke(() =>
            {
                var analyzer = new SpectrumAnalyzer(new FftProcessor(),
                                                     new SpectrumConverter(_mainWindow._gainParameters),
                                                     SynchronizationContext.Current);
                if (_mainWindow.RenderElement is { ActualWidth: > 0, ActualHeight: > 0 })
                {
                    _mainWindow._renderer?.Dispose();
                    _mainWindow._renderer = new Renderer(_mainWindow._spectrumStyles ?? new SpectrumBrushes(),
                                                         _mainWindow, analyzer, _mainWindow.RenderElement);
                }
                return analyzer;
            });
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || _state?.Analyzer == null)
                return;
            var samples = new float[e.BytesRecorded / 4];
            try
            {
                Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
            }
            catch (Exception ex)
            {
                Log.Error($"Ошибка обработки аудио-данных: {ex}");
                return;
            }
            _ = _state.Analyzer.AddSamplesAsync(samples, _state.Capture.WaveFormat.SampleRate);
        }

        private async Task MonitorCaptureAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (TaskCanceledException)
            {
                // Завершение мониторинга – ожидаемо
            }
        }

        private void UpdateStatus(bool isRecording)
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                IsRecording = isRecording;
                _mainWindow.IsRecording = isRecording;
                _mainWindow.StatusText = isRecording ? MwConstants.RecordingStatus : MwConstants.ReadyStatus;
                _mainWindow.OnPropertyChanged(nameof(_mainWindow.IsRecording),
                                                nameof(_mainWindow.CanStartCapture),
                                                nameof(_mainWindow.StatusText));
            });
        }

        private void DisposeState()
        {
            _state?.CTS.Dispose();
            _state?.Capture.Dispose();
            _state?.Analyzer.Dispose();
            _state = null;
        }
    }
}
