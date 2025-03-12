namespace SpectrumNet
{
    public abstract class BaseSpectrumRenderer : ISpectrumRenderer
    {
        #region Constants
        protected const float DEFAULT_SMOOTHING_FACTOR = 0.3f;
        protected const float OVERLAY_SMOOTHING_FACTOR = 0.5f;
        protected const float MIN_MAGNITUDE_THRESHOLD = 0.01f;
        protected const int PARALLEL_BATCH_SIZE = 32;
        #endregion

        #region Fields
        protected bool _isInitialized;
        protected RenderQuality _quality = RenderQuality.Medium;
        protected float[]? _previousSpectrum, _processedSpectrum;
        protected float _smoothingFactor = DEFAULT_SMOOTHING_FACTOR;

        // Общие настройки качества для всех рендереров
        protected bool _useAntiAlias = true;
        protected SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        protected bool _useAdvancedEffects = true;

        // Общие объекты для синхронизации
        protected readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        protected readonly object _spectrumLock = new();

        // Флаг для отслеживания состояния Dispose
        protected bool _disposed;

        // Пул красок для повторного использования
        protected readonly SKPaintPool _paintPool = new(5);
        #endregion

        #region Properties
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
        #endregion

        #region Initialization and Configuration
        public virtual void Initialize() => SmartLogger.Safe(() =>
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                SmartLogger.Log(LogLevel.Debug, $"[{GetType().Name}]", "Initialized");
            }
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{GetType().Name}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });

        public virtual void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium) => SmartLogger.Safe(() =>
        {
            Quality = quality;
            _smoothingFactor = isOverlayActive ? OVERLAY_SMOOTHING_FACTOR : DEFAULT_SMOOTHING_FACTOR;
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{GetType().Name}.Configure",
            ErrorMessage = "Failed to configure renderer"
        });
        #endregion

        #region Abstract Methods
        public abstract void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo> drawPerformanceInfo);
        #endregion

        #region Spectrum Processing
        /// <summary>
        /// Масштабирует спектр до нужного количества точек
        /// </summary>
        protected float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength) =>
            SmartLogger.Safe(() =>
            {
                float[] scaledSpectrum = new float[targetCount];
                float blockSize = (float)spectrumLength / targetCount;

                if (targetCount >= PARALLEL_BATCH_SIZE && Vector.IsHardwareAccelerated)
                {
                    Parallel.For(0, targetCount, i =>
                    {
                        int start = (int)(i * blockSize);
                        int end = Math.Min((int)((i + 1) * blockSize), spectrumLength);

                        float sum = 0;
                        for (int j = start; j < end; j++)
                        {
                            sum += spectrum[j];
                        }

                        scaledSpectrum[i] = sum / (end - start);
                    });
                }
                else
                {
                    for (int i = 0; i < targetCount; i++)
                    {
                        int start = (int)(i * blockSize);
                        int end = Math.Min((int)((i + 1) * blockSize), spectrumLength);

                        float sum = 0;
                        for (int j = start; j < end; j++)
                        {
                            sum += spectrum[j];
                        }

                        scaledSpectrum[i] = sum / (end - start);
                    }
                }

                return scaledSpectrum;
            }, new float[0], new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.ScaleSpectrum",
                ErrorMessage = "Failed to scale spectrum"
            }).Result;

        /// <summary>
        /// Сглаживает спектр с использованием предыдущего состояния
        /// </summary>
        protected float[] SmoothSpectrum(float[] spectrum, int targetCount, float? customSmoothingFactor = null) =>
            SmartLogger.Safe(() =>
            {
                float smoothing = customSmoothingFactor ?? _smoothingFactor;

                if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
                {
                    _previousSpectrum = new float[targetCount];
                }

                float[] smoothedSpectrum = new float[targetCount];

                if (Vector.IsHardwareAccelerated && targetCount >= Vector<float>.Count)
                {
                    int vectorSize = Vector<float>.Count;
                    int vectorizedLength = targetCount - (targetCount % vectorSize);

                    for (int i = 0; i < vectorizedLength; i += vectorSize)
                    {
                        Vector<float> currentValues = new(spectrum, i);
                        Vector<float> previousValues = new(_previousSpectrum, i);
                        Vector<float> smoothedValues = previousValues * (1 - smoothing) + currentValues * smoothing;

                        smoothedValues.CopyTo(smoothedSpectrum, i);
                        smoothedValues.CopyTo(_previousSpectrum, i);
                    }

                    // Обработка оставшихся элементов
                    for (int i = vectorizedLength; i < targetCount; i++)
                    {
                        ProcessSingleSpectrumValue(spectrum, smoothedSpectrum, i, smoothing);
                    }
                }
                else
                {
                    for (int i = 0; i < targetCount; i++)
                    {
                        ProcessSingleSpectrumValue(spectrum, smoothedSpectrum, i, smoothing);
                    }
                }

                return smoothedSpectrum;
            }, new float[0], new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.SmoothSpectrum",
                ErrorMessage = "Failed to smooth spectrum"
            }).Result;

        /// <summary>
        /// Обрабатывает одно значение спектра
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void ProcessSingleSpectrumValue(float[] spectrum, float[] smoothedSpectrum, int i, float smoothing)
        {
            if (_previousSpectrum == null) return;

            float currentValue = spectrum[i];
            float smoothedValue = _previousSpectrum[i] * (1 - smoothing) + currentValue * smoothing;

            smoothedSpectrum[i] = smoothedValue;
            _previousSpectrum[i] = smoothedValue;
        }

        /// <summary>
        /// Применяет нелинейное преобразование к спектру для улучшения визуализации
        /// </summary>
        protected float[] ApplyNonlinearTransform(float[] spectrum, float exponent = 2.0f, float scale = 1.0f) =>
            SmartLogger.Safe(() =>
            {
                float[] transformed = new float[spectrum.Length];

                for (int i = 0; i < spectrum.Length; i++)
                {
                    transformed[i] = (float)Math.Pow(spectrum[i], exponent) * scale;
                }

                return transformed;
            }, new float[0], new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.ApplyNonlinearTransform",
                ErrorMessage = "Failed to apply nonlinear transform"
            }).Result;

        /// <summary>
        /// Вычисляет среднее значение спектра
        /// </summary>
        protected float CalculateAverageAmplitude(float[] spectrum) =>
            SmartLogger.Safe(() => spectrum.Length == 0 ? 0 : spectrum.Sum() / spectrum.Length,
                0f, new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{GetType().Name}.CalculateAverageAmplitude",
                    ErrorMessage = "Failed to calculate average amplitude"
                }).Result;

        /// <summary>
        /// Вычисляет максимальное значение спектра
        /// </summary>
        protected float CalculateMaxAmplitude(float[] spectrum) =>
            SmartLogger.Safe(() => spectrum.Length == 0 ? 0 : spectrum.Max(),
                0f, new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{GetType().Name}.CalculateMaxAmplitude",
                    ErrorMessage = "Failed to calculate max amplitude"
                }).Result;

        /// <summary>
        /// Общий метод проверки параметров рендеринга
        /// </summary>
        protected bool ValidateRenderParameters(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? paint,
            string rendererName) =>
            SmartLogger.Safe(() =>
            {
                if (!_isInitialized)
                {
                    SmartLogger.Log(LogLevel.Error, $"[{rendererName}]", "Not initialized before rendering");
                    return false;
                }

                if (canvas == null)
                {
                    SmartLogger.Log(LogLevel.Error, $"[{rendererName}]", "Cannot render with null canvas");
                    return false;
                }

                if (spectrum == null || spectrum.Length == 0)
                {
                    SmartLogger.Log(LogLevel.Error, $"[{rendererName}]", "Cannot render with null or empty spectrum");
                    return false;
                }

                if (paint == null)
                {
                    SmartLogger.Log(LogLevel.Error, $"[{rendererName}]", "Cannot render with null paint");
                    return false;
                }

                if (info.Width <= 0 || info.Height <= 0)
                {
                    SmartLogger.Log(LogLevel.Error, $"[{rendererName}]", "Cannot render with invalid canvas dimensions");
                    return false;
                }

                return true;
            }, false, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.ValidateRenderParameters",
                ErrorMessage = "Failed to validate render parameters"
            }).Result;

        /// <summary>
        /// Проверяет, находится ли область рендеринга в пределах видимости
        /// </summary>
        protected bool IsRenderAreaVisible(SKCanvas canvas, float x, float y, float width, float height) =>
            !canvas.QuickReject(new SKRect(x, y, x + width, y + height));

        /// <summary>
        /// Асинхронно обрабатывает спектр
        /// </summary>
        protected async Task ProcessSpectrumAsync(float[] spectrum, int targetCount, int spectrumLength, CancellationToken cancellationToken = default) =>
            await SmartLogger.SafeAsync(async () =>
            {
                await Task.Run(() =>
                {
                    float[] scaledSpectrum = ScaleSpectrum(spectrum, targetCount, spectrumLength);
                    _processedSpectrum = SmoothSpectrum(scaledSpectrum, targetCount);
                }, cancellationToken).ConfigureAwait(false);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.ProcessSpectrumAsync",
                ErrorMessage = "Error processing spectrum asynchronously"
            });
        #endregion

        #region Paint Utilities
        /// <summary>
        /// Обновляет настройки качества для краски
        /// </summary>
        protected void UpdatePaintQuality(SKPaint paint)
        {
            if (paint == null) return;

            paint.IsAntialias = _useAntiAlias;
            paint.FilterQuality = _filterQuality;
        }

        /// <summary>
        /// Создает краску с эффектом свечения
        /// </summary>
        protected SKPaint CreateGlowPaint(SKPaint basePaint, float blurRadius, byte alpha) =>
            SmartLogger.Safe(() =>
            {
                var glowPaint = _paintPool.GetFrom(basePaint);
                glowPaint.IsAntialias = _useAntiAlias;
                glowPaint.FilterQuality = _filterQuality;
                glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);
                glowPaint.Color = basePaint.Color.WithAlpha(alpha);
                return glowPaint;
            }, null, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.CreateGlowPaint",
                ErrorMessage = "Failed to create glow paint"
            }).Result;

        /// <summary>
        /// Создает краску для контура
        /// </summary>
        protected SKPaint CreateOutlinePaint(SKPaint basePaint, byte alpha, float strokeWidth) =>
            SmartLogger.Safe(() =>
            {
                var outlinePaint = _paintPool.GetFrom(basePaint);
                outlinePaint.IsAntialias = _useAntiAlias;
                outlinePaint.FilterQuality = _filterQuality;
                outlinePaint.Color = basePaint.Color.WithAlpha(alpha);
                outlinePaint.StrokeWidth = strokeWidth;
                outlinePaint.Style = SKPaintStyle.Stroke;
                return outlinePaint;
            }, null, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.CreateOutlinePaint",
                ErrorMessage = "Failed to create outline paint"
            }).Result;

        /// <summary>
        /// Создает краску для заливки
        /// </summary>
        protected SKPaint CreateFillPaint(SKPaint basePaint, byte alpha) =>
            SmartLogger.Safe(() =>
            {
                var fillPaint = _paintPool.GetFrom(basePaint);
                fillPaint.IsAntialias = _useAntiAlias;
                fillPaint.FilterQuality = _filterQuality;
                fillPaint.Style = SKPaintStyle.Fill;
                fillPaint.Color = basePaint.Color.WithAlpha(alpha);
                return fillPaint;
            }, null, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.CreateFillPaint",
                ErrorMessage = "Failed to create fill paint"
            }).Result;

        /// <summary>
        /// Класс для пулинга объектов SKPaint
        /// </summary>
        protected record SKPaintPool(int capacity) : IDisposable
        {
            private bool _disposed;

            // Просто создаем новую краску вместо использования пула
            public SKPaint Get() => new();

            // Просто создаем новую краску и копируем свойства
            public SKPaint GetFrom(SKPaint basePaint) =>
                SmartLogger.Safe(() =>
                {
                    if (basePaint == null)
                        return new SKPaint();

                    return new SKPaint
                    {
                        Color = basePaint.Color,
                        Style = basePaint.Style,
                        StrokeWidth = basePaint.StrokeWidth,
                        StrokeCap = basePaint.StrokeCap,
                        StrokeJoin = basePaint.StrokeJoin,
                        StrokeMiter = basePaint.StrokeMiter,
                        IsAntialias = basePaint.IsAntialias,
                        FilterQuality = basePaint.FilterQuality,
                        TextSize = basePaint.TextSize,
                        TextAlign = basePaint.TextAlign,
                        Typeface = basePaint.Typeface
                    };
                }, new SKPaint(), new SmartLogger.ErrorHandlingOptions
                {
                    Source = "SKPaintPool.GetFrom",
                    ErrorMessage = "Failed to copy paint properties"
                }).Result;

            // Просто освобождаем краску
            public void Return(SKPaint paint) => SmartLogger.SafeDispose(paint, "SKPaint", new SmartLogger.ErrorHandlingOptions
            {
                Source = "SKPaintPool.Return",
                ErrorMessage = "Failed to dispose paint"
            });

            public void Dispose() => _disposed = true;
        }
        #endregion

        #region Drawing Utilities
        /// <summary>
        /// Рисует стандартную информацию о производительности
        /// </summary>
        protected void DrawDefaultPerformanceInfo(
            SKCanvas canvas,
            SKImageInfo info,
            string text = "Performance Info",
            float fontSize = 14,
            SKColor? color = null) => SmartLogger.Safe(() =>
            {
                using var paint = new SKPaint
                {
                    Color = color ?? SKColors.White,
                    IsAntialias = _useAntiAlias,
                    FilterQuality = _filterQuality,
                    TextSize = fontSize
                };

                canvas.DrawText(text, 10, info.Height - fontSize - 10, paint);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.DrawDefaultPerformanceInfo",
                ErrorMessage = "Failed to draw performance info"
            });

        /// <summary>
        /// Рисует градиентный эффект на пути
        /// </summary>
        protected void DrawGradientPath(
            SKCanvas canvas,
            SKPath path,
            SKPoint start,
            SKPoint end,
            SKColor startColor,
            SKColor endColor,
            SKPaintStyle style = SKPaintStyle.Fill) => SmartLogger.Safe(() =>
            {
                var paint = _paintPool.Get();
                paint.IsAntialias = _useAntiAlias;
                paint.FilterQuality = _filterQuality;
                paint.Style = style;
                paint.Shader = SKShader.CreateLinearGradient(
                    start,
                    end,
                    new[] { startColor, endColor },
                    null,
                    SKShaderTileMode.Clamp);

                canvas.DrawPath(path, paint);
                _paintPool.Return(paint);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.DrawGradientPath",
                ErrorMessage = "Failed to draw gradient path"
            });

        /// <summary>
        /// Рисует круговой градиент
        /// </summary>
        protected void DrawRadialGradient(
            SKCanvas canvas,
            SKPoint center,
            float radius,
            SKColor centerColor,
            SKColor edgeColor) => SmartLogger.Safe(() =>
            {
                using var paint = _paintPool.Get();
                paint.IsAntialias = _useAntiAlias;
                paint.FilterQuality = _filterQuality;
                paint.Style = SKPaintStyle.Fill;
                paint.Shader = SKShader.CreateRadialGradient(
                    center,
                    radius,
                    new[] { centerColor, edgeColor },
                    null,
                    SKShaderTileMode.Clamp);

                canvas.DrawCircle(center.X, center.Y, radius, paint);
                _paintPool.Return(paint);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.DrawRadialGradient",
                ErrorMessage = "Failed to draw radial gradient"
            });
        #endregion

        #region Quality Settings
        protected virtual void ApplyQualitySettings() => SmartLogger.Safe(() =>
        {
            (_useAntiAlias, _filterQuality, _useAdvancedEffects) = _quality switch
            {
                RenderQuality.Low => (false, SKFilterQuality.Low, false),
                RenderQuality.Medium => (true, SKFilterQuality.Medium, true),
                RenderQuality.High => (true, SKFilterQuality.High, true),
                _ => (true, SKFilterQuality.Medium, true)
            };
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{GetType().Name}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
        #endregion

        #region Animation Utilities
        /// <summary>
        /// Типы функций плавности для анимаций
        /// </summary>
        protected enum EasingType
        {
            Linear,
            EaseIn,
            EaseOut,
            EaseInOut
        }

        /// <summary>
        /// Вычисляет плавное значение для анимации
        /// </summary>
        protected float CalculateEasedValue(float start, float end, float progress, EasingType easingType = EasingType.EaseInOut) =>
            SmartLogger.Safe(() =>
            {
                float t = Math.Clamp(progress, 0, 1);

                return easingType switch
                {
                    EasingType.Linear => start + (end - start) * t,
                    EasingType.EaseIn => start + (end - start) * (t * t),
                    EasingType.EaseOut => start + (end - start) * (1 - (1 - t) * (1 - t)),
                    EasingType.EaseInOut => start + (end - start) * (t < 0.5f
                        ? 2 * t * t
                        : 1 - 2 * (1 - t) * (1 - t)),
                    _ => start + (end - start) * t
                };
            }, start, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.CalculateEasedValue",
                ErrorMessage = "Failed to calculate eased value"
            }).Result;
        #endregion

        #region Disposal
        public virtual void Dispose() => SmartLogger.Safe(() =>
        {
            if (!_disposed)
            {
                SmartLogger.SafeDispose(_spectrumSemaphore, "SpectrumSemaphore", new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{GetType().Name}.Dispose",
                    ErrorMessage = "Failed to dispose spectrum semaphore"
                });

                SmartLogger.SafeDispose(_paintPool, "PaintPool", new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{GetType().Name}.Dispose",
                    ErrorMessage = "Failed to dispose paint pool"
                });

                _previousSpectrum = null;
                _processedSpectrum = null;
                _disposed = true;
                SmartLogger.Log(LogLevel.Debug, $"[{GetType().Name}]", "Disposed");
            }
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{GetType().Name}.Dispose",
            ErrorMessage = "Failed to dispose renderer"
        });
        #endregion
    }
}