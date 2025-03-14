#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Renderer that visualizes spectrum data as dots with various optimizations.
    /// </summary>
    public sealed class DotsRenderer : BaseSpectrumRenderer
    {
        #region Constants
        private static class Constants
        {
            // Logging
            public const string LOG_PREFIX = "DotsRenderer";

            // Rendering thresholds and limits
            public const float MIN_INTENSITY_THRESHOLD = 0.01f;  // Minimum intensity to render a dot
            public const float MIN_DOT_RADIUS = 2.0f;           // Minimum dot radius in pixels
            public const float MAX_DOT_MULTIPLIER = 0.5f;       // Maximum multiplier for dot size

            // Alpha and binning parameters
            public const float ALPHA_MULTIPLIER = 255.0f;       // Multiplier for alpha calculation
            public const int ALPHA_BINS = 16;                   // Number of alpha bins for batching

            // Dot size parameters
            public const float NORMAL_DOT_MULTIPLIER = 1.0f;    // Normal dot size multiplier
            public const float OVERLAY_DOT_MULTIPLIER = 1.5f;   // Overlay dot size multiplier

            // Performance settings
            public const int VECTOR_SIZE = 4;                   // Size for SIMD vectorization
            public const int MIN_BATCH_SIZE = 32;               // Minimum batch size for parallel processing
        }
        #endregion

        #region Fields
        private static readonly Lazy<DotsRenderer> _instance = new(() => new DotsRenderer());
        private readonly object _lockObject = new();

        // Cached objects
        private SKPaint? _dotPaint;
        private SKPicture? _cachedBackground;
        private Task? _backgroundCalculationTask;

        // Rendering state
        private float _dotRadiusMultiplier = Constants.NORMAL_DOT_MULTIPLIER;
        private bool _useHardwareAcceleration = true;
        private bool _useVectorization = true;
        private bool _batchProcessing = true;
        #endregion

        #region Structures
        private struct CircleData
        {
            public float X;
            public float Y;
            public float Radius;
            public float Intensity;
        }
        #endregion

        #region Constructor and Initialization
        private DotsRenderer() { }

        public static DotsRenderer GetInstance() => _instance.Value;

        public override void Initialize() => SmartLogger.Safe(() =>
        {
            base.Initialize();
            InitializePaints();
            SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });

        private void InitializePaints() => SmartLogger.Safe(() =>
        {
            _dotPaint?.Dispose();
            _dotPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                Style = SKPaintStyle.Fill
            };
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.InitializePaints",
            ErrorMessage = "Failed to initialize paints"
        });
        #endregion

        #region Configuration
        public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium) => SmartLogger.Safe(() =>
        {
            base.Configure(isOverlayActive, quality);

            _dotRadiusMultiplier = isOverlayActive ?
                Constants.OVERLAY_DOT_MULTIPLIER :
                Constants.NORMAL_DOT_MULTIPLIER;
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Configure",
            ErrorMessage = "Failed to configure renderer"
        });

        protected override void ApplyQualitySettings() => SmartLogger.Safe(() =>
        {
            base.ApplyQualitySettings();

            switch (_quality)
            {
                case RenderQuality.Low:
                    _useHardwareAcceleration = true;
                    _useVectorization = false;
                    _batchProcessing = false;
                    break;

                case RenderQuality.Medium:
                    _useHardwareAcceleration = true;
                    _useVectorization = true;
                    _batchProcessing = true;
                    break;

                case RenderQuality.High:
                    _useHardwareAcceleration = true;
                    _useVectorization = true;
                    _batchProcessing = true;
                    break;
            }

            if (_dotPaint != null)
            {
                _dotPaint.IsAntialias = _useAntiAlias;
                _dotPaint.FilterQuality = _filterQuality;
            }

            _cachedBackground?.Dispose();
            _cachedBackground = null;
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
        #endregion

        #region Rendering Methods
        public override void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!QuickValidate(canvas, spectrum, info, paint))
            {
                drawPerformanceInfo?.Invoke(canvas!, info);
                return;
            }

            SmartLogger.Safe(() =>
            {
                UpdatePaint(paint!);

                int spectrumLength = spectrum!.Length;
                int actualBarCount = Math.Min(spectrumLength, barCount);
                float canvasHeight = info.Height;
                float calculatedBarWidth = barWidth * Constants.MAX_DOT_MULTIPLIER * _dotRadiusMultiplier;
                float totalWidth = barWidth + barSpacing;

                float[] processedSpectrum = PrepareSpectrum(spectrum, actualBarCount, spectrumLength);

                List<CircleData> circles = CalculateCircleData(
                    processedSpectrum,
                    calculatedBarWidth,
                    totalWidth,
                    canvasHeight);

                List<List<CircleData>> circleBins = GroupCirclesByAlphaBin(circles);

                DrawCircles(canvas!, circleBins);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Render",
                ErrorMessage = "Error during rendering"
            });

            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        private void UpdatePaint(SKPaint basePaint) => SmartLogger.Safe(() =>
        {
            if (_dotPaint == null)
            {
                InitializePaints();
            }

            _dotPaint!.Color = basePaint.Color;
            _dotPaint.IsAntialias = _useAntiAlias;
            _dotPaint.FilterQuality = _filterQuality;
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdatePaint",
            ErrorMessage = "Failed to update paint"
        });
        #endregion

        #region Circle Calculation and Drawing
        private List<CircleData> CalculateCircleData(
            float[] smoothedSpectrum,
            float multiplier,
            float totalWidth,
            float canvasHeight)
        {
            var result = SmartLogger.Safe(() =>
            {
                var circles = new List<CircleData>(smoothedSpectrum.Length);
                if (_useVectorization && Vector.IsHardwareAccelerated && smoothedSpectrum.Length >= 4)
                {
                    return CalculateCircleDataOptimized(smoothedSpectrum, multiplier, totalWidth, canvasHeight);
                }
                for (int i = 0; i < smoothedSpectrum.Length; i++)
                {
                    float intensity = smoothedSpectrum[i];
                    if (intensity < Constants.MIN_INTENSITY_THRESHOLD) continue;
                    float dotRadius = Math.Max(multiplier * intensity, Constants.MIN_DOT_RADIUS);
                    float x = i * totalWidth + dotRadius;
                    float y = canvasHeight - (intensity * canvasHeight);
                    circles.Add(new CircleData
                    {
                        X = x,
                        Y = y,
                        Radius = dotRadius,
                        Intensity = intensity
                    });
                }
                return circles;
            }, new List<CircleData>(), new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.CalculateCircleData",
                ErrorMessage = "Error calculating circle data"
            });

            return result.Result ?? new List<CircleData>();
        }

        private List<CircleData> CalculateCircleDataOptimized(
           float[] smoothedSpectrum,
           float multiplier,
           float totalWidth,
           float canvasHeight)
        {
            var result = SmartLogger.Safe(() =>
            {
                var circles = new List<CircleData>(smoothedSpectrum.Length);
                const int chunkSize = 16;
                int chunks = smoothedSpectrum.Length / chunkSize;
                for (int chunk = 0; chunk < chunks; chunk++)
                {
                    int startIdx = chunk * chunkSize;
                    int endIdx = startIdx + chunkSize;
                    for (int i = startIdx; i < endIdx; i++)
                    {
                        float intensity = smoothedSpectrum[i];
                        if (intensity < Constants.MIN_INTENSITY_THRESHOLD) continue;
                        float dotRadius = Math.Max(multiplier * intensity, Constants.MIN_DOT_RADIUS);
                        float x = i * totalWidth + dotRadius;
                        float y = canvasHeight - (intensity * canvasHeight);
                        circles.Add(new CircleData
                        {
                            X = x,
                            Y = y,
                            Radius = dotRadius,
                            Intensity = intensity
                        });
                    }
                }
                for (int i = chunks * chunkSize; i < smoothedSpectrum.Length; i++)
                {
                    float intensity = smoothedSpectrum[i];
                    if (intensity < Constants.MIN_INTENSITY_THRESHOLD) continue;
                    float dotRadius = Math.Max(multiplier * intensity, Constants.MIN_DOT_RADIUS);
                    float x = i * totalWidth + dotRadius;
                    float y = canvasHeight - (intensity * canvasHeight);
                    circles.Add(new CircleData
                    {
                        X = x,
                        Y = y,
                        Radius = dotRadius,
                        Intensity = intensity
                    });
                }
                return circles;
            }, new List<CircleData>(), new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.CalculateCircleDataOptimized",
                ErrorMessage = "Error calculating optimized circle data"
            });

            return result.Result ?? new List<CircleData>();
        }

        private List<List<CircleData>> GroupCirclesByAlphaBin(List<CircleData> circles)
        {
            var result = SmartLogger.Safe(() =>
            {
                List<List<CircleData>> circleBins = new List<List<CircleData>>(Constants.ALPHA_BINS);
                for (int i = 0; i < Constants.ALPHA_BINS; i++)
                {
                    circleBins.Add(new List<CircleData>(circles.Count / Constants.ALPHA_BINS + 1));
                }
                float binStep = 255f / (Constants.ALPHA_BINS - 1);
                foreach (var circle in circles)
                {
                    byte alpha = (byte)Math.Min(circle.Intensity * Constants.ALPHA_MULTIPLIER, 255);
                    int binIndex = Math.Min((int)(alpha / binStep), Constants.ALPHA_BINS - 1);
                    circleBins[binIndex].Add(circle);
                }
                return circleBins;
            }, new List<List<CircleData>>(), new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.GroupCirclesByAlphaBin",
                ErrorMessage = "Error grouping circles by alpha bin"
            });

            return result.Result ?? new List<List<CircleData>>();
        }

        private void DrawCircles(SKCanvas canvas, List<List<CircleData>> circleBins) => SmartLogger.Safe(() =>
        {
            if (_dotPaint == null) return;

            SKRect canvasBounds = new SKRect(0, 0, canvas.DeviceClipBounds.Width, canvas.DeviceClipBounds.Height);

            float binStep = 255f / (Constants.ALPHA_BINS - 1);

            for (int binIndex = 0; binIndex < Constants.ALPHA_BINS; binIndex++)
            {
                var bin = circleBins[binIndex];
                if (bin.Count == 0)
                    continue;

                byte binAlpha = (byte)(binIndex * binStep);
                _dotPaint.Color = _dotPaint.Color.WithAlpha(binAlpha);

                if (bin.Count <= 5 || !_batchProcessing)
                {
                    foreach (var circle in bin)
                    {
                        // Skip circles outside the canvas
                        SKRect circleBounds = new SKRect(
                            circle.X - circle.Radius,
                            circle.Y - circle.Radius,
                            circle.X + circle.Radius,
                            circle.Y + circle.Radius);

                        if (!canvas.QuickReject(circleBounds))
                        {
                            canvas.DrawCircle(circle.X, circle.Y, circle.Radius, _dotPaint);
                        }
                    }
                }
                else
                {
                    using var path = new SKPath();

                    foreach (var circle in bin)
                    {
                        SKRect circleBounds = new SKRect(
                            circle.X - circle.Radius,
                            circle.Y - circle.Radius,
                            circle.X + circle.Radius,
                            circle.Y + circle.Radius);

                        if (!canvas.QuickReject(circleBounds))
                        {
                            path.AddCircle(circle.X, circle.Y, circle.Radius);
                        }
                    }

                    canvas.DrawPath(path, _dotPaint);
                }
            }
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.DrawCircles",
            ErrorMessage = "Error drawing circles"
        });
        #endregion

        #region Disposal
        public override void Dispose()
        {
            if (!_disposed)
            {
                SmartLogger.Safe(() =>
                {
                    _dotPaint?.Dispose();
                    _dotPaint = null;

                    _cachedBackground?.Dispose();
                    _cachedBackground = null;

                    _backgroundCalculationTask?.Wait(100);

                    base.Dispose();
                }, new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{Constants.LOG_PREFIX}.Dispose",
                    ErrorMessage = "Error during disposal"
                });
            }
        }
        #endregion
    }
}