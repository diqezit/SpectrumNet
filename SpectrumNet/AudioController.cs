#nullable enable

using NAudio.Wave;
using Serilog;
using System.Runtime.InteropServices;

namespace SpectrumNet
{
    /// <summary>
    /// The AudioController class is responsible for capturing audio data using WasapiLoopbackCapture and processing it using SpectrumAnalyzer.
    /// It manages the recording status and provides methods to start and stop audio capture.
    /// </summary>
    public sealed class AudioController : IDisposable
    {
        private const int MonitorCaptureStatusDelay = 16;
        private const string RecordingStatusText = "Запись...";
        private const string ReadyStatusText = "Готово";

        private SpectrumAnalyzer? _analyzer;
        private readonly MainWindow _mainWindow;
        private readonly CaptureOperations _captureOperations;
        private WasapiLoopbackCapture? _capture;
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>
        /// Initializes a new instance of the AudioController class.
        /// </summary>
        /// <param name="analyzer">The SpectrumAnalyzer instance used for processing audio data.</param>
        /// <param name="mainWindow">The MainWindow instance used for updating the recording status.</param>
        /// <param name="captureOperations">The CaptureOperations instance used for updating the recording status and status text.</param>
        public AudioController(SpectrumAnalyzer analyzer, MainWindow mainWindow, CaptureOperations captureOperations)
        {
            _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _captureOperations = captureOperations ?? throw new ArgumentNullException(nameof(captureOperations));
            Log.Debug("[AudioController] Initialized");
        }

        #region Capture Management

        /// <summary>
        /// Starts the audio capture asynchronously.
        /// </summary>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public async Task StartCaptureAsync()
        {
            Log.Information("[AudioController] Starting capture");
            await StopCaptureAsync();

            _cancellationTokenSource = new CancellationTokenSource();
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;

            UpdateRecordingStatus(true, RecordingStatusText);
            _capture.StartRecording();

            await MonitorCaptureStatusAsync(_cancellationTokenSource.Token);
        }

        /// <summary>
        /// Stops the audio capture asynchronously.
        /// </summary>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public async Task StopCaptureAsync()
        {
            if (_cancellationTokenSource != null)
            {
                Log.Information("[AudioController] Stopping capture");
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _capture?.StopRecording();
                _capture?.Dispose();
                _analyzer?.Dispose();

                _cancellationTokenSource = null;
                _capture = null;
                _analyzer = null;
            }

            UpdateRecordingStatus(false, ReadyStatusText);
        }

        /// <summary>
        /// Monitors the capture status asynchronously until cancellation is requested.
        /// </summary>
        /// <param name="token">The CancellationToken used to signal cancellation.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        private async Task MonitorCaptureStatusAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                    await Task.Delay(MonitorCaptureStatusDelay, token);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("[AudioController] Monitoring stopped");
            }
        }

        #endregion

        #region Event Handling

        /// <summary>
        /// Handles the DataAvailable event, processing the captured audio data.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The WaveInEventArgs containing the captured audio data.</param>
        private async void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || _capture == null || _analyzer == null)
            {
                Log.Warning("[AudioController] Invalid data or null objects in OnDataAvailable");
                return;
            }

            float[] samples = new float[e.BytesRecorded / 4];
            Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
            int sampleRate = _capture.WaveFormat.SampleRate;
            await _analyzer.AddSamplesAsync(samples, sampleRate);
        }

        #endregion

        #region Status Management

        /// <summary>
        /// Updates the recording status and status text in the MainWindow.
        /// </summary>
        /// <param name="isRecording">A boolean indicating whether recording is active.</param>
        /// <param name="statusText">The status text to display.</param>
        private void UpdateRecordingStatus(bool isRecording, string statusText)
        {
            _captureOperations.SetRecordingStatus(isRecording, _mainWindow.Dispatcher);
            _captureOperations.SetStatusText(statusText);
            Log.Debug("[AudioController] Recording status updated: {Status}", statusText);
        }

        #endregion

        #region Disposal

        /// <summary>
        /// Disposes of the AudioController, stopping the capture and releasing resources.
        /// </summary>
        public void Dispose()
        {
            Log.Debug("[AudioController] Disposing");
            StopCaptureAsync().GetAwaiter().GetResult();
        }

        #endregion
    }
}