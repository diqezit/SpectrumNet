#nullable enable

namespace SpectrumNet
{
    public class LedMeterRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants
        private static class LedMeterConstants
        {
            // --- Quality Settings ---
            public const RenderQuality DEFAULT_QUALITY = RenderQuality.Medium;   // Default render quality

            // --- Animation & Smoothing ---
            public const float ANIMATION_SPEED = 0.015f;       // Animation speed for LED animation
            public const float SMOOTHING_FACTOR_NORMAL = 0.3f;   // Smoothing factor in normal mode
            public const float SMOOTHING_FACTOR_OVERLAY = 0.5f;  // Smoothing factor in overlay mode
            public const float PEAK_DECAY_RATE = 0.04f;          // Decay rate for LED peak
            public const float GLOW_INTENSITY = 0.3f;            // Base glow intensity for active LED

            // --- Loudness Thresholds & LED Count ---
            public const float MIN_LOUDNESS_THRESHOLD = 0.001f;  // Minimal loudness to render
            public const float HIGH_LOUDNESS_THRESHOLD = 0.7f;   // Threshold for high loudness (for vibration effect)
            public const float MEDIUM_LOUDNESS_THRESHOLD = 0.4f; // Threshold for medium loudness (for color selection)
            public const int DEFAULT_LED_COUNT = 22;             // Default number of LEDs
            public const float LED_SPACING = 0.1f;               // Spacing between LEDs

            // --- Panel & Geometry ---
            public const float LED_ROUNDING_RADIUS = 2.5f;       // Corner radius for LED rounding
            public const float PANEL_PADDING = 12f;              // Padding inside the panel
            public const float TICK_MARK_WIDTH = 22f;            // Width allocated for tick marks
            public const float BEVEL_SIZE = 3f;                  // Size of panel bevel effect
            public const float CORNER_RADIUS = 14f;              // Overall panel corner radius
            public const int PERFORMANCE_INFO_BOTTOM_MARGIN = 30;// Margin for performance info drawing

            // --- Screw & Texture Dimensions ---
            public const int SCREW_TEXTURE_SIZE = 24;            // Size of the screw texture bitmap
            public const int BRUSHED_METAL_TEXTURE_SIZE = 100;     // Size of the brushed metal texture bitmap

            // --- Logging ---
            public const string LOG_PREFIX = "[LedMeterRenderer] "; // Logging prefix
        }
        #endregion

        #region Fields
        // Singleton instance
        private static LedMeterRenderer? _instance;

        // Quality settings fields
        private RenderQuality _quality = LedMeterConstants.DEFAULT_QUALITY;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;

        // Initialization & disposal
        private bool _isInitialized;
        private bool _disposed = false;

        // Synchronization
        private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
        private readonly object _loudnessLock = new();

        // Cached SKPath objects reused in rendering
        private readonly SKPath _ledPath = new();
        private readonly SKPath _highlightPath = new();

        // Pre-calculated screw angles (static positions)
        private readonly float[] _screwAngles = { 45f, 120f, 10f, 80f };

        // Animation and vibration
        private float _animationPhase = 0f;
        private float _vibrationOffset = 0f;

        // Cached bitmaps & paints
        private SKBitmap? _screwBitmap;
        private SKBitmap? _brushedMetalBitmap;
        private SKPaint? _cachedLedPaint;
        private SKPaint? _cachedGlowPaint;
        private SKPaint? _cachedHighlightPaint;
        private SKPaint? _cachedLedBasePaint;
        private SKPaint? _cachedInactiveLedPaint;
        private SKPaint? _panelPaint;
        private SKPaint? _outerCasePaint;

        // LED variations & color variations (precomputed)
        private readonly List<float> _ledVariations = new(30);
        private readonly List<SKColor> _ledColorVariations = new(30);

        // LED thresholds and counts
        private float _smoothingFactor = LedMeterConstants.SMOOTHING_FACTOR_NORMAL;
        private float _previousLoudness = 0f;
        private float _peakLoudness = 0f;
        private float? _cachedLoudness;
        private float[] _ledAnimationPhases = Array.Empty<float>();
        #endregion

        #region Quality Settings
        public RenderQuality Quality
        {
            get => _quality;
            set
            {
                if (_quality != value)
                {
                    _quality = value;
                    ApplyQualitySettings();
                }
            }
        }

        private void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _filterQuality = SKFilterQuality.Low;
                    _useAdvancedEffects = false;
                    break;
                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.Medium;
                    _useAdvancedEffects = true;
                    break;
                case RenderQuality.High:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.High;
                    _useAdvancedEffects = true;
                    break;
            }
            // Обновление существующих объектов Paint, если требуется
            if (_cachedLedPaint != null)
            {
                _cachedLedPaint.IsAntialias = _useAntiAlias;
                _cachedLedPaint.FilterQuality = _filterQuality;
            }
        }

        // Реализация метода Configure с учетом настроек качества
        public void Configure(bool isOverlayActive, RenderQuality quality = LedMeterConstants.DEFAULT_QUALITY)
        {
            Quality = quality;
            _smoothingFactor = isOverlayActive ? LedMeterConstants.SMOOTHING_FACTOR_OVERLAY : LedMeterConstants.SMOOTHING_FACTOR_NORMAL;
        }
        #endregion

        #region Initialization
        private LedMeterRenderer() { }

        public static LedMeterRenderer GetInstance() => _instance ??= new LedMeterRenderer();

        public void Initialize()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LedMeterRenderer));

            if (_isInitialized) return;

            InitializeVariationsAndTextures();
            CreateCachedResources();

            _isInitialized = true;
            _previousLoudness = 0f;
            _peakLoudness = 0f;
            SmartLogger.Log(LogLevel.Debug, LedMeterConstants.LOG_PREFIX, "LedMeterRenderer initialized");
        }

        private void InitializeVariationsAndTextures()
        {
            // Precompute LED brightness variations and color variations with fixed seed for determinism
            Random fixedRandom = new(42);

            for (int i = 0; i < 30; i++)
                _ledVariations.Add(0.85f + (float)fixedRandom.NextDouble() * 0.3f);

            // Base colors for groups
            SKColor greenBase = new SKColor(30, 200, 30);
            SKColor yellowBase = new SKColor(220, 200, 0);
            SKColor redBase = new SKColor(230, 30, 30);

            for (int j = 0; j < 10; j++)
            {
                _ledColorVariations.Add(new SKColor(
                    (byte)Math.Clamp(greenBase.Red + fixedRandom.Next(-10, 10), 0, 255),
                    (byte)Math.Clamp(greenBase.Green + fixedRandom.Next(-10, 10), 0, 255),
                    (byte)Math.Clamp(greenBase.Blue + fixedRandom.Next(-10, 10), 0, 255)
                ));
            }
            for (int j = 0; j < 10; j++)
            {
                _ledColorVariations.Add(new SKColor(
                    (byte)Math.Clamp(yellowBase.Red + fixedRandom.Next(-10, 10), 0, 255),
                    (byte)Math.Clamp(yellowBase.Green + fixedRandom.Next(-10, 10), 0, 255),
                    (byte)Math.Clamp(yellowBase.Blue + fixedRandom.Next(-10, 10), 0, 255)
                ));
            }
            for (int j = 0; j < 10; j++)
            {
                _ledColorVariations.Add(new SKColor(
                    (byte)Math.Clamp(redBase.Red + fixedRandom.Next(-10, 10), 0, 255),
                    (byte)Math.Clamp(redBase.Green + fixedRandom.Next(-10, 10), 0, 255),
                    (byte)Math.Clamp(redBase.Blue + fixedRandom.Next(-10, 10), 0, 255)
                ));
            }
        }

        private void CreateCachedResources()
        {
            _screwBitmap = CreateScrewTexture();
            _brushedMetalBitmap = CreateBrushedMetalTexture();

            _cachedLedPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = _useAntiAlias
            };

            _cachedGlowPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = _useAntiAlias,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2)
            };

            _cachedHighlightPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.7f,
                IsAntialias = _useAntiAlias
            };

            _cachedLedBasePaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = new SKColor(8, 8, 8),
                IsAntialias = _useAntiAlias
            };

            _cachedInactiveLedPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = _useAntiAlias
            };

            if (_brushedMetalBitmap != null)
            {
                _panelPaint = new SKPaint
                {
                    Shader = SKShader.CreateBitmap(
                        _brushedMetalBitmap,
                        SKShaderTileMode.Repeat,
                        SKShaderTileMode.Repeat,
                        SKMatrix.CreateScale(1.5f, 1.5f)
                    ),
                    IsAntialias = _useAntiAlias
                };
            }

            _outerCasePaint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(0, 200),
                    new[] { new SKColor(70, 70, 70), new SKColor(40, 40, 40), new SKColor(55, 55, 55) },
                    new[] { 0.0f, 0.7f, 1.0f },
                    SKShaderTileMode.Clamp
                ),
                IsAntialias = _useAntiAlias
            };
        }

        private SKBitmap CreateScrewTexture()
        {
            var bitmap = new SKBitmap(LedMeterConstants.SCREW_TEXTURE_SIZE, LedMeterConstants.SCREW_TEXTURE_SIZE);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);

            using var circlePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(4, 4),
                    new SKPoint(20, 20),
                    new SKColor[] { new SKColor(220, 220, 220), new SKColor(140, 140, 140) },
                    null,
                    SKShaderTileMode.Clamp
                )
            };
            canvas.DrawCircle(12, 12, 10, circlePaint);

            using var slotPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2.5f,
                Color = new SKColor(50, 50, 50, 180)
            };
            canvas.DrawLine(7, 12, 17, 12, slotPaint);

            using var highlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                Color = new SKColor(255, 255, 255, 100)
            };
            canvas.DrawArc(new SKRect(4, 4, 20, 20), 200, 160, false, highlightPaint);

            using var shadowPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                Color = new SKColor(0, 0, 0, 100)
            };
            canvas.DrawCircle(12, 12, 9, shadowPaint);

            return bitmap;
        }

        private SKBitmap CreateBrushedMetalTexture()
        {
            var bitmap = new SKBitmap(LedMeterConstants.BRUSHED_METAL_TEXTURE_SIZE, LedMeterConstants.BRUSHED_METAL_TEXTURE_SIZE);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(190, 190, 190));

            Random texRandom = new(42);
            using var linePaint = new SKPaint
            {
                IsAntialias = false,
                StrokeWidth = 1
            };

            for (int i = 0; i < 150; i++)
            {
                float y = (float)texRandom.NextDouble() * LedMeterConstants.BRUSHED_METAL_TEXTURE_SIZE;
                linePaint.Color = new SKColor(210, 210, 210, (byte)texRandom.Next(10, 20));
                canvas.DrawLine(0, y, LedMeterConstants.BRUSHED_METAL_TEXTURE_SIZE, y, linePaint);
            }
            for (int i = 0; i < 30; i++)
            {
                float y = (float)texRandom.NextDouble() * LedMeterConstants.BRUSHED_METAL_TEXTURE_SIZE;
                linePaint.Color = new SKColor(100, 100, 100, (byte)texRandom.Next(5, 10));
                canvas.DrawLine(0, y, LedMeterConstants.BRUSHED_METAL_TEXTURE_SIZE, y, linePaint);
            }
            using var gradientPaint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(LedMeterConstants.BRUSHED_METAL_TEXTURE_SIZE, LedMeterConstants.BRUSHED_METAL_TEXTURE_SIZE),
                    new[] { new SKColor(255, 255, 255, 20), new SKColor(0, 0, 0, 20) },
                    null,
                    SKShaderTileMode.Clamp
                )
            };
            canvas.DrawRect(0, 0, LedMeterConstants.BRUSHED_METAL_TEXTURE_SIZE, LedMeterConstants.BRUSHED_METAL_TEXTURE_SIZE, gradientPaint);

            return bitmap;
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
            Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            try
            {
                if (!ValidateRenderParameters(canvas, spectrum, info, paint))
                    return;

                float loudness = 0f;
                bool semaphoreAcquired = false;

                try
                {
                    semaphoreAcquired = _renderSemaphore.Wait(0);

                    if (semaphoreAcquired)
                    {
                        _animationPhase = (_animationPhase + LedMeterConstants.ANIMATION_SPEED) % 1.0f;
                        // Вычисляем громкость с использованием SIMD в CalculateLoudness
                        loudness = CalculateAndSmoothLoudness(spectrum!);
                        _cachedLoudness = loudness;

                        if (loudness > _peakLoudness)
                            _peakLoudness = loudness;
                        else
                            _peakLoudness = Math.Max(0, _peakLoudness - LedMeterConstants.PEAK_DECAY_RATE);

                        if (loudness > LedMeterConstants.HIGH_LOUDNESS_THRESHOLD)
                        {
                            float vibrationIntensity = (loudness - LedMeterConstants.HIGH_LOUDNESS_THRESHOLD) / (1 - LedMeterConstants.HIGH_LOUDNESS_THRESHOLD);
                            _vibrationOffset = (float)Math.Sin(_animationPhase * Math.PI * 10) * 0.8f * vibrationIntensity;
                        }
                        else
                        {
                            _vibrationOffset = 0;
                        }
                    }
                    else
                    {
                        lock (_loudnessLock)
                            loudness = _cachedLoudness ?? CalculateAndSmoothLoudness(spectrum!);
                    }
                }
                finally
                {
                    if (semaphoreAcquired)
                        _renderSemaphore.Release();
                }

                // Пример использования QuickReject для отсечения невидимой области (если канва предоставляет такую возможность)
                if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
                    return;

                canvas.Save();
                // Перенос и отрисовка контента LED-метра
                RenderMeterContent(canvas, info, loudness, _peakLoudness, paint!);
                canvas.Restore();

                canvas.Save();
                canvas.Translate(0, info.Height - LedMeterConstants.PERFORMANCE_INFO_BOTTOM_MARGIN);
                drawPerformanceInfo(canvas, info);
                canvas.Restore();
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LedMeterConstants.LOG_PREFIX, $"Error rendering LED meter: {ex.Message}");
            }
        }

        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LedMeterRenderer));

            if (!_isInitialized)
            {
                SmartLogger.Log(LogLevel.Error, LedMeterConstants.LOG_PREFIX, "LedMeterRenderer not initialized");
                return false;
            }
            if (canvas == null || spectrum == null || spectrum.Length == 0 ||
                paint == null || info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, LedMeterConstants.LOG_PREFIX, "Invalid render parameters for LedMeterRenderer");
                return false;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculateAndSmoothLoudness(float[] spectrum)
        {
            float rawLoudness = CalculateLoudness(spectrum.AsSpan());
            float smoothedLoudness = _previousLoudness + (rawLoudness - _previousLoudness) * _smoothingFactor;
            smoothedLoudness = Math.Clamp(smoothedLoudness, LedMeterConstants.MIN_LOUDNESS_THRESHOLD, 1f);
            _previousLoudness = smoothedLoudness;
            return smoothedLoudness;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculateLoudness(ReadOnlySpan<float> spectrum)
        {
            if (spectrum.IsEmpty)
                return 0f;

            // Оптимизация суммирования абсолютных значений с использованием SIMD
            float sum = 0f;
            int vectorSize = Vector<float>.Count;
            int i = 0;
            Vector<float> sumVector = Vector<float>.Zero;

            for (; i <= spectrum.Length - vectorSize; i += vectorSize)
            {
                var vec = new Vector<float>(spectrum.Slice(i));
                vec = Vector.Abs(vec);
                sumVector += vec;
            }
            for (int j = 0; j < vectorSize; j++)
            {
                sum += sumVector[j];
            }
            for (; i < spectrum.Length; i++)
            {
                sum += Math.Abs(spectrum[i]);
            }
            return Math.Clamp(sum / spectrum.Length, 0f, 1f);
        }

        private void RenderMeterContent(SKCanvas canvas, SKImageInfo info, float loudness, float peakLoudness, SKPaint basePaint)
        {
            if (loudness < LedMeterConstants.MIN_LOUDNESS_THRESHOLD)
                return;

            canvas.Save();
            canvas.Translate(_vibrationOffset, 0);

            // Определение размеров панели
            float outerPadding = 5f;
            float panelLeft = LedMeterConstants.PANEL_PADDING;
            float panelTop = LedMeterConstants.PANEL_PADDING;
            float panelWidth = info.Width - (LedMeterConstants.PANEL_PADDING * 2);
            float panelHeight = info.Height - (LedMeterConstants.PANEL_PADDING * 2);

            SKRect outerRect = new SKRect(
                outerPadding,
                outerPadding,
                info.Width - outerPadding,
                info.Height - outerPadding
            );

            RenderOuterCase(canvas, outerRect);
            SKRect panelRect = new SKRect(panelLeft, panelTop, panelLeft + panelWidth, panelTop + panelHeight);
            RenderPanel(canvas, panelRect);
            RenderLabels(canvas, panelRect);

            float meterLeft = panelLeft + LedMeterConstants.TICK_MARK_WIDTH + 5;
            float meterWidth = panelWidth - (LedMeterConstants.TICK_MARK_WIDTH + 15);
            float meterHeight = panelHeight - 25;
            float meterTop = panelTop + 20;

            int ledCount = Math.Max(10, Math.Min(LedMeterConstants.DEFAULT_LED_COUNT, (int)(meterHeight / 12)));

            if (_ledAnimationPhases.Length != ledCount)
            {
                _ledAnimationPhases = new float[ledCount];
                Random phaseRandom = new(42);
                for (int i = 0; i < ledCount; i++)
                    _ledAnimationPhases[i] = (float)phaseRandom.NextDouble();
            }

            float totalLedSpace = meterHeight * 0.95f;
            float totalSpacingSpace = meterHeight * 0.05f;
            float ledHeight = (totalLedSpace - totalSpacingSpace) / ledCount;
            float spacing = totalSpacingSpace / (ledCount - 1);
            float ledWidth = meterWidth; // Use full width

            RenderRecessedLedPanel(canvas, meterLeft - 3, meterTop - 3, meterWidth + 6, meterHeight + 6);

            int activeLedCount = (int)(loudness * ledCount);
            int peakLedIndex = (int)(peakLoudness * ledCount);

            RenderTickMarks(canvas, panelLeft, meterTop, LedMeterConstants.TICK_MARK_WIDTH, meterHeight);
            RenderLedArray(canvas, meterLeft, meterTop, ledWidth, ledHeight, spacing, ledCount, activeLedCount, peakLedIndex);
            RenderFixedScrews(canvas, panelRect);
            canvas.Restore();
        }

        private void RenderOuterCase(SKCanvas canvas, SKRect rect)
        {
            if (_outerCasePaint == null) return;
            // Используем DrawRoundRect вместо DrawPath для простоты
            canvas.DrawRoundRect(rect, LedMeterConstants.CORNER_RADIUS, LedMeterConstants.CORNER_RADIUS, _outerCasePaint);
            using var highlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f,
                Color = new SKColor(255, 255, 255, 40)
            };
            canvas.DrawLine(rect.Left + LedMeterConstants.CORNER_RADIUS, rect.Top + 1.5f,
                rect.Right - LedMeterConstants.CORNER_RADIUS, rect.Top + 1.5f, highlightPaint);
        }

        private void RenderPanel(SKCanvas canvas, SKRect rect)
        {
            using var roundRect = new SKRoundRect(rect, LedMeterConstants.CORNER_RADIUS - 4, LedMeterConstants.CORNER_RADIUS - 4);
            if (_panelPaint != null)
                canvas.DrawRoundRect(roundRect, _panelPaint);
            RenderPanelBevel(canvas, roundRect);
            using var vignettePaint = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(rect.MidX, rect.MidY),
                    Math.Max(rect.Width, rect.Height) * 0.75f,
                    new[] { new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 30) },
                    null,
                    SKShaderTileMode.Clamp
                )
            };
            canvas.DrawRoundRect(roundRect, vignettePaint);
        }

        private void RenderPanelBevel(SKCanvas canvas, SKRoundRect roundRect)
        {
            using var outerHighlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = LedMeterConstants.BEVEL_SIZE,
                Color = new SKColor(255, 255, 255, 120)
            };

            using var highlightPath = new SKPath();
            float radOffset = LedMeterConstants.BEVEL_SIZE / 2;
            highlightPath.MoveTo(roundRect.Rect.Left + LedMeterConstants.CORNER_RADIUS, roundRect.Rect.Bottom - radOffset);
            highlightPath.ArcTo(new SKRect(roundRect.Rect.Left + radOffset, roundRect.Rect.Bottom - LedMeterConstants.CORNER_RADIUS * 2 + radOffset,
                roundRect.Rect.Left + LedMeterConstants.CORNER_RADIUS * 2 - radOffset, roundRect.Rect.Bottom), 90, 90, false);
            highlightPath.LineTo(roundRect.Rect.Left + radOffset, roundRect.Rect.Top + LedMeterConstants.CORNER_RADIUS);
            highlightPath.ArcTo(new SKRect(roundRect.Rect.Left + radOffset, roundRect.Rect.Top + radOffset,
                roundRect.Rect.Left + LedMeterConstants.CORNER_RADIUS * 2 - radOffset, roundRect.Rect.Top + LedMeterConstants.CORNER_RADIUS * 2 - radOffset), 180, 90, false);
            highlightPath.LineTo(roundRect.Rect.Right - LedMeterConstants.CORNER_RADIUS, roundRect.Rect.Top + radOffset);
            canvas.DrawPath(highlightPath, outerHighlightPaint);

            using var outerShadowPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = LedMeterConstants.BEVEL_SIZE,
                Color = new SKColor(0, 0, 0, 90)
            };
            using var shadowPath = new SKPath();
            shadowPath.MoveTo(roundRect.Rect.Right - LedMeterConstants.CORNER_RADIUS, roundRect.Rect.Top + radOffset);
            shadowPath.ArcTo(new SKRect(roundRect.Rect.Right - LedMeterConstants.CORNER_RADIUS * 2 + radOffset, roundRect.Rect.Top + radOffset,
                roundRect.Rect.Right - radOffset, roundRect.Rect.Top + LedMeterConstants.CORNER_RADIUS * 2 - radOffset), 270, 90, false);
            shadowPath.LineTo(roundRect.Rect.Right - radOffset, roundRect.Rect.Bottom - LedMeterConstants.CORNER_RADIUS);
            shadowPath.ArcTo(new SKRect(roundRect.Rect.Right - LedMeterConstants.CORNER_RADIUS * 2 + radOffset, roundRect.Rect.Bottom - LedMeterConstants.CORNER_RADIUS * 2 + radOffset,
                roundRect.Rect.Right - radOffset, roundRect.Rect.Bottom - radOffset), 0, 90, false);
            shadowPath.LineTo(roundRect.Rect.Left + LedMeterConstants.CORNER_RADIUS, roundRect.Rect.Bottom - radOffset);
            canvas.DrawPath(shadowPath, outerShadowPaint);
        }

        private void RenderRecessedLedPanel(SKCanvas canvas, float x, float y, float width, float height)
        {
            float recessRadius = 6f;
            SKRect recessRect = new SKRect(x, y, x + width, y + height);
            using var recessRoundRect = new SKRoundRect(recessRect, recessRadius, recessRadius);
            using var backgroundPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(12, 12, 12)
            };
            canvas.DrawRoundRect(recessRoundRect, backgroundPaint);
            using var innerShadowPaint = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(x, y),
                    new SKPoint(x, y + height * 0.2f),
                    new SKColor[] { new SKColor(0, 0, 0, 120), new SKColor(0, 0, 0, 0) },
                    null,
                    SKShaderTileMode.Clamp
                )
            };
            canvas.DrawRoundRect(recessRoundRect, innerShadowPaint);
            using var borderPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                Color = new SKColor(0, 0, 0, 180)
            };
            canvas.DrawRoundRect(recessRoundRect, borderPaint);
        }

        private void RenderFixedScrews(SKCanvas canvas, SKRect panelRect)
        {
            if (_screwBitmap == null) return;

            float cornerOffset = LedMeterConstants.CORNER_RADIUS - 4;
            DrawScrew(canvas, panelRect.Left + cornerOffset, panelRect.Top + cornerOffset, _screwAngles[0]);
            DrawScrew(canvas, panelRect.Right - cornerOffset, panelRect.Top + cornerOffset, _screwAngles[1]);
            DrawScrew(canvas, panelRect.Left + cornerOffset, panelRect.Bottom - cornerOffset, _screwAngles[2]);
            DrawScrew(canvas, panelRect.Right - cornerOffset, panelRect.Bottom - cornerOffset, _screwAngles[3]);

            float labelX = panelRect.Right - 65;
            float labelY = panelRect.Bottom - 8;
            using var labelPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(230, 230, 230, 120),
                TextSize = 8,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
                TextAlign = SKTextAlign.Right
            };
            canvas.DrawText("SpectrumNet™ Audio", labelX, labelY, labelPaint);
        }

        private void DrawScrew(SKCanvas canvas, float x, float y, float angle)
        {
            if (_screwBitmap == null) return;
            canvas.Save();
            canvas.Translate(x, y);
            canvas.RotateDegrees(angle);
            canvas.Translate(-12, -12);
            canvas.DrawBitmap(_screwBitmap, 0, 0);
            canvas.Restore();
        }

        private void RenderLabels(SKCanvas canvas, SKRect panelRect)
        {
            float labelX = panelRect.Left + 10;
            float labelY = panelRect.Top + 14;
            using var labelPaint = new SKPaint
            {
                IsAntialias = true,
                TextSize = 14,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
                TextAlign = SKTextAlign.Left
            };
            labelPaint.Color = new SKColor(30, 30, 30, 180);
            canvas.DrawText("VU", labelX + 1, labelY + 1, labelPaint);
            labelPaint.Color = new SKColor(230, 230, 230, 200);
            canvas.DrawText("VU", labelX, labelY, labelPaint);
            labelPaint.TextSize = 10;
            labelPaint.Color = new SKColor(200, 200, 200, 150);
            canvas.DrawText("dB METER", labelX + 30, labelY, labelPaint);
            labelPaint.TextSize = 8;
            labelPaint.TextAlign = SKTextAlign.Right;
            labelPaint.Color = new SKColor(200, 200, 200, 120);
            canvas.DrawText("PRO SERIES", panelRect.Right - 10, panelRect.Top + 14, labelPaint);
            labelPaint.TextAlign = SKTextAlign.Left;
            canvas.DrawText("dB", panelRect.Left + 10, panelRect.Bottom - 10, labelPaint);
        }

        private void RenderTickMarks(SKCanvas canvas, float x, float y, float width, float height)
        {
            using var tickAreaPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(30, 30, 30, 70)
            };
            SKRect tickAreaRect = new SKRect(x, y, x + width - 2, y + height);
            canvas.DrawRect(tickAreaRect, tickAreaPaint);
            using var tickPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                Color = SKColors.LightGray.WithAlpha(150),
                IsAntialias = true
            };
            using var textPaint = new SKPaint
            {
                TextSize = 9,
                Color = SKColors.LightGray.WithAlpha(180),
                IsAntialias = true,
                TextAlign = SKTextAlign.Right
            };
            string[] dbValues = { "0", "-3", "-6", "-10", "-20", "-40" };
            float[] dbPositions = { 1.0f, 0.85f, 0.7f, 0.55f, 0.3f, 0.0f };
            for (int i = 0; i < dbValues.Length; i++)
            {
                float yPos = y + height - dbPositions[i] * height;
                canvas.DrawLine(x, yPos, x + width - 5, yPos, tickPaint);
                using var shadowPaint = new SKPaint
                {
                    TextSize = 9,
                    Color = SKColors.Black.WithAlpha(80),
                    IsAntialias = true,
                    TextAlign = SKTextAlign.Right
                };
                canvas.DrawText(dbValues[i], x + width - 7, yPos + 3.5f, shadowPaint);
                canvas.DrawText(dbValues[i], x + width - 8, yPos + 3, textPaint);
            }
            tickPaint.Color = tickPaint.Color.WithAlpha(80);
            for (int i = 0; i < 10; i++)
            {
                float ratio = i / 10f;
                float yPos = y + ratio * height;
                canvas.DrawLine(x, yPos, x + width * 0.6f, yPos, tickPaint);
            }
        }

        private void RenderLedArray(SKCanvas canvas, float x, float y, float width, float height, float spacing, int ledCount, int activeLedCount, int peakLedIndex)
        {
            float ledY;
            // Рендер неактивных LED (фон)
            if (_cachedInactiveLedPaint != null)
            {
                for (int i = 0; i < ledCount; i++)
                {
                    float normalizedPosition = (float)i / ledCount;
                    ledY = y + (ledCount - i - 1) * (height + spacing);
                    SKColor color = GetLedColorForPosition(normalizedPosition, i);
                    bool isActive = i < activeLedCount;
                    bool isPeak = i == peakLedIndex;
                    if (!isActive && !isPeak)
                    {
                        _ledPath.Reset();
                        var ledRect = new SKRoundRect(new SKRect(x, ledY, x + width, ledY + height), LedMeterConstants.LED_ROUNDING_RADIUS, LedMeterConstants.LED_ROUNDING_RADIUS);
                        _ledPath.AddRoundRect(ledRect);
                        canvas.DrawPath(_ledPath, _cachedLedBasePaint);
                        float inset = 1f;
                        var ledSurfaceRect = new SKRoundRect(new SKRect(x + inset, ledY + inset, x + width - inset, ledY + height - inset),
                            Math.Max(1, LedMeterConstants.LED_ROUNDING_RADIUS - inset * 0.5f),
                            Math.Max(1, LedMeterConstants.LED_ROUNDING_RADIUS - inset * 0.5f));
                        SKColor offColor = MultiplyColor(color, 0.10f);
                        _cachedInactiveLedPaint.Color = offColor;
                        canvas.DrawRoundRect(ledSurfaceRect, _cachedInactiveLedPaint);
                    }
                }
            }
            // Рендер активных LED (фон и свечение)
            for (int i = 0; i < ledCount; i++)
            {
                float normalizedPosition = (float)i / ledCount;
                ledY = y + (ledCount - i - 1) * (height + spacing);
                SKColor color = GetLedColorForPosition(normalizedPosition, i);
                bool isActive = i < activeLedCount;
                bool isPeak = i == peakLedIndex;
                if (isActive || isPeak)
                {
                    _ledAnimationPhases[i] = (_ledAnimationPhases[i] + LedMeterConstants.ANIMATION_SPEED * (0.5f + normalizedPosition)) % 1.0f;
                    RenderActiveLed(canvas, x, ledY, width, height, color, isActive, isPeak, i);
                }
            }
        }

        private SKColor GetLedColorForPosition(float normalizedPosition, int index)
        {
            int colorGroup;
            if (normalizedPosition >= LedMeterConstants.HIGH_LOUDNESS_THRESHOLD)
                colorGroup = 2;
            else if (normalizedPosition >= LedMeterConstants.MEDIUM_LOUDNESS_THRESHOLD)
                colorGroup = 1;
            else
                colorGroup = 0;
            int variationIndex = index % 10;
            int colorIndex = colorGroup * 10 + variationIndex;
            if (colorIndex < _ledColorVariations.Count)
                return _ledColorVariations[colorIndex];
            if (colorGroup == 2) return new SKColor(220, 30, 30);
            if (colorGroup == 1) return new SKColor(230, 200, 0);
            return new SKColor(40, 200, 40);
        }

        private void RenderActiveLed(SKCanvas canvas, float x, float y, float width, float height, SKColor color, bool isActive, bool isPeak, int index)
        {
            if (_cachedLedPaint == null || _cachedGlowPaint == null || _cachedHighlightPaint == null || _cachedLedBasePaint == null)
                return;
            float brightnessVariation = _ledVariations[index % _ledVariations.Count];
            float animPhase = _ledAnimationPhases[index % _ledAnimationPhases.Length];
            _ledPath.Reset();
            var ledRect = new SKRoundRect(new SKRect(x, y, x + width, y + height), LedMeterConstants.LED_ROUNDING_RADIUS, LedMeterConstants.LED_ROUNDING_RADIUS);
            _ledPath.AddRoundRect(ledRect);
            canvas.DrawPath(_ledPath, _cachedLedBasePaint);
            float inset = 1f;
            var ledSurfaceRect = new SKRoundRect(new SKRect(x + inset, y + inset, x + width - inset, y + height - inset),
                Math.Max(1, LedMeterConstants.LED_ROUNDING_RADIUS - inset * 0.5f),
                Math.Max(1, LedMeterConstants.LED_ROUNDING_RADIUS - inset * 0.5f));
            SKColor ledOnColor = color;
            SKColor ledOffColor = new SKColor(10, 10, 10, 220);
            float pulse = isPeak ?
                0.7f + (float)Math.Sin(animPhase * Math.PI * 2) * 0.3f :
                brightnessVariation;
            ledOnColor = MultiplyColor(ledOnColor, pulse);
            if (index > ledRect.Rect.Height * 0.7f)
            {
                float glowIntensity = LedMeterConstants.GLOW_INTENSITY * (0.8f + (float)Math.Sin(animPhase * Math.PI * 2) * 0.2f * brightnessVariation);
                _cachedGlowPaint.Color = ledOnColor.WithAlpha((byte)(glowIntensity * 160 * brightnessVariation));
                canvas.DrawRoundRect(ledSurfaceRect, _cachedGlowPaint);
            }
            _cachedLedPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(x, y),
                new SKPoint(x, y + height),
                new[] { ledOnColor, MultiplyColor(ledOnColor, 0.9f), ledOffColor },
                new[] { 0.0f, 0.7f, 1.0f },
                SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(ledSurfaceRect, _cachedLedPaint);
            _highlightPath.Reset();
            float arcWidth = width * 0.9f;
            float arcHeight = height * 0.4f;
            float arcX = x + (width - arcWidth) / 2;
            float arcY = y + height * 0.05f;
            _highlightPath.AddRoundRect(new SKRoundRect(new SKRect(arcX, arcY, arcX + arcWidth, arcY + arcHeight), LedMeterConstants.LED_ROUNDING_RADIUS, LedMeterConstants.LED_ROUNDING_RADIUS));
            _cachedHighlightPaint.Color = new SKColor(255, 255, 255, 180);
            using var highlightFillPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 50),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawPath(_highlightPath, highlightFillPaint);
            canvas.DrawPath(_highlightPath, _cachedHighlightPaint);
        }

        private SKColor MultiplyColor(SKColor color, float factor)
        {
            return new SKColor(
                (byte)Math.Clamp(color.Red * factor, 0, 255),
                (byte)Math.Clamp(color.Green * factor, 0, 255),
                (byte)Math.Clamp(color.Blue * factor, 0, 255),
                color.Alpha);
        }
        #endregion

        #region Disposal
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;
            if (disposing)
            {
                _renderSemaphore?.Dispose();
                _ledPath?.Dispose();
                _highlightPath?.Dispose();
                _screwBitmap?.Dispose();
                _brushedMetalBitmap?.Dispose();
                _panelPaint?.Dispose();
                _outerCasePaint?.Dispose();
                _cachedLedPaint?.Dispose();
                _cachedGlowPaint?.Dispose();
                _cachedHighlightPaint?.Dispose();
                _cachedLedBasePaint?.Dispose();
                _cachedInactiveLedPaint?.Dispose();
                _ledColorVariations.Clear();
                _ledVariations.Clear();
            }
            _disposed = true;
            SmartLogger.Log(LogLevel.Debug, LedMeterConstants.LOG_PREFIX, "LedMeterRenderer disposed");
        }

        ~LedMeterRenderer()
        {
            Dispose(disposing: false);
        }
        #endregion
    }
}