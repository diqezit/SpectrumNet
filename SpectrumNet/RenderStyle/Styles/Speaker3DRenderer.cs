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

        public void Configure(bool isOverlayActive)
        {
            _smoothingFactor = isOverlayActive ? 0.6f : 0.3f;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                          float unused1, float unused2, int unused3,
                          SKPaint? paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || _disposed || canvas == null || spectrum == null || paint == null) return;

            _center = new Vector2(info.Width / 2f, info.Height / 2f);

            float magnitude = ProcessSpectrum(spectrum);
            float deflection = -magnitude * DEFLECTION;

            RenderSpeaker(canvas);
            drawPerformanceInfo(canvas, info);
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

        private void RenderSpeaker(SKCanvas canvas)
        {
            // Draw background elements first
            DrawSpeakerBody(canvas);

            // Draw foreground speaker components on top of the body
            DrawOuterFrame(canvas);
            DrawDiffuser(canvas);
            DrawCentralCap(canvas);

            // Draw magnet on top of everything
            DrawMagnet(canvas);
        }

        private void DrawOuterFrame(SKCanvas canvas)
        {
            // Основной контур
            var suspensionRect = new SKRect(
                _center.X - RADIUS_X,
                _center.Y - RADIUS_Y,
                _center.X + RADIUS_X,
                _center.Y + RADIUS_Y
            );

            // Рамка динамика
            using (var suspensionPaint = new SKPaint
            {
                Color = SKColors.DarkGray,
                Style = SKPaintStyle.Fill, // Закрашиваем полностью
                IsAntialias = true
            })
            {
                canvas.DrawOval(suspensionRect, suspensionPaint);
            }

            // Внутренний обод с градиентом
            var innerRect = new SKRect(
                suspensionRect.Left + 4 * STROKE_SCALE,
                suspensionRect.Top + 4 * STROKE_SCALE,
                suspensionRect.Right - 4 * STROKE_SCALE,
                suspensionRect.Bottom - 4 * STROKE_SCALE
            );

            using (var shadowPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 6 * STROKE_SCALE,
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(_center.X, _center.Y),
                    RADIUS_X,
                    new SKColor[] { SKColors.Black.WithAlpha(80), SKColors.Transparent },
                    new float[] { 0.7f, 1f },
                    SKShaderTileMode.Clamp
                )
            })
            {
                canvas.DrawOval(innerRect, shadowPaint);
            }
        }

        private void DrawDiffuser(SKCanvas canvas)
        {
            // Диффузор — основной элемент
            var diffuserRect = new SKRect(
                _center.X - RADIUS_X * 0.95f,
                _center.Y - RADIUS_Y * 0.95f,
                _center.X + RADIUS_X * 0.95f,
                _center.Y + RADIUS_Y * 0.95f
            );

            using (var diffuserPaint = new SKPaint { IsAntialias = true })
            {
                // Градиент для диффузора
                var gradientColors = new SKColor[] { SKColors.Gray, SKColors.Black.WithAlpha(200) };
                var gradientPositions = new float[] { 0.2f, 1f };

                using (var gradient = SKShader.CreateRadialGradient(
                    new SKPoint(_center.X, _center.Y),
                    RADIUS_X * 0.95f,
                    gradientColors,
                    gradientPositions,
                    SKShaderTileMode.Clamp
                ))
                {
                    diffuserPaint.Shader = gradient;
                    canvas.DrawOval(diffuserRect, diffuserPaint);
                }
            }

            // Текстурные кольца (для эффекта рельефа)
            using (var ringPaint = new SKPaint
            {
                Color = SKColors.Gray.WithAlpha(150),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1 * STROKE_SCALE,
                IsAntialias = true
            })
            {
                for (int i = 1; i <= RINGS; i++)
                {
                    float ratio = (float)i / (RINGS + 1);
                    var ringRect = new SKRect(
                        _center.X - (RADIUS_X * ratio),
                        _center.Y - (RADIUS_Y * ratio),
                        _center.X + (RADIUS_X * ratio),
                        _center.Y + (RADIUS_Y * ratio)
                    );

                    ringPaint.StrokeWidth = 1 * STROKE_SCALE + (i % 2 == 0 ? 0.5f * STROKE_SCALE : 0f); // Чередование толщины
                    canvas.DrawOval(ringRect, ringPaint);
                }
            }
        }

        private void DrawCentralCap(SKCanvas canvas)
        {
            // Центральная крышка
            float capRadiusX = RADIUS_X * CAP_RADIUS_RATIO;
            float capRadiusY = RADIUS_Y * CAP_RADIUS_RATIO;
            var capRect = new SKRect(
                _center.X - capRadiusX,
                _center.Y - capRadiusY,
                _center.X + capRadiusX,
                _center.Y + capRadiusY
            );

            using (var capPaint = new SKPaint { IsAntialias = true })
            {
                // Градиент для крышки
                var capGradientColors = new SKColor[] { SKColors.Black, SKColors.Gray.WithAlpha(150) };
                var capGradientPositions = new float[] { 0f, 1f };

                using (var capGradient = SKShader.CreateRadialGradient(
                    new SKPoint(_center.X, _center.Y),
                    capRadiusX,
                    capGradientColors,
                    capGradientPositions,
                    SKShaderTileMode.Clamp
                ))
                {
                    capPaint.Shader = capGradient;
                    canvas.DrawOval(capRect, capPaint);
                }
            }

            // Наружное кольцо на крышке
            using (var ringPaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2 * STROKE_SCALE,
                IsAntialias = true
            })
            {
                canvas.DrawOval(capRect, ringPaint);
            }
        }

        private void DrawSpeakerBody(SKCanvas canvas)
        {
            // Define key points for the complex body structure
            float topY = _center.Y;
            float bottomY = _center.Y + RADIUS_Y * 2f;
            float midY = topY + (bottomY - topY) * 0.4f;

            // Define multiple segments for more complex shape
            float[] xRatios = new float[] { 1.0f, 0.9f, 0.7f, 0.25f }; // Decreasing ratios for segments

            using (var path = new SKPath())
            {
                // Create points for the left side
                var leftPoints = new List<SKPoint>
        {
            new SKPoint(_center.X - RADIUS_X * xRatios[0], topY),
            new SKPoint(_center.X - RADIUS_X * xRatios[1], topY + (midY - topY) * 0.3f),
            new SKPoint(_center.X - RADIUS_X * xRatios[2], midY),
            new SKPoint(_center.X - RADIUS_X * xRatios[3], bottomY)
        };

                // Create points for the right side
                var rightPoints = new List<SKPoint>
        {
            new SKPoint(_center.X + RADIUS_X * xRatios[0], topY),
            new SKPoint(_center.X + RADIUS_X * xRatios[1], topY + (midY - topY) * 0.3f),
            new SKPoint(_center.X + RADIUS_X * xRatios[2], midY),
            new SKPoint(_center.X + RADIUS_X * xRatios[3], bottomY)
        };

                // Draw the complex body shape
                path.MoveTo(leftPoints[0]);

                // Draw left side
                for (int i = 1; i < leftPoints.Count; i++)
                {
                    path.LineTo(leftPoints[i]);
                }

                // Draw bottom connecting line
                path.LineTo(rightPoints[rightPoints.Count - 1]);

                // Draw right side in reverse
                for (int i = rightPoints.Count - 2; i >= 0; i--)
                {
                    path.LineTo(rightPoints[i]);
                }

                path.Close();

                // Create complex gradient for metallic effect
                using (var bodyPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Shader = SKShader.CreateLinearGradient(
                        new SKPoint(_center.X, topY),
                        new SKPoint(_center.X, bottomY),
                        new SKColor[]
                        {
                    SKColors.LightGray.WithAlpha(240),
                    SKColors.Gray.WithAlpha(220),
                    SKColors.DarkGray.WithAlpha(200),
                    SKColors.Gray.WithAlpha(180)
                        },
                        new float[] { 0.0f, 0.3f, 0.7f, 1.0f },
                        SKShaderTileMode.Clamp
                    )
                })
                {
                    canvas.DrawPath(path, bodyPaint);
                }

                // Add edge highlights
                using (var highlightPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.5f * STROKE_SCALE,
                    Color = SKColors.White.WithAlpha(60)
                })
                {
                    // Draw highlights on each segment
                    for (int i = 0; i < leftPoints.Count - 1; i++)
                    {
                        canvas.DrawLine(leftPoints[i], leftPoints[i + 1], highlightPaint);
                        canvas.DrawLine(rightPoints[i], rightPoints[i + 1], highlightPaint);
                    }
                }

                // Add shadows for depth
                using (var shadowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 2f * STROKE_SCALE,
                    Color = SKColors.Black.WithAlpha(40)
                })
                {
                    // Draw shadows on segment transitions
                    for (int i = 1; i < leftPoints.Count - 1; i++)
                    {
                        canvas.DrawLine(
                            leftPoints[i].X, leftPoints[i].Y,
                            rightPoints[i].X, rightPoints[i].Y,
                            shadowPaint
                        );
                    }
                }
            }
        }

        private void DrawMagnet(SKCanvas canvas)
        {
            // Корректируем размеры магнита для более овальной формы
            float magnetHeight = RADIUS_Y * 0.35f; // Уменьшаем высоту
            float magnetWidth = RADIUS_X * 0.6f;   // Увеличиваем ширину
            float magnetBottomOffset = RADIUS_Y * 2f;

            // Создаем путь для отсечения - форма корпуса динамика
            using (var clipPath = new SKPath())
            {
                float topY = _center.Y;
                float bottomY = _center.Y + RADIUS_Y * 2f;
                float p1X = _center.X - RADIUS_X;
                float p2X = _center.X + RADIUS_X;
                float p3X = _center.X - RADIUS_X * 0.25f;
                float p4X = _center.X + RADIUS_X * 0.25f;

                clipPath.MoveTo(p1X, topY);
                clipPath.LineTo(p2X, topY);
                clipPath.LineTo(p4X, bottomY);
                clipPath.LineTo(p3X, bottomY);
                clipPath.Close();

                canvas.Save();
                clipPath.FillType = SKPathFillType.InverseWinding;
                canvas.ClipPath(clipPath);

                // Основная часть магнита - делаем более овальной
                SKRect magnetEllipseRect = new SKRect(
                    _center.X - magnetWidth / 2,
                    _center.Y + magnetBottomOffset,
                    _center.X + magnetWidth / 2,
                    _center.Y + magnetBottomOffset + magnetHeight
                );

                // Тень под магнитом
                using (var shadowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = SKColors.Black.WithAlpha(40)
                })
                {
                    SKRect shadowRect = new SKRect(
                        magnetEllipseRect.Left - 4 * STROKE_SCALE,
                        magnetEllipseRect.Top - 2 * STROKE_SCALE,
                        magnetEllipseRect.Right + 4 * STROKE_SCALE,
                        magnetEllipseRect.Bottom + 2 * STROKE_SCALE
                    );
                    shadowRect.Offset(0, 4 * STROKE_SCALE);
                    canvas.DrawOval(shadowRect, shadowPaint);
                }

                // Основное тело магнита с более металлическим градиентом
                using (var magnetPaint = new SKPaint { IsAntialias = true })
                {
                    SKColor[] colors = new SKColor[]
                    {
                new SKColor(190, 190, 190), // Светлее
                new SKColor(130, 130, 130), // Средний
                new SKColor(100, 100, 100)  // Темнее
                    };

                    using (var gradient = SKShader.CreateLinearGradient(
                        new SKPoint(_center.X, magnetEllipseRect.Top),
                        new SKPoint(_center.X, magnetEllipseRect.Bottom),
                        colors,
                        new float[] { 0.0f, 0.5f, 1.0f },
                        SKShaderTileMode.Clamp
                    ))
                    {
                        magnetPaint.Shader = gradient;
                        canvas.DrawOval(magnetEllipseRect, magnetPaint);
                    }
                }

                // Верхняя кромка магнита - делаем более тонкой
                float topEdgeHeight = magnetHeight * 0.12f;
                SKRect topEdgeRect = new SKRect(
                    _center.X - magnetWidth / 2,
                    _center.Y + magnetBottomOffset,
                    _center.X + magnetWidth / 2,
                    _center.Y + magnetBottomOffset + topEdgeHeight
                );

                using (var topEdgePaint = new SKPaint { IsAntialias = true })
                {
                    SKColor[] topEdgeColors = new SKColor[]
                    {
                new SKColor(210, 210, 210), // Светлее
                new SKColor(160, 160, 160)  // Темнее
                    };

                    using (var gradient = SKShader.CreateLinearGradient(
                        new SKPoint(_center.X, topEdgeRect.Top),
                        new SKPoint(_center.X, topEdgeRect.Bottom),
                        topEdgeColors,
                        null,
                        SKShaderTileMode.Clamp
                    ))
                    {
                        topEdgePaint.Shader = gradient;
                        canvas.DrawOval(topEdgeRect, topEdgePaint);
                    }
                }

                // Блик на магните
                using (var highlightPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.5f * STROKE_SCALE,
                    Color = SKColors.White.WithAlpha(60)
                })
                {
                    SKRect highlightRect = new SKRect(
                        magnetEllipseRect.Left + 2 * STROKE_SCALE,
                        magnetEllipseRect.Top + 2 * STROKE_SCALE,
                        magnetEllipseRect.Right - 2 * STROKE_SCALE,
                        magnetEllipseRect.Bottom - 2 * STROKE_SCALE
                    );
                    canvas.DrawOval(highlightRect, highlightPaint);
                }

                // Тонкая обводка магнита
                using (var outlinePaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1 * STROKE_SCALE,
                    Color = SKColors.Black.WithAlpha(100)
                })
                {
                    canvas.DrawOval(magnetEllipseRect, outlinePaint);
                }

                canvas.Restore();
            }
        }

        private float ProcessSpectrum(float[] spectrum)
        {
            int sampleSize = Math.Min(spectrum.Length / 2, 10);
            float sum = 0;

            for (int i = 0; i < sampleSize; i++)
            {
                sum += spectrum[i];
            }

            float magnitude = sum / sampleSize;
            magnitude = (float)Math.Log10(1 + magnitude) * 0.5f;

            if (_previousSpectrum == null)
            {
                _previousSpectrum = new float[] { magnitude };
                return magnitude;
            }

            float smoothedMagnitude = _previousSpectrum[0] + (magnitude - _previousSpectrum[0]) * _smoothingFactor;
            _previousSpectrum[0] = smoothedMagnitude;

            return smoothedMagnitude;
        }

        #endregion
    }
}