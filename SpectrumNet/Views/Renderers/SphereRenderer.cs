#nullable enable

using SpectrumNet.Service.Enums;

namespace SpectrumNet.Views.Renderers;

/// <summary>
/// Renderer that visualizes spectrum data as orbiting spheres around a center point.
/// </summary>
public sealed class SphereRenderer : BaseSpectrumRenderer
{
    #region Singleton Pattern
    private static readonly Lazy<SphereRenderer> _instance = new(() => new SphereRenderer());
    private SphereRenderer() { } // Приватный конструктор
    public static SphereRenderer GetInstance() => _instance.Value;
    #endregion

    #region Constants
    private static class Constants
    {
        // Logging
        public const string LOG_PREFIX = "SphereRenderer";

        // Spectrum processing
        public const float MIN_MAGNITUDE = 0.01f;
        public const float MAX_INTENSITY_MULTIPLIER = 3f;
        public const float MIN_ALPHA = 0.1f;
        public const float PI_OVER_180 = (float)(PI / 180);

        // Geometry
        public const float DEFAULT_RADIUS = 40f;
        public const float MIN_RADIUS = 1.0f;
        public const float DEFAULT_SPACING = 10f;
        public const int DEFAULT_COUNT = 8;

        // Quality presets
        public static readonly (float SmoothingFactor, bool AntiAlias, int SphereSegments) LOW_QUALITY =
            (0.1f, false, 0);
        public static readonly (float SmoothingFactor, bool AntiAlias, int SphereSegments) MEDIUM_QUALITY =
            (0.2f, true, 0);
        public static readonly (float SmoothingFactor, bool AntiAlias, int SphereSegments) HIGH_QUALITY =
            (0.3f, true, 8);

        // Configuration presets
        public static readonly (float Radius, float Spacing, int Count) DEFAULT_CONFIG =
            (DEFAULT_RADIUS, DEFAULT_SPACING, DEFAULT_COUNT);
        public static readonly (float Radius, float Spacing, int Count) OVERLAY_CONFIG =
            (20f, 5f, 16);

        // Performance
        public const int BATCH_SIZE = 128;
    }
    #endregion

    #region Fields
    // Object pools for efficient memory management
    private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 3);

    // Configuration state
    private bool _isOverlayActive;
    private new bool _disposed;
    private float _sphereRadius, _sphereSpacing;
    private int _sphereCount;

    // Cached data
    private float[]? _cosValues, _sinValues, _currentAlphas;
    private float[]? _processedSpectrum;

    // Synchronization and resources
    private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
    private readonly ThreadLocal<SKPath> _spherePath = new(() => new SKPath());

    // Quality settings
    private float _alphaSmoothingFactor = Constants.MEDIUM_QUALITY.SmoothingFactor;
    private new bool _useAntiAlias = Constants.MEDIUM_QUALITY.AntiAlias;
    private int _sphereSegments = Constants.MEDIUM_QUALITY.SphereSegments;
    private new RenderQuality _quality = RenderQuality.Medium;
    #endregion

    #region Initialization and Configuration
    /// <summary>
    /// Initializes the renderer and prepares resources for rendering.
    /// </summary>
    public override void Initialize()
    {
        Safe(() =>
        {
            base.Initialize();

            UpdateConfiguration(Constants.DEFAULT_CONFIG);
            ApplyQualitySettings();

            Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });
    }

    /// <summary>
    /// Configures the renderer with overlay status and quality settings.
    /// </summary>
    /// <param name="isOverlayActive">Indicates if the renderer is used in overlay mode.</param>
    /// <param name="quality">The rendering quality level.</param>
    public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
    {
        Safe(() =>
        {
            base.Configure(isOverlayActive, quality);

            bool configChanged = _isOverlayActive != isOverlayActive || _quality != quality;

            _isOverlayActive = isOverlayActive;
            UpdateConfiguration(isOverlayActive ? Constants.OVERLAY_CONFIG : Constants.DEFAULT_CONFIG);

            if (_quality != quality)
            {
                _quality = quality;
                ApplyQualitySettings();
            }

        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Configure",
            ErrorMessage = "Failed to configure renderer"
        });
    }

    /// <summary>
    /// Applies quality settings based on the current quality level.
    /// </summary>
    protected override void ApplyQualitySettings()
    {
        Safe(() =>
        {
            base.ApplyQualitySettings();

            switch (_quality)
            {
                case RenderQuality.Low:
                    (_alphaSmoothingFactor, _useAntiAlias, _sphereSegments) = Constants.LOW_QUALITY;
                    break;

                case RenderQuality.Medium:
                    (_alphaSmoothingFactor, _useAntiAlias, _sphereSegments) = Constants.MEDIUM_QUALITY;
                    break;

                case RenderQuality.High:
                    (_alphaSmoothingFactor, _useAntiAlias, _sphereSegments) = Constants.HIGH_QUALITY;
                    break;
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
    }

    /// <summary>
    /// Updates the configuration of the sphere renderer.
    /// </summary>
    private void UpdateConfiguration((float Radius, float Spacing, int Count) config)
    {
        Safe(() =>
        {
            (_sphereRadius, _sphereSpacing, _sphereCount) = config;
            _sphereRadius = Max(Constants.MIN_RADIUS, _sphereRadius);

            EnsureArrayCapacity(ref _currentAlphas, _sphereCount);
            PrecomputeTrigValues();
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdateConfiguration",
            ErrorMessage = "Failed to update configuration"
        });
    }

    /// <summary>
    /// Adjusts configuration based on canvas dimensions and bar parameters.
    /// </summary>
    private void AdjustConfiguration(int barCount, float barSpacing, int canvasWidth, int canvasHeight)
    {
        Safe(() =>
        {
            _sphereRadius = Max(5f, Constants.DEFAULT_RADIUS - barCount * 0.2f + barSpacing * 0.5f);
            _sphereSpacing = Max(2f, Constants.DEFAULT_SPACING - barCount * 0.1f + barSpacing * 0.3f);
            _sphereCount = Clamp(barCount / 2, 4, 64);

            float maxRadius = Min(canvasWidth, canvasHeight) / 2f - (_sphereRadius + _sphereSpacing);
            if (_sphereRadius > maxRadius)
                _sphereRadius = maxRadius;

            _sphereRadius = Max(Constants.MIN_RADIUS, _sphereRadius);

            EnsureArrayCapacity(ref _currentAlphas, _sphereCount);
            PrecomputeTrigValues();
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.AdjustConfiguration",
            ErrorMessage = "Failed to adjust configuration"
        });
    }
    #endregion

    #region Rendering
    /// <summary>
    /// Renders the visualization on the canvas using spectrum data.
    /// </summary>
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
        // Validate rendering parameters
        if (!ValidateRenderParameters(canvas, spectrum, info, paint))
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }

        // Quick reject if canvas area is not visible
        if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
        {
            drawPerformanceInfo?.Invoke(canvas, info);
            return;
        }

        Safe(() =>
        {
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
                    int sphereCount = Min(spectrum!.Length, _sphereCount);
                    float centerRadius = info.Height / 2f - (_sphereRadius + _sphereSpacing);

                    RenderSpheres(
                        canvas!,
                        _processedSpectrum,
                        sphereCount,
                        info.Width / 2f,
                        info.Height / 2f,
                        centerRadius,
                        paint!);
                }
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _spectrumSemaphore.Release();
                }
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Render",
            ErrorMessage = "Error during rendering"
        });

        // Draw performance info
        drawPerformanceInfo?.Invoke(canvas!, info);
    }

    /// <summary>
    /// Validates all render parameters before processing.
    /// </summary>
    private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint)
    {
        if (_disposed)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, "Renderer is disposed");
            return false;
        }

        if (canvas == null || spectrum == null || paint == null)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, "Invalid render parameters: null values");
            return false;
        }

        if (info.Width <= 0 || info.Height <= 0)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
            return false;
        }

        if (spectrum.Length < 2)
        {
            Log(LogLevel.Warning, Constants.LOG_PREFIX, "Insufficient spectrum data");
            return false;
        }

        return true;
    }
    #endregion

    #region Rendering Implementation
    /// <summary>
    /// Renders the spheres on the canvas.
    /// </summary>
    private void RenderSpheres(
        SKCanvas canvas,
        float[] spectrum,
        int sphereCount,
        float centerX,
        float centerY,
        float maxRadius,
        SKPaint paint)
    {
        if (!AreArraysValid(sphereCount))
            return;

        var alphaGroups = GetAlphaGroups(sphereCount, 5);

        if (_sphereSegments > 0)
        {
            // High quality rendering with gradient spheres
            foreach (var group in alphaGroups)
            {
                if (group.end <= group.start)
                    continue;

                float groupAlpha = group.alpha;
                var centerColor = paint.Color.WithAlpha((byte)(255 * groupAlpha));
                var edgeColor = paint.Color.WithAlpha(0);

                using var shader = SKShader.CreateRadialGradient(
                    new SKPoint(0, 0),
                    1.0f,
                    new[] { centerColor, edgeColor },
                    new[] { 0.0f, 1.0f },
                    SKShaderTileMode.Clamp);

                using var groupPaint = _paintPool.Get();
                groupPaint.Reset();
                groupPaint.Shader = shader;
                groupPaint.IsAntialias = _useAntiAlias;

                for (int i = group.start; i < group.end; i++)
                {
                    float magnitude = spectrum[i];

                    if (magnitude < Constants.MIN_MAGNITUDE)
                        continue;

                    float x = centerX + _cosValues![i] * maxRadius;
                    float y = centerY + _sinValues![i] * maxRadius;
                    float circleSize = MathF.Max(magnitude * _sphereRadius, 2f) + _sphereSpacing * 0.2f;

                    SKRect bounds = new(x - circleSize, y - circleSize, x + circleSize, y + circleSize);
                    if (canvas.QuickReject(bounds))
                        continue;

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
            // Simple sphere rendering for low and medium quality
            using var spherePaint = _paintPool.Get();
            spherePaint.Reset();
            spherePaint.IsAntialias = _useAntiAlias;

            foreach (var group in alphaGroups)
            {
                if (group.end <= group.start)
                    continue;

                spherePaint.Color = paint.Color.WithAlpha((byte)(255 * group.alpha));

                for (int i = group.start; i < group.end; i++)
                {
                    float magnitude = spectrum[i];

                    if (magnitude < Constants.MIN_MAGNITUDE)
                        continue;

                    float x = centerX + _cosValues![i] * maxRadius;
                    float y = centerY + _sinValues![i] * maxRadius;
                    float circleSize = MathF.Max(magnitude * _sphereRadius, 2f) + _sphereSpacing * 0.2f;

                    SKRect bounds = new(x - circleSize, y - circleSize, x + circleSize, y + circleSize);
                    if (canvas.QuickReject(bounds))
                        continue;

                    canvas.DrawCircle(x, y, circleSize, spherePaint);
                }
            }
        }
    }

    /// <summary>
    /// Groups spheres by alpha value for batch rendering optimization.
    /// </summary>
    private (int start, int end, float alpha)[] GetAlphaGroups(int length, int maxGroups)
    {
        if (_currentAlphas == null || length == 0)
            return Array.Empty<(int, int, float)>();

        List<(int start, int end, float alpha)> groups = new(maxGroups);

        int currentStart = 0;
        float currentAlpha = _currentAlphas[0];

        for (int i = 1; i < length; i++)
        {
            if (Abs(_currentAlphas[i] - currentAlpha) > 0.1f ||
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

    #region Spectrum Processing
    /// <summary>
    /// Processes spectrum data for visualization.
    /// </summary>
    private void ProcessSpectrum(float[] spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount)
    {
        AdjustConfiguration(barCount, barSpacing, info.Width, info.Height);
        int sphereCount = Min(spectrum.Length, _sphereCount);

        EnsureProcessedSpectrumCapacity(sphereCount);

        if (IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
        {
            ProcessSpectrumSIMD(spectrum, _processedSpectrum!, sphereCount);
        }
        else
        {
            ScaleSpectrum(spectrum, _processedSpectrum!, sphereCount);
        }

        UpdateAlphas(sphereCount);
    }

    /// <summary>
    /// Processes spectrum data using SIMD optimizations.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private void ProcessSpectrumSIMD(float[] source, float[] target, int targetCount)
    {
        float blockSize = source.Length / (float)targetCount;

        Parallel.For(0, targetCount, i =>
        {
            int start = (int)(i * blockSize);
            int end = (int)((i + 1) * blockSize);
            end = Min(end, source.Length);

            if (start >= end)
            {
                target[i] = 0;
                return;
            }

            float sum = 0;
            int count = end - start;

            int vectorSize = Vector<float>.Count;
            int vectorizableLength = (end - start) / vectorSize * vectorSize;

            for (int j = start; j < start + vectorizableLength; j += vectorSize)
            {
                Vector<float> vec = new Vector<float>(source, j);
                sum += Sum(vec);
            }

            for (int j = start + vectorizableLength; j < end; j++)
            {
                sum += source[j];
            }

            target[i] = sum / count;
        });
    }

    /// <summary>
    /// Ensures the processed spectrum buffer has sufficient capacity.
    /// </summary>
    private void EnsureProcessedSpectrumCapacity(int requiredSize)
    {
        if (_processedSpectrum != null && _processedSpectrum.Length >= requiredSize)
            return;

        if (_processedSpectrum != null)
            ArrayPool<float>.Shared.Return(_processedSpectrum);

        _processedSpectrum = ArrayPool<float>.Shared.Rent(requiredSize);
    }

    /// <summary>
    /// Scales the spectrum data to the target count.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private static void ScaleSpectrum(float[] source, float[] target, int targetCount)
    {
        float blockSize = source.Length / (float)targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            int start = (int)(i * blockSize);
            int end = (int)((i + 1) * blockSize);
            end = Min(end, source.Length);

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

    /// <summary>
    /// Updates alpha values for smooth transitions.
    /// </summary>
    private void UpdateAlphas(int length)
    {
        if (_processedSpectrum == null || _currentAlphas == null || _currentAlphas.Length < length)
            return;

        for (int i = 0; i < length; i++)
        {
            float targetAlpha = MathF.Max(Constants.MIN_ALPHA, _processedSpectrum[i] * Constants.MAX_INTENSITY_MULTIPLIER);
            _currentAlphas[i] = _currentAlphas[i] +
                               (targetAlpha - _currentAlphas[i]) * _alphaSmoothingFactor;
        }
    }
    #endregion

    #region Helper Methods
    /// <summary>
    /// Precomputes trigonometric values for optimized rendering.
    /// </summary>
    private void PrecomputeTrigValues()
    {
        EnsureArrayCapacity(ref _cosValues, _sphereCount);
        EnsureArrayCapacity(ref _sinValues, _sphereCount);

        float angleStepRad = 360f / _sphereCount * Constants.PI_OVER_180;

        for (int i = 0; i < _sphereCount; i++)
        {
            float angle = i * angleStepRad;
            _cosValues![i] = MathF.Cos(angle);
            _sinValues![i] = MathF.Sin(angle);
        }
    }

    /// <summary>
    /// Checks if required arrays are valid and have sufficient capacity.
    /// </summary>
    private bool AreArraysValid(int requiredLength) =>
        _cosValues != null && _sinValues != null && _currentAlphas != null &&
        _cosValues.Length >= requiredLength &&
        _sinValues.Length >= requiredLength &&
        _currentAlphas.Length >= requiredLength;

    /// <summary>
    /// Ensures an array has sufficient capacity, reallocating if necessary.
    /// </summary>
    private static void EnsureArrayCapacity<T>(ref T[]? array, int requiredSize) where T : struct
    {
        if (array == null || array.Length < requiredSize)
            array = new T[requiredSize];
    }
    #endregion

    #region Disposal
    /// <summary>
    /// Disposes of resources used by the renderer.
    /// </summary>
    public override void Dispose()
    {
        if (!_disposed)
        {
            Safe(() =>
            {
                // Dispose synchronization primitives
                _spectrumSemaphore.Dispose();

                // Dispose thread local resources
                if (_spherePath.IsValueCreated && _spherePath.Value != null)
                {
                    _spherePath.Value.Dispose();
                }
                _spherePath.Dispose();

                // Return pooled arrays
                if (_processedSpectrum != null)
                    ArrayPool<float>.Shared.Return(_processedSpectrum);

                // Dispose object pools
                _paintPool.Dispose();

                // Clear references
                _cosValues = _sinValues = _currentAlphas = _processedSpectrum = null;

                // Call base disposal
                base.Dispose();

                Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Dispose",
                ErrorMessage = "Error during disposal"
            });

            _disposed = true;
        }
    }
    #endregion
}