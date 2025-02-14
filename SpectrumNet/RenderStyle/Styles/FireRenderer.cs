#nullable enable

namespace SpectrumNet
{
    public sealed class FireRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static readonly Lazy<FireRenderer> _instance = new(() => new FireRenderer());
        private bool _isInitialized;
        private float[] _previousSpectrum = Array.Empty<float>();
        private readonly Random _random = new();
        private float _time;
        private readonly SKPath _path = new();
        private readonly object _lock = new();
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
        }
        #endregion

        #region Constructors and Instance Management
        private FireRenderer()
        {
            // Private constructor for singleton pattern
        }

        public static FireRenderer GetInstance() => _instance.Value;
        #endregion

        #region Public Methods
        public void Initialize()
        {
            lock (_lock)
            {
                if (_isInitialized)
                    return;

                _isInitialized = true;
                _time = 0f;
            }
        }

        public void Configure(bool isOverlayActive)
        {
            // Configuration method kept for interface compatibility
        }

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
            if (!ValidateRenderParameters(canvas, spectrum, basePaint))
                return;

            using var renderScope = new RenderScope(this, canvas!, spectrum!, info, barWidth, barSpacing, barCount, basePaint!);
            renderScope.Execute(drawPerformanceInfo);
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
            }

            public void Execute(Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
            {
                _renderer._time += Config.TimeStep;

                EnsureSpectrumBuffer();

                var actualBarCount = Math.Min(_spectrum.Length / 2, _barCount);
                var scaledSpectrum = ScaleSpectrum();
                var totalBarWidth = CalculateTotalBarWidth(actualBarCount);

                RenderFlames(scaledSpectrum, actualBarCount, totalBarWidth);
                UpdatePreviousSpectrum();

                drawPerformanceInfo(_canvas, _info);
            }

            private void EnsureSpectrumBuffer()
            {
                if (_renderer._previousSpectrum.Length != _spectrum.Length)
                {
                    _renderer._previousSpectrum = new float[_spectrum.Length];
                    Array.Copy(_spectrum, _renderer._previousSpectrum, _spectrum.Length);
                }
            }

            private float[] ScaleSpectrum()
            {
                int spectrumLength = _spectrum.Length / 2;
                int targetCount = Math.Min(spectrumLength, _barCount);
                float[] scaledSpectrum = new float[targetCount];
                float blockSize = (float)spectrumLength / targetCount;
                float[] localSpectrum = _spectrum;

                Parallel.For(0, targetCount, i =>
                {
                    float sum = 0;
                    int start = (int)(i * blockSize);
                    int end = (int)((i + 1) * blockSize);

                    for (int j = start; j < end && j < spectrumLength; j++)
                        sum += localSpectrum[j];

                    scaledSpectrum[i] = sum / (end - start);
                });

                return scaledSpectrum;
            }

            private float CalculateTotalBarWidth(int actualBarCount)
            {
                float totalBarWidth = _barWidth + _barSpacing;
                if (actualBarCount < Config.MinBarCount)
                    totalBarWidth *= (float)Config.MinBarCount / actualBarCount;
                return totalBarWidth;
            }

            private void RenderFlames(float[] scaledSpectrum, int actualBarCount, float totalBarWidth)
            {
                for (int i = 0; i < actualBarCount; i++)
                {
                    var flameParams = CalculateFlameParameters(i, totalBarWidth, scaledSpectrum[i]);
                    RenderSingleFlame(flameParams);
                }
            }

            private FlameParameters CalculateFlameParameters(int index, float totalBarWidth, float spectrumValue)
            {
                float x = index * totalBarWidth;
                float waveOffset = (float)Math.Sin(_renderer._time * Config.WaveSpeed + index * Config.PositionPhaseShift)
                    * Config.WaveAmplitude;
                float currentHeight = spectrumValue * _info.Height * (1 + waveOffset);
                float previousHeight = _renderer._previousSpectrum[index] * _info.Height;

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
                _renderer._path.MoveTo(x, flameBottom);
                _renderer._path.LineTo(x + _barWidth, flameBottom);

                using var bottomPaint = _workingPaint.Clone();
                byte bottomAlpha = (byte)(255 * Config.MinBottomAlpha);
                bottomPaint.Color = bottomPaint.Color.WithAlpha(bottomAlpha);
                _canvas.DrawPath(_renderer._path, bottomPaint);
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

            private void UpdatePreviousSpectrum()
            {
                for (int i = 0; i < _spectrum.Length; i++)
                {
                    _renderer._previousSpectrum[i] = Math.Max(
                        _spectrum[i],
                        _renderer._previousSpectrum[i] - Config.DecayRate
                    );
                }
            }

            public void Dispose()
            {
                _workingPaint.Dispose();
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
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKPaint? basePaint)
        {
            return _isInitialized &&
                   canvas != null &&
                   spectrum != null &&
                   spectrum.Length > 0 &&
                   basePaint != null;
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            lock (_lock)
            {
                if (!_isInitialized)
                    return;

                _path.Dispose();
                _previousSpectrum = Array.Empty<float>();
                _isInitialized = false;
            }

            GC.SuppressFinalize(this);
        }
        #endregion
    }
}