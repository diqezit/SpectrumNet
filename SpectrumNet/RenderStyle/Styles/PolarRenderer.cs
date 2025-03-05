#nullable enable

namespace SpectrumNet
{
    public sealed class PolarRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        static PolarRenderer? _instance;
        bool _isInitialized, _isOverlayActive, _pathsNeedUpdate;
        volatile bool _disposed;
        readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        float[]? _processedSpectrum, _previousSpectrum, _tempSpectrum;
        SKPath? _outerPath, _innerPath;
        SKPaint? _fillPaint, _strokePaint, _centerPaint, _glowPaint, _highlightPaint;
        SKShader? _gradientShader;
        SKPathEffect? _dashEffect;
        SKImageFilter? _glowFilter;
        SKPicture? _cachedCenterCircle;
        float _rotation, _time, _pulseEffect;
        int _currentPointCount, _visualQuality = 1;
        Vector<float> _smoothingVec, _oneMinusSmoothing;
        SKPoint[]? _outerPoints, _innerPoints;
        SKColor _lastBaseColor;
        SKRect _centerCircleBounds;
        #endregion

        #region Constants
        const int MAX_POINT_COUNT = 120;         // Maximum number of points for rendering
        const int POINT_COUNT_OVERLAY = 80;      // Number of points when overlay is active
        const int MIN_POINT_COUNT = 24;          // Minimum number of points to ensure smooth rendering
        const float MIN_BAR_WIDTH = 0.5f;        // Minimum bar width to ensure visibility
        const float MAX_BAR_WIDTH = 4.0f;        // Maximum bar width to prevent excessive GPU load
        const float SMOOTHING_FACTOR = 0.15f;    // Smoothing factor for spectrum
        const float MIN_RADIUS = 30f;            // Minimum radius for rendering
        const float RADIUS_MULTIPLIER = 200f;    // Radius multiplier for spectrum
        const float INNER_RADIUS_RATIO = 0.5f;   // Ratio for inner path radius
        const float ROTATION_SPEED = 0.3f;       // Rotation speed of the graph
        const float TIME_STEP = 0.016f;          // Time step for animation
        const float DEFAULT_STROKE_WIDTH = 1.5f; // Base line width for stroke
        const float CENTER_CIRCLE_SIZE = 6f;     // Size of the center point
        const byte  FILL_ALPHA = 120;            // Fill transparency
        const float MODULATION_FACTOR = 0.3f;    // Modulation amplitude for radius
        const float MODULATION_FREQ = 5f;        // Modulation frequency in radians
        const float TIME_MODIFIER = 0.01f;       // Time modifier for rotation
        const float SPECTRUM_SCALE = 2.0f;       // Spectrum scaling multiplier
        const float MAX_SPECTRUM_VALUE = 1.0f;   // Maximum spectrum value
        const float CHANGE_THRESHOLD = 0.01f;    // Change threshold for path updates
        const float DEG_TO_RAD = (float)(Math.PI / 180.0); // Degrees to radians conversion
        const float PULSE_SPEED = 2.0f;          // Speed of pulsation effect
        const float PULSE_AMPLITUDE = 0.2f;      // Amplitude of pulsation effect
        const float DASH_LENGTH = 6.0f;          // Length of dashes for path effects
        const float DASH_PHASE_SPEED = 0.5f;     // Speed of dash animation
        const float HIGHLIGHT_FACTOR = 1.4f;     // Color multiplier for highlights
        const float GLOW_RADIUS = 8.0f;          // Radius of glow effect
        const float GLOW_SIGMA = 2.5f;           // Sigma for glow blur effect
        const byte GLOW_ALPHA = 80;              // Alpha for glow effect
        const byte HIGHLIGHT_ALPHA = 160;        // Alpha for highlight effect
        #endregion

        #region Constructor and Initialization
        PolarRenderer() { }

        public static PolarRenderer GetInstance() => _instance ??= new PolarRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _outerPath = new SKPath();
            _innerPath = new SKPath();
            _outerPoints = new SKPoint[MAX_POINT_COUNT + 1];
            _innerPoints = new SKPoint[MAX_POINT_COUNT + 1];

            _fillPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                FilterQuality = SKFilterQuality.Medium
            };

            _strokePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = DEFAULT_STROKE_WIDTH,
                StrokeJoin = SKStrokeJoin.Round,
                StrokeCap = SKStrokeCap.Round,
                FilterQuality = SKFilterQuality.Medium
            };

            _centerPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                FilterQuality = SKFilterQuality.Medium
            };

            _glowPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = DEFAULT_STROKE_WIDTH * 1.5f,
                FilterQuality = SKFilterQuality.Medium,
                BlendMode = SKBlendMode.SrcOver
            };

            _highlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = DEFAULT_STROKE_WIDTH * 0.5f,
                StrokeCap = SKStrokeCap.Round,
                FilterQuality = SKFilterQuality.Medium
            };

            _processedSpectrum = new float[MAX_POINT_COUNT];
            _previousSpectrum = new float[MAX_POINT_COUNT];
            _tempSpectrum = new float[MAX_POINT_COUNT];

            _smoothingVec = new Vector<float>(SMOOTHING_FACTOR);
            _oneMinusSmoothing = new Vector<float>(1 - SMOOTHING_FACTOR);

            _currentPointCount = MAX_POINT_COUNT;
            _pathsNeedUpdate = true;

            _centerCircleBounds = new SKRect(-CENTER_CIRCLE_SIZE * 1.5f, -CENTER_CIRCLE_SIZE * 1.5f,
                                          CENTER_CIRCLE_SIZE * 1.5f, CENTER_CIRCLE_SIZE * 1.5f);

            UpdateCenterCircle(SKColors.White);

            _isInitialized = true;
            Log.Debug("PolarRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            bool wasOverlayActive = _isOverlayActive;
            _isOverlayActive = isOverlayActive;

            if (wasOverlayActive != isOverlayActive)
            {
                _currentPointCount = isOverlayActive ? POINT_COUNT_OVERLAY : MAX_POINT_COUNT;
                _pathsNeedUpdate = true;
            }
        }

        private void UpdateCenterCircle(SKColor baseColor)
        {
            if (_centerPaint == null) return;

            _cachedCenterCircle?.Dispose();

            _glowFilter = SKImageFilter.CreateBlur(GLOW_RADIUS, GLOW_SIGMA);

            using (SKPictureRecorder recorder = new SKPictureRecorder())
            {
                SKCanvas pictureCanvas = recorder.BeginRecording(_centerCircleBounds);

                using (SKPaint glowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = baseColor.WithAlpha(GLOW_ALPHA),
                    ImageFilter = _glowFilter
                })
                {
                    pictureCanvas.DrawCircle(0, 0, CENTER_CIRCLE_SIZE * 0.8f, glowPaint);
                }

                pictureCanvas.DrawCircle(0, 0, CENTER_CIRCLE_SIZE * 0.7f, _centerPaint);

                using (SKPaint highlightPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = SKColors.White.WithAlpha(HIGHLIGHT_ALPHA)
                })
                {
                    pictureCanvas.DrawCircle(-CENTER_CIRCLE_SIZE * 0.25f, -CENTER_CIRCLE_SIZE * 0.25f,
                                           CENTER_CIRCLE_SIZE * 0.2f, highlightPaint);
                }

                _cachedCenterCircle = recorder.EndRecording();
            }
        }

        private void UpdateVisualEffects(SKColor baseColor)
        {
            if (_fillPaint == null || _strokePaint == null ||
                _glowPaint == null || _highlightPaint == null)
                return;

            if (baseColor.Red == _lastBaseColor.Red &&
                baseColor.Green == _lastBaseColor.Green &&
                baseColor.Blue == _lastBaseColor.Blue)
                return;

            _lastBaseColor = baseColor;

            SKColor gradientStart = baseColor.WithAlpha(FILL_ALPHA);
            SKColor gradientEnd = new SKColor(
                (byte)Math.Min(255, baseColor.Red * 0.7),
                (byte)Math.Min(255, baseColor.Green * 0.7),
                (byte)Math.Min(255, baseColor.Blue * 0.7),
                20);

            _gradientShader?.Dispose();
            _gradientShader = SKShader.CreateRadialGradient(
                new SKPoint(0, 0),
                MIN_RADIUS + MAX_SPECTRUM_VALUE * RADIUS_MULTIPLIER,
                new[] { gradientStart, gradientEnd },
                SKShaderTileMode.Clamp);

            _fillPaint.Shader = _gradientShader;
            _strokePaint.Color = baseColor;
            _glowFilter?.Dispose();
            _glowFilter = SKImageFilter.CreateBlur(GLOW_RADIUS, GLOW_SIGMA);
            _glowPaint.Color = baseColor.WithAlpha(GLOW_ALPHA);
            _glowPaint.ImageFilter = _glowFilter;
            _highlightPaint.Color = new SKColor(
                (byte)Math.Min(255, baseColor.Red * HIGHLIGHT_FACTOR),
                (byte)Math.Min(255, baseColor.Green * HIGHLIGHT_FACTOR),
                (byte)Math.Min(255, baseColor.Blue * HIGHLIGHT_FACTOR),
                HIGHLIGHT_ALPHA);

            float[] intervals = { DASH_LENGTH, DASH_LENGTH * 2 };
            _dashEffect?.Dispose();
            _dashEffect = SKPathEffect.CreateDash(intervals, (_time * DASH_PHASE_SPEED) % (DASH_LENGTH * 3));
            _centerPaint!.Color = baseColor;
            UpdateCenterCircle(baseColor);
        }
        #endregion

        #region Rendering
        public void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!ValidateRenderParams(canvas, spectrum, info, paint, drawPerformanceInfo))
                return;

            try
            {
                int safeBarCount = Math.Min(Math.Max(barCount, MIN_POINT_COUNT), MAX_POINT_COUNT);
                float safeBarWidth = Math.Clamp(barWidth, MIN_BAR_WIDTH, MAX_BAR_WIDTH);

                Task.Run(() => ProcessSpectrum(spectrum!, safeBarCount));

                if (_pathsNeedUpdate)
                {
                    UpdatePolarPaths(info, safeBarCount);
                    _pathsNeedUpdate = false;
                }

                // Update visual effects based on the base paint color
                UpdateVisualEffects(paint!.Color);

                RenderPolarGraph(canvas!, info, paint!, safeBarWidth);
                drawPerformanceInfo?.Invoke(canvas!, info);
            }
            catch (Exception ex)
            {
                Log.Error($"Error in PolarRenderer.Render: {ex.Message}");
            }
        }

        void RenderPolarGraph(SKCanvas canvas, SKImageInfo info, SKPaint basePaint, float barWidth)
        {
            if (_outerPath == null || _innerPath == null ||
                _fillPaint == null || _strokePaint == null ||
                _centerPaint == null || _cachedCenterCircle == null ||
                _glowPaint == null || _highlightPaint == null)
                return;

            // Update pulse effect animation
            _pulseEffect = (float)Math.Sin(_time * PULSE_SPEED) * PULSE_AMPLITUDE + 1.0f;

            // Adjust stroke widths based on pulse and bar width
            _strokePaint.StrokeWidth = barWidth * _pulseEffect;
            _glowPaint.StrokeWidth = barWidth * 1.5f * _pulseEffect;
            _highlightPaint.StrokeWidth = barWidth * 0.5f * _pulseEffect;

            // Use animated dash effect for inner path in high quality mode
            if (_visualQuality > 0)
            {
                _strokePaint.PathEffect = _dashEffect;
            }

            canvas.Save();
            canvas.Translate(info.Width / 2f, info.Height / 2f);
            canvas.RotateDegrees(_rotation);

            float maxRadius = MIN_RADIUS + MAX_SPECTRUM_VALUE * RADIUS_MULTIPLIER * (1 + MODULATION_FACTOR);
            SKRect clipBounds = new SKRect(-maxRadius, -maxRadius, maxRadius, maxRadius);

            if (!canvas.QuickReject(clipBounds))
            {

                if (_visualQuality > 0)
                {
                    canvas.DrawPath(_outerPath, _glowPaint);
                }

                canvas.DrawPath(_outerPath, _fillPaint);
                canvas.DrawPath(_outerPath, _strokePaint);
                _strokePaint.PathEffect = _dashEffect;
                canvas.DrawPath(_innerPath, _strokePaint);
                _strokePaint.PathEffect = null;

                if (_visualQuality > 0)
                {
                    canvas.DrawPath(_innerPath, _highlightPaint);
                }

                float pulseScale = 1.0f + (float)Math.Sin(_time * PULSE_SPEED * 0.5f) * 0.1f;
                canvas.Save();
                canvas.Scale(pulseScale, pulseScale);
                canvas.DrawPicture(_cachedCenterCircle);
                canvas.Restore();
            }

            canvas.Restore();
        }
        #endregion

        #region Path Generation
        void UpdatePolarPaths(SKImageInfo info, int barCount)
        {
            if (_processedSpectrum == null || _outerPath == null || _innerPath == null ||
                _outerPoints == null || _innerPoints == null)
                return;

            _time += TIME_STEP;
            _rotation += ROTATION_SPEED * _time * TIME_MODIFIER;

            int effectivePointCount = Math.Min(barCount, _currentPointCount);
            float angleStep = 360f / effectivePointCount;

            for (int i = 0; i <= effectivePointCount; i++)
            {
                float angle = i * angleStep * DEG_TO_RAD;
                float cosAngle = (float)Math.Cos(angle);
                float sinAngle = (float)Math.Sin(angle);

                int index = i % effectivePointCount;
                float spectrumValue = index < _processedSpectrum.Length
                    ? _processedSpectrum[index]
                    : 0f;

                float timeOffset = _time * 0.5f + i * 0.1f;
                float modulation = 1 + MODULATION_FACTOR * (float)Math.Sin(i * angleStep * MODULATION_FREQ * DEG_TO_RAD + _time * 2);
                modulation += PULSE_AMPLITUDE * 0.5f * (float)Math.Sin(timeOffset);

                float outerRadius = MIN_RADIUS + spectrumValue * modulation * RADIUS_MULTIPLIER;
                _outerPoints[i] = new SKPoint(
                    outerRadius * cosAngle,
                    outerRadius * sinAngle
                );

                float innerSpectrumValue = spectrumValue * INNER_RADIUS_RATIO;
                float innerModulation = 1 + MODULATION_FACTOR * (float)Math.Sin(i * angleStep * MODULATION_FREQ * DEG_TO_RAD + _time * 2 + Math.PI);
                innerModulation += PULSE_AMPLITUDE * 0.5f * (float)Math.Sin(timeOffset + Math.PI);

                float innerRadius = MIN_RADIUS + innerSpectrumValue * innerModulation * RADIUS_MULTIPLIER;
                _innerPoints[i] = new SKPoint(
                    innerRadius * cosAngle,
                    innerRadius * sinAngle
                );
            }

            _outerPath.Reset();
            _innerPath.Reset();

            try
            {
                int pointsToUse = Math.Min(effectivePointCount + 1, _outerPoints.Length);

                _outerPath.AddPoly(_outerPoints.Take(pointsToUse).ToArray(), true);
                _innerPath.AddPoly(_innerPoints.Take(pointsToUse).ToArray(), true);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to create path: {ex.Message}");

                _outerPath.Reset();
                _innerPath.Reset();

                for (int i = 0; i <= effectivePointCount; i++)
                {
                    int safeIndex = Math.Min(i, _outerPoints.Length - 1);

                    if (i == 0)
                    {
                        _outerPath.MoveTo(_outerPoints[safeIndex]);
                        _innerPath.MoveTo(_innerPoints[safeIndex]);
                    }
                    else
                    {
                        _outerPath.LineTo(_outerPoints[safeIndex]);
                        _innerPath.LineTo(_innerPoints[safeIndex]);
                    }
                }

                _outerPath.Close();
                _innerPath.Close();
            }
        }
        #endregion

        #region Spectrum Processing
        void ProcessSpectrum(float[] spectrum, int barCount)
        {
            if (_disposed || _tempSpectrum == null || _previousSpectrum == null ||
                _processedSpectrum == null)
                return;

            try
            {
                _spectrumSemaphore.Wait();

                int pointCount = Math.Min(barCount, _currentPointCount);

                for (int i = 0; i < pointCount && i < _tempSpectrum.Length; i++)
                {
                    float spectrumIndex = i * spectrum.Length / (2f * pointCount);
                    int baseIndex = (int)spectrumIndex;
                    float fraction = spectrumIndex - baseIndex;

                    if (baseIndex >= spectrum.Length / 2 - 1)
                    {
                        _tempSpectrum[i] = spectrum[Math.Min(spectrum.Length / 2 - 1, spectrum.Length - 1)];
                    }
                    else if (baseIndex + 1 < spectrum.Length)
                    {
                        _tempSpectrum[i] = spectrum[baseIndex] * (1 - fraction) + spectrum[baseIndex + 1] * fraction;
                    }
                    else
                    {
                        _tempSpectrum[i] = spectrum[baseIndex];
                    }

                    _tempSpectrum[i] = Math.Min(_tempSpectrum[i] * SPECTRUM_SCALE, MAX_SPECTRUM_VALUE);
                }

                float maxChange = 0f;
                for (int i = 0; i < pointCount && i < _tempSpectrum.Length; i += Vector<float>.Count)
                {
                    int remaining = Math.Min(Vector<float>.Count, pointCount - i);
                    remaining = Math.Min(remaining, _tempSpectrum.Length - i);

                    if (remaining < Vector<float>.Count)
                    {
                        for (int j = 0; j < remaining; j++)
                        {
                            float newValue = _previousSpectrum[i + j] * (1 - SMOOTHING_FACTOR) +
                                           _tempSpectrum[i + j] * SMOOTHING_FACTOR;
                            float change = Math.Abs(newValue - _previousSpectrum[i + j]);
                            maxChange = Math.Max(maxChange, change);
                            _processedSpectrum[i + j] = newValue;
                            _previousSpectrum[i + j] = newValue;
                        }
                    }
                    else
                    {
                        Vector<float> current = new Vector<float>(_tempSpectrum, i);
                        Vector<float> previous = new Vector<float>(_previousSpectrum, i);
                        Vector<float> smoothed = previous * _oneMinusSmoothing + current * _smoothingVec;

                        Vector<float> change = Vector.Abs(smoothed - previous);
                        float batchMaxChange = 0f;
                        for (int j = 0; j < Vector<float>.Count; j++)
                        {
                            if (change[j] > batchMaxChange)
                                batchMaxChange = change[j];
                        }
                        maxChange = Math.Max(maxChange, batchMaxChange);

                        smoothed.CopyTo(_processedSpectrum, i);
                        smoothed.CopyTo(_previousSpectrum, i);
                    }
                }

                if (maxChange > CHANGE_THRESHOLD)
                    _pathsNeedUpdate = true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing spectrum: {ex.Message}");
            }
            finally
            {
                _spectrumSemaphore.Release();
            }
        }
        #endregion

        #region Helper Methods
        bool ValidateRenderParams(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (_disposed)
                return false;

            if (!_isInitialized)
            {
                Log.Error("PolarRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null ||
                spectrum == null || spectrum.Length < 2 ||
                paint == null ||
                drawPerformanceInfo == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                Log.Error("Invalid render parameters for PolarRenderer");
                return false;
            }

            return true;
        }
        #endregion

        #region Disposal
        public void Dispose()
        {
            if (_disposed) return;

            _spectrumSemaphore.Dispose();
            _outerPath?.Dispose();
            _innerPath?.Dispose();
            _fillPaint?.Dispose();
            _strokePaint?.Dispose();
            _centerPaint?.Dispose();
            _cachedCenterCircle?.Dispose();
            _glowPaint?.Dispose();
            _highlightPaint?.Dispose();
            _gradientShader?.Dispose();
            _dashEffect?.Dispose();
            _glowFilter?.Dispose();

            _outerPath = null;
            _innerPath = null;
            _fillPaint = null;
            _strokePaint = null;
            _centerPaint = null;
            _cachedCenterCircle = null;
            _processedSpectrum = null;
            _previousSpectrum = null;
            _tempSpectrum = null;
            _outerPoints = null;
            _innerPoints = null;
            _glowPaint = null;
            _highlightPaint = null;
            _gradientShader = null;
            _dashEffect = null;
            _glowFilter = null;

            _disposed = true;
            _isInitialized = false;
            Log.Debug("PolarRenderer disposed");
        }
        #endregion
    }
}