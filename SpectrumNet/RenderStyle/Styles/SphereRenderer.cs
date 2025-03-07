#nullable enable

namespace SpectrumNet
{
    public sealed class SphereRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants
        private static class Constants
        {
            public const float MinMagnitude = 0.01f;
            public const float MaxIntensityMultiplier = 3f;
            public const float MinAlpha = 0.1f;
            public const float PiOver180 = (float)(Math.PI / 180);

            public const float DefaultRadius = 40f;
            public const float MinRadius = 1.0f;
            public const float DefaultSpacing = 10f;
            public const int DefaultCount = 8;

            public static readonly (float SmoothingFactor, bool AntiAlias, int SphereSegments) LowQuality =
                (0.1f, false, 0);
            public static readonly (float SmoothingFactor, bool AntiAlias, int SphereSegments) MediumQuality =
                (0.2f, true, 0);
            public static readonly (float SmoothingFactor, bool AntiAlias, int SphereSegments) HighQuality =
                (0.3f, true, 8);

            public static readonly (float Radius, float Spacing, int Count) DefaultConfig =
                (DefaultRadius, DefaultSpacing, DefaultCount);
            public static readonly (float Radius, float Spacing, int Count) OverlayConfig =
                (20f, 5f, 16);
        }
        #endregion

        #region Fields and Properties
        private static SphereRenderer? _instance;
        private bool _isInitialized;
        private bool _isOverlayActive;
        private volatile bool _disposed;

        private float _sphereRadius, _sphereSpacing;
        private int _sphereCount;
        private float[]? _cosValues, _sinValues, _currentAlphas;
        private float[]? _processedSpectrum;

        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        private readonly ThreadLocal<SKPath> _spherePath = new(() => new SKPath());

        private RenderQuality _quality = RenderQuality.Medium;
        private float _alphaSmoothingFactor = Constants.MediumQuality.SmoothingFactor;
        private bool _useAntiAlias = Constants.MediumQuality.AntiAlias;
        private int _sphereSegments = Constants.MediumQuality.SphereSegments;

        private const string LogPrefix = "SphereRenderer";

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

        #region Constructor and Instance Management
        private SphereRenderer() { }

        public static SphereRenderer GetInstance() => _instance ??= new SphereRenderer();
        #endregion

        #region ISpectrumRenderer Implementation
        public void Initialize()
        {
            if (_isInitialized) return;

            UpdateConfiguration(Constants.DefaultConfig);
            ApplyQualitySettings();
            _isInitialized = true;

            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Initialized");
        }

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            bool configChanged = _isOverlayActive != isOverlayActive || _quality != quality;

            _isOverlayActive = isOverlayActive;
            UpdateConfiguration(isOverlayActive ? Constants.OverlayConfig : Constants.DefaultConfig);

            Quality = quality;

            if (configChanged)
            {
                // Больше не требуется InvalidateCachedResources, так как кэширование удалено
            }

            SmartLogger.Log(LogLevel.Debug, LogPrefix,
                $"Configured: Overlay={isOverlayActive}, Quality={quality}");
        }

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

            if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
                return;

            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    ProcessSpectrum(spectrum!, info, barWidth, barSpacing, barCount);
                }

                if (_processedSpectrum != null)
                {
                    int sphereCount = Math.Min(spectrum!.Length, _sphereCount);
                    float centerRadius = info.Height / 2f - (_sphereRadius + _sphereSpacing);

                    RenderSpheres(
                        canvas!,
                        _processedSpectrum.AsSpan(0, sphereCount),
                        info.Width / 2f,
                        info.Height / 2f,
                        centerRadius,
                        paint!);
                }

                drawPerformanceInfo?.Invoke(canvas!, info);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Render error: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                    _spectrumSemaphore.Release();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _spectrumSemaphore.Dispose();

                if (_spherePath.IsValueCreated && _spherePath.Value != null)
                {
                    _spherePath.Value.Dispose();
                }
                _spherePath.Dispose();

                if (_processedSpectrum != null)
                    ArrayPool<float>.Shared.Return(_processedSpectrum);

                _cosValues = _sinValues = _currentAlphas = _processedSpectrum = null;
            }

            _disposed = true;
            _isInitialized = false;
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Disposed");
        }
        #endregion

        #region Quality Management
        private void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    (_alphaSmoothingFactor, _useAntiAlias, _sphereSegments) = Constants.LowQuality;
                    break;

                case RenderQuality.Medium:
                    (_alphaSmoothingFactor, _useAntiAlias, _sphereSegments) = Constants.MediumQuality;
                    break;

                case RenderQuality.High:
                    (_alphaSmoothingFactor, _useAntiAlias, _sphereSegments) = Constants.HighQuality;
                    break;
            }
        }
        #endregion

        #region Processing Methods
        private void ProcessSpectrum(float[] spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount)
        {
            AdjustConfiguration(barCount, barSpacing, info.Width, info.Height);
            int sphereCount = Math.Min(spectrum.Length, _sphereCount);

            EnsureProcessedSpectrumCapacity(sphereCount);

            if (Vector.IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
            {
                ProcessSpectrumSIMD(spectrum, _processedSpectrum!, sphereCount);
            }
            else
            {
                ScaleSpectrum(spectrum, _processedSpectrum!, sphereCount);
            }

            UpdateAlphas(_processedSpectrum!.AsSpan(0, sphereCount));
        }

        private void ProcessSpectrumSIMD(float[] source, float[] target, int targetCount)
        {
            float blockSize = source.Length / (float)targetCount;

            Parallel.For(0, targetCount, i =>
            {
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);
                end = Math.Min(end, source.Length);

                if (start >= end)
                {
                    target[i] = 0;
                    return;
                }

                float sum = 0;

                int vectorSize = Vector<float>.Count;
                int vectorizableLength = ((end - start) / vectorSize) * vectorSize;

                for (int j = start; j < start + vectorizableLength; j += vectorSize)
                {
                    Vector<float> vec = new Vector<float>(source, j);
                    sum += Vector.Sum(vec);
                }

                for (int j = start + vectorizableLength; j < end; j++)
                {
                    sum += source[j];
                }

                target[i] = sum / (end - start);
            });
        }

        private void EnsureProcessedSpectrumCapacity(int requiredSize)
        {
            if (_processedSpectrum != null && _processedSpectrum.Length >= requiredSize)
                return;

            if (_processedSpectrum != null)
                ArrayPool<float>.Shared.Return(_processedSpectrum);

            _processedSpectrum = ArrayPool<float>.Shared.Rent(requiredSize);
        }

        private void UpdateConfiguration((float Radius, float Spacing, int Count) config)
        {
            (_sphereRadius, _sphereSpacing, _sphereCount) = config;
            _sphereRadius = Math.Max(Constants.MinRadius, _sphereRadius);

            EnsureArrayCapacity(ref _currentAlphas, _sphereCount);
            PrecomputeTrigValues();
        }

        private void AdjustConfiguration(int barCount, float barSpacing, int canvasWidth, int canvasHeight)
        {
            try
            {
                _sphereRadius = Math.Max(5f, Constants.DefaultRadius - barCount * 0.2f + barSpacing * 0.5f);
                _sphereSpacing = Math.Max(2f, Constants.DefaultSpacing - barCount * 0.1f + barSpacing * 0.3f);
                _sphereCount = Math.Clamp(barCount / 2, 4, 64);

                float maxRadius = Math.Min(canvasWidth, canvasHeight) / 2f - (_sphereRadius + _sphereSpacing);
                if (_sphereRadius > maxRadius)
                    _sphereRadius = maxRadius;

                _sphereRadius = Math.Max(Constants.MinRadius, _sphereRadius);

                EnsureArrayCapacity(ref _currentAlphas, _sphereCount);
                PrecomputeTrigValues();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Configuration error: {ex.Message}");

                _sphereRadius = Constants.DefaultRadius;
                _sphereSpacing = Constants.DefaultSpacing;
                _sphereCount = Constants.DefaultCount;
            }
        }
        #endregion

        #region Rendering Methods
        private void RenderSpheres(
            SKCanvas canvas,
            ReadOnlySpan<float> spectrum,
            float centerX,
            float centerY,
            float maxRadius,
            SKPaint paint)
        {
            if (!AreArraysValid(spectrum.Length))
                return;

            if (_sphereSegments > 0) 
            {
                var alphaGroups = GetAlphaGroups(spectrum.Length, 5);
                foreach (var group in alphaGroups)
                {
                    if (group.end <= group.start) continue;
                    float groupAlpha = group.alpha;
                    var centerColor = paint.Color.WithAlpha((byte)(255 * groupAlpha));
                    var edgeColor = paint.Color.WithAlpha(0);
                    using var shader = SKShader.CreateRadialGradient(
                        new SKPoint(0, 0),
                        1.0f,
                        new[] { centerColor, edgeColor },
                        new[] { 0.0f, 1.0f },
                        SKShaderTileMode.Clamp);
                    using var groupPaint = paint.Clone();
                    groupPaint.Shader = shader;
                    groupPaint.IsAntialias = _useAntiAlias;
                    for (int i = group.start; i < group.end; i++)
                    {
                        float magnitude = spectrum[i];
                        if (magnitude < Constants.MinMagnitude) continue;
                        float x = centerX + _cosValues![i] * maxRadius;
                        float y = centerY + _sinValues![i] * maxRadius;
                        float circleSize = MathF.Max(magnitude * _sphereRadius, 2f) + _sphereSpacing * 0.2f;
                        SKRect bounds = new(x - circleSize, y - circleSize, x + circleSize, y + circleSize);
                        if (canvas.QuickReject(bounds)) continue;
                        canvas.Save();
                        canvas.Translate(x, y);
                        canvas.Scale(circleSize);
                        canvas.DrawCircle(0, 0, 1.0f, groupPaint);
                        canvas.Restore();
                    }
                }
            }
            else 
            {
                using var spherePaint = paint.Clone();
                spherePaint.IsAntialias = _useAntiAlias;
                var alphaGroups = GetAlphaGroups(spectrum.Length, 5);
                foreach (var group in alphaGroups)
                {
                    if (group.end <= group.start) continue;
                    spherePaint.Color = paint.Color.WithAlpha((byte)(255 * group.alpha));
                    for (int i = group.start; i < group.end; i++)
                    {
                        float magnitude = spectrum[i];
                        if (magnitude < Constants.MinMagnitude) continue;
                        float x = centerX + _cosValues![i] * maxRadius;
                        float y = centerY + _sinValues![i] * maxRadius;
                        float circleSize = MathF.Max(magnitude * _sphereRadius, 2f) + _sphereSpacing * 0.2f;
                        SKRect bounds = new(x - circleSize, y - circleSize, x + circleSize, y + circleSize);
                        if (canvas.QuickReject(bounds)) continue;
                        canvas.DrawCircle(x, y, circleSize, spherePaint);
                    }
                }
            }
        }

        private (int start, int end, float alpha)[] GetAlphaGroups(int length, int maxGroups)
        {
            List<(int start, int end, float alpha)> groups = new(maxGroups);

            if (_currentAlphas == null || length == 0)
                return Array.Empty<(int, int, float)>();

            int currentStart = 0;
            float currentAlpha = _currentAlphas[0];

            for (int i = 1; i < length; i++)
            {
                if (Math.Abs(_currentAlphas[i] - currentAlpha) > 0.1f ||
                    groups.Count >= maxGroups - 1)
                {
                    groups.Add((currentStart, i, currentAlpha));
                    currentStart = i;
                    currentAlpha = _currentAlphas[i];
                }
            }

            groups.Add((currentStart, length, currentAlpha));

            return groups.ToArray();
        }
        #endregion

        #region Helper Methods
        private void PrecomputeTrigValues()
        {
            EnsureArrayCapacity(ref _cosValues, _sphereCount);
            EnsureArrayCapacity(ref _sinValues, _sphereCount);

            float angleStepRad = 360f / _sphereCount * Constants.PiOver180;

            for (int i = 0; i < _sphereCount; i++)
            {
                float angle = i * angleStepRad;
                _cosValues![i] = MathF.Cos(angle);
                _sinValues![i] = MathF.Sin(angle);
            }
        }

        private static void ScaleSpectrum(float[] source, float[] target, int targetCount)
        {
            float blockSize = source.Length / (float)targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);
                end = Math.Min(end, source.Length);

                if (start >= end)
                {
                    target[i] = 0;
                    continue;
                }

                float sum = 0;
                for (int j = start; j < end; j++)
                    sum += source[j];

                target[i] = sum / (end - start);
            }
        }

        private void UpdateAlphas(ReadOnlySpan<float> spectrum)
        {
            if (_currentAlphas == null || _currentAlphas.Length < spectrum.Length)
                return;

            for (int i = 0; i < spectrum.Length; i++)
            {
                float targetAlpha = MathF.Max(Constants.MinAlpha, spectrum[i] * Constants.MaxIntensityMultiplier);
                _currentAlphas[i] = _currentAlphas[i] +
                                   (targetAlpha - _currentAlphas[i]) * _alphaSmoothingFactor;
            }
        }

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
                info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Invalid render parameters");
                return false;
            }

            return true;
        }

        private bool AreArraysValid(int requiredLength) =>
            _cosValues != null && _sinValues != null && _currentAlphas != null &&
            _cosValues.Length >= requiredLength &&
            _sinValues.Length >= requiredLength &&
            _currentAlphas.Length >= requiredLength;

        private static void EnsureArrayCapacity<T>(ref T[]? array, int requiredSize) where T : struct
        {
            if (array == null || array.Length < requiredSize)
                array = new T[requiredSize];
        }
        #endregion
    }
}