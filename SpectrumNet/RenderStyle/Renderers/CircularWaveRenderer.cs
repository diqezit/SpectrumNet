#nullable enable

namespace SpectrumNet
{
    public sealed class CircularWaveRenderer : BaseSpectrumRenderer
    {
        #region Constants
        private static class Constants
        {
            public const string LOG_PREFIX = "CircularWaveRenderer";
            public const float ROTATION_SPEED = 0.5f;          // Скорость вращения волны
            public const float RADIUS_PROPORTION = 0.4f;       // Доля радиуса визуализации относительно размера холста
            public const float AMPLITUDE_SCALE = 0.5f;         // Масштаб амплитуды для визуального эффекта
            public const float MIN_MAGNITUDE_THRESHOLD = 0.01f; // Минимальный порог величины для отрисовки точки
            public const float OVERLAY_SMOOTHING_FACTOR = 0.5f; // Фактор сглаживания для режима оверлея
            public const int MAX_POINT_COUNT = 180;            // Максимальное количество точек в волне
            public const float GLOW_INTENSITY = 0.5f;          // Интенсивность свечения
            public const float HIGH_INTENSITY_THRESHOLD = 0.7f; // Порог для дополнительных эффектов
            public const float WAVE_ALPHA_MULTIPLIER = 1.2f;   // Множитель прозрачности для волны
            public const int MIN_POINT_COUNT = 12;             // Минимальное количество точек в волне
            public const float INNER_GLOW_ALPHA = 0.3f;        // Альфа для внутреннего свечения
            public const float MIN_BLUR_RADIUS = 4f;           // Минимальный радиус размытия
            public const float MAX_BLUR_RADIUS = 12f;          // Максимальный радиус размытия
            public const float BLUR_WIDTH_RATIO = 0.8f;        // Соотношение размытия к ширине бара
            public const float OUTLINE_WIDTH_RATIO = 0.2f;     // Соотношение ширины контура к ширине бара
            public const float FILL_ALPHA_RATIO = 0.4f;        // Соотношение альфы заливки к общей альфе
            public const float RADIUS_SCALE_FACTOR = 0.1f;     // Фактор масштабирования радиуса
            public const float AMPLITUDE_SCALE_FACTOR = 0.2f;  // Фактор масштабирования амплитуды
            public const float BAR_COUNT_NORMALIZATION = 100f; // Нормализация количества баров
        }
        #endregion

        #region Fields
        private static CircularWaveRenderer? _instance;
        private bool _isOverlayActive;
        private float _rotation;
        private float _rotationSpeed = Constants.ROTATION_SPEED;
        private float _radiusProportion = Constants.RADIUS_PROPORTION;
        private float _amplitudeScale = Constants.AMPLITUDE_SCALE;
        private float _minMagnitudeThreshold = Constants.MIN_MAGNITUDE_THRESHOLD;

        private float[]? _precomputedCosValues;
        private float[]? _precomputedSinValues;
        private int _previousPointCount;
        private int _maxPointCount = Constants.MAX_POINT_COUNT;
        private readonly SKPath _path = new();
        #endregion

        #region Constructor and Initialization
        private CircularWaveRenderer() { }

        public static CircularWaveRenderer GetInstance()
        {
            return _instance ??= new CircularWaveRenderer();
        }

        public override void Initialize()
        {
            if (!_isInitialized)
            {
                base.Initialize();
                _isInitialized = true;
                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "CircularWaveRenderer initialized");
            }
        }
        #endregion

        #region Configuration
        public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            base.Configure(isOverlayActive, quality);
            _isOverlayActive = isOverlayActive;
            _smoothingFactor = isOverlayActive ? Constants.OVERLAY_SMOOTHING_FACTOR : DEFAULT_SMOOTHING_FACTOR;
        }

        public void ConfigureAdvanced(
            bool? isOverlayActive = null,
            float? rotationSpeed = null,
            float? radiusProportion = null,
            float? amplitudeScale = null,
            float? minMagnitudeThreshold = null,
            int? maxPointCount = null)
        {
            _isOverlayActive = isOverlayActive ?? _isOverlayActive;
            _rotationSpeed = rotationSpeed ?? _rotationSpeed;
            _radiusProportion = radiusProportion ?? _radiusProportion;
            _amplitudeScale = amplitudeScale ?? _amplitudeScale;
            _minMagnitudeThreshold = minMagnitudeThreshold ?? _minMagnitudeThreshold;
            _maxPointCount = maxPointCount ?? _maxPointCount;
        }
        #endregion

        #region Rendering
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
            if (!ValidateRenderParameters(canvas, spectrum, info, paint, Constants.LOG_PREFIX))
                return;

            int pointCount = Math.Max(Constants.MIN_POINT_COUNT, Math.Min(Math.Min(spectrum!.Length, _maxPointCount), barCount));
            float adjustedRotationSpeed = _rotationSpeed * (0.5f + 0.5f * pointCount / Math.Max(barCount, 1));

            // Обработка спектра
            float[] renderSpectrum = ProcessSpectrum(spectrum!, pointCount, adjustedRotationSpeed);

            // Расчет параметров рендеринга с учетом размера холста и количества баров
            float normalizationFactor = 1f - (float)Math.Min(barCount, Constants.BAR_COUNT_NORMALIZATION) / Constants.BAR_COUNT_NORMALIZATION;
            float radius = MathF.Min(info.Width, info.Height) * _radiusProportion *
                           (1f + Constants.RADIUS_SCALE_FACTOR * normalizationFactor);
            float amplitudeScale = _amplitudeScale *
                                   (1f + Constants.AMPLITUDE_SCALE_FACTOR * normalizationFactor);

            // Проверка видимости области рендеринга
            float maxRadius = radius * (1f + amplitudeScale);
            if (canvas!.QuickReject(new SKRect(
                info.Width / 2f - maxRadius,
                info.Height / 2f - maxRadius,
                info.Width / 2f + maxRadius,
                info.Height / 2f + maxRadius)))
            {
                drawPerformanceInfo?.Invoke(canvas!, info);
                return;
            }

            RenderCircularWave(canvas!, renderSpectrum, pointCount, radius, info.Width / 2f, info.Height / 2f, paint!, amplitudeScale, barWidth);

            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        private float[] ProcessSpectrum(float[] spectrum, int pointCount, float adjustedRotationSpeed)
        {
            float[] renderSpectrum;
            bool semaphoreAcquired = false;

            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);
                if (semaphoreAcquired)
                {
                    _rotation = (_rotation + adjustedRotationSpeed) % 360f;
                    if (_previousPointCount != pointCount)
                    {
                        PrecomputeTrigonometryValues(pointCount);
                        _previousPointCount = pointCount;
                    }

                    // Используем базовые методы обработки спектра
                    float[] scaledSpectrum = ScaleSpectrum(spectrum, pointCount, spectrum.Length);
                    _processedSpectrum = SmoothSpectrum(scaledSpectrum, pointCount);
                }

                lock (_spectrumLock)
                {
                    renderSpectrum = _processedSpectrum ?? ScaleSpectrum(spectrum, pointCount, spectrum.Length);
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, $"Error processing spectrum: {ex.Message}");
                renderSpectrum = new float[pointCount]; // Возвращаем пустой спектр в случае ошибки
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _spectrumSemaphore.Release();
                }
            }

            return renderSpectrum;
        }
        #endregion

        #region Spectrum Processing
        private void PrecomputeTrigonometryValues(int pointCount)
        {
            if (_precomputedCosValues?.Length == pointCount)
                return;

            _precomputedCosValues = new float[pointCount];
            _precomputedSinValues = new float[pointCount];
            float angleStep = 2 * MathF.PI / pointCount;

            for (int i = 0; i < pointCount; i++)
            {
                float angle = i * angleStep;
                _precomputedCosValues[i] = MathF.Cos(angle);
                _precomputedSinValues[i] = MathF.Sin(angle);
            }
        }
        #endregion

        #region Wave Rendering
        private void RenderCircularWave(
            SKCanvas canvas,
            float[] spectrum,
            int pointCount,
            float radius,
            float centerX,
            float centerY,
            SKPaint paint,
            float amplitudeScale,
            float barWidth)
        {
            if (canvas == null || spectrum == null || paint == null || _disposed)
                return;

            float rad = _rotation * MathF.PI / 180f;
            float cosDelta = MathF.Cos(rad);
            float sinDelta = MathF.Sin(rad);

            _path.Reset();

            // Находим максимальную амплитуду для эффектов
            float maxAmplitude = CalculateMaxAmplitude(spectrum);

            // Создаем путь волны
            if (!BuildWavePath(spectrum, pointCount, radius, centerX, centerY, amplitudeScale, cosDelta, sinDelta))
            {
                return; // Если путь пустой, прекращаем рендеринг
            }

            // Рассчитываем общие параметры для эффектов
            byte alpha = (byte)(paint.Color.Alpha * Math.Min(maxAmplitude * Constants.WAVE_ALPHA_MULTIPLIER, 1.0f));
            float blurRadius = Math.Max(Constants.MIN_BLUR_RADIUS, Math.Min(barWidth * Constants.BLUR_WIDTH_RATIO, Constants.MAX_BLUR_RADIUS));

            // Рендерим эффекты и контуры
            if (_useAdvancedEffects)
            {
                RenderGlowEffects(canvas, alpha, blurRadius, paint);

                if (maxAmplitude > Constants.HIGH_INTENSITY_THRESHOLD)
                {
                    RenderInnerGlow(canvas, alpha, blurRadius);
                }
            }

            RenderOutline(canvas, alpha, barWidth, paint);
            RenderFill(canvas, alpha, paint);
        }

        private bool BuildWavePath(
            float[] spectrum,
            int pointCount,
            float radius,
            float centerX,
            float centerY,
            float amplitudeScale,
            float cosDelta,
            float sinDelta)
        {
            bool firstPoint = true;
            for (int i = 0; i < pointCount; i++)
            {
                float amplitude = spectrum[i];
                if (amplitude < _minMagnitudeThreshold)
                    continue;

                float r = radius * (1f + amplitude * amplitudeScale);
                float baseCos = _precomputedCosValues![i];
                float baseSin = _precomputedSinValues![i];
                float rotatedCos = baseCos * cosDelta - baseSin * sinDelta;
                float rotatedSin = baseSin * cosDelta + baseCos * sinDelta;
                float x = centerX + r * rotatedCos;
                float y = centerY + r * rotatedSin;

                if (firstPoint)
                {
                    _path.MoveTo(x, y);
                    firstPoint = false;
                }
                else
                {
                    _path.LineTo(x, y);
                }
            }

            if (firstPoint)
            {
                return false; // Путь пустой, нечего рендерить
            }

            _path.Close();
            return true;
        }

        private void RenderGlowEffects(SKCanvas canvas, byte alpha, float blurRadius, SKPaint basePaint)
        {
            using var glowPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                Style = SKPaintStyle.Fill,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius),
                Color = basePaint.Color.WithAlpha((byte)(alpha * Constants.GLOW_INTENSITY))
            };
            canvas.DrawPath(_path, glowPaint);
        }

        private void RenderInnerGlow(SKCanvas canvas, byte alpha, float blurRadius)
        {
            using var innerGlowPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                Style = SKPaintStyle.Fill,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius * 0.5f),
                Color = new SKColor(255, 255, 255, (byte)(alpha * Constants.INNER_GLOW_ALPHA))
            };
            canvas.DrawPath(_path, innerGlowPaint);
        }

        private void RenderOutline(SKCanvas canvas, byte alpha, float barWidth, SKPaint basePaint)
        {
            using var outlinePaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                Style = SKPaintStyle.Stroke,
                Color = basePaint.Color.WithAlpha(alpha),
                StrokeWidth = Math.Max(1f, barWidth * Constants.OUTLINE_WIDTH_RATIO)
            };
            canvas.DrawPath(_path, outlinePaint);
        }

        private void RenderFill(SKCanvas canvas, byte alpha, SKPaint basePaint)
        {
            using var fillPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                Style = SKPaintStyle.Fill,
                Color = basePaint.Color.WithAlpha((byte)(alpha * Constants.FILL_ALPHA_RATIO))
            };
            canvas.DrawPath(_path, fillPaint);
        }
        #endregion

        #region Advanced Effects
        /// <summary>
        /// Рисует радиальный градиент внутри волны для создания эффекта глубины
        /// </summary>
        private void RenderRadialGradient(SKCanvas canvas, float centerX, float centerY, float radius, SKPaint basePaint, byte alpha)
        {
            if (!_useAdvancedEffects || _quality != RenderQuality.High)
                return;

            SKColor centerColor = new SKColor(
                (byte)Math.Min(basePaint.Color.Red + 40, 255),
                (byte)Math.Min(basePaint.Color.Green + 40, 255),
                (byte)Math.Min(basePaint.Color.Blue + 40, 255),
                (byte)(alpha * 0.7f)
            );

            SKColor edgeColor = basePaint.Color.WithAlpha((byte)(alpha * 0.2f));

            using var gradientPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(centerX, centerY),
                    radius * 0.8f,
                    new SKColor[] { centerColor, edgeColor },
                    null,
                    SKShaderTileMode.Clamp)
            };

            canvas.DrawCircle(centerX, centerY, radius * 0.8f, gradientPaint);
        }

        /// <summary>
        /// Рисует анимированные частицы внутри волны для создания динамического эффекта
        /// </summary>
        private void RenderAnimatedParticles(SKCanvas canvas, float centerX, float centerY, float radius, float barWidth, byte alpha)
        {
            if (!_useAdvancedEffects || _quality != RenderQuality.High)
                return;

            // Генерация псевдослучайных позиций частиц на основе текущего вращения
            float particleCount = 5 + (int)(_rotation / 30) % 10;
            float baseAngle = _rotation * MathF.PI / 180;

            using var particlePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White.WithAlpha((byte)(alpha * 0.6f))
            };

            for (int i = 0; i < particleCount; i++)
            {
                float angle = baseAngle + i * MathF.PI * 2 / particleCount;
                float distance = radius * (0.3f + 0.4f * ((i * 7 + (int)_rotation) % 10) / 10f);
                float size = barWidth * (0.5f + 1.5f * ((i * 3 + (int)_rotation) % 5) / 5f);

                float x = centerX + MathF.Cos(angle) * distance;
                float y = centerY + MathF.Sin(angle) * distance;

                canvas.DrawCircle(x, y, size, particlePaint);
            }
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Создает базовую краску с заданными параметрами
        /// </summary>
        private static SKPaint CreateBasePaint(bool useAntiAlias, SKFilterQuality filterQuality)
        {
            return new SKPaint
            {
                IsAntialias = useAntiAlias,
                FilterQuality = filterQuality
            };
        }

        /// <summary>
        /// Проверяет, находится ли область рендеринга в пределах видимости
        /// </summary>
        private bool IsRenderAreaOutsideViewport(SKCanvas canvas, float centerX, float centerY, float radius)
        {
            return canvas.QuickReject(new SKRect(
                centerX - radius,
                centerY - radius,
                centerX + radius,
                centerY + radius
            ));
        }

        /// <summary>
        /// Безопасно вычисляет максимальное значение спектра
        /// </summary>
        private float SafeCalculateMaxAmplitude(float[] spectrum)
        {
            if (spectrum == null || spectrum.Length == 0)
                return 0;

            float max = 0;
            for (int i = 0; i < spectrum.Length; i++)
            {
                if (spectrum[i] > max)
                    max = spectrum[i];
            }
            return max;
        }
        #endregion

        #region Disposal
        public override void Dispose()
        {
            if (!_disposed)
            {
                base.Dispose();
                _precomputedCosValues = _precomputedSinValues = null;
                _path.Dispose();
                _disposed = true;
                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "CircularWaveRenderer disposed");
            }
        }
        #endregion
    }
}