#nullable enable

namespace SpectrumNet
{
    public sealed class FireRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static readonly Lazy<FireRenderer> _instance = new(() => new FireRenderer());
        private bool _isInitialized;
        private float[] _previousSpectrum = Array.Empty<float>();
        private float[]? _processedSpectrum;
        private readonly Random _random = new();
        private float _time;
        private readonly SKPath _path = new();
        private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
        private readonly object _spectrumLock = new();
        private bool _disposed;
        #endregion

        #region Configuration
        private static class Config
        {
            public const float TimeStep = 0.016f;              // ~60 FPS
            public const float DecayRate = 0.08f;
            public const float ControlPointProportion = 0.4f;
            public const float RandomOffsetProportion = 0.5f;
            public const float RandomOffsetCenter = 0.25f;
            public const float FlameBottomProportion = 0.25f;
            public const float FlameBottomMax = 6f;
            public const float MinBottomAlpha = 0.3f;
            public const float WaveSpeed = 2.0f;
            public const float WaveAmplitude = 0.2f;
            public const float HorizontalWaveFactor = 0.15f;
            public const float CubicControlPoint1 = 0.33f;
            public const float CubicControlPoint2 = 0.66f;
            public const float OpacityWaveSpeed = 3.0f;
            public const float OpacityPhaseShift = 0.2f;
            public const float OpacityWaveAmplitude = 0.1f;
            public const float OpacityBase = 0.9f;
            public const float PositionPhaseShift = 0.5f;
            public const int MinBarCount = 10;
            public const float GlowIntensity = 0.3f;
            public const float HighIntensityThreshold = 0.7f;
        }
        #endregion

        #region Constructors and Instance Management
        private FireRenderer() { /*Private constructor for singleton pattern*/ }

        public static FireRenderer GetInstance() => _instance.Value;
        #endregion

        #region Public Methods
        public void Initialize()
        {
            try
            {
                _renderSemaphore.Wait();

                if (_isInitialized)
                    return;

                _isInitialized = true;
                _time = 0f;

                Log.Debug("FireRenderer initialized");
            }
            catch (Exception ex)
            {
                Log.Error($"Error initializing FireRenderer: {ex.Message}");
            }
            finally
            {
                _renderSemaphore.Release();
            }
        }

        public void Configure(bool isOverlayActive) { /* Configuration method kept for interface compatibility*/ }

        public void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? basePaint,
            Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, basePaint))
                return;

            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _renderSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    _time += Config.TimeStep;
                    ProcessSpectrumData(spectrum!, barCount);
                }

                float[] renderSpectrum;
                lock (_spectrumLock)
                {
                    renderSpectrum = _processedSpectrum ??
                                     ProcessSpectrumSynchronously(spectrum!, barCount);
                }

                using var renderScope = new RenderScope(
                    this, canvas!, renderSpectrum, info, barWidth, barSpacing, barCount, basePaint!);
                renderScope.Execute(drawPerformanceInfo);
            }
            catch (Exception ex)
            {
                Log.Error($"Error rendering flames: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _renderSemaphore.Release();
                }
            }
        }

        private void ProcessSpectrumData(float[] spectrum, int barCount)
        {
            try
            {
                EnsureSpectrumBuffer(spectrum.Length);

                int halfSpectrumLength = spectrum.Length / 2;
                int actualBarCount = Math.Min(halfSpectrumLength, barCount);
                float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, halfSpectrumLength);

                UpdatePreviousSpectrum(spectrum);

                lock (_spectrumLock)
                {
                    _processedSpectrum = scaledSpectrum;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing spectrum data: {ex.Message}");
            }
        }

        private float[] ProcessSpectrumSynchronously(float[] spectrum, int barCount)
        {
            int halfSpectrumLength = spectrum.Length / 2;
            int actualBarCount = Math.Min(halfSpectrumLength, barCount);
            return ScaleSpectrum(spectrum, actualBarCount, halfSpectrumLength);
        }

        private void EnsureSpectrumBuffer(int length)
        {
            if (_previousSpectrum.Length != length)
            {
                _previousSpectrum = new float[length];
                Array.Copy(_previousSpectrum, _previousSpectrum, length);
            }
        }

        private float[] ScaleSpectrum(float[] spectrum, int targetCount, int halfSpectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)halfSpectrumLength / targetCount;
            float[] localSpectrum = spectrum;

            Parallel.For(0, targetCount, i =>
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);

                for (int j = start; j < end && j < halfSpectrumLength; j++)
                    sum += localSpectrum[j];

                scaledSpectrum[i] = sum / (end - start);
            });

            return scaledSpectrum;
        }

        private void UpdatePreviousSpectrum(float[] spectrum)
        {
            for (int i = 0; i < spectrum.Length; i++)
            {
                _previousSpectrum[i] = Math.Max(
                    spectrum[i],
                    _previousSpectrum[i] - Config.DecayRate
                );
            }
        }
        #endregion

        #region Private Helper Classes
        private readonly struct RenderScope : IDisposable
        {
            private readonly FireRenderer _renderer;
            private readonly SKCanvas _canvas;
            private readonly float[] _spectrum;
            private readonly SKImageInfo _info;
            private readonly float _barWidth;
            private readonly float _barSpacing;
            private readonly int _barCount;
            private readonly SKPaint _basePaint;
            private readonly SKPaint _workingPaint;
            private readonly SKPaint _glowPaint;

            public RenderScope(
                FireRenderer renderer,
                SKCanvas canvas,
                float[] spectrum,
                SKImageInfo info,
                float barWidth,
                float barSpacing,
                int barCount,
                SKPaint basePaint)
            {
                _renderer = renderer;
                _canvas = canvas;
                _spectrum = spectrum;
                _info = info;
                _barWidth = barWidth;
                _barSpacing = barSpacing;
                _barCount = barCount;
                _basePaint = basePaint;
                _workingPaint = basePaint.Clone();

                _glowPaint = basePaint.Clone();
                _glowPaint.ImageFilter = SKImageFilter.CreateBlur(3f, 3f);
            }

            public void Execute(Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
            {
                var actualBarCount = _spectrum.Length;
                var totalBarWidth = CalculateTotalBarWidth(actualBarCount);

                RenderFlames(actualBarCount, totalBarWidth);

                drawPerformanceInfo(_canvas, _info);
            }

            private float CalculateTotalBarWidth(int actualBarCount)
            {
                float totalBarWidth = _barWidth + _barSpacing;
                if (actualBarCount < Config.MinBarCount)
                    totalBarWidth *= (float)Config.MinBarCount / actualBarCount;
                return totalBarWidth;
            }

            private void RenderFlames(int actualBarCount, float totalBarWidth)
            {
                for (int i = 0; i < actualBarCount; i++)
                {
                    var flameParams = CalculateFlameParameters(i, totalBarWidth, _spectrum[i]);
                    RenderSingleFlame(flameParams);
                }
            }

            private FlameParameters CalculateFlameParameters(int index, float totalBarWidth, float spectrumValue)
            {
                float x = index * totalBarWidth;
                float waveOffset = (float)Math.Sin(_renderer._time * Config.WaveSpeed + index * Config.PositionPhaseShift)
                    * Config.WaveAmplitude;
                float currentHeight = spectrumValue * _info.Height * (1 + waveOffset);
                float previousHeight = _renderer._previousSpectrum.Length > index ?
                                      _renderer._previousSpectrum[index] * _info.Height : 0;

                return new FlameParameters(
                    x, currentHeight, previousHeight,
                    _barWidth, _info.Height, index
                );
            }

            private void RenderSingleFlame(FlameParameters parameters)
            {
                _renderer._path.Reset();

                var (flameTop, flameBottom) = CalculateFlameVerticalPositions(parameters);
                var x = CalculateHorizontalPosition(parameters);

                if (parameters.CurrentHeight / parameters.CanvasHeight > Config.HighIntensityThreshold)
                {
                    RenderFlameGlow(x, flameTop, flameBottom, parameters);
                }

                RenderFlameBase(x, flameBottom);
                RenderFlameBody(x, flameTop, flameBottom, parameters);
            }

            private (float flameTop, float flameBottom) CalculateFlameVerticalPositions(FlameParameters parameters)
            {
                float flameTop = parameters.CanvasHeight - Math.Max(parameters.CurrentHeight, parameters.PreviousHeight);
                float flameBottom = parameters.CanvasHeight -
                    Math.Min(parameters.CurrentHeight * Config.FlameBottomProportion, Config.FlameBottomMax);
                return (flameTop, flameBottom);
            }

            private float CalculateHorizontalPosition(FlameParameters parameters)
            {
                float waveOffset = (float)Math.Sin(_renderer._time * Config.WaveSpeed +
                    parameters.Index * Config.PositionPhaseShift) *
                    (parameters.BarWidth * Config.HorizontalWaveFactor);
                return parameters.X + waveOffset;
            }

            private void RenderFlameBase(float x, float flameBottom)
            {
                _renderer._path.Reset();
                _renderer._path.MoveTo(x, flameBottom);
                _renderer._path.LineTo(x + _barWidth, flameBottom);

                using var bottomPaint = _workingPaint.Clone();
                byte bottomAlpha = (byte)(255 * Config.MinBottomAlpha);
                bottomPaint.Color = bottomPaint.Color.WithAlpha(bottomAlpha);
                _canvas.DrawPath(_renderer._path, bottomPaint);
            }

            private void RenderFlameGlow(float x, float flameTop, float flameBottom, FlameParameters parameters)
            {
                _renderer._path.Reset();
                _renderer._path.MoveTo(x, flameBottom);

                float height = flameBottom - flameTop;
                var controlPoints = CalculateControlPoints(x, flameBottom, height, parameters.BarWidth);

                _renderer._path.CubicTo(
                    controlPoints.cp1X, controlPoints.cp1Y,
                    controlPoints.cp2X, controlPoints.cp2Y,
                    x + parameters.BarWidth, flameBottom
                );

                float intensity = parameters.CurrentHeight / parameters.CanvasHeight;
                byte glowAlpha = (byte)(255 * intensity * Config.GlowIntensity);
                _glowPaint.Color = _glowPaint.Color.WithAlpha(glowAlpha);

                _canvas.DrawPath(_renderer._path, _glowPaint);
            }

            private void RenderFlameBody(float x, float flameTop, float flameBottom, FlameParameters parameters)
            {
                _renderer._path.Reset();
                _renderer._path.MoveTo(x, flameBottom);

                float height = flameBottom - flameTop;
                var controlPoints = CalculateControlPoints(x, flameBottom, height, parameters.BarWidth);

                _renderer._path.CubicTo(
                    controlPoints.cp1X, controlPoints.cp1Y,
                    controlPoints.cp2X, controlPoints.cp2Y,
                    x + parameters.BarWidth, flameBottom
                );

                UpdatePaintForFlame(parameters);
                _canvas.DrawPath(_renderer._path, _workingPaint);
            }

            private (float cp1X, float cp1Y, float cp2X, float cp2Y) CalculateControlPoints(
                float x, float flameBottom, float height, float barWidth)
            {
                float cp1Y = flameBottom - height * Config.CubicControlPoint1;
                float cp2Y = flameBottom - height * Config.CubicControlPoint2;

                float randomOffset1 = (float)(_renderer._random.NextDouble() *
                    barWidth * Config.RandomOffsetProportion -
                    barWidth * Config.RandomOffsetCenter);
                float randomOffset2 = (float)(_renderer._random.NextDouble() *
                    barWidth * Config.RandomOffsetProportion -
                    barWidth * Config.RandomOffsetCenter);

                return (
                    x + barWidth * Config.CubicControlPoint1 + randomOffset1,
                    cp1Y,
                    x + barWidth * Config.CubicControlPoint2 + randomOffset2,
                    cp2Y
                );
            }

            private void UpdatePaintForFlame(FlameParameters parameters)
            {
                float opacityWave = (float)Math.Sin(_renderer._time * Config.OpacityWaveSpeed +
                    parameters.Index * Config.OpacityPhaseShift) *
                    Config.OpacityWaveAmplitude + Config.OpacityBase;

                byte alpha = (byte)(255 * Math.Min(
                    parameters.CurrentHeight / parameters.CanvasHeight * opacityWave, 1.0f));
                _workingPaint.Color = _workingPaint.Color.WithAlpha(alpha);
            }

            public void Dispose()
            {
                _workingPaint.Dispose();
                _glowPaint.Dispose();
            }
        }

        private readonly record struct FlameParameters(
            float X,
            float CurrentHeight,
            float PreviousHeight,
            float BarWidth,
            float CanvasHeight,
            int Index
        );
        #endregion

        #region Validation Methods
        private bool ValidateRenderParameters(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? basePaint)
        {
            if (!_isInitialized)
            {
                Log.Error("FireRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null)
            {
                Log.Error("Cannot render flames with null canvas");
                return false;
            }

            if (spectrum == null || spectrum.Length == 0)
            {
                Log.Error("Cannot render flames with null or empty spectrum");
                return false;
            }

            if (basePaint == null)
            {
                Log.Error("Cannot render flames with null paint");
                return false;
            }

            if (info.Width <= 0 || info.Height <= 0)
            {
                Log.Error("Cannot render flames with invalid canvas dimensions");
                return false;
            }

            return true;
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                _renderSemaphore.Wait();

                if (!_isInitialized)
                    return;

                _path.Dispose();
                _renderSemaphore.Dispose();
                _previousSpectrum = Array.Empty<float>();
                _processedSpectrum = null;
                _isInitialized = false;
                _disposed = true;

                Log.Debug("FireRenderer disposed");
            }
            catch (Exception ex)
            {
                Log.Error($"Error disposing FireRenderer: {ex.Message}");
            }
            finally
            {
                _renderSemaphore.Release();
                GC.SuppressFinalize(this);
            }
        }
        #endregion
    }
}