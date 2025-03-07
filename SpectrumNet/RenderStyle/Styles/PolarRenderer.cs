#nullable enable

namespace SpectrumNet
{
    public sealed class PolarRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants
        private static class Constants
        {
            // Rendering core properties
            public const float MinRadius = 30f;            // Minimum base radius for visualization
            public const float RadiusMultiplier = 200f;    // Multiplier for spectrum values to radius
            public const float InnerRadiusRatio = 0.5f;    // Ratio between inner and outer path radius
            public const float MaxSpectrumValue = 1.0f;    // Maximum clamped spectrum value
            public const float SpectrumScale = 2.0f;       // Scaling factor for raw spectrum data
            public const float ChangeThreshold = 0.01f;    // Minimum change to trigger path updates
            public const float DegToRad = (float)(Math.PI / 180.0); // Degrees to radians conversion

            // Animation properties
            public const float RotationSpeed = 0.3f;       // Speed of rotation animation
            public const float TimeStep = 0.016f;          // Time increment per frame (~60 FPS)
            public const float TimeModifier = 0.01f;       // Time scaling for rotation effect
            public const float ModulationFactor = 0.3f;    // Amplitude of radius modulation
            public const float ModulationFreq = 5f;        // Frequency of radius modulation
            public const float PulseSpeed = 2.0f;          // Speed of pulsation effect
            public const float PulseAmplitude = 0.2f;      // Amplitude of pulsation effect
            public const float DashPhaseSpeed = 0.5f;      // Animation speed for dash pattern

            // Visual elements
            public const float DefaultStrokeWidth = 1.5f;  // Base width for stroke paths
            public const float CenterCircleSize = 6f;      // Size of center circle element
            public const byte FillAlpha = 120;             // Alpha for gradient fill
            public const float DashLength = 6.0f;          // Length of dash segments
            public const float HighlightFactor = 1.4f;     // Color multiplier for highlights
            public const float GlowRadius = 8.0f;          // Radius of glow blur effect
            public const float GlowSigma = 2.5f;           // Sigma parameter for glow blur
            public const byte GlowAlpha = 80;              // Alpha for glow effect
            public const byte HighlightAlpha = 160;        // Alpha for highlight elements

            // Point counts and sampling
            public const int MaxPointCount = 120;          // Maximum number of points for rendering
            public const int PointCountOverlay = 80;       // Number of points in overlay mode
            public const int MinPointCount = 24;           // Minimum points for smooth visualization
            public const float MinBarWidth = 0.5f;         // Minimum width for visible bars
            public const float MaxBarWidth = 4.0f;         // Maximum width to limit GPU load

            // Quality-specific properties
            public static class Low
            {
                public const float SmoothingFactor = 0.10f;  // Spectrum smoothing intensity
                public const int MaxPoints = 40;             // Maximum number of points
                public const bool UseAntiAlias = false;      // Anti-aliasing setting
                public const bool UseAdvancedEffects = false; // Enable complex visual effects
                public const SKFilterQuality FilterQuality = SKFilterQuality.Low; // Texture filtering
                public const float StrokeMultiplier = 0.75f; // Stroke width scaling
                public const bool UseGlow = false;           // Enable glow effect
                public const bool UseHighlight = false;      // Enable highlight effect
                public const bool UsePulseEffect = false;    // Enable pulsation animation
                public const bool UseDashEffect = false;     // Enable dash pattern
                public const float PathSimplification = 0.5f; // Point reduction factor
            }

            public static class Medium
            {
                public const float SmoothingFactor = 0.15f;  // Spectrum smoothing intensity
                public const int MaxPoints = 80;             // Maximum number of points
                public const bool UseAntiAlias = true;       // Anti-aliasing setting
                public const bool UseAdvancedEffects = true; // Enable complex visual effects
                public const SKFilterQuality FilterQuality = SKFilterQuality.Medium; // Texture filtering
                public const float StrokeMultiplier = 1.0f;  // Stroke width scaling
                public const bool UseGlow = false;           // Enable glow effect
                public const bool UseHighlight = true;       // Enable highlight effect
                public const bool UsePulseEffect = true;     // Enable pulsation animation
                public const bool UseDashEffect = true;      // Enable dash pattern
                public const float PathSimplification = 0.25f; // Point reduction factor
            }

            public static class High
            {
                public const float SmoothingFactor = 0.20f;  // Spectrum smoothing intensity
                public const int MaxPoints = 120;            // Maximum number of points
                public const bool UseAntiAlias = true;       // Anti-aliasing setting
                public const bool UseAdvancedEffects = true; // Enable complex visual effects
                public const SKFilterQuality FilterQuality = SKFilterQuality.High; // Texture filtering
                public const float StrokeMultiplier = 1.25f; // Stroke width scaling
                public const bool UseGlow = true;            // Enable glow effect
                public const bool UseHighlight = true;       // Enable highlight effect
                public const bool UsePulseEffect = true;     // Enable pulsation animation
                public const bool UseDashEffect = true;      // Enable dash pattern
                public const float PathSimplification = 0.0f; // Point reduction factor
            }
        }
        #endregion

        #region Fields and Properties
        private static PolarRenderer? _instance;
        private bool _isInitialized, _isOverlayActive, _pathsNeedUpdate;
        private volatile bool _disposed;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        private readonly SemaphoreSlim _pathUpdateSemaphore = new(1, 1);

        // Spectrum data
        private float[]? _processedSpectrum, _previousSpectrum, _tempSpectrum;

        // Rendering resources
        private SKPath? _outerPath, _innerPath;
        private readonly ObjectPool<SKPath> _pathPool = new(() => new SKPath(), path => path.Reset(), 4);
        private SKPaint? _fillPaint, _strokePaint, _centerPaint, _glowPaint, _highlightPaint;
        private SKShader? _gradientShader;
        private SKPathEffect? _dashEffect;
        private SKImageFilter? _glowFilter;
        private SKPicture? _cachedCenterCircle;

        // Animation state
        private float _rotation, _time, _pulseEffect;

        // Configuration
        private int _currentPointCount;
        private RenderQuality _quality = RenderQuality.Medium;
        private float _smoothingFactor = Constants.Medium.SmoothingFactor;
        private SKFilterQuality _filterQuality = Constants.Medium.FilterQuality;
        private bool _useAntiAlias = Constants.Medium.UseAntiAlias;
        private bool _useAdvancedEffects = Constants.Medium.UseAdvancedEffects;
        private bool _useGlow = Constants.Medium.UseGlow;
        private bool _useHighlight = Constants.Medium.UseHighlight;
        private bool _usePulseEffect = Constants.Medium.UsePulseEffect;
        private bool _useDashEffect = Constants.Medium.UseDashEffect;
        private float _pathSimplification = Constants.Medium.PathSimplification;
        private float _strokeMultiplier = Constants.Medium.StrokeMultiplier;
        private int _maxPoints = Constants.Medium.MaxPoints;

        // SIMD vectors
        private Vector<float> _smoothingVec, _oneMinusSmoothing;

        // Drawing state
        private SKPoint[]? _outerPoints, _innerPoints;
        private SKColor _lastBaseColor;
        private SKRect _centerCircleBounds;
        private SKRect _clipBounds;

        // Performance tracking
        private int _frameCounter;
        private float _lastFrameTime;
        private float _avgFrameTime;
        private const int FrameAverageCount = 30;

        private const string LogPrefix = "PolarRenderer";

        /// <summary>
        /// Уровень качества рендеринга
        /// </summary>
        public RenderQuality Quality
        {
            get => _quality;
            set
            {
                if (_quality != value)
                {
                    _quality = value;
                    ApplyQualitySettings();

                    SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {_quality}");
                }
            }
        }
        #endregion

        #region Constructor and Initialization
        private PolarRenderer() { }

        public static PolarRenderer GetInstance() => _instance ??= new PolarRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                // Create paths with initial capacity
                _outerPath = _pathPool.Get();
                _innerPath = _pathPool.Get();

                InitializePoints();
                InitializePaints();
                InitializeSpectrum();

                _currentPointCount = Constants.MaxPointCount;
                _pathsNeedUpdate = true;

                _centerCircleBounds = new SKRect(
                    -Constants.CenterCircleSize * 1.5f,
                    -Constants.CenterCircleSize * 1.5f,
                    Constants.CenterCircleSize * 1.5f,
                    Constants.CenterCircleSize * 1.5f
                );

                UpdateCenterCircle(SKColors.White);

                _isInitialized = true;
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Initialized");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Initialization failed: {ex.Message}");
            }
        }

        private void InitializePoints()
        {
            _outerPoints = new SKPoint[Constants.MaxPointCount + 1];
            _innerPoints = new SKPoint[Constants.MaxPointCount + 1];
        }

        private void InitializePaints()
        {
            _fillPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = SKPaintStyle.Fill,
                FilterQuality = _filterQuality
            };

            _strokePaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Constants.DefaultStrokeWidth * _strokeMultiplier,
                StrokeJoin = SKStrokeJoin.Round,
                StrokeCap = SKStrokeCap.Round,
                FilterQuality = _filterQuality
            };

            _centerPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = SKPaintStyle.Fill,
                FilterQuality = _filterQuality
            };

            _glowPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Constants.DefaultStrokeWidth * 1.5f * _strokeMultiplier,
                FilterQuality = _filterQuality,
                BlendMode = SKBlendMode.SrcOver
            };

            _highlightPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Constants.DefaultStrokeWidth * 0.5f * _strokeMultiplier,
                StrokeCap = SKStrokeCap.Round,
                FilterQuality = _filterQuality
            };
        }

        private void InitializeSpectrum()
        {
            _processedSpectrum = new float[Constants.MaxPointCount];
            _previousSpectrum = new float[Constants.MaxPointCount];
            _tempSpectrum = new float[Constants.MaxPointCount];

            _smoothingVec = new Vector<float>(_smoothingFactor);
            _oneMinusSmoothing = new Vector<float>(1 - _smoothingFactor);
        }

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            bool overlayChanged = _isOverlayActive != isOverlayActive;
            bool qualityChanged = _quality != quality;

            _isOverlayActive = isOverlayActive;

            if (overlayChanged)
            {
                _currentPointCount = isOverlayActive
                    ? Constants.PointCountOverlay
                    : Math.Min(_maxPoints, Constants.MaxPointCount);
                _pathsNeedUpdate = true;
            }

            // Обновление качества (вызовет ApplyQualitySettings если нужно)
            if (qualityChanged)
            {
                Quality = quality;
            }

            // Сбросить кэши если любой параметр изменился
            if (overlayChanged || qualityChanged)
            {
                InvalidateCachedResources();
            }

            SmartLogger.Log(LogLevel.Debug, LogPrefix,
                $"Configured: Overlay={isOverlayActive}, Quality={quality}");
        }

        private void ApplyQualitySettings()
        {
            // Инвалидация кэшей перед изменением настроек
            InvalidateCachedResources();

            switch (_quality)
            {
                case RenderQuality.Low:
                    _smoothingFactor = Constants.Low.SmoothingFactor;
                    _maxPoints = Constants.Low.MaxPoints;
                    _useAntiAlias = Constants.Low.UseAntiAlias;
                    _useAdvancedEffects = Constants.Low.UseAdvancedEffects;
                    _filterQuality = Constants.Low.FilterQuality;
                    _strokeMultiplier = Constants.Low.StrokeMultiplier;
                    _useGlow = Constants.Low.UseGlow;
                    _useHighlight = Constants.Low.UseHighlight;
                    _usePulseEffect = Constants.Low.UsePulseEffect;
                    _useDashEffect = Constants.Low.UseDashEffect;
                    _pathSimplification = Constants.Low.PathSimplification;
                    break;

                case RenderQuality.Medium:
                    _smoothingFactor = Constants.Medium.SmoothingFactor;
                    _maxPoints = Constants.Medium.MaxPoints;
                    _useAntiAlias = Constants.Medium.UseAntiAlias;
                    _useAdvancedEffects = Constants.Medium.UseAdvancedEffects;
                    _filterQuality = Constants.Medium.FilterQuality;
                    _strokeMultiplier = Constants.Medium.StrokeMultiplier;
                    _useGlow = Constants.Medium.UseGlow;
                    _useHighlight = Constants.Medium.UseHighlight;
                    _usePulseEffect = Constants.Medium.UsePulseEffect;
                    _useDashEffect = Constants.Medium.UseDashEffect;
                    _pathSimplification = Constants.Medium.PathSimplification;
                    break;

                case RenderQuality.High:
                    _smoothingFactor = Constants.High.SmoothingFactor;
                    _maxPoints = Constants.High.MaxPoints;
                    _useAntiAlias = Constants.High.UseAntiAlias;
                    _useAdvancedEffects = Constants.High.UseAdvancedEffects;
                    _filterQuality = Constants.High.FilterQuality;
                    _strokeMultiplier = Constants.High.StrokeMultiplier;
                    _useGlow = Constants.High.UseGlow;
                    _useHighlight = Constants.High.UseHighlight;
                    _usePulseEffect = Constants.High.UsePulseEffect;
                    _useDashEffect = Constants.High.UseDashEffect;
                    _pathSimplification = Constants.High.PathSimplification;
                    break;
            }

            _currentPointCount = _isOverlayActive
                ? Constants.PointCountOverlay
                : Math.Min(_maxPoints, Constants.MaxPointCount);

            // Обновить векторы сглаживания
            _smoothingVec = new Vector<float>(_smoothingFactor);
            _oneMinusSmoothing = new Vector<float>(1 - _smoothingFactor);

            // Обновить настройки кистей
            if (_fillPaint != null && _strokePaint != null &&
                _centerPaint != null && _glowPaint != null && _highlightPaint != null)
            {
                _fillPaint.IsAntialias = _useAntiAlias;
                _fillPaint.FilterQuality = _filterQuality;

                _strokePaint.IsAntialias = _useAntiAlias;
                _strokePaint.FilterQuality = _filterQuality;
                _strokePaint.StrokeWidth = Constants.DefaultStrokeWidth * _strokeMultiplier;

                _centerPaint.IsAntialias = _useAntiAlias;
                _centerPaint.FilterQuality = _filterQuality;

                _glowPaint.IsAntialias = _useAntiAlias;
                _glowPaint.FilterQuality = _filterQuality;
                _glowPaint.StrokeWidth = Constants.DefaultStrokeWidth * 1.5f * _strokeMultiplier;

                _highlightPaint.IsAntialias = _useAntiAlias;
                _highlightPaint.FilterQuality = _filterQuality;
                _highlightPaint.StrokeWidth = Constants.DefaultStrokeWidth * 0.5f * _strokeMultiplier;
            }

            _pathsNeedUpdate = true;
        }

        private void InvalidateCachedResources()
        {
            _cachedCenterCircle?.Dispose();
            _cachedCenterCircle = null;

            _dashEffect?.Dispose();
            _dashEffect = null;

            _gradientShader?.Dispose();
            _gradientShader = null;

            _glowFilter?.Dispose();
            _glowFilter = null;

            _pathsNeedUpdate = true;
        }

        private void UpdateCenterCircle(SKColor baseColor)
        {
            if (_centerPaint == null) return;

            _cachedCenterCircle?.Dispose();

            float effectiveGlowRadius = _useGlow ? Constants.GlowRadius : Constants.GlowRadius * 0.5f;
            float effectiveGlowSigma = _useGlow ? Constants.GlowSigma : Constants.GlowSigma * 0.5f;

            _glowFilter?.Dispose();
            _glowFilter = SKImageFilter.CreateBlur(effectiveGlowRadius, effectiveGlowSigma);

            using (SKPictureRecorder recorder = new SKPictureRecorder())
            {
                SKCanvas pictureCanvas = recorder.BeginRecording(_centerCircleBounds);

                if (_useGlow)
                {
                    using (SKPaint glowPaint = new SKPaint
                    {
                        IsAntialias = _useAntiAlias,
                        Style = SKPaintStyle.Fill,
                        Color = baseColor.WithAlpha(Constants.GlowAlpha),
                        ImageFilter = _glowFilter
                    })
                    {
                        pictureCanvas.DrawCircle(0, 0, Constants.CenterCircleSize * 0.8f, glowPaint);
                    }
                }

                pictureCanvas.DrawCircle(0, 0, Constants.CenterCircleSize * 0.7f, _centerPaint);

                if (_useHighlight)
                {
                    using (SKPaint highlightPaint = new SKPaint
                    {
                        IsAntialias = _useAntiAlias,
                        Style = SKPaintStyle.Fill,
                        Color = SKColors.White.WithAlpha(Constants.HighlightAlpha)
                    })
                    {
                        pictureCanvas.DrawCircle(
                            -Constants.CenterCircleSize * 0.25f,
                            -Constants.CenterCircleSize * 0.25f,
                            Constants.CenterCircleSize * 0.2f,
                            highlightPaint);
                    }
                }

                _cachedCenterCircle = recorder.EndRecording();
            }
        }

        private void UpdateVisualEffects(SKColor baseColor)
        {
            if (_fillPaint == null || _strokePaint == null ||
                _glowPaint == null || _highlightPaint == null)
                return;

            // Оптимизация: проверяем, нужно ли обновлять эффекты
            bool colorChanged =
                baseColor.Red != _lastBaseColor.Red ||
                baseColor.Green != _lastBaseColor.Green ||
                baseColor.Blue != _lastBaseColor.Blue;

            if (!colorChanged && _gradientShader != null)
                return;

            _lastBaseColor = baseColor;

            // Градиент для заливки
            SKColor gradientStart = baseColor.WithAlpha(Constants.FillAlpha);
            SKColor gradientEnd = new SKColor(
                (byte)Math.Min(255, baseColor.Red * 0.7),
                (byte)Math.Min(255, baseColor.Green * 0.7),
                (byte)Math.Min(255, baseColor.Blue * 0.7),
                20);

            _gradientShader?.Dispose();
            _gradientShader = SKShader.CreateRadialGradient(
                new SKPoint(0, 0),
                Constants.MinRadius + Constants.MaxSpectrumValue * Constants.RadiusMultiplier,
                new[] { gradientStart, gradientEnd },
                SKShaderTileMode.Clamp);

            _fillPaint.Shader = _gradientShader;
            _strokePaint.Color = baseColor;

            // Свечение только при высоком качестве или по явному указанию
            if (_useGlow)
            {
                _glowFilter?.Dispose();
                _glowFilter = SKImageFilter.CreateBlur(Constants.GlowRadius, Constants.GlowSigma);
                _glowPaint.Color = baseColor.WithAlpha(Constants.GlowAlpha);
                _glowPaint.ImageFilter = _glowFilter;
            }

            // Подсветка 
            if (_useHighlight)
            {
                _highlightPaint.Color = new SKColor(
                    (byte)Math.Min(255, baseColor.Red * Constants.HighlightFactor),
                    (byte)Math.Min(255, baseColor.Green * Constants.HighlightFactor),
                    (byte)Math.Min(255, baseColor.Blue * Constants.HighlightFactor),
                    Constants.HighlightAlpha);
            }

            // Штриховой эффект только при определенных настройках качества
            if (_useDashEffect)
            {
                float[] intervals = { Constants.DashLength, Constants.DashLength * 2 };
                _dashEffect?.Dispose();
                _dashEffect = SKPathEffect.CreateDash(
                    intervals,
                    (_time * Constants.DashPhaseSpeed) % (Constants.DashLength * 3));
            }

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
            float frameStartTime = (float)DateTime.Now.Ticks / TimeSpan.TicksPerSecond;

            if (!ValidateRenderParams(canvas, spectrum, info, paint, drawPerformanceInfo))
                return;

            try
            {
                int safeBarCount = Math.Min(Math.Max(barCount, Constants.MinPointCount), _maxPoints);
                float safeBarWidth = Math.Clamp(barWidth, Constants.MinBarWidth, Constants.MaxBarWidth);

                // Быстрая проверка видимости области рендеринга
                float maxRadius = Constants.MinRadius + Constants.MaxSpectrumValue *
                    Constants.RadiusMultiplier * (1 + Constants.ModulationFactor);
                _clipBounds = new SKRect(
                    -maxRadius, -maxRadius,
                    maxRadius, maxRadius
                );

                // Проверка, находится ли область рендеринга в видимой части canvas
                SKRect canvasBounds = new SKRect(0, 0, info.Width, info.Height);
                if (canvas!.QuickReject(canvasBounds))
                    return;

                // Запуск обработки спектра в отдельном потоке
                Task processTask = Task.Run(() => ProcessSpectrum(spectrum!, safeBarCount));

                // Если пути нуждаются в обновлении, обновляем их (может быть выполнено в отдельном потоке)
                if (_pathsNeedUpdate)
                {
                    bool lockAcquired = _pathUpdateSemaphore.Wait(0);
                    if (lockAcquired)
                    {
                        try
                        {
                            UpdatePolarPaths(info, safeBarCount);
                            _pathsNeedUpdate = false;
                        }
                        finally
                        {
                            _pathUpdateSemaphore.Release();
                        }
                    }
                }

                // Дожидаемся завершения обработки спектра
                processTask.Wait();

                // Обновление визуальных эффектов на основе цвета базовой краски
                UpdateVisualEffects(paint!.Color);

                // Рендеринг с учетом качества
                RenderPolarGraph(canvas!, info, paint!, safeBarWidth);

                // Отображение информации о производительности
                drawPerformanceInfo?.Invoke(canvas!, info);

                // Отслеживание скорости кадров
                TrackFrameTime(frameStartTime);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Render error: {ex.Message}");
            }
        }

        private void RenderPolarGraph(SKCanvas canvas, SKImageInfo info, SKPaint basePaint, float barWidth)
        {
            if (_outerPath == null || _innerPath == null ||
                _fillPaint == null || _strokePaint == null ||
                _centerPaint == null || _cachedCenterCircle == null ||
                _glowPaint == null || _highlightPaint == null)
                return;

            // Update pulse effect animation
            if (_usePulseEffect)
            {
                _pulseEffect = (float)Math.Sin(_time * Constants.PulseSpeed) *
                               Constants.PulseAmplitude + 1.0f;
            }
            else
            {
                _pulseEffect = 1.0f;
            }

            // Adjust stroke widths based on pulse and bar width
            _strokePaint.StrokeWidth = barWidth * _pulseEffect * _strokeMultiplier;

            if (_useGlow)
            {
                _glowPaint.StrokeWidth = barWidth * 1.5f * _pulseEffect * _strokeMultiplier;
            }

            if (_useHighlight)
            {
                _highlightPaint.StrokeWidth = barWidth * 0.5f * _pulseEffect * _strokeMultiplier;
            }

            // Use animated dash effect for inner path when enabled
            if (_useDashEffect && _dashEffect != null)
            {
                _strokePaint.PathEffect = _dashEffect;
            }
            else
            {
                _strokePaint.PathEffect = null;
            }

            canvas.Save();
            canvas.Translate(info.Width / 2f, info.Height / 2f);
            canvas.RotateDegrees(_rotation);

            // Проверка видимости области рендеринга
            if (!canvas.QuickReject(_clipBounds))
            {
                // Оптимизация: в один DrawPicture для сложных эффектов при высоком качестве
                if (_useAdvancedEffects && _quality == RenderQuality.High)
                {
                    using (SKPictureRecorder recorder = new SKPictureRecorder())
                    {
                        SKCanvas pictureCanvas = recorder.BeginRecording(_clipBounds);

                        // Рисуем все эффекты на pictureCanvas
                        if (_useGlow)
                        {
                            pictureCanvas.DrawPath(_outerPath, _glowPaint);
                        }

                        pictureCanvas.DrawPath(_outerPath, _fillPaint);
                        pictureCanvas.DrawPath(_outerPath, _strokePaint);

                        SKPathEffect? originalEffect = _strokePaint.PathEffect;
                        _strokePaint.PathEffect = _dashEffect;
                        pictureCanvas.DrawPath(_innerPath, _strokePaint);
                        _strokePaint.PathEffect = originalEffect;

                        if (_useHighlight)
                        {
                            pictureCanvas.DrawPath(_innerPath, _highlightPaint);
                        }

                        using (SKPicture combinedPicture = recorder.EndRecording())
                        {
                            canvas.DrawPicture(combinedPicture);
                        }
                    }
                }
                else
                {
                    // Стандартное рендеринг с оптимизацией групп вызовов
                    if (_useGlow)
                    {
                        canvas.DrawPath(_outerPath, _glowPaint);
                    }

                    canvas.DrawPath(_outerPath, _fillPaint);
                    canvas.DrawPath(_outerPath, _strokePaint);

                    if (_useDashEffect && _dashEffect != null)
                    {
                        _strokePaint.PathEffect = _dashEffect;
                    }
                    canvas.DrawPath(_innerPath, _strokePaint);
                    _strokePaint.PathEffect = null;

                    if (_useHighlight)
                    {
                        canvas.DrawPath(_innerPath, _highlightPaint);
                    }
                }

                // Рисуем центральный круг с пульсацией
                float pulseScale = _usePulseEffect
                    ? 1.0f + (float)Math.Sin(_time * Constants.PulseSpeed * 0.5f) * 0.1f
                    : 1.0f;

                canvas.Save();
                canvas.Scale(pulseScale, pulseScale);
                canvas.DrawPicture(_cachedCenterCircle);
                canvas.Restore();
            }

            canvas.Restore();
        }

        private void TrackFrameTime(float frameStartTime)
        {
            float frameEndTime = (float)DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
            float frameTime = frameEndTime - frameStartTime;

            _lastFrameTime = frameTime;
            _avgFrameTime = (_avgFrameTime * _frameCounter + frameTime) / (_frameCounter + 1);

            _frameCounter = (_frameCounter + 1) % FrameAverageCount;

            // Динамическая адаптация качества на основе производительности
            // Раскомментировать при необходимости автоматической адаптации
            /*
            if (_frameCounter == 0 && _avgFrameTime > 0)
            {
                float targetFrameTime = 1.0f / 60.0f; // 60 FPS
                
                if (_avgFrameTime > targetFrameTime * 1.5f && _quality != RenderQuality.Low)
                {
                    // Понижаем качество если FPS слишком низкий
                    Quality = _quality == RenderQuality.High 
                        ? RenderQuality.Medium 
                        : RenderQuality.Low;
                }
                else if (_avgFrameTime < targetFrameTime * 0.5f && _quality != RenderQuality.High)
                {
                    // Повышаем качество если есть запас производительности
                    Quality = _quality == RenderQuality.Low 
                        ? RenderQuality.Medium 
                        : RenderQuality.High;
                }
            }
            */
        }
        #endregion

        #region Path Generation
        private void UpdatePolarPaths(SKImageInfo info, int barCount)
        {
            if (_processedSpectrum == null || _outerPath == null || _innerPath == null ||
                _outerPoints == null || _innerPoints == null)
                return;

            _time += Constants.TimeStep;
            _rotation += Constants.RotationSpeed * _time * Constants.TimeModifier;

            int effectivePointCount = Math.Min(barCount, _currentPointCount);

            // Применяем упрощение путей в зависимости от уровня качества
            int skipFactor = _pathSimplification > 0
                ? Math.Max(1, (int)(1.0f / (1.0f - _pathSimplification)))
                : 1;

            int actualPoints = effectivePointCount / skipFactor;
            float angleStep = 360f / actualPoints;

            for (int i = 0, pointIndex = 0; i <= effectivePointCount; i += skipFactor, pointIndex++)
            {
                float angle = pointIndex * angleStep * Constants.DegToRad;
                float cosAngle = (float)Math.Cos(angle);
                float sinAngle = (float)Math.Sin(angle);

                int spectrumIndex = i % effectivePointCount;
                float spectrumValue = spectrumIndex < _processedSpectrum.Length
                    ? _processedSpectrum[spectrumIndex]
                    : 0f;

                // Более простая модуляция при низком качестве
                float timeOffset = _time * 0.5f + pointIndex * 0.1f;
                float modulation;

                if (_quality == RenderQuality.Low)
                {
                    modulation = 1.0f;
                }
                else
                {
                    modulation = 1 + Constants.ModulationFactor *
                        (float)Math.Sin(pointIndex * angleStep * Constants.ModulationFreq * Constants.DegToRad + _time * 2);

                    if (_usePulseEffect)
                    {
                        modulation += Constants.PulseAmplitude * 0.5f * (float)Math.Sin(timeOffset);
                    }
                }

                float outerRadius = Constants.MinRadius + spectrumValue * modulation * Constants.RadiusMultiplier;
                if (pointIndex < _outerPoints.Length)
                {
                    _outerPoints[pointIndex] = new SKPoint(
                        outerRadius * cosAngle,
                        outerRadius * sinAngle
                    );
                }

                float innerSpectrumValue = spectrumValue * Constants.InnerRadiusRatio;
                float innerModulation;

                if (_quality == RenderQuality.Low)
                {
                    innerModulation = 1.0f;
                }
                else
                {
                    innerModulation = 1 + Constants.ModulationFactor *
                        (float)Math.Sin(pointIndex * angleStep * Constants.ModulationFreq * Constants.DegToRad + _time * 2 + Math.PI);

                    if (_usePulseEffect)
                    {
                        innerModulation += Constants.PulseAmplitude * 0.5f * (float)Math.Sin(timeOffset + Math.PI);
                    }
                }

                float innerRadius = Constants.MinRadius + innerSpectrumValue * innerModulation * Constants.RadiusMultiplier;
                if (pointIndex < _innerPoints.Length)
                {
                    _innerPoints[pointIndex] = new SKPoint(
                        innerRadius * cosAngle,
                        innerRadius * sinAngle
                    );
                }
            }

            _outerPath.Reset();
            _innerPath.Reset();

            try
            {
                int pointsToUse = Math.Min(actualPoints + 1, _outerPoints.Length);

                // Оптимизация: используем нативное добавление полигона вместо LINQ
                SKPoint[] outerPointsSlice = new SKPoint[pointsToUse];
                SKPoint[] innerPointsSlice = new SKPoint[pointsToUse];

                Array.Copy(_outerPoints, outerPointsSlice, pointsToUse);
                Array.Copy(_innerPoints, innerPointsSlice, pointsToUse);

                _outerPath.AddPoly(outerPointsSlice, true);
                _innerPath.AddPoly(innerPointsSlice, true);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to create path: {ex.Message}");

                // Fallback метод в случае ошибки
                _outerPath.Reset();
                _innerPath.Reset();

                for (int i = 0, pointIndex = 0; i <= effectivePointCount; i += skipFactor, pointIndex++)
                {
                    int safeIndex = Math.Min(pointIndex, _outerPoints.Length - 1);

                    if (pointIndex == 0)
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
        private void ProcessSpectrum(float[] spectrum, int barCount)
        {
            if (_disposed || _tempSpectrum == null || _previousSpectrum == null ||
                _processedSpectrum == null)
                return;

            try
            {
                _spectrumSemaphore.Wait();

                int pointCount = Math.Min(barCount, _currentPointCount);

                // Быстрое чтение спектра с учетом уровня качества - отсамплирование с интерполяцией
                ExtractSpectrumPoints(spectrum, pointCount);

                // Сглаживание спектра с использованием SIMD
                float maxChange = SmoothSpectrumSIMD(pointCount);

                if (maxChange > Constants.ChangeThreshold)
                    _pathsNeedUpdate = true;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing spectrum: {ex.Message}");
            }
            finally
            {
                _spectrumSemaphore.Release();
            }
        }

        private void ExtractSpectrumPoints(float[] spectrum, int pointCount)
        {
            if (_tempSpectrum == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Temporary spectrum array is null");
                return;
            }

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

                _tempSpectrum[i] = Math.Min(_tempSpectrum[i] * Constants.SpectrumScale, Constants.MaxSpectrumValue);
            }
        }

        private float SmoothSpectrumSIMD(int pointCount)
        {
            float maxChange = 0f;

            // Проверяем все массивы на null
            if (_tempSpectrum == null || _previousSpectrum == null || _processedSpectrum == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Spectrum data arrays are null");
                return maxChange;
            }

            // Проверка границ массивов
            int safePointCount = Math.Min(pointCount,
                Math.Min(_tempSpectrum.Length,
                    Math.Min(_previousSpectrum.Length, _processedSpectrum.Length)));

            // Используем SIMD для ускорения обработки
            if (Vector.IsHardwareAccelerated && safePointCount >= Vector<float>.Count)
            {
                for (int i = 0; i < safePointCount; i += Vector<float>.Count)
                {
                    int remaining = Math.Min(Vector<float>.Count, safePointCount - i);

                    if (remaining < Vector<float>.Count)
                    {
                        // Обрабатываем остаток обычным способом
                        for (int j = 0; j < remaining; j++)
                        {
                            float newValue = _previousSpectrum[i + j] * (1 - _smoothingFactor) +
                                           _tempSpectrum[i + j] * _smoothingFactor;
                            float change = Math.Abs(newValue - _previousSpectrum[i + j]);
                            maxChange = Math.Max(maxChange, change);
                            _processedSpectrum[i + j] = newValue;
                            _previousSpectrum[i + j] = newValue;
                        }
                    }
                    else
                    {
                        try
                        {
                            // SIMD-ускоренная обработка блока данных
                            Vector<float> current = new Vector<float>(_tempSpectrum, i);
                            Vector<float> previous = new Vector<float>(_previousSpectrum, i);
                            Vector<float> smoothed = previous * _oneMinusSmoothing + current * _smoothingVec;

                            // Вычисляем максимальное изменение для определения необходимости обновления пути
                            Vector<float> change = Vector.Abs(smoothed - previous);
                            float batchMaxChange = 0f;
                            for (int j = 0; j < Vector<float>.Count; j++)
                            {
                                if (change[j] > batchMaxChange)
                                    batchMaxChange = change[j];
                            }
                            maxChange = Math.Max(maxChange, batchMaxChange);

                            // Безопасное копирование результатов
                            smoothed.CopyTo(_processedSpectrum, i);
                            smoothed.CopyTo(_previousSpectrum, i);
                        }
                        catch (NullReferenceException)
                        {
                            SmartLogger.Log(LogLevel.Error, LogPrefix, "Null reference in SIMD processing");

                            // Fallback к обычному способу в случае ошибки
                            for (int j = 0; j < Vector<float>.Count && i + j < safePointCount; j++)
                            {
                                float newValue = _previousSpectrum[i + j] * (1 - _smoothingFactor) +
                                               _tempSpectrum[i + j] * _smoothingFactor;
                                float change = Math.Abs(newValue - _previousSpectrum[i + j]);
                                maxChange = Math.Max(maxChange, change);
                                _processedSpectrum[i + j] = newValue;
                                _previousSpectrum[i + j] = newValue;
                            }
                        }
                    }
                }
            }
            else
            {
                // Fallback для систем без SIMD
                for (int i = 0; i < safePointCount; i++)
                {
                    float newValue = _previousSpectrum[i] * (1 - _smoothingFactor) +
                                   _tempSpectrum[i] * _smoothingFactor;
                    float change = Math.Abs(newValue - _previousSpectrum[i]);
                    maxChange = Math.Max(maxChange, change);
                    _processedSpectrum[i] = newValue;
                    _previousSpectrum[i] = newValue;
                }
            }

            return maxChange;
        }
        #endregion

        #region Helper Methods
        private bool ValidateRenderParams(
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
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Not initialized before rendering");
                return false;
            }

            if (canvas == null ||
                spectrum == null || spectrum.Length < 2 ||
                paint == null ||
                drawPerformanceInfo == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Invalid render parameters");
                return false;
            }

            return true;
        }
        #endregion

        #region Disposal
        public void Dispose()
        {
            if (_disposed) return;

            // Освобождаем все семафоры
            _spectrumSemaphore.Dispose();
            _pathUpdateSemaphore.Dispose();

            // Освобождаем графические ресурсы
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

            // Освобождаем пул путей
            _pathPool.Dispose();

            // Зануляем ссылки
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
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Disposed");
        }
        #endregion
    }

    public class ObjectPool<T> : IDisposable where T : class
    {
        private readonly ConcurrentBag<T> _objects;
        private readonly Func<T> _objectGenerator;
        private readonly Action<T> _objectReset;

        public ObjectPool(Func<T> objectGenerator, Action<T> objectReset, int initialCount = 0)
        {
            _objects = new ConcurrentBag<T>();
            _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
            _objectReset = objectReset;

            // Предварительное заполнение пула
            for (int i = 0; i < initialCount; i++)
            {
                _objects.Add(_objectGenerator());
            }
        }

        public T Get()
        {
            if (_objects.TryTake(out T? item))
            {
                _objectReset?.Invoke(item);
                return item;
            }

            return _objectGenerator();
        }

        public void Return(T item)
        {
            _objects.Add(item);
        }

        public void Dispose()
        {
            if (typeof(IDisposable).IsAssignableFrom(typeof(T)))
            {
                foreach (var obj in _objects)
                {
                    (obj as IDisposable)?.Dispose();
                }

                _objects.Clear();
            }
        }
    }
}