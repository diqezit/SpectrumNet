#nullable enable

namespace SpectrumNet
{
    public sealed class FireRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static readonly Lazy<FireRenderer> _instance = new(() => new FireRenderer());
        private bool _isInitialized;
        private float[] _previousSpectrum = Array.Empty<float>();
        private float[]? _processedSpectrum;
        private readonly Random _random = new();
        private float _time;
        private readonly object _spectrumLock = new();
        private bool _disposed;
        private RenderQuality _quality = RenderQuality.Medium;
        private bool _isOverlayActive;

        // Quality-dependent settings
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;
        private int _sampleCount = 2;
        private float _pathSimplification = 0.2f;
        private int _maxDetailLevel = 4;

        // Object pools
        private readonly ObjectPool<SKPath> _pathPool = new(() => new SKPath(), path => path.Reset(), 10);
        private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 5);

        // Cached resources
        private SKPicture? _cachedBasePicture;
        private readonly SemaphoreSlim _renderSemaphore = new(1, 1);

        // Immutable settings
        private const string LogPrefix = "FireRenderer";
        #endregion

        #region Configuration
        private static class Config
        {
            // Time and animation
            public const float TimeStep = 0.016f;              // Time increment per frame (~60 FPS)
            public const float DecayRate = 0.08f;              // How quickly frequencies decrease when no longer present

            // Flame shape parameters
            public const float ControlPointProportion = 0.4f;  // Controls flame curvature 
            public const float RandomOffsetProportion = 0.5f;  // How much randomness in flame shape
            public const float RandomOffsetCenter = 0.25f;     // Center point for random distribution
            public const float FlameBottomProportion = 0.25f;  // Height of flame base relative to total height
            public const float FlameBottomMax = 6f;            // Maximum pixel height for flame base
            public const float MinBottomAlpha = 0.3f;          // Minimum opacity for flame base

            // Wave animation parameters  
            public const float WaveSpeed = 2.0f;               // Speed of wave animation
            public const float WaveAmplitude = 0.2f;           // Height of wave animation
            public const float HorizontalWaveFactor = 0.15f;   // Horizontal movement factor

            // Bezier curve control points
            public const float CubicControlPoint1 = 0.33f;     // First control point position (x-axis)
            public const float CubicControlPoint2 = 0.66f;     // Second control point position (x-axis)

            // Opacity animation parameters
            public const float OpacityWaveSpeed = 3.0f;        // Speed of opacity pulsing
            public const float OpacityPhaseShift = 0.2f;       // Phase offset between flames
            public const float OpacityWaveAmplitude = 0.1f;    // Amount of opacity variation
            public const float OpacityBase = 0.9f;             // Base opacity level

            // Positioning
            public const float PositionPhaseShift = 0.5f;      // Offset between flame positions
            public const int MinBarCount = 10;                 // Minimum number of flame columns

            // Effects
            public const float GlowIntensity = 0.3f;           // Intensity of glow effect
            public const float HighIntensityThreshold = 0.7f;  // Threshold to trigger glow effects

            // Quality settings (Low)
            public const float GlowRadiusLow = 1.5f;           // Blur radius for glow in low quality
            public const int MaxDetailLevelLow = 2;            // Detail level for low quality

            // Quality settings (Medium)
            public const float GlowRadiusMedium = 3f;          // Blur radius for glow in medium quality
            public const int MaxDetailLevelMedium = 4;         // Detail level for medium quality

            // Quality settings (High) 
            public const float GlowRadiusHigh = 5f;            // Blur radius for glow in high quality
            public const int MaxDetailLevelHigh = 8;           // Detail level for high quality

            // Performance optimization
            public const int SpectrumProcessingChunkSize = 128; // Batch size for parallel processing
        }
        #endregion

        #region Constructors and Instance Management
        private FireRenderer() { /*Private constructor for singleton pattern*/ }

        public static FireRenderer GetInstance() => _instance.Value;
        #endregion

        #region Public Methods
        public void Initialize()
        {
            try
            {
                _renderSemaphore.Wait();

                if (_isInitialized)
                    return;

                _isInitialized = true;
                _time = 0f;

                // Initial quality settings
                ApplyQualitySettings();

                SmartLogger.Log(LogLevel.Debug, LogPrefix, "FireRenderer initialized");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error initializing FireRenderer: {ex.Message}");
            }
            finally
            {
                _renderSemaphore.Release();
            }
        }

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            bool configChanged = _isOverlayActive != isOverlayActive || _quality != quality;

            // Update overlay mode
            _isOverlayActive = isOverlayActive;

            // Update quality if needed
            if (_quality != quality)
            {
                _quality = quality;
                ApplyQualitySettings();
            }

            // If config changed, invalidate cached resources
            if (configChanged)
            {
                InvalidateCachedResources();
            }

            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Configured: Overlay={isOverlayActive}, Quality={quality}");
        }

        public void Configure(bool isOverlayActive) => Configure(isOverlayActive, _quality);

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
            if (!ValidateRenderParameters(canvas, spectrum, info, basePaint))
                return;

            // Quick reject if canvas area is not visible
            if (canvas.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
                return;

            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _renderSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    _time += Config.TimeStep;
                    ProcessSpectrumData(spectrum!, barCount);
                }

                float[] renderSpectrum;
                lock (_spectrumLock)
                {
                    renderSpectrum = _processedSpectrum ??
                                     ProcessSpectrumSynchronously(spectrum!, barCount);
                }

                using var renderScope = new RenderScope(
                    this, canvas!, renderSpectrum, info, barWidth, barSpacing, barCount, basePaint!);
                renderScope.Execute(drawPerformanceInfo);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error rendering flames: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _renderSemaphore.Release();
                }
            }
        }

        private void ProcessSpectrumData(float[] spectrum, int barCount)
        {
            try
            {
                EnsureSpectrumBuffer(spectrum.Length);

                int spectrumLength = spectrum.Length;
                int actualBarCount = Math.Min(spectrumLength, barCount);

                // Process only at the quality-dependent sample rate
                float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, spectrumLength);

                UpdatePreviousSpectrum(spectrum);

                lock (_spectrumLock)
                {
                    _processedSpectrum = scaledSpectrum;
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing spectrum data: {ex.Message}");
            }
        }

        private float[] ProcessSpectrumSynchronously(float[] spectrum, int barCount)
        {
            int spectrumLength = spectrum.Length;
            int actualBarCount = Math.Min(spectrumLength, barCount);
            return ScaleSpectrum(spectrum, actualBarCount, spectrumLength);
        }

        private void EnsureSpectrumBuffer(int length)
        {
            if (_previousSpectrum.Length != length)
            {
                _previousSpectrum = new float[length];
            }
        }

        private float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;
            float[] localSpectrum = spectrum;

            if (System.Numerics.Vector.IsHardwareAccelerated && spectrumLength >= System.Numerics.Vector<float>.Count)
            {
                int chunkSize = Math.Min(Config.SpectrumProcessingChunkSize, targetCount);

                Parallel.For(0, (targetCount + chunkSize - 1) / chunkSize, chunkIndex =>
                {
                    int startIdx = chunkIndex * chunkSize;
                    int endIdx = Math.Min(startIdx + chunkSize, targetCount);

                    for (int i = startIdx; i < endIdx; i++)
                    {
                        float sum = 0;
                        int start = (int)(i * blockSize);
                        int end = (int)((i + 1) * blockSize);
                        end = Math.Min(end, spectrumLength);

                        if (end - start >= System.Numerics.Vector<float>.Count)
                        {
                            int vectorizableEnd = start + ((end - start) / System.Numerics.Vector<float>.Count) * System.Numerics.Vector<float>.Count;

                            System.Numerics.Vector<float> sumVector = System.Numerics.Vector<float>.Zero;
                            for (int j = start; j < vectorizableEnd; j += System.Numerics.Vector<float>.Count)
                            {
                                sumVector += new System.Numerics.Vector<float>(localSpectrum, j);
                            }

                            for (int j = 0; j < System.Numerics.Vector<float>.Count; j++)
                            {
                                sum += sumVector[j];
                            }

                            for (int j = vectorizableEnd; j < end; j++)
                            {
                                sum += localSpectrum[j];
                            }
                        }
                        else
                        {
                            for (int j = start; j < end; j++)
                            {
                                sum += localSpectrum[j];
                            }
                        }

                        scaledSpectrum[i] = sum / (end - start);
                    }
                });
            }
            else
            {
                Parallel.For(0, targetCount, i =>
                {
                    float sum = 0;
                    int start = (int)(i * blockSize);
                    int end = (int)((i + 1) * blockSize);
                    end = Math.Min(end, spectrumLength);

                    for (int j = start; j < end; j++)
                        sum += localSpectrum[j];

                    scaledSpectrum[i] = sum / (end - start);
                });
            }

            return scaledSpectrum;
        }

        private void UpdatePreviousSpectrum(float[] spectrum)
        {
            for (int i = 0; i < spectrum.Length; i++)
            {
                _previousSpectrum[i] = Math.Max(
                    spectrum[i],
                    _previousSpectrum[i] - Config.DecayRate
                );
            }
        }
        #endregion

        #region Quality Settings

        private void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _filterQuality = SKFilterQuality.Low;
                    _useAdvancedEffects = false;
                    _sampleCount = 1;
                    _pathSimplification = 0.5f;
                    _maxDetailLevel = Config.MaxDetailLevelLow;
                    break;

                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.Medium;
                    _useAdvancedEffects = true;
                    _sampleCount = 2;
                    _pathSimplification = 0.2f;
                    _maxDetailLevel = Config.MaxDetailLevelMedium;
                    break;

                case RenderQuality.High:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.High;
                    _useAdvancedEffects = true;
                    _sampleCount = 4;
                    _pathSimplification = 0.0f;
                    _maxDetailLevel = Config.MaxDetailLevelHigh;
                    break;
            }

            // Invalidate caches dependent on quality
            InvalidateCachedResources();
        }

        private void InvalidateCachedResources()
        {
            _cachedBasePicture?.Dispose();
            _cachedBasePicture = null;
        }
        #endregion

        #region Rendering Implementation
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
            private readonly SKPaint _glowPaint;
            private readonly float _glowRadius;
            private readonly int _maxDetailLevel;
            private readonly bool _useAdvancedEffects;

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

                // Инициализация рабочей кисти
                _workingPaint = renderer._paintPool.Get();
                _workingPaint.Color = basePaint.Color;
                _workingPaint.Style = basePaint.Style;
                _workingPaint.StrokeWidth = basePaint.StrokeWidth;
                _workingPaint.IsStroke = basePaint.IsStroke;
                _workingPaint.IsAntialias = renderer._useAntiAlias;
                _workingPaint.FilterQuality = renderer._filterQuality;
                _workingPaint.Shader = basePaint.Shader; // Прямое присваивание вместо CreateCopy

                // Инициализация кисти для свечения
                _glowPaint = renderer._paintPool.Get();
                _glowPaint.Color = basePaint.Color;
                _glowPaint.Style = basePaint.Style;
                _glowPaint.StrokeWidth = basePaint.StrokeWidth;
                _glowPaint.IsStroke = basePaint.IsStroke;
                _glowPaint.IsAntialias = renderer._useAntiAlias;
                _glowPaint.FilterQuality = renderer._filterQuality;

                // Настройка радиуса свечения в зависимости от качества
                switch (renderer._quality)
                {
                    case RenderQuality.Low:
                        _glowRadius = Config.GlowRadiusLow;
                        break;
                    case RenderQuality.Medium:
                        _glowRadius = Config.GlowRadiusMedium;
                        break;
                    case RenderQuality.High:
                        _glowRadius = Config.GlowRadiusHigh;
                        break;
                    default:
                        _glowRadius = Config.GlowRadiusMedium;
                        break;
                }

                _glowPaint.ImageFilter = SKImageFilter.CreateBlur(_glowRadius, _glowRadius);
                _maxDetailLevel = renderer._maxDetailLevel;
                _useAdvancedEffects = renderer._useAdvancedEffects;
            }

            public void Execute(Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
            {
                var actualBarCount = _spectrum.Length;
                var totalBarWidth = CalculateTotalBarWidth(actualBarCount);

                RenderFlames(actualBarCount, totalBarWidth);

                drawPerformanceInfo(_canvas, _info);
            }

            private float CalculateTotalBarWidth(int actualBarCount)
            {
                float totalBarWidth = _barWidth + _barSpacing;
                return actualBarCount < Config.MinBarCount
                    ? totalBarWidth * (float)Config.MinBarCount / actualBarCount
                    : totalBarWidth;
            }

            private void RenderFlames(int actualBarCount, float totalBarWidth)
            {
                _canvas.Save();
                _canvas.ClipRect(new SKRect(0, 0, _info.Width, _info.Height));

                var flameGroups = new List<(List<FlameParameters> Flames, float Intensity)>();
                var currentGroup = new List<FlameParameters>();
                float currentIntensity = 0;

                for (int i = 0; i < actualBarCount; i++)
                {
                    if (i >= _barCount) break;

                    var spectrumValue = _spectrum[i];
                    if (spectrumValue < 0.01f) continue;

                    var flameParams = CalculateFlameParameters(i, totalBarWidth, spectrumValue);
                    float intensity = flameParams.CurrentHeight / flameParams.CanvasHeight;

                    if (currentGroup.Count > 0 && Math.Abs(intensity - currentIntensity) > 0.2f)
                    {
                        flameGroups.Add((currentGroup, currentIntensity));
                        currentGroup = new List<FlameParameters>();
                    }

                    currentGroup.Add(flameParams);
                    currentIntensity = intensity;
                }

                if (currentGroup.Count > 0)
                    flameGroups.Add((currentGroup, currentIntensity));

                foreach (var group in flameGroups.OrderBy(g => g.Intensity))
                {
                    foreach (var flameParams in group.Flames)
                    {
                        RenderSingleFlame(flameParams);
                    }
                }

                _canvas.Restore();
            }

            private FlameParameters CalculateFlameParameters(int index, float totalBarWidth, float spectrumValue)
            {
                float x = index * totalBarWidth;
                float waveOffset = (float)Math.Sin(_renderer._time * Config.WaveSpeed + index * Config.PositionPhaseShift)
                    * Config.WaveAmplitude;
                float currentHeight = spectrumValue * _info.Height * (1 + waveOffset);
                float previousHeight = _renderer._previousSpectrum.Length > index ?
                                      _renderer._previousSpectrum[index] * _info.Height : 0;
                float baselinePosition = _info.Height;

                return new FlameParameters(
                    x, currentHeight, previousHeight,
                    _barWidth, _info.Height, index,
                    baselinePosition  
                );
            }

            private void RenderSingleFlame(FlameParameters parameters)
            {
                var path = _renderer._pathPool.Get();

                var (flameTop, flameBottom) = CalculateFlameVerticalPositions(parameters);
                var x = CalculateHorizontalPosition(parameters);

                // Skip rendering if the flame would be too small to be visible
                if (flameBottom - flameTop < 1)
                {
                    _renderer._pathPool.Return(path);
                    return;
                }

                // Only render glow for high intensity flames and when advanced effects are enabled
                if (_useAdvancedEffects &&
                    parameters.CurrentHeight / parameters.CanvasHeight > Config.HighIntensityThreshold)
                {
                    RenderFlameGlow(path, x, flameTop, flameBottom, parameters);
                }

                RenderFlameBase(path, x, flameBottom);
                RenderFlameBody(path, x, flameTop, flameBottom, parameters);

                _renderer._pathPool.Return(path);
            }

            private (float flameTop, float flameBottom) CalculateFlameVerticalPositions(FlameParameters parameters)
            {
                float flameTop = parameters.CanvasHeight - Math.Max(parameters.CurrentHeight, parameters.PreviousHeight);
                float flameBottom = parameters.CanvasHeight - Config.FlameBottomMax;

                return (flameTop, flameBottom);
            }

            private float CalculateHorizontalPosition(FlameParameters parameters)
            {
                float waveOffset = (float)Math.Sin(_renderer._time * Config.WaveSpeed +
                    parameters.Index * Config.PositionPhaseShift) *
                    (parameters.BarWidth * Config.HorizontalWaveFactor);
                return parameters.X + waveOffset;
            }

            private void RenderFlameBase(SKPath path, float x, float flameBottom)
            {
                path.Reset();
                path.MoveTo(x, flameBottom);
                path.LineTo(x + _barWidth, flameBottom);

                using var bottomPaint = _renderer._paintPool.Get();
                bottomPaint.Color = _workingPaint.Color.WithAlpha((byte)(255 * Config.MinBottomAlpha));
                bottomPaint.Style = _workingPaint.Style;
                bottomPaint.StrokeWidth = _workingPaint.StrokeWidth;
                bottomPaint.IsStroke = _workingPaint.IsStroke;
                bottomPaint.IsAntialias = _workingPaint.IsAntialias;
                bottomPaint.FilterQuality = _workingPaint.FilterQuality;
                bottomPaint.Shader = _workingPaint.Shader; 

                _canvas.DrawPath(path, bottomPaint);
            }

            private void RenderFlameGlow(SKPath path, float x, float flameTop, float flameBottom, FlameParameters parameters)
            {
                path.Reset();
                path.MoveTo(x, flameBottom);

                float height = flameBottom - flameTop;
                var controlPoints = CalculateControlPoints(x, flameBottom, height, parameters.BarWidth);

                path.CubicTo(
                    controlPoints.cp1X, controlPoints.cp1Y,
                    controlPoints.cp2X, controlPoints.cp2Y,
                    x + parameters.BarWidth, flameBottom
                );

                float intensity = parameters.CurrentHeight / parameters.CanvasHeight;
                byte glowAlpha = (byte)(255 * intensity * Config.GlowIntensity);
                _glowPaint.Color = _glowPaint.Color.WithAlpha(glowAlpha);

                _canvas.DrawPath(path, _glowPaint);
            }

            private void RenderFlameBody(SKPath path, float x, float flameTop, float flameBottom, FlameParameters parameters)
            {
                path.Reset();
                path.MoveTo(x, flameBottom);

                float height = flameBottom - flameTop;
                var controlPoints = CalculateControlPoints(x, flameBottom, height, parameters.BarWidth);

                path.CubicTo(
                    controlPoints.cp1X, controlPoints.cp1Y,
                    controlPoints.cp2X, controlPoints.cp2Y,
                    x + parameters.BarWidth, flameBottom
                );

                UpdatePaintForFlame(parameters);
                _canvas.DrawPath(path, _workingPaint);
            }

            private (float cp1X, float cp1Y, float cp2X, float cp2Y) CalculateControlPoints(
                float x, float flameBottom, float height, float barWidth)
            {
                float cp1Y = flameBottom - height * Config.CubicControlPoint1;
                float cp2Y = flameBottom - height * Config.CubicControlPoint2;

                // Add randomness based on quality level and detail
                float detailFactor = (float)_maxDetailLevel / Config.MaxDetailLevelHigh;
                float randomnessFactor = detailFactor * Config.RandomOffsetProportion;

                float randomOffset1 = (float)(_renderer._random.NextDouble() *
                    barWidth * randomnessFactor -
                    barWidth * Config.RandomOffsetCenter);
                float randomOffset2 = (float)(_renderer._random.NextDouble() *
                    barWidth * randomnessFactor -
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

            public void Dispose()
            {
                // Return paints to pool instead of disposing
                _renderer._paintPool.Return(_workingPaint);
                _renderer._paintPool.Return(_glowPaint);
            }
        }

        private readonly record struct FlameParameters(
            float X,
            float CurrentHeight,
            float PreviousHeight,
            float BarWidth,
            float CanvasHeight,
            int Index,
            float BaselinePosition
        );
        #endregion

        #region Object Pooling
        private class ObjectPool<T> where T : class
        {
            private readonly Func<T> _factory;
            private readonly Action<T>? _reset;
            private readonly ConcurrentQueue<T> _objects = new();
            private readonly int _initialSize;

            public ObjectPool(Func<T> factory, Action<T>? reset = null, int initialSize = 0)
            {
                _factory = factory;
                _reset = reset;
                _initialSize = initialSize;

                // Pre-populate pool
                for (int i = 0; i < initialSize; i++)
                {
                    _objects.Enqueue(factory());
                }
            }

            public T Get()
            {
                if (_objects.TryDequeue(out var item))
                {
                    return item;
                }

                return _factory();
            }

            public void Return(T item)
            {
                _reset?.Invoke(item);
                _objects.Enqueue(item);
            }

            public void Clear()
            {
                while (_objects.TryDequeue(out var item))
                {
                    if (item is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
        }
        #endregion

        #region Validation
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? basePaint)
        {
            if (canvas == null || spectrum == null || basePaint == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Invalid render parameters: null values");
                return false;
            }

            if (info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Invalid image dimensions: {info.Width}x{info.Height}");
                return false;
            }

            if (spectrum.Length == 0)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Empty spectrum data");
                return false;
            }

            return true;
        }
        #endregion

        #region Dispose Implementation
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _cachedBasePicture?.Dispose();
            _cachedBasePicture = null;

            _pathPool.Clear();
            _paintPool.Clear();

            _renderSemaphore.Dispose();
        }
        #endregion
    }
}