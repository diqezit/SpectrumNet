namespace SpectrumNet
{
    public sealed class Speaker3DRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields

        private static readonly Lazy<Speaker3DRenderer> _instance = new(() => new Speaker3DRenderer());
        private bool _isInitialized;
        private bool _disposed;
        private float _smoothingFactor = 0.3f;
        private float[]? _previousSpectrum;

        // Constants
        private const float SCALE = 2f; // Scaling factor
        private const float STROKE_SCALE = SCALE / 2; // Scaling for stroke widths

        // Realistic proportions scaled by SCALE
        private const float RADIUS_X = 100f * SCALE;
        private const float RADIUS_Y = 20f * SCALE;
        private const float DEFLECTION = 1f * SCALE; // Scaled deflection
        private const int RINGS = 4; // Number of texture rings on the diffuser
        private const float CAP_RADIUS_RATIO = 0.2f; // Central cap radius ratio

        private Vector2 _center;

        #endregion

        #region Constructor

        private Speaker3DRenderer() { }

        #endregion

        #region Public Methods

        public static Speaker3DRenderer GetInstance() => _instance.Value;

        public void Initialize()
        {
            if (_isInitialized || _disposed) return;
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive) => _smoothingFactor = isOverlayActive ? 0.6f : 0.3f;

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                          float unused1, float unused2, int unused3,
                          SKPaint? paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || _disposed || canvas == null || spectrum == null || paint == null) return;

            _center = new Vector2(info.Width / 2f, info.Height / 2f);

            float[] smoothedSpectrum = SmoothValue(spectrum, ref _previousSpectrum);
            float loudness = CalculateLoudness(smoothedSpectrum);
            float deflection = -(float)(Math.Pow(loudness, 1.3) * DEFLECTION);

            RenderSpeaker(canvas, deflection);
            drawPerformanceInfo(canvas, info);
        }

        private static float CalculateLoudness(ReadOnlySpan<float> spectrum)
        {
            if (spectrum.Length == 0) return 0f;

            int subBassRange = spectrum.Length / 16;
            int bassRange = spectrum.Length / 8;
            int midRange = spectrum.Length / 4;

            float subBassSum = 0f;
            float bassSum = 0f;
            float midSum = 0f;
            float highSum = 0f;

            for (int i = 0; i < subBassRange; i++)
                subBassSum += Math.Abs(spectrum[i]) * 1.4f;

            for (int i = subBassRange; i < bassRange; i++)
                bassSum += Math.Abs(spectrum[i]) * 1.2f;

            for (int i = bassRange; i < midRange; i++)
                midSum += Math.Abs(spectrum[i]);

            for (int i = midRange; i < spectrum.Length; i++)
                highSum += Math.Abs(spectrum[i]) * 0.8f;

            float weightedSum = (subBassSum / subBassRange * 0.4f) +
                               (bassSum / (bassRange - subBassRange) * 0.3f) +
                               (midSum / (midRange - bassRange) * 0.2f) +
                               (highSum / (spectrum.Length - midRange) * 0.1f);

            return Math.Clamp(weightedSum * 3.5f, 0f, 1f);
        }

        private float[] SmoothValue(float[] current, ref float[] previous)
        {
            previous ??= new float[current.Length];
            float adaptiveSmoothingFactor = _smoothingFactor *
                (1f + (float)Math.Pow(CalculateLoudness(current), 2) * 0.3f);

            for (int i = 0; i < current.Length; i++)
                previous[i] += (current[i] - previous[i]) * adaptiveSmoothingFactor;

            return previous;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _previousSpectrum = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private Methods

        private void RenderSpeaker(SKCanvas canvas, float deflection)
        {
            DrawSpeakerBody(canvas);
            DrawOuterFrame(canvas);
            DrawDiffuser(canvas, deflection);
            DrawCentralCap(canvas, deflection);
            DrawMagnet(canvas);
        }

        private void DrawOuterFrame(SKCanvas canvas)
        {
            // Основной контур с более точной перспективой
            var suspensionRect = new SKRect(
                _center.X - RADIUS_X,
                _center.Y - RADIUS_Y * 0.99f,
                _center.X + RADIUS_X,
                _center.Y + RADIUS_Y * 1.01f
            );

            // Улучшенный металлический градиент рамки
            using (var suspensionPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(suspensionRect.Left, suspensionRect.Top),
                    new SKPoint(suspensionRect.Right, suspensionRect.Bottom),
                    new SKColor[]
                    {
                SKColors.Silver.WithAlpha(255),
                SKColors.White.WithAlpha(245),
                SKColors.LightGray.WithAlpha(240),
                SKColors.Silver.WithAlpha(235),
                SKColors.Gray.WithAlpha(230)
                    },
                    new float[] { 0.0f, 0.25f, 0.5f, 0.75f, 1.0f },
                    SKShaderTileMode.Clamp
                )
            })
            {
                canvas.DrawOval(suspensionRect, suspensionPaint);
            }

            // Более тонкая внутренняя окантовка
            var innerRect = new SKRect(
                suspensionRect.Left + 2.5f * STROKE_SCALE,
                suspensionRect.Top + 2.5f * STROKE_SCALE,
                suspensionRect.Right - 2.5f * STROKE_SCALE,
                suspensionRect.Bottom - 2.5f * STROKE_SCALE
            );

            using (var framePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f * STROKE_SCALE,
                Color = SKColors.Gray.WithAlpha(140)
            })
            {
                canvas.DrawOval(innerRect, framePaint);
            }

            // Более естественный блик
            using (var highlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f * STROKE_SCALE,
                Color = SKColors.White.WithAlpha(35)
            })
            {
                canvas.DrawArc(suspensionRect, 180, 180, false, highlightPaint);
            }
        }

        private void DrawDiffuser(SKCanvas canvas, float deflection)
        {
            float loudness = Math.Abs(deflection / DEFLECTION);
            float deformationFactor = 1f + (loudness * 0.08f);

            var diffuserRect = new SKRect(
                _center.X - RADIUS_X * 0.94f,
                _center.Y - (RADIUS_Y * 0.92f * deformationFactor) + deflection,
                _center.X + RADIUS_X * 0.94f,
                _center.Y + (RADIUS_Y * 0.92f / deformationFactor) + deflection
            );

            // Основной диффузор
            using (var diffuserPaint = new SKPaint { IsAntialias = true })
            {
                var gradientColors = new SKColor[] {
            new SKColor(85, 85, 85, 255),  // Светло-серый
            new SKColor(75, 75, 75, 252),  // Серый
            new SKColor(65, 65, 65, 250),  // Темно-серый
            new SKColor(55, 55, 55, 248),  // Еще темнее
            new SKColor(45, 45, 45, 245),  // Почти черный
            new SKColor(35, 35, 35, 242),  // Глубокий черный
            new SKColor(25, 25, 25, 240)   // Самый темный
        };

                var gradientPositions = new float[] {
            0.0f, 0.2f, 0.4f, 0.6f, 0.7f, 0.85f, 1.0f
        };

                using (var gradient = SKShader.CreateRadialGradient(
                    new SKPoint(_center.X, _center.Y + deflection),
                    RADIUS_X * (0.94f + loudness * 0.06f),
                    gradientColors,
                    gradientPositions,
                    SKShaderTileMode.Clamp))
                {
                    diffuserPaint.Shader = gradient;
                    canvas.DrawOval(diffuserRect, diffuserPaint);
                }
            }

            // Кольца диффузора
            int baseRings = 18;
            int dynamicRings = (int)(baseRings + (loudness * 4));
            float baseAlpha = 65f;

            using (var ringPaint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true })
            {
                for (int i = 1; i <= dynamicRings; i++)
                {
                    float ratio = (float)i / (dynamicRings + 1);
                    float ringDeformation = 1f + (loudness * 0.05f * ratio);
                    float intensityFactor = 1f - (float)Math.Pow(ratio - 0.5f, 2) * 2;

                    var ringRect = new SKRect(
                        _center.X - (RADIUS_X * 0.94f * ratio),
                        _center.Y - (RADIUS_Y * 0.92f * ratio * ringDeformation) + deflection,
                        _center.X + (RADIUS_X * 0.94f * ratio),
                        _center.Y + (RADIUS_Y * 0.92f * ratio / ringDeformation) + deflection
                    );

                    // Основные кольца
                    ringPaint.Color = new SKColor(0, 0, 0,
                        (byte)(baseAlpha + (loudness * 45 * intensityFactor)));
                    ringPaint.StrokeWidth = 0.7f * STROKE_SCALE *
                                          (1f + loudness * 0.15f * intensityFactor);
                    canvas.DrawOval(ringRect, ringPaint);

                    // Блики на кольцах при высокой громкости
                    if (loudness > 0.6f && i % 2 == 0)
                    {
                        ringPaint.Color = new SKColor(255, 255, 255,
                            (byte)(18 * intensityFactor * (loudness - 0.6f) / 0.4f));
                        canvas.DrawOval(ringRect, ringPaint);
                    }
                }
            }

            // Эффекты глубины и теней
            using (var shadowPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.0f * STROKE_SCALE,
            })
            {
                // Нижняя тень
                shadowPaint.Color = new SKColor(0, 0, 0, (byte)(35 + loudness * 20));
                canvas.DrawArc(diffuserRect, -45, 270, false, shadowPaint);

                // Верхний блик
                shadowPaint.Color = new SKColor(255, 255, 255, (byte)(15 + loudness * 10));
                canvas.DrawArc(diffuserRect, 135, 270, false, shadowPaint);
            }

            // Дополнительный эффект объема при высокой громкости
            if (loudness > 0.7f)
            {
                using (var volumePaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.5f * STROKE_SCALE,
                    Color = new SKColor(255, 255, 255, (byte)(20 * (loudness - 0.7f) / 0.3f))
                })
                {
                    canvas.DrawOval(diffuserRect, volumePaint);
                }
            }
        }

        private void DrawCentralCap(SKCanvas canvas, float deflection)
        {
            float capRadiusX = RADIUS_X * 0.12f;
            float capRadiusY = RADIUS_Y * 0.12f;
            float loudness = Math.Abs(deflection / DEFLECTION);

            var capRect = new SKRect(
                _center.X - capRadiusX,
                _center.Y - capRadiusY * 0.99f + deflection,
                _center.X + capRadiusX,
                _center.Y + capRadiusY * 1.01f + deflection
            );

            using (var capPaint = new SKPaint { IsAntialias = true })
            {
                var capGradientColors = new SKColor[] {
            new SKColor(100, 100, 100, 255),
            new SKColor(80, 80, 80, 250),
            new SKColor(60, 60, 60, 245),
            new SKColor(40, 40, 40, 240),
            new SKColor(20, 20, 20, 235)
        };
                var capGradientPositions = new float[] { 0.0f, 0.3f, 0.5f, 0.7f, 1.0f };

                using (var capGradient = SKShader.CreateRadialGradient(
                    new SKPoint(_center.X, _center.Y + deflection),
                    capRadiusX,
                    capGradientColors,
                    capGradientPositions,
                    SKShaderTileMode.Clamp
                ))
                {
                    capPaint.Shader = capGradient;
                    canvas.DrawOval(capRect, capPaint);
                }

                // Добавляем металлическую текстуру
                using (var textureShader = SKShader.CreatePerlinNoiseFractalNoise(
                    0.8f, 0.8f, 3, 0.0f))
                {
                    capPaint.Shader = SKShader.CreateCompose(
                        capPaint.Shader,
                        textureShader,
                        SKBlendMode.Overlay);
                    canvas.DrawOval(capRect, capPaint);
                }
            }

            // Основной блик
            using (var highlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.8f * STROKE_SCALE,
            })
            {
                highlightPaint.Color = new SKColor(255, 255, 255, 30);
                canvas.DrawArc(capRect, 180, 180, false, highlightPaint);
            }

            // Окантовка и тени
            using (var ringPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.8f * STROKE_SCALE,
                IsAntialias = true
            })
            {
                // Основная окантовка с металлическим эффектом
                var metalGradient = SKShader.CreateLinearGradient(
                    new SKPoint(capRect.Left, capRect.Top),
                    new SKPoint(capRect.Right, capRect.Bottom),
                    new SKColor[] {
                new SKColor(40, 40, 40, 180),
                new SKColor(100, 100, 100, 160),
                new SKColor(40, 40, 40, 180)
                    },
                    new float[] { 0.0f, 0.5f, 1.0f },
                    SKShaderTileMode.Clamp
                );

                ringPaint.Shader = metalGradient;
                canvas.DrawOval(capRect, ringPaint);

                // Динамическая нижняя тень
                float shadowIntensity = 50 + (loudness * 30);
                ringPaint.Shader = null;
                ringPaint.Color = new SKColor(0, 0, 0, (byte)shadowIntensity);
                canvas.DrawArc(capRect, -30, 240, false, ringPaint);

                // Динамический верхний блик
                float highlightIntensity = 30 + (loudness * 20);
                ringPaint.Color = new SKColor(255, 255, 255, (byte)highlightIntensity);
                canvas.DrawArc(capRect, 150, 240, false, ringPaint);
            }

            // Внутренние детали колпачка с улучшенной глубиной
            var innerCapRect = new SKRect(
                capRect.Left + 1.8f * STROKE_SCALE,
                capRect.Top + 1.8f * STROKE_SCALE,
                capRect.Right - 1.8f * STROKE_SCALE,
                capRect.Bottom - 1.8f * STROKE_SCALE
            );

            // Внутренний круг с градиентом
            using (var innerRingPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.6f * STROKE_SCALE,
            })
            {
                var innerGradient = SKShader.CreateLinearGradient(
                    new SKPoint(innerCapRect.Left, innerCapRect.Top),
                    new SKPoint(innerCapRect.Right, innerCapRect.Bottom),
                    new SKColor[] {
                new SKColor(60, 60, 60, 90),
                new SKColor(20, 20, 20, 80),
                new SKColor(60, 60, 60, 90)
                    },
                    new float[] { 0.0f, 0.5f, 1.0f },
                    SKShaderTileMode.Clamp
                );

                innerRingPaint.Shader = innerGradient;
                canvas.DrawOval(innerCapRect, innerRingPaint);

                // Динамическая внутренняя тень
                innerRingPaint.Shader = null;
                innerRingPaint.Color = new SKColor(0, 0, 0, (byte)(40 + loudness * 20));
                canvas.DrawArc(innerCapRect, -30, 240, false, innerRingPaint);
            }

            // Улучшенная центральная точка с градиентом
            using (var centerPaint = new SKPaint { IsAntialias = true })
            {
                var centerGradient = SKShader.CreateRadialGradient(
                    new SKPoint(_center.X, _center.Y + deflection),
                    1.0f * STROKE_SCALE,
                    new SKColor[] {
                new SKColor(60, 60, 60, 150),
                new SKColor(20, 20, 20, 180),
                new SKColor(0, 0, 0, 200)
                    },
                    new float[] { 0.0f, 0.5f, 1.0f },
                    SKShaderTileMode.Clamp
                );

                centerPaint.Shader = centerGradient;
                canvas.DrawCircle(
                    _center.X,
                    _center.Y + deflection,
                    0.8f * STROKE_SCALE,
                    centerPaint
                );
            }

            // Дополнительные динамические блики
            using (var detailPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.5f * STROKE_SCALE,
            })
            {
                // Верхний динамический блик
                float topHighlightIntensity = 20 + (loudness * 15);
                detailPaint.Color = new SKColor(255, 255, 255, (byte)topHighlightIntensity);
                canvas.DrawArc(
                    innerCapRect,
                    160,
                    220,
                    false,
                    detailPaint
                );

                // Нижняя динамическая тень
                float bottomShadowIntensity = 30 + (loudness * 20);
                detailPaint.Color = new SKColor(0, 0, 0, (byte)bottomShadowIntensity);
                canvas.DrawArc(
                    innerCapRect,
                    -20,
                    220,
                    false,
                    detailPaint
                );
            }
        }

        private void DrawSpeakerBody(SKCanvas canvas)
        {
            float topY = _center.Y;
            float bottomY = _center.Y + RADIUS_Y * 1.3f;
            float midY = topY + (bottomY - topY) * 0.35f; // Изменено с 0.4f на 0.35f

            float[] xRatios = new float[] { 1.0f, 0.96f, 0.88f, 0.35f }; // Скорректированы для лучшей перспективы
            float topCompression = 0.985f;    // Уменьшено сжатие сверху
            float bottomExpansion = 1.015f;   // Уменьшено расширение снизу

            using var path = new SKPath();

            var leftPoints = new List<SKPoint>
    {
        new(_center.X - RADIUS_X * xRatios[0], topY * topCompression),
        new(_center.X - RADIUS_X * xRatios[1], (topY + (midY - topY) * 0.3f)),
        new(_center.X - RADIUS_X * xRatios[2], midY),
        new(_center.X - RADIUS_X * xRatios[3], bottomY * bottomExpansion)
    };

            var rightPoints = new List<SKPoint>
    {
        new(_center.X + RADIUS_X * xRatios[0], topY * topCompression),
        new(_center.X + RADIUS_X * xRatios[1], (topY + (midY - topY) * 0.3f)),
        new(_center.X + RADIUS_X * xRatios[2], midY),
        new(_center.X + RADIUS_X * xRatios[3], bottomY * bottomExpansion)
    };

            path.MoveTo(leftPoints[0]);
            for (int i = 0; i < leftPoints.Count - 1; i++)
            {
                var current = leftPoints[i];
                var next = leftPoints[i + 1];
                var controlPoint1 = new SKPoint(
                    current.X + (next.X - current.X) * 0.2f, // Увеличено с 0.15f для более плавного изгиба
                    current.Y + (next.Y - current.Y) * 0.45f // Уменьшено с 0.5f для лучшей перспективы
                );
                var controlPoint2 = new SKPoint(
                    next.X - (next.X - current.X) * 0.15f,
                    next.Y - (next.Y - current.Y) * 0.5f
                );
                path.CubicTo(controlPoint1, controlPoint2, next);
            }

            path.LineTo(rightPoints[rightPoints.Count - 1]);

            for (int i = rightPoints.Count - 2; i >= 0; i--)
            {
                var current = rightPoints[i + 1];
                var next = rightPoints[i];
                var controlPoint1 = new SKPoint(
                    current.X + (next.X - current.X) * 0.2f, // Увеличено с 0.15f для более плавного изгиба
                    current.Y + (next.Y - current.Y) * 0.45f // Уменьшено с 0.5f для лучшей перспективы
                );
                var controlPoint2 = new SKPoint(
                    next.X + (current.X - next.X) * 0.15f,
                    next.Y + (current.Y - next.Y) * 0.5f
                );
                path.CubicTo(controlPoint1, controlPoint2, next);
            }

            path.Close();

            // Основной градиент корпуса
            using (var bodyPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill })
            {
                var gradientColors = new SKColor[]
                {
            new SKColor(240, 240, 240, 255),
            new SKColor(220, 220, 220, 255),
            new SKColor(200, 200, 200, 255),
            new SKColor(180, 180, 180, 255),
            new SKColor(200, 200, 200, 255),
            new SKColor(220, 220, 220, 255),
            new SKColor(240, 240, 240, 255)
                };

                var gradientPositions = new float[] { 0.0f, 0.2f, 0.4f, 0.5f, 0.6f, 0.8f, 1.0f };

                using (var gradient = SKShader.CreateLinearGradient(
                    new SKPoint(_center.X - RADIUS_X, topY * topCompression),
                    new SKPoint(_center.X + RADIUS_X, bottomY * bottomExpansion),
                    gradientColors,
                    gradientPositions,
                    SKShaderTileMode.Clamp))
                {
                    bodyPaint.Shader = gradient;
                    canvas.DrawPath(path, bodyPaint);
                }

                // Текстура металла
                using (var textureShader = SKShader.CreatePerlinNoiseFractalNoise(
                    0.7f, 0.7f, 3, 0.0f))
                {
                    bodyPaint.Shader = SKShader.CreateCompose(
                        bodyPaint.Shader,
                        textureShader,
                        SKBlendMode.Overlay);
                    canvas.DrawPath(path, bodyPaint);
                }
            }

            // Эффекты теней и бликов
            using (var effectPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.0f * STROKE_SCALE
            })
            {
                // Верхний блик
                effectPaint.Color = new SKColor(255, 255, 255, 45);
                canvas.DrawLine(leftPoints[0].X, leftPoints[0].Y,
                               rightPoints[0].X, rightPoints[0].Y,
                               effectPaint);

                // Боковые эффекты
                for (int i = 0; i < leftPoints.Count - 1; i++)
                {
                    float alpha = (float)(35 - i * 7);

                    // Левая сторона
                    effectPaint.Color = new SKColor(255, 255, 255, (byte)alpha);
                    canvas.DrawLine(leftPoints[i].X, leftPoints[i].Y,
                                  leftPoints[i + 1].X, leftPoints[i + 1].Y,
                                  effectPaint);

                    // Правая сторона
                    effectPaint.Color = new SKColor(0, 0, 0, (byte)(alpha * 0.7f));
                    canvas.DrawLine(rightPoints[i].X, rightPoints[i].Y,
                                  rightPoints[i + 1].X, rightPoints[i + 1].Y,
                                  effectPaint);
                }

                // Горизонтальные тени
                for (int i = 1; i < leftPoints.Count - 1; i++)
                {
                    effectPaint.Color = new SKColor(0, 0, 0, (byte)(15 + i * 12));
                    canvas.DrawLine(leftPoints[i].X, leftPoints[i].Y,
                                  rightPoints[i].X, rightPoints[i].Y,
                                  effectPaint);
                }
            }

            // Металлическая окантовка
            using (var edgePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.8f * STROKE_SCALE
            })
            {
                var edgeGradient = SKShader.CreateLinearGradient(
                    new SKPoint(_center.X - RADIUS_X, topY),
                    new SKPoint(_center.X + RADIUS_X, bottomY),
                    new SKColor[]
                    {
                new SKColor(180, 180, 180, 120),
                new SKColor(140, 140, 140, 140),
                new SKColor(100, 100, 100, 160)
                    },
                new float[] { 0.0f, 0.5f, 1.0f },
                SKShaderTileMode.Clamp
                    );

                edgePaint.Shader = edgeGradient;
                canvas.DrawPath(path, edgePaint);

                // Дополнительные эффекты глубины
                using (var depthPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.6f * STROKE_SCALE
                })
                {
                    // Внутренняя тень
                    depthPaint.Color = new SKColor(0, 0, 0, 40);
                    canvas.DrawPath(path, depthPaint);

                    // Внешний блик
                    depthPaint.Color = new SKColor(255, 255, 255, 25);
                    depthPaint.StrokeWidth = 0.4f * STROKE_SCALE;
                    canvas.DrawPath(path, depthPaint);
                }
            }

            // Дополнительные детали корпуса
            using (var detailPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.5f * STROKE_SCALE
            })
            {
                // Верхние блики
                detailPaint.Color = new SKColor(255, 255, 255, 30);
                canvas.DrawLine(
                    leftPoints[0].X + RADIUS_X * 0.1f, leftPoints[0].Y + RADIUS_Y * 0.05f,
                    rightPoints[0].X - RADIUS_X * 0.1f, rightPoints[0].Y + RADIUS_Y * 0.05f,
                    detailPaint
                );

                // Боковые акценты
                for (int i = 0; i < 3; i++)
                {
                    float y = topY + (bottomY - topY) * (0.3f + i * 0.2f);
                    float xOffset = RADIUS_X * (0.8f - i * 0.2f);

                    detailPaint.Color = new SKColor(0, 0, 0, (byte)(20 + i * 10));
                    canvas.DrawLine(
                        _center.X - xOffset, y,
                        _center.X + xOffset, y,
                        detailPaint
                    );
                }
            }

            // Финальные штрихи
            using (var finalPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.3f * STROKE_SCALE
            })
            {
                // Тонкие линии для создания эффекта металлической поверхности
                finalPaint.Color = new SKColor(255, 255, 255, 15);
                for (int i = 0; i < 4; i++)
                {
                    float y = topY + (bottomY - topY) * (0.2f + i * 0.2f);
                    canvas.DrawLine(
                        _center.X - RADIUS_X * 0.9f, y,
                        _center.X + RADIUS_X * 0.9f, y,
                        finalPaint
                    );
                }

                // Дополнительные вертикальные акценты
                finalPaint.Color = new SKColor(0, 0, 0, 20);
                canvas.DrawLine(
                    _center.X - RADIUS_X * 0.5f, topY,
                    _center.X - RADIUS_X * 0.2f, bottomY,
                    finalPaint
                );
                canvas.DrawLine(
                    _center.X + RADIUS_X * 0.5f, topY,
                    _center.X + RADIUS_X * 0.2f, bottomY,
                    finalPaint
                );
            }
        }

        private void DrawMagnet(SKCanvas canvas)
        {
            float magnetHeight = RADIUS_Y * 0.18f; // Уменьшено с 0.2f
            float magnetWidth = RADIUS_X * 0.82f;  // Уменьшено с 0.85f
            float magnetBottomOffset = RADIUS_Y * 1.25f; // Уменьшено с 1.28f
            float cornerRadius = magnetHeight * 0.15f;

            // Корректируем коэффициенты перспективы для соответствия корпусу
            float topCompression = 0.94f;    // Изменено с 0.92f
            float bottomExpansion = 1.06f;   // Изменено с 1.08f

            using (var clipPath = new SKPath())
            {
                float topY = _center.Y;
                float bottomY = _center.Y + RADIUS_Y * 1.3f;

                // Корректируем точки отсечения для соответствия перспективе корпуса
                float p1X = _center.X - RADIUS_X * 1.1f;
                float p2X = _center.X + RADIUS_X * 1.1f;
                float p3X = _center.X - RADIUS_X * 0.39f;  // Изменено с 0.42f
                float p4X = _center.X + RADIUS_X * 0.39f;  // Изменено с 0.42f

                // Создаем трапециевидный путь отсечения с учетом перспективы
                clipPath.MoveTo(p1X, topY);
                clipPath.LineTo(p2X, topY);

                // Добавляем небольшую кривизну для более плавного перехода
                var control1 = new SKPoint(p2X, (topY + bottomY) * 0.5f);
                var control2 = new SKPoint(p4X, bottomY - RADIUS_Y * 0.1f);
                clipPath.QuadTo(control1, new SKPoint(p4X, bottomY));

                clipPath.LineTo(p3X, bottomY);

                control1 = new SKPoint(p1X, (topY + bottomY) * 0.5f);
                control2 = new SKPoint(p3X, bottomY - RADIUS_Y * 0.1f);
                clipPath.QuadTo(control1, new SKPoint(p1X, topY));

                clipPath.Close();

                canvas.Save();
                clipPath.FillType = SKPathFillType.InverseWinding;
                canvas.ClipPath(clipPath);

                // Создаем эллипс с учетом перспективы
                float perspectiveScale = 0.88f; // Увеличено с 0.85f
                SKRect magnetRect = new SKRect(
                    _center.X - magnetWidth / 2,
                    (_center.Y + magnetBottomOffset - magnetHeight * 0.1f) * topCompression,
                    _center.X + magnetWidth / 2,
                    (_center.Y + magnetBottomOffset + magnetHeight * 0.9f) * bottomExpansion
                );

                // Применяем перспективное преобразование
                magnetRect = new SKRect(
                    magnetRect.Left,
                    magnetRect.Top,
                    magnetRect.Right,
                    magnetRect.Top + (magnetRect.Height * perspectiveScale)
                );

                // Улучшенная фоновая тень с учетом перспективы
                using (var backgroundShadowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Shader = SKShader.CreateRadialGradient(
                        new SKPoint(magnetRect.MidX, magnetRect.MidY),
                        magnetWidth * 0.6f,
                        new SKColor[] {
                    new SKColor(0, 0, 0, 45),
                    new SKColor(0, 0, 0, 20),
                    SKColors.Transparent
                        },
                        new float[] { 0.0f, 0.6f, 1.0f },
                        SKShaderTileMode.Clamp
                    )
                })
                {
                    SKRect shadowRect = magnetRect;
                    shadowRect.Offset(1.2f * STROKE_SCALE, 1.8f * STROKE_SCALE);
                    shadowRect.Inflate(3.5f * STROKE_SCALE, 1.8f * STROKE_SCALE * perspectiveScale);
                    canvas.DrawOval(shadowRect, backgroundShadowPaint);
                }

                // Основной градиент магнита с учетом перспективы
                using (var magnetPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill })
                {
                    var gradientColors = new SKColor[]
                    {
                new SKColor(100, 100, 100, 255),
                new SKColor(120, 120, 120, 255),
                new SKColor(100, 100, 100, 255),
                new SKColor(80, 80, 80, 255),
                new SKColor(90, 90, 90, 255),
                new SKColor(110, 110, 110, 255),
                new SKColor(90, 90, 90, 255)
                    };

                    var gradientPositions = new float[] { 0.0f, 0.2f, 0.4f, 0.5f, 0.6f, 0.8f, 1.0f };

                    using (var gradient = SKShader.CreateLinearGradient(
                        new SKPoint(magnetRect.Left, magnetRect.Top),
                        new SKPoint(magnetRect.Right, magnetRect.Bottom),
                        gradientColors,
                        gradientPositions,
                        SKShaderTileMode.Clamp))
                    {
                        magnetPaint.Shader = gradient;
                        canvas.DrawOval(magnetRect, magnetPaint);
                    }

                    // Текстура с учетом перспективы
                    using (var textureShader = SKShader.CreatePerlinNoiseFractalNoise(
                        0.8f * perspectiveScale, 0.8f, 4, 0.0f))
                    {
                        magnetPaint.Shader = SKShader.CreateCompose(
                            magnetPaint.Shader,
                            textureShader,
                            SKBlendMode.Overlay);
                        canvas.DrawOval(magnetRect, magnetPaint);
                    }
                }

                // Блики с учетом перспективы
                using (var highlightPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.9f * STROKE_SCALE * perspectiveScale
                })
                {
                    highlightPaint.Color = new SKColor(255, 255, 255, 40);
                    canvas.DrawArc(magnetRect, 160, 220, false, highlightPaint);

                    highlightPaint.Color = new SKColor(255, 255, 255, 25);
                    float sideOffset = cornerRadius * perspectiveScale;
                    canvas.DrawLine(
                        magnetRect.Left + sideOffset,
                        magnetRect.Top + sideOffset * topCompression,
                        magnetRect.Left + sideOffset,
                        magnetRect.Bottom - sideOffset * bottomExpansion,
                        highlightPaint
                    );
                    canvas.DrawLine(
                        magnetRect.Right - sideOffset,
                        magnetRect.Top + sideOffset * topCompression,
                        magnetRect.Right - sideOffset,
                        magnetRect.Bottom - sideOffset * bottomExpansion,
                        highlightPaint
                    );
                }

                // Тени с учетом перспективы
                using (var shadowPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke })
                {
                    shadowPaint.Color = new SKColor(0, 0, 0, 60); // Уменьшено с 70
                    shadowPaint.StrokeWidth = 1.3f * STROKE_SCALE * perspectiveScale; // Уменьшено с 1.5f
                    canvas.DrawArc(magnetRect, -35, 250, false, shadowPaint); // Изменены углы

                    // Корректируем мягкую тень
                    shadowPaint.Color = new SKColor(0, 0, 0, 25); // Уменьшено с 30
                    shadowPaint.StrokeWidth = 2.0f * STROKE_SCALE * perspectiveScale; // Уменьшено с 2.2f
                    canvas.DrawArc(magnetRect, -25, 230, false, shadowPaint); // Изменены углы
                }

                // Окантовка с учетом перспективы
                using (var edgePaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.1f * STROKE_SCALE * perspectiveScale
                })
                {
                    var edgeGradient = SKShader.CreateLinearGradient(
                        new SKPoint(magnetRect.Left, magnetRect.Top),
                        new SKPoint(magnetRect.Right, magnetRect.Bottom),
                        new SKColor[]
                        {
                    new SKColor(140, 140, 140, 100),
                    new SKColor(100, 100, 100, 120),
                    new SKColor(80, 80, 80, 140),
                    new SKColor(100, 100, 100, 120),
                    new SKColor(140, 140, 140, 100)
                        },
                        new float[] { 0.0f, 0.3f, 0.5f, 0.7f, 1.0f },
                        SKShaderTileMode.Clamp
                    );

                    edgePaint.Shader = edgeGradient;
                    canvas.DrawOval(magnetRect, edgePaint);
                }

                // Эффект соединения с корпусом с учетом перспективы
                using (var connectionPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.8f * STROKE_SCALE * perspectiveScale
                })
                {
                    // Верхнее соединение
                    connectionPaint.Color = new SKColor(0, 0, 0, 40);
                    float topOffset = magnetRect.Height * 0.05f;
                    canvas.DrawLine(
                        magnetRect.Left + magnetWidth * 0.1f,
                        magnetRect.Top + topOffset,
                        magnetRect.Right - magnetWidth * 0.1f,
                        magnetRect.Top + topOffset,
                        connectionPaint
                    );

                    // Боковые соединения
                    connectionPaint.Color = new SKColor(0, 0, 0, 30);
                    float sideOffset = magnetWidth * 0.05f;
                    canvas.DrawLine(
                        magnetRect.Left + sideOffset,
                        magnetRect.Top + magnetRect.Height * 0.2f,
                        magnetRect.Left + sideOffset,
                        magnetRect.Bottom - magnetRect.Height * 0.2f,
                        connectionPaint
                    );
                    canvas.DrawLine(
                        magnetRect.Right - sideOffset,
                        magnetRect.Top + magnetRect.Height * 0.2f,
                        magnetRect.Right - sideOffset,
                        magnetRect.Bottom - magnetRect.Height * 0.2f,
                        connectionPaint
                    );
                }

                // Дополнительные детали с учетом перспективы
                using (var detailPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.5f * STROKE_SCALE * perspectiveScale
                })
                {
                    // Горизонтальные линии
                    detailPaint.Color = new SKColor(255, 255, 255, 15);
                    for (int i = 1; i <= 3; i++)
                    {
                        float y = magnetRect.Top + (magnetRect.Height * i / 4);
                        float horizontalScale = 1.0f - (i * 0.05f); // Уменьшаем длину линий к низу
                        canvas.DrawLine(
                            magnetRect.Left + magnetWidth * (0.15f / horizontalScale),
                            y,
                            magnetRect.Right - magnetWidth * (0.15f / horizontalScale),
                            y,
                            detailPaint
                        );
                    }

                    // Угловые акценты
                    detailPaint.Color = new SKColor(0, 0, 0, 25);
                    float accentLength = magnetHeight * 0.3f * perspectiveScale;
                    float cornerOffset = cornerRadius * perspectiveScale;

                    // Верхние углы (немного шире)
                    canvas.DrawLine(
                        magnetRect.Left + cornerOffset,
                        magnetRect.Top + cornerOffset,
                        magnetRect.Left + cornerOffset + accentLength * 1.2f,
                        magnetRect.Top + cornerOffset,
                        detailPaint
                    );
                    canvas.DrawLine(
                        magnetRect.Right - cornerOffset,
                        magnetRect.Top + cornerOffset,
                        magnetRect.Right - cornerOffset - accentLength * 1.2f,
                        magnetRect.Top + cornerOffset,
                        detailPaint
                    );

                    // Нижние углы (немного уже)
                    canvas.DrawLine(
                        magnetRect.Left + cornerOffset,
                        magnetRect.Bottom - cornerOffset,
                        magnetRect.Left + cornerOffset + accentLength * 0.8f,
                        magnetRect.Bottom - cornerOffset,
                        detailPaint
                    );
                    canvas.DrawLine(
                        magnetRect.Right - cornerOffset,
                        magnetRect.Bottom - cornerOffset,
                        magnetRect.Right - cornerOffset - accentLength * 0.8f,
                        magnetRect.Bottom - cornerOffset,
                        detailPaint
                    );
                }

                canvas.Restore();
            }
        }

        #endregion
    }
}