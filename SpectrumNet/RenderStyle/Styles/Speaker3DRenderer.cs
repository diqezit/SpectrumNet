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
        public const float SCALE = 2f;
        public const float STROKE_SCALE = SCALE / 2;

        // Realistic proportions scaled by SCALE
        private const float RADIUS_X = 100f * SCALE;
        private const float RADIUS_Y = 20f * SCALE;
        private const float DEFLECTION = 1f * SCALE;

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
            float loudness = Math.Abs(deflection / DEFLECTION);
            float capRadiusX = RADIUS_X * 0.12f;
            float capRadiusY = RADIUS_Y * 0.12f;

            SpeakerBodyDrawer.DrawSpeakerBody(canvas, _center, RADIUS_X, RADIUS_Y, STROKE_SCALE);
            OuterFrameDrawer.DrawOuterFrame(canvas, _center, RADIUS_X, RADIUS_Y, STROKE_SCALE);
            DiffuserDrawer.DrawDiffuser(canvas, _center, deflection, RADIUS_X, RADIUS_Y, DEFLECTION);
            CapDrawer.DrawCentralCap(canvas, _center, capRadiusX, capRadiusY, deflection, loudness);
            MagnetDrawer.DrawMagnet(canvas, _center, RADIUS_X, RADIUS_Y);
        }

        public static class OuterFrameDrawer
        {
            public static void DrawOuterFrame(SKCanvas canvas, Vector2 center, float radiusX, float radiusY, float strokeScale)
            {
                // Основной контур с более точной перспективой
                var suspensionRect = new SKRect(
                    center.X - radiusX,
                    center.Y - radiusY * 0.99f,
                    center.X + radiusX,
                    center.Y + radiusY * 1.01f
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
                    suspensionRect.Left + 2.5f * strokeScale,
                    suspensionRect.Top + 2.5f * strokeScale,
                    suspensionRect.Right - 2.5f * strokeScale,
                    suspensionRect.Bottom - 2.5f * strokeScale
                );

                using (var framePaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.5f * strokeScale,
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
                    StrokeWidth = 1.2f * strokeScale,
                    Color = SKColors.White.WithAlpha(35)
                })
                {
                    canvas.DrawArc(suspensionRect, 180, 180, false, highlightPaint);
                }
            }
        }

        public static class DiffuserDrawer
        {
            public static void DrawDiffuser(SKCanvas canvas, Vector2 center, float deflection, float radiusX, float radiusY, float deflectionFactor)
            {
                float loudness = Math.Abs(deflection / deflectionFactor);
                float deformationFactor = 1f + (loudness * 0.08f);
                var diffuserRect = CalculateDiffuserRect(deflection, deformationFactor, center, radiusX, radiusY);

                DrawMainDiffuser(canvas, diffuserRect, center, deflection, loudness, radiusX, radiusY);
                DrawDiffuserRings(canvas, diffuserRect, center, deflection, loudness, radiusX, radiusY);
                DrawDepthAndShadowEffects(canvas, diffuserRect, loudness);
                DrawVolumeEffect(canvas, diffuserRect, loudness);
            }

            private static SKRect CalculateDiffuserRect(float deflection, float deformationFactor, Vector2 center, float radiusX, float radiusY)
            {
                return new SKRect(
                    center.X - radiusX * 0.94f,
                    center.Y - (radiusY * 0.92f * deformationFactor) + deflection,
                    center.X + radiusX * 0.94f,
                    center.Y + (radiusY * 0.92f / deformationFactor) + deflection
                );
            }

            private static void DrawMainDiffuser(SKCanvas canvas, SKRect diffuserRect, Vector2 center, float deflection, float loudness, float radiusX, float radiusY)
            {
                using var diffuserPaint = new SKPaint { IsAntialias = true };
                var gradientColors = new SKColor[]
                {
                    new SKColor(85, 85, 85, 255), new SKColor(75, 75, 75, 252),
                    new SKColor(65, 65, 65, 250), new SKColor(55, 55, 55, 248),
                    new SKColor(45, 45, 45, 245), new SKColor(35, 35, 35, 242),
                    new SKColor(25, 25, 25, 240)
                };
                var gradientPositions = new float[] { 0.0f, 0.2f, 0.4f, 0.6f, 0.7f, 0.85f, 1.0f };

                using var gradient = SKShader.CreateRadialGradient(
                    new SKPoint(center.X, center.Y + deflection),
                    radiusX * (0.94f + loudness * 0.06f),
                    gradientColors,
                    gradientPositions,
                    SKShaderTileMode.Clamp);
                diffuserPaint.Shader = gradient;
                canvas.DrawOval(diffuserRect, diffuserPaint);
            }

            private static void DrawDiffuserRings(SKCanvas canvas, SKRect diffuserRect, Vector2 center, float deflection, float loudness, float radiusX, float radiusY)
            {
                int baseRings = 18;
                int dynamicRings = (int)(baseRings + (loudness * 4));
                float baseAlpha = 65f;

                using var ringPaint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true };
                for (int i = 1; i <= dynamicRings; i++)
                {
                    float ratio = (float)i / (dynamicRings + 1);
                    float ringDeformation = 1f + (loudness * 0.05f * ratio);
                    float intensityFactor = 1f - (float)Math.Pow(ratio - 0.5f, 2) * 2;

                    var ringRect = CalculateRingRect(diffuserRect, ratio, ringDeformation, deflection, center, radiusX, radiusY);

                    DrawMainRing(canvas, ringPaint, ringRect, baseAlpha, loudness, intensityFactor);
                    DrawHighlightRing(canvas, ringPaint, ringRect, loudness, intensityFactor, i);
                }
            }

            private static SKRect CalculateRingRect(SKRect diffuserRect, float ratio, float ringDeformation, float deflection, Vector2 center, float radiusX, float radiusY)
            {
                return new SKRect(
                    center.X - (radiusX * 0.94f * ratio),
                    center.Y - (radiusY * 0.92f * ratio * ringDeformation) + deflection,
                    center.X + (radiusX * 0.94f * ratio),
                    center.Y + (radiusY * 0.92f * ratio / ringDeformation) + deflection
                );
            }

            private static void DrawMainRing(SKCanvas canvas, SKPaint ringPaint, SKRect ringRect, float baseAlpha, float loudness, float intensityFactor)
            {
                ringPaint.Color = new SKColor(0, 0, 0, (byte)(baseAlpha + (loudness * 45 * intensityFactor)));
                ringPaint.StrokeWidth = 0.7f * STROKE_SCALE * (1f + loudness * 0.15f * intensityFactor);
                canvas.DrawOval(ringRect, ringPaint);
            }

            private static void DrawHighlightRing(SKCanvas canvas, SKPaint ringPaint, SKRect ringRect, float loudness, float intensityFactor, int ringIndex)
            {
                if (loudness > 0.6f && ringIndex % 2 == 0)
                {
                    ringPaint.Color = new SKColor(255, 255, 255, (byte)(18 * intensityFactor * (loudness - 0.6f) / 0.4f));
                    canvas.DrawOval(ringRect, ringPaint);
                }
            }

            private static void DrawDepthAndShadowEffects(SKCanvas canvas, SKRect diffuserRect, float loudness)
            {
                using var shadowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.0f * STROKE_SCALE,
                };

                shadowPaint.Color = new SKColor(0, 0, 0, (byte)(35 + loudness * 20));
                canvas.DrawArc(diffuserRect, -45, 270, false, shadowPaint);

                shadowPaint.Color = new SKColor(255, 255, 255, (byte)(15 + loudness * 10));
                canvas.DrawArc(diffuserRect, 135, 270, false, shadowPaint);
            }

            private static void DrawVolumeEffect(SKCanvas canvas, SKRect diffuserRect, float loudness)
            {
                if (loudness > 0.7f)
                {
                    using var volumePaint = new SKPaint
                    {
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = 0.5f * STROKE_SCALE,
                        Color = new SKColor(255, 255, 255, (byte)(20 * (loudness - 0.7f) / 0.3f))
                    };
                    canvas.DrawOval(diffuserRect, volumePaint);
                }
            }
        }

        public static class CapDrawer
        {
            public static void DrawCentralCap(SKCanvas canvas, Vector2 center, float capRadiusX, float capRadiusY, float deflection, float loudness)
            {
                var capRect = new SKRect(
                    center.X - capRadiusX,
                    center.Y - capRadiusY * 0.99f + deflection,
                    center.X + capRadiusX,
                    center.Y + capRadiusY * 1.01f + deflection
                );

                DrawMainCap(canvas, center, capRect, deflection);
                DrawCapHighlights(canvas, capRect, loudness);
                DrawInnerCapDetails(canvas, center, capRect, deflection, loudness);
            }

            public static void DrawMainCap(SKCanvas canvas, Vector2 center, SKRect capRect, float deflection)
            {
                using var capPaint = new SKPaint { IsAntialias = true };
                var capGradientColors = new SKColor[]
                {
        new SKColor(100, 100, 100, 255), new SKColor(80, 80, 80, 250),
        new SKColor(60, 60, 60, 245), new SKColor(40, 40, 40, 240),
        new SKColor(20, 20, 20, 235)
                };
                var capGradientPositions = new float[] { 0.0f, 0.3f, 0.5f, 0.7f, 1.0f };

                using var capGradient = SKShader.CreateRadialGradient(
                    new SKPoint(center.X, center.Y + deflection),
                    capRect.Width / 2,
                    capGradientColors,
                    capGradientPositions,
                    SKShaderTileMode.Clamp
                );
                capPaint.Shader = capGradient;
                canvas.DrawOval(capRect, capPaint);

                using var textureShader = SKShader.CreatePerlinNoiseFractalNoise(0.8f, 0.8f, 3, 0.0f);
                capPaint.Shader = SKShader.CreateCompose(capPaint.Shader, textureShader, SKBlendMode.Overlay);
                canvas.DrawOval(capRect, capPaint);
            }

            public static void DrawCapHighlights(SKCanvas canvas, SKRect capRect, float loudness)
            {
                using var highlightPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.8f * STROKE_SCALE,
                };

                highlightPaint.Color = new SKColor(255, 255, 255, 30);
                canvas.DrawArc(capRect, 180, 180, false, highlightPaint);

                using var ringPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.8f * STROKE_SCALE,
                    IsAntialias = true
                };

                var metalGradient = SKShader.CreateLinearGradient(
                    new SKPoint(capRect.Left, capRect.Top),
                    new SKPoint(capRect.Right, capRect.Bottom),
                    new SKColor[] { new SKColor(40, 40, 40, 180), new SKColor(100, 100, 100, 160), new SKColor(40, 40, 40, 180) },
                    new float[] { 0.0f, 0.5f, 1.0f },
                    SKShaderTileMode.Clamp
                );

                ringPaint.Shader = metalGradient;
                canvas.DrawOval(capRect, ringPaint);

                ringPaint.Shader = null;
                ringPaint.Color = new SKColor(0, 0, 0, (byte)(50 + (loudness * 30)));
                canvas.DrawArc(capRect, -30, 240, false, ringPaint);

                ringPaint.Color = new SKColor(255, 255, 255, (byte)(30 + (loudness * 20)));
                canvas.DrawArc(capRect, 150, 240, false, ringPaint);
            }

            public static void DrawInnerCapDetails(SKCanvas canvas, Vector2 center, SKRect capRect, float deflection, float loudness)
            {
                var innerCapRect = new SKRect(
                    capRect.Left + 1.8f * STROKE_SCALE,
                    capRect.Top + 1.8f * STROKE_SCALE,
                    capRect.Right - 1.8f * STROKE_SCALE,
                    capRect.Bottom - 1.8f * STROKE_SCALE
                );

                using var innerRingPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.6f * STROKE_SCALE,
                };

                var innerGradient = SKShader.CreateLinearGradient(
                    new SKPoint(innerCapRect.Left, innerCapRect.Top),
                    new SKPoint(innerCapRect.Right, innerCapRect.Bottom),
                    new SKColor[] { new SKColor(60, 60, 60, 90), new SKColor(20, 20, 20, 80), new SKColor(60, 60, 60, 90) },
                    new float[] { 0.0f, 0.5f, 1.0f },
                    SKShaderTileMode.Clamp
                );

                innerRingPaint.Shader = innerGradient;
                canvas.DrawOval(innerCapRect, innerRingPaint);

                innerRingPaint.Shader = null;
                innerRingPaint.Color = new SKColor(0, 0, 0, (byte)(40 + loudness * 20));
                canvas.DrawArc(innerCapRect, -30, 240, false, innerRingPaint);

                DrawCenterPoint(canvas, center, deflection);
                DrawDynamicHighlights(canvas, innerCapRect, loudness);
            }

            public static void DrawCenterPoint(SKCanvas canvas, Vector2 center, float deflection)
            {
                using var centerPaint = new SKPaint { IsAntialias = true };
                var centerGradient = SKShader.CreateRadialGradient(
                    new SKPoint(center.X, center.Y + deflection),
                    1.0f * STROKE_SCALE,
                    new SKColor[] { new SKColor(60, 60, 60, 150), new SKColor(20, 20, 20, 180), new SKColor(0, 0, 0, 200) },
                    new float[] { 0.0f, 0.5f, 1.0f },
                    SKShaderTileMode.Clamp
                );

                centerPaint.Shader = centerGradient;
                canvas.DrawCircle(center.X, center.Y + deflection, 0.8f * STROKE_SCALE, centerPaint);
            }

            public static void DrawDynamicHighlights(SKCanvas canvas, SKRect innerCapRect, float loudness)
            {
                using var detailPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.5f * STROKE_SCALE,
                };

                detailPaint.Color = new SKColor(255, 255, 255, (byte)(20 + (loudness * 15)));
                canvas.DrawArc(innerCapRect, 160, 220, false, detailPaint);

                detailPaint.Color = new SKColor(0, 0, 0, (byte)(30 + (loudness * 20)));
                canvas.DrawArc(innerCapRect, -20, 220, false, detailPaint);
            }
        }

        public static class SpeakerBodyDrawer
        {
            public static void DrawSpeakerBody(SKCanvas canvas, Vector2 center, float radiusX, float radiusY, float strokeScale)
            {
                var (path, leftPoints, rightPoints) = CreateSpeakerPath(center, radiusX, radiusY);
                DrawBodyGradient(canvas, path);
                DrawEffects(canvas, leftPoints, rightPoints, strokeScale);
                DrawEdgeAndDetails(canvas, path, leftPoints, rightPoints, strokeScale, center, radiusX, radiusY);
                DrawMagnet(canvas, center, radiusX, radiusY, strokeScale);
            }

            private static (SKPath path, List<SKPoint> leftPoints, List<SKPoint> rightPoints) CreateSpeakerPath(Vector2 center, float radiusX, float radiusY)
            {
                float topY = center.Y;
                float bottomY = center.Y + radiusY * 1.3f;
                float midY = topY + (bottomY - topY) * 0.35f;
                float[] xRatios = { 1.0f, 0.96f, 0.88f, 0.35f };
                float topCompression = 0.985f;
                float bottomExpansion = 1.015f;

                var leftPoints = new List<SKPoint>();
                var rightPoints = new List<SKPoint>();
                for (int i = 0; i < xRatios.Length; i++)
                {
                    float y = i == 0 ? topY * topCompression :
                              i == xRatios.Length - 1 ? bottomY * bottomExpansion :
                              i == 1 ? topY + (midY - topY) * 0.3f : midY;
                    leftPoints.Add(new SKPoint(center.X - radiusX * xRatios[i], y));
                    rightPoints.Add(new SKPoint(center.X + radiusX * xRatios[i], y));
                }

                var path = new SKPath();
                path.MoveTo(leftPoints[0]);
                DrawCurvedPath(path, leftPoints);
                path.LineTo(rightPoints[rightPoints.Count - 1]);
                DrawCurvedPath(path, rightPoints.AsEnumerable().Reverse().ToList());
                path.Close();

                return (path, leftPoints, rightPoints);
            }

            private static void DrawCurvedPath(SKPath path, List<SKPoint> points)
            {
                for (int i = 0; i < points.Count - 1; i++)
                {
                    var current = points[i];
                    var next = points[i + 1];
                    var controlPoint1 = new SKPoint(
                        current.X + (next.X - current.X) * 0.2f,
                        current.Y + (next.Y - current.Y) * 0.45f
                    );
                    var controlPoint2 = new SKPoint(
                        next.X - (next.X - current.X) * 0.15f,
                        next.Y - (next.Y - current.Y) * 0.5f
                    );
                    path.CubicTo(controlPoint1, controlPoint2, next);
                }
            }

            private static void DrawBodyGradient(SKCanvas canvas, SKPath path)
            {
                using var bodyPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
                var gradientColors = new SKColor[]
                {
                new SKColor(240, 240, 240, 255), new SKColor(220, 220, 220, 255),
                new SKColor(200, 200, 200, 255), new SKColor(180, 180, 180, 255),
                new SKColor(200, 200, 200, 255), new SKColor(220, 220, 220, 255),
                new SKColor(240, 240, 240, 255)
                };
                var gradientPositions = new float[] { 0.0f, 0.2f, 0.4f, 0.5f, 0.6f, 0.8f, 1.0f };

                using (var gradient = SKShader.CreateLinearGradient(
                    new SKPoint(path.Bounds.Left, path.Bounds.Top),
                    new SKPoint(path.Bounds.Right, path.Bounds.Bottom),
                    gradientColors, gradientPositions, SKShaderTileMode.Clamp))
                {
                    bodyPaint.Shader = gradient;
                    canvas.DrawPath(path, bodyPaint);
                }

                using (var textureShader = SKShader.CreatePerlinNoiseFractalNoise(0.7f, 0.7f, 3, 0.0f))
                {
                    bodyPaint.Shader = SKShader.CreateCompose(bodyPaint.Shader, textureShader, SKBlendMode.Overlay);
                    canvas.DrawPath(path, bodyPaint);
                }
            }

            private static void DrawEffects(SKCanvas canvas, List<SKPoint> leftPoints, List<SKPoint> rightPoints, float strokeScale)
            {
                using var effectPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.0f * strokeScale
                };

                effectPaint.Color = new SKColor(255, 255, 255, 45);
                canvas.DrawLine(leftPoints[0], rightPoints[0], effectPaint);

                for (int i = 0; i < leftPoints.Count - 1; i++)
                {
                    float alpha = 35 - i * 7;
                    effectPaint.Color = new SKColor(255, 255, 255, (byte)alpha);
                    canvas.DrawLine(leftPoints[i], leftPoints[i + 1], effectPaint);

                    effectPaint.Color = new SKColor(0, 0, 0, (byte)(alpha * 0.7f));
                    canvas.DrawLine(rightPoints[i], rightPoints[i + 1], effectPaint);

                    if (i > 0)
                    {
                        effectPaint.Color = new SKColor(0, 0, 0, (byte)(15 + i * 12));
                        canvas.DrawLine(leftPoints[i], rightPoints[i], effectPaint);
                    }
                }
            }

            private static void DrawEdgeAndDetails(SKCanvas canvas, SKPath path, List<SKPoint> leftPoints, List<SKPoint> rightPoints, float strokeScale, Vector2 center, float radiusX, float radiusY)
            {
                using var edgePaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.8f * strokeScale
                };

                var edgeGradient = SKShader.CreateLinearGradient(
                    new SKPoint(path.Bounds.Left, path.Bounds.Top),
                    new SKPoint(path.Bounds.Right, path.Bounds.Bottom),
                    new SKColor[] { new SKColor(180, 180, 180, 120), new SKColor(140, 140, 140, 140), new SKColor(100, 100, 100, 160) },
                    new float[] { 0.0f, 0.5f, 1.0f },
                    SKShaderTileMode.Clamp
                );

                edgePaint.Shader = edgeGradient;
                canvas.DrawPath(path, edgePaint);

                DrawDepthEffects(canvas, path, strokeScale);
                DrawAdditionalDetails(canvas, leftPoints, rightPoints, strokeScale, center, radiusX, radiusY);
                DrawFinalTouches(canvas, strokeScale, center, radiusX, radiusY);
            }

            private static void DrawDepthEffects(SKCanvas canvas, SKPath path, float strokeScale)
            {
                using var depthPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.6f * strokeScale
                };

                depthPaint.Color = new SKColor(0, 0, 0, 40);
                canvas.DrawPath(path, depthPaint);

                depthPaint.Color = new SKColor(255, 255, 255, 25);
                depthPaint.StrokeWidth = 0.4f * strokeScale;
                canvas.DrawPath(path, depthPaint);
            }

            private static void DrawAdditionalDetails(SKCanvas canvas, List<SKPoint> leftPoints, List<SKPoint> rightPoints, float strokeScale, Vector2 center, float radiusX, float radiusY)
            {
                using var detailPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.5f * strokeScale
                };

                detailPaint.Color = new SKColor(255, 255, 255, 30);
                canvas.DrawLine(
                    leftPoints[0].X + radiusX * 0.1f, leftPoints[0].Y + radiusY * 0.05f,
                    rightPoints[0].X - radiusX * 0.1f, rightPoints[0].Y + radiusY * 0.05f,
                    detailPaint
                );

                for (int i = 0; i < 3; i++)
                {
                    float y = center.Y + radiusY * (0.3f + i * 0.2f);
                    float xOffset = radiusX * (0.8f - i * 0.2f);

                    detailPaint.Color = new SKColor(0, 0, 0, (byte)(20 + i * 10));
                    canvas.DrawLine(center.X - xOffset, y, center.X + xOffset, y, detailPaint);
                }
            }

            private static void DrawFinalTouches(SKCanvas canvas, float strokeScale, Vector2 center, float radiusX, float radiusY)
            {
                using var finalPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.3f * strokeScale
                };

                finalPaint.Color = new SKColor(255, 255, 255, 15);
                for (int i = 0; i < 4; i++)
                {
                    float y = center.Y + radiusY * (0.2f + i * 0.2f);
                    canvas.DrawLine(center.X - radiusX * 0.9f, y, center.X + radiusX * 0.9f, y, finalPaint);
                }

                finalPaint.Color = new SKColor(0, 0, 0, 20);
                canvas.DrawLine(center.X - radiusX * 0.5f, center.Y, center.X - radiusX * 0.2f, center.Y + radiusY * 1.3f, finalPaint);
                canvas.DrawLine(center.X + radiusX * 0.5f, center.Y, center.X + radiusX * 0.2f, center.Y + radiusY * 1.3f, finalPaint);
            }

            private static void DrawMagnet(SKCanvas canvas, Vector2 center, float radiusX, float radiusY, float strokeScale)
            {
                float magnetHeight = radiusY * 0.18f, magnetWidth = radiusX * 0.82f;
                float magnetBottomOffset = radiusY * 1.25f, cornerRadius = magnetHeight * 0.15f;
                float topCompression = 0.94f, bottomExpansion = 1.06f, perspectiveScale = 0.88f;

                using var clipPath = new SKPath();
                float topY = center.Y, bottomY = center.Y + radiusY * 1.3f;
                float p1X = center.X - radiusX * 1.1f, p2X = center.X + radiusX * 1.1f;
                float p3X = center.X - radiusX * 0.39f, p4X = center.X + radiusX * 0.39f;

                clipPath.MoveTo(p1X, topY);
                clipPath.LineTo(p2X, topY);
                clipPath.QuadTo(new SKPoint(p2X, (topY + bottomY) * 0.5f), new SKPoint(p4X, bottomY));
                clipPath.LineTo(p3X, bottomY);
                clipPath.QuadTo(new SKPoint(p1X, (topY + bottomY) * 0.5f), new SKPoint(p1X, topY));
                clipPath.Close();

                canvas.Save();
                canvas.ClipPath(clipPath, SKClipOperation.Difference);

                SKRect magnetRect = new SKRect(
                    center.X - magnetWidth / 2,
                    (center.Y + magnetBottomOffset - magnetHeight * 0.1f) * topCompression,
                    center.X + magnetWidth / 2,
                    (center.Y + magnetBottomOffset + magnetHeight * 0.9f) * bottomExpansion
                );
                magnetRect = new SKRect(magnetRect.Left, magnetRect.Top, magnetRect.Right, magnetRect.Top + (magnetRect.Height * perspectiveScale));

                DrawMagnetShadow(canvas, magnetRect, magnetWidth);
                DrawMagnetBody(canvas, magnetRect, perspectiveScale);
                DrawMagnetHighlights(canvas, magnetRect, cornerRadius, perspectiveScale, topCompression, bottomExpansion);
                DrawMagnetShadows(canvas, magnetRect, perspectiveScale);
                DrawMagnetEdge(canvas, magnetRect, perspectiveScale);
                DrawMagnetConnections(canvas, magnetRect, magnetWidth, perspectiveScale);
                DrawMagnetDetails(canvas, magnetRect, magnetWidth, magnetHeight, cornerRadius, perspectiveScale);

                canvas.Restore();
            }

            private static void DrawMagnetShadow(SKCanvas canvas, SKRect magnetRect, float magnetWidth)
            {
                using var backgroundShadowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Shader = SKShader.CreateRadialGradient(
                        new SKPoint(magnetRect.MidX, magnetRect.MidY),
                        magnetWidth * 0.6f,
                        new SKColor[] { new SKColor(0, 0, 0, 45), new SKColor(0, 0, 0, 20), SKColors.Transparent },
                        new float[] { 0.0f, 0.6f, 1.0f },
                        SKShaderTileMode.Clamp
                    )
                };

                SKRect shadowRect = magnetRect;
                shadowRect.Offset(1.2f * STROKE_SCALE, 1.8f * STROKE_SCALE);
                shadowRect.Inflate(3.5f * STROKE_SCALE, 1.8f * STROKE_SCALE * (magnetRect.Height / magnetRect.Width));
                canvas.DrawOval(shadowRect, backgroundShadowPaint);
            }

            private static void DrawMagnetBody(SKCanvas canvas, SKRect magnetRect, float perspectiveScale)
            {
                using var magnetPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
                var gradientColors = new SKColor[]
                {
                new SKColor(100, 100, 100, 255), new SKColor(120, 120, 120, 255),
                new SKColor(100, 100, 100, 255), new SKColor(80, 80, 80, 255),
                new SKColor(90, 90, 90, 255), new SKColor(110, 110, 110, 255),
                new SKColor(90, 90, 90, 255)
                };
                var gradientPositions = new float[] { 0.0f, 0.2f, 0.4f, 0.5f, 0.6f, 0.8f, 1.0f };

                using (var gradient = SKShader.CreateLinearGradient(
                    new SKPoint(magnetRect.Left, magnetRect.Top),
                    new SKPoint(magnetRect.Right, magnetRect.Bottom),
                    gradientColors, gradientPositions, SKShaderTileMode.Clamp))
                {
                    magnetPaint.Shader = gradient;
                    canvas.DrawOval(magnetRect, magnetPaint);
                }

                using (var textureShader = SKShader.CreatePerlinNoiseFractalNoise(0.8f * perspectiveScale, 0.8f, 4, 0.0f))
                {
                    magnetPaint.Shader = SKShader.CreateCompose(magnetPaint.Shader, textureShader, SKBlendMode.Overlay);
                    canvas.DrawOval(magnetRect, magnetPaint);
                }
            }

            private static void DrawMagnetHighlights(SKCanvas canvas, SKRect magnetRect, float cornerRadius, float perspectiveScale, float topCompression, float bottomExpansion)
            {
                using var highlightPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.9f * STROKE_SCALE * perspectiveScale
                };

                highlightPaint.Color = new SKColor(255, 255, 255, 40);
                canvas.DrawArc(magnetRect, 160, 220, false, highlightPaint);

                highlightPaint.Color = new SKColor(255, 255, 255, 25);
                float sideOffset = cornerRadius * perspectiveScale;
                canvas.DrawLine(
                    magnetRect.Left + sideOffset, magnetRect.Top + sideOffset * topCompression,
                    magnetRect.Left + sideOffset, magnetRect.Bottom - sideOffset * bottomExpansion,
                    highlightPaint
                );
                canvas.DrawLine(
                    magnetRect.Right - sideOffset, magnetRect.Top + sideOffset * topCompression,
                    magnetRect.Right - sideOffset, magnetRect.Bottom - sideOffset * bottomExpansion,
                    highlightPaint
                );
            }

            private static void DrawMagnetShadows(SKCanvas canvas, SKRect magnetRect, float perspectiveScale)
            {
                using var shadowPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

                shadowPaint.Color = new SKColor(0, 0, 0, 60);
                shadowPaint.StrokeWidth = 1.3f * STROKE_SCALE * perspectiveScale;
                canvas.DrawArc(magnetRect, -35, 250, false, shadowPaint);

                shadowPaint.Color = new SKColor(0, 0, 0, 25);
                shadowPaint.StrokeWidth = 2.0f * STROKE_SCALE * perspectiveScale;
                canvas.DrawArc(magnetRect, -25, 230, false, shadowPaint);
            }

            private static void DrawMagnetEdge(SKCanvas canvas, SKRect magnetRect, float perspectiveScale)
            {
                using var edgePaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.1f * STROKE_SCALE * perspectiveScale
                };

                var edgeGradient = SKShader.CreateLinearGradient(
                    new SKPoint(magnetRect.Left, magnetRect.Top),
                    new SKPoint(magnetRect.Right, magnetRect.Bottom),
                    new SKColor[]
                    {
                    new SKColor(140, 140, 140, 100), new SKColor(100, 100, 100, 120),
                    new SKColor(80, 80, 80, 140), new SKColor(100, 100, 100, 120),
                    new SKColor(140, 140, 140, 100)
                    },
                    new float[] { 0.0f, 0.3f, 0.5f, 0.7f, 1.0f },
                    SKShaderTileMode.Clamp
                );

                edgePaint.Shader = edgeGradient;
                canvas.DrawOval(magnetRect, edgePaint);
            }

            private static void DrawMagnetConnections(SKCanvas canvas, SKRect magnetRect, float magnetWidth, float perspectiveScale)
            {
                using var connectionPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.8f * STROKE_SCALE * perspectiveScale
                };

                connectionPaint.Color = new SKColor(0, 0, 0, 40);
                float topOffset = magnetRect.Height * 0.05f;
                canvas.DrawLine(
                    magnetRect.Left + magnetWidth * 0.1f, magnetRect.Top + topOffset,
                    magnetRect.Right - magnetWidth * 0.1f, magnetRect.Top + topOffset,
                    connectionPaint
                );

                connectionPaint.Color = new SKColor(0, 0, 0, 30);
                float sideOffset = magnetWidth * 0.05f;
                canvas.DrawLine(
                    magnetRect.Left + sideOffset, magnetRect.Top + magnetRect.Height * 0.2f,
                    magnetRect.Left + sideOffset, magnetRect.Bottom - magnetRect.Height * 0.2f,
                    connectionPaint
                );
                canvas.DrawLine(
                    magnetRect.Right - sideOffset, magnetRect.Top + magnetRect.Height * 0.2f,
                    magnetRect.Right - sideOffset, magnetRect.Bottom - magnetRect.Height * 0.2f,
                    connectionPaint
                );
            }

            private static void DrawMagnetDetails(SKCanvas canvas, SKRect magnetRect, float magnetWidth, float magnetHeight, float cornerRadius, float perspectiveScale)
            {
                using var detailPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.5f * STROKE_SCALE * perspectiveScale
                };

                detailPaint.Color = new SKColor(255, 255, 255, 15);
                for (int i = 1; i <= 3; i++)
                {
                    float y = magnetRect.Top + (magnetRect.Height * i / 4);
                    float horizontalScale = 1.0f - (i * 0.05f);
                    canvas.DrawLine(
                        magnetRect.Left + magnetWidth * (0.15f / horizontalScale),
                        y,
                        magnetRect.Right - magnetWidth * (0.15f / horizontalScale),
                        y,
                        detailPaint
                    );
                }

                detailPaint.Color = new SKColor(0, 0, 0, 25);
                float accentLength = magnetHeight * 0.3f * perspectiveScale;
                float cornerOffset = cornerRadius * perspectiveScale;

                DrawCornerAccent(canvas, magnetRect.Left, magnetRect.Top, cornerOffset, accentLength * 1.2f, detailPaint);
                DrawCornerAccent(canvas, magnetRect.Right, magnetRect.Top, cornerOffset, -accentLength * 1.2f, detailPaint);
                DrawCornerAccent(canvas, magnetRect.Left, magnetRect.Bottom, cornerOffset, accentLength * 0.8f, detailPaint);
                DrawCornerAccent(canvas, magnetRect.Right, magnetRect.Bottom, cornerOffset, -accentLength * 0.8f, detailPaint);
            }

            private static void DrawCornerAccent(SKCanvas canvas, float x, float y, float offset, float length, SKPaint paint)
            {
                canvas.DrawLine(x + (length > 0 ? offset : -offset), y + offset, x + offset + length, y + offset, paint);
            }
        }

        public static class MagnetDrawer
        {
            public static void DrawMagnet(SKCanvas canvas, Vector2 center, float radiusX, float radiusY)
            {
                float magnetHeight = radiusY * 0.18f;
                float magnetWidth = radiusX * 0.82f;
                float magnetBottomOffset = radiusY * 1.25f;
                float cornerRadius = magnetHeight * 0.15f;
                float topCompression = 0.94f;
                float bottomExpansion = 1.06f;
                float perspectiveScale = 0.88f;

                using var clipPath = new SKPath();
                float topY = center.Y;
                float bottomY = center.Y + radiusY * 1.3f;
                float p1X = center.X - radiusX * 1.1f;
                float p2X = center.X + radiusX * 1.1f;
                float p3X = center.X - radiusX * 0.39f;
                float p4X = center.X + radiusX * 0.39f;

                clipPath.MoveTo(p1X, topY);
                clipPath.LineTo(p2X, topY);
                clipPath.QuadTo(new SKPoint(p2X, (topY + bottomY) * 0.5f), new SKPoint(p4X, bottomY));
                clipPath.LineTo(p3X, bottomY);
                clipPath.QuadTo(new SKPoint(p1X, (topY + bottomY) * 0.5f), new SKPoint(p1X, topY));
                clipPath.Close();

                canvas.Save();
                canvas.ClipPath(clipPath, SKClipOperation.Difference);

                SKRect magnetRect = new SKRect(
                    center.X - magnetWidth / 2,
                    (center.Y + magnetBottomOffset - magnetHeight * 0.1f) * topCompression,
                    center.X + magnetWidth / 2,
                    (center.Y + magnetBottomOffset + magnetHeight * 0.9f) * bottomExpansion
                );
                magnetRect = new SKRect(magnetRect.Left, magnetRect.Top, magnetRect.Right, magnetRect.Top + (magnetRect.Height * perspectiveScale));

                DrawMagnetShadow(canvas, magnetRect, magnetWidth);
                DrawMagnetBody(canvas, magnetRect, perspectiveScale);
                DrawMagnetHighlights(canvas, magnetRect, cornerRadius, perspectiveScale, topCompression, bottomExpansion);
                DrawMagnetShadows(canvas, magnetRect, perspectiveScale);
                DrawMagnetEdge(canvas, magnetRect, perspectiveScale);
                DrawMagnetConnections(canvas, magnetRect, magnetWidth, perspectiveScale);
                DrawMagnetDetails(canvas, magnetRect, magnetWidth, magnetHeight, cornerRadius, perspectiveScale);

                canvas.Restore();
            }

            private static void DrawMagnetShadow(SKCanvas canvas, SKRect magnetRect, float magnetWidth)
            {
                using var backgroundShadowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Shader = SKShader.CreateRadialGradient(
                        new SKPoint(magnetRect.MidX, magnetRect.MidY),
                        magnetWidth * 0.6f,
                        new SKColor[] { new SKColor(0, 0, 0, 45), new SKColor(0, 0, 0, 20), SKColors.Transparent },
                        new float[] { 0.0f, 0.6f, 1.0f },
                        SKShaderTileMode.Clamp
                    )
                };

                SKRect shadowRect = magnetRect;
                shadowRect.Offset(1.2f * STROKE_SCALE, 1.8f * STROKE_SCALE);
                shadowRect.Inflate(3.5f * STROKE_SCALE, 1.8f * STROKE_SCALE * (magnetRect.Height / magnetRect.Width));
                canvas.DrawOval(shadowRect, backgroundShadowPaint);
            }

            private static void DrawMagnetBody(SKCanvas canvas, SKRect magnetRect, float perspectiveScale)
            {
                using var magnetPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
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
                    gradientColors, gradientPositions, SKShaderTileMode.Clamp))
                {
                    magnetPaint.Shader = gradient;
                    canvas.DrawOval(magnetRect, magnetPaint);
                }

                using (var textureShader = SKShader.CreatePerlinNoiseFractalNoise(0.8f * perspectiveScale, 0.8f, 4, 0.0f))
                {
                    magnetPaint.Shader = SKShader.CreateCompose(magnetPaint.Shader, textureShader, SKBlendMode.Overlay);
                    canvas.DrawOval(magnetRect, magnetPaint);
                }
            }

            private static void DrawMagnetHighlights(SKCanvas canvas, SKRect magnetRect, float cornerRadius, float perspectiveScale, float topCompression, float bottomExpansion)
            {
                using var highlightPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.9f * STROKE_SCALE * perspectiveScale
                };

                highlightPaint.Color = new SKColor(255, 255, 255, 40);
                canvas.DrawArc(magnetRect, 160, 220, false, highlightPaint);

                highlightPaint.Color = new SKColor(255, 255, 255, 25);
                float sideOffset = cornerRadius * perspectiveScale;
                canvas.DrawLine(
                    magnetRect.Left + sideOffset, magnetRect.Top + sideOffset * topCompression,
                    magnetRect.Left + sideOffset, magnetRect.Bottom - sideOffset * bottomExpansion,
                    highlightPaint
                );
                canvas.DrawLine(
                    magnetRect.Right - sideOffset, magnetRect.Top + sideOffset * topCompression,
                    magnetRect.Right - sideOffset, magnetRect.Bottom - sideOffset * bottomExpansion,
                    highlightPaint
                );
            }

            private static void DrawMagnetShadows(SKCanvas canvas, SKRect magnetRect, float perspectiveScale)
            {
                using var shadowPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

                shadowPaint.Color = new SKColor(0, 0, 0, 60);
                shadowPaint.StrokeWidth = 1.3f * STROKE_SCALE * perspectiveScale;
                canvas.DrawArc(magnetRect, -35, 250, false, shadowPaint);

                shadowPaint.Color = new SKColor(0, 0, 0, 25);
                shadowPaint.StrokeWidth = 2.0f * STROKE_SCALE * perspectiveScale;
                canvas.DrawArc(magnetRect, -25, 230, false, shadowPaint);
            }

            private static void DrawMagnetEdge(SKCanvas canvas, SKRect magnetRect, float perspectiveScale)
            {
                using var edgePaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.1f * STROKE_SCALE * perspectiveScale
                };

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

            private static void DrawMagnetConnections(SKCanvas canvas, SKRect magnetRect, float magnetWidth, float perspectiveScale)
            {
                using var connectionPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.8f * STROKE_SCALE * perspectiveScale
                };

                connectionPaint.Color = new SKColor(0, 0, 0, 40);
                float topOffset = magnetRect.Height * 0.05f;
                canvas.DrawLine(
                    magnetRect.Left + magnetWidth * 0.1f, magnetRect.Top + topOffset,
                    magnetRect.Right - magnetWidth * 0.1f, magnetRect.Top + topOffset,
                    connectionPaint
                );

                connectionPaint.Color = new SKColor(0, 0, 0, 30);
                float sideOffset = magnetWidth * 0.05f;
                canvas.DrawLine(
                    magnetRect.Left + sideOffset, magnetRect.Top + magnetRect.Height * 0.2f,
                    magnetRect.Left + sideOffset, magnetRect.Bottom - magnetRect.Height * 0.2f,
                    connectionPaint
                );
                canvas.DrawLine(
                    magnetRect.Right - sideOffset, magnetRect.Top + magnetRect.Height * 0.2f,
                    magnetRect.Right - sideOffset, magnetRect.Bottom - magnetRect.Height * 0.2f,
                    connectionPaint
                );
            }

            private static void DrawMagnetDetails(SKCanvas canvas, SKRect magnetRect, float magnetWidth, float magnetHeight, float cornerRadius, float perspectiveScale)
            {
                using var detailPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.5f * STROKE_SCALE * perspectiveScale
                };

                // Horizontal lines
                detailPaint.Color = new SKColor(255, 255, 255, 15);
                for (int i = 1; i <= 3; i++)
                {
                    float y = magnetRect.Top + (magnetRect.Height * i / 4);
                    float horizontalScale = 1.0f - (i * 0.05f);
                    canvas.DrawLine(
                        magnetRect.Left + magnetWidth * (0.15f / horizontalScale),
                        y,
                        magnetRect.Right - magnetWidth * (0.15f / horizontalScale),
                        y,
                        detailPaint
                    );
                }

                // Corner accents
                detailPaint.Color = new SKColor(0, 0, 0, 25);
                float accentLength = magnetHeight * 0.3f * perspectiveScale;

                // Top corners
                DrawCornerAccent(canvas, magnetRect.Left, magnetRect.Top, cornerRadius, accentLength * 1.2f, detailPaint);
                DrawCornerAccent(canvas, magnetRect.Right, magnetRect.Top, cornerRadius, -accentLength * 1.2f, detailPaint);

                // Bottom corners
                DrawCornerAccent(canvas, magnetRect.Left, magnetRect.Bottom, cornerRadius, accentLength * 0.8f, detailPaint);
                DrawCornerAccent(canvas, magnetRect.Right, magnetRect.Bottom, cornerRadius, -accentLength * 0.8f, detailPaint);
            }

            private static void DrawCornerAccent(SKCanvas canvas, float x, float y, float cornerRadius, float length, SKPaint paint)
            {
                canvas.DrawLine(x + (length > 0 ? cornerRadius : -cornerRadius), y + cornerRadius, x + cornerRadius + length, y + cornerRadius, paint);
            }
        }

        #endregion
    }
}