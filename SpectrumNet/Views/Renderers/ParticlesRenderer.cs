#nullable enable

using SpectrumNet.DataSettings;
using SpectrumNet.Service.Enums;

namespace SpectrumNet.Views.Renderers;

/// <summary>
/// Renderer that visualizes spectrum data as animated particles.
/// </summary>
public sealed class ParticlesRenderer : BaseSpectrumRenderer
{
    #region Singleton Pattern
    private static readonly Lazy<ParticlesRenderer> _instance = new(() => new ParticlesRenderer());
    private ParticlesRenderer()
    {
        PrecomputeAlphaCurve();
        InitializeVelocityLookup(Settings.ParticleVelocityMin);
        _processingThread = new Thread(ProcessSpectrumThreadFunc) { IsBackground = true, Name = "ParticlesProcessor" };
        _processingRunning = true;
        _processingThread.Start();
    }
    public static ParticlesRenderer GetInstance() => _instance.Value;
    #endregion

    #region Constants
    private static class Constants
    {
        // Logging
        public const string LOG_PREFIX = "ParticlesRenderer";

        // Rendering constants
        public const float DEFAULT_LINE_WIDTH = 3f;
        public const float GLOW_INTENSITY = 0.3f;
        public const float HIGH_MAGNITUDE_THRESHOLD = 0.7f;
        public const float OFFSET = 10f;
        public const float BASELINE_OFFSET = 2f;

        // Spectrum processing constants
        public const float SMOOTHING_FACTOR_NORMAL = 0.3f;
        public const float SMOOTHING_FACTOR_OVERLAY = 0.5f;
        public const int MAX_SAMPLE_POINTS = 256;

        // Velocity lookup
        public const int VELOCITY_LOOKUP_SIZE = 1024;
    }
    #endregion

    #region Fields
    // Thread-local random for thread safety
    [ThreadStatic] private static Random? _threadLocalRandom;

    // Particle system state
    private CircularParticleBuffer? _particleBuffer;
    private RenderCache _renderCache = new();
    private Settings Settings => Settings.Instance;

    // Spectrum processing
    private float[]? _spectrumBuffer;
    private float[]? _velocityLookup;
    private float[]? _alphaCurve;
    private readonly Thread _processingThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly AutoResetEvent _spectrumDataAvailable = new(false);
    private readonly AutoResetEvent _processingComplete = new(false);
    private readonly object _spectrumLock = new();
    private float[]? _spectrumToProcess;
    private int _spectrumLength;
    private bool _processingRunning;
    private float _spawnY;
    private int _canvasWidth;
    private float _barWidth;

    // Quality settings
    private new bool _useAntiAlias = true;
    private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private new bool _useAdvancedEffects = true;
    private int _sampleCount = 2;
    private float _smoothingFactor = Constants.SMOOTHING_FACTOR_NORMAL;

    public readonly bool _isOverlayActive;

    // Caching
    private SKPicture? _cachedPicture;
    private new bool _disposed;
    #endregion

    #region Initialization and Configuration
    /// <summary>
    /// Initializes the particles renderer and prepares resources for rendering.
    /// </summary>
    public override void Initialize()
    {
        Safe(() =>
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ParticlesRenderer));
            if (_isInitialized) return;

            base.Initialize();

            _particleBuffer = new CircularParticleBuffer(
                Settings.MaxParticles,
                Settings.ParticleLife,
                Settings.ParticleLifeDecay,
                Settings.VelocityMultiplier);

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
            _smoothingFactor = isOverlayActive ?
                Constants.SMOOTHING_FACTOR_OVERLAY :
                Constants.SMOOTHING_FACTOR_NORMAL;

            if (_quality != quality)
            {
                ApplyQualitySettings();
            }

            if (configChanged)
            {
                InvalidateCachedResources();
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
                    _useAntiAlias = false;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
                    _useAdvancedEffects = false;
                    _sampleCount = 1;
                    break;

                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _useAdvancedEffects = true;
                    _sampleCount = 2;
                    break;

                case RenderQuality.High:
                    _useAntiAlias = true;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _useAdvancedEffects = true;
                    _sampleCount = 4;
                    break;
            }

            InvalidateCachedResources();

            Log(LogLevel.Debug, Constants.LOG_PREFIX, $"Quality changed to {_quality}");
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
    }

    /// <summary>
    /// Invalidates cached rendering resources.
    /// </summary>
    private void InvalidateCachedResources()
    {
        _cachedPicture?.Dispose();
        _cachedPicture = null;
        RebuildShaders();
    }

    /// <summary>
    /// Rebuilds shader resources.
    /// </summary>
    private void RebuildShaders()
    {
        Log(LogLevel.Debug, Constants.LOG_PREFIX, "Rebuilding shaders.");
    }
    #endregion

    #region Rendering
    /// <summary>
    /// Renders the particles visualization on the canvas using spectrum data.
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
        if (!ValidateRenderParameters(canvas, spectrum, info, paint, drawPerformanceInfo))
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
            // Update render cache dimensions
            _renderCache.Width = info.Width;
            _renderCache.Height = info.Height;
            UpdateRenderCacheBounds(info.Height);
            _renderCache.StepSize = barCount > 0 ? info.Width / barCount : 0f;
            _barWidth = barWidth;

            // Process spectrum data and update particles
            ProcessSpectrumData(spectrum!, _sampleCount);
            UpdateParticles(_renderCache.UpperBound);

            // Render particles with current quality settings
            RenderWithQualitySettings(canvas!, info, paint!);
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
    [MethodImpl(AggressiveInlining)]
    private bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo) =>
        !_disposed &&
        _isInitialized &&
        canvas != null &&
        spectrum != null &&
        spectrum.Length >= 2 &&
        paint != null &&
        drawPerformanceInfo != null &&
        info.Width > 0 &&
        info.Height > 0;

    /// <summary>
    /// Renders particles with current quality settings.
    /// </summary>
    private void RenderWithQualitySettings(SKCanvas canvas, SKImageInfo info, SKPaint externalPaint)
    {
        using var paint = externalPaint.Clone();
        paint.IsAntialias = _useAntiAlias;

        // Render basic visualization
        RenderBasicVisualization(canvas, paint);

        // Apply advanced effects if enabled
        if (_useAdvancedEffects)
        {
            // Advanced effects disabled for now
        }
    }

    /// <summary>
    /// Renders the basic particle visualization.
    /// </summary>
    private void RenderBasicVisualization(SKCanvas canvas, SKPaint paint)
    {
        RenderParticles(canvas, paint, _renderCache.UpperBound, _renderCache.LowerBound);
    }

    /// <summary>
    /// Renders all active particles to the canvas.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private void RenderParticles(SKCanvas canvas, SKPaint paint, float upperBound, float lowerBound)
    {
        if (_particleBuffer == null)
            return;

        var activeParticles = _particleBuffer.GetActiveParticles();
        int count = activeParticles.Length;

        if (count == 0)
            return;

        SKColor originalColor = paint.Color;
        paint.Style = Fill;
        paint.StrokeCap = SKStrokeCap.Round;

        for (int i = 0; i < count; i++)
        {
            ref readonly var particle = ref activeParticles[i];

            // Skip particles outside visible bounds
            if (particle.Y < upperBound || particle.Y > lowerBound)
                continue;

            // Apply particle alpha to color
            SKColor particleColor = originalColor.WithAlpha((byte)(particle.Alpha * 255));
            paint.Color = particleColor;

            // Draw particle as a circle
            canvas.DrawCircle(particle.X, particle.Y, particle.Size / 2, paint);
        }

        // Restore original color
        paint.Color = originalColor;
    }
    #endregion

    #region Spectrum Processing
    /// <summary>
    /// Processes spectrum data for visualization.
    /// </summary>
    private void ProcessSpectrumData(float[] spectrum, int sampleCount)
    {
        SubmitSpectrumForProcessing(spectrum, _renderCache.LowerBound, (int)_renderCache.Width, _barWidth);
    }

    /// <summary>
    /// Submits spectrum data for background processing.
    /// </summary>
    private void SubmitSpectrumForProcessing(float[] spectrum, float spawnY, int canvasWidth, float barWidth)
    {
        lock (_spectrumLock)
        {
            _spectrumToProcess = spectrum;
            _spectrumLength = spectrum.Length;
            _spawnY = spawnY;
            _canvasWidth = canvasWidth;
            _barWidth = barWidth;
        }

        _spectrumDataAvailable.Set();
        _processingComplete.WaitOne(5); // Short timeout to avoid blocking UI
    }

    /// <summary>
    /// Background thread function for processing spectrum data.
    /// </summary>
    private void ProcessSpectrumThreadFunc()
    {
        try
        {
            while (_processingRunning && !_cts.Token.IsCancellationRequested)
            {
                _spectrumDataAvailable.WaitOne();

                float[]? spectrumCopy;
                int spectrumLength;
                float spawnY;
                int canvasWidth;
                float barWidth;

                lock (_spectrumLock)
                {
                    if (_spectrumToProcess == null)
                    {
                        _processingComplete.Set();
                        continue;
                    }

                    spectrumCopy = _spectrumToProcess;
                    spectrumLength = _spectrumLength;
                    spawnY = _spawnY;
                    canvasWidth = _canvasWidth;
                    barWidth = _barWidth;
                }

                ProcessSpectrumAndSpawnParticles(
                    spectrumCopy.AsSpan(0, Min(spectrumLength, 2048)),
                    spawnY,
                    canvasWidth,
                    barWidth);

                _processingComplete.Set();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when canceling the thread
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, $"Error in processing thread: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes spectrum data and spawns particles based on magnitude.
    /// </summary>
    private void ProcessSpectrumAndSpawnParticles(ReadOnlySpan<float> spectrum, float spawnY, int canvasWidth, float barWidth)
    {
        if (_particleBuffer == null) return;

        // Get threshold and size based on overlay mode
        float threshold = _isOverlayActive ? Settings.SpawnThresholdOverlay : Settings.SpawnThresholdNormal;
        float baseSize = _isOverlayActive ? Settings.ParticleSizeOverlay : Settings.ParticleSizeNormal;

        // Prepare spectrum buffer
        int targetCount = Min(spectrum.Length, 2048);
        if (_spectrumBuffer == null)
            _spectrumBuffer = ArrayPool<float>.Shared.Rent(targetCount);

        var spectrumBufferSpan = new Span<float>(_spectrumBuffer, 0, targetCount);
        ScaleSpectrum(spectrum, spectrumBufferSpan);

        // Calculate step size for particle positioning
        float xStep = _renderCache.StepSize;

        // Get thread-local random instance
        var rnd = _threadLocalRandom ??= new Random();

        // Process each spectrum value and spawn particles
        for (int i = 0; i < targetCount; i++)
        {
            float spectrumValue = spectrumBufferSpan[i];

            // Skip if below threshold
            if (spectrumValue <= threshold) continue;

            // Calculate density factor based on spectrum intensity
            float densityFactor = MathF.Min(spectrumValue / threshold, 3f);

            // Probabilistic spawning based on spectrum intensity
            if (rnd.NextDouble() >= densityFactor * Settings.SpawnProbability) continue;

            // Create and add new particle
            _particleBuffer.Add(new Particle
            {
                X = i * xStep + (float)rnd.NextDouble() * barWidth,
                Y = spawnY,
                Velocity = GetRandomVelocity() * densityFactor,
                Size = baseSize * densityFactor,
                Life = Settings.ParticleLife,
                Alpha = 1f,
                IsActive = true
            });
        }
    }

    /// <summary>
    /// Precomputes alpha curve for particle fading.
    /// </summary>
    private void PrecomputeAlphaCurve()
    {
        if (_alphaCurve == null) _alphaCurve = ArrayPool<float>.Shared.Rent(101);

        float step = 1f / (_alphaCurve.Length - 1);
        for (int i = 0; i < _alphaCurve.Length; i++)
            _alphaCurve[i] = (float)Pow(i * step, Settings.AlphaDecayExponent);
    }

    /// <summary>
    /// Initializes velocity lookup table for random velocity selection.
    /// </summary>
    private void InitializeVelocityLookup(float minVelocity)
    {
        if (_velocityLookup == null) _velocityLookup = ArrayPool<float>.Shared.Rent(Constants.VELOCITY_LOOKUP_SIZE);

        float velocityRange = Settings.ParticleVelocityMax - Settings.ParticleVelocityMin;
        for (int i = 0; i < Constants.VELOCITY_LOOKUP_SIZE; i++)
            _velocityLookup[i] = minVelocity + velocityRange * i / Constants.VELOCITY_LOOKUP_SIZE;
    }

    /// <summary>
    /// Updates all active particles' positions and properties.
    /// </summary>
    private void UpdateParticles(float upperBound)
    {
        if (_particleBuffer == null || _alphaCurve == null) return;
        _particleBuffer.Update(upperBound, _alphaCurve);
    }

    /// <summary>
    /// Updates render cache bounds based on canvas height and overlay mode.
    /// </summary>
    private void UpdateRenderCacheBounds(float height)
    {
        float overlayHeight = height * Settings.OverlayHeightMultiplier;

        if (_isOverlayActive)
        {
            _renderCache.UpperBound = height - overlayHeight;
            _renderCache.LowerBound = height;
            _renderCache.OverlayHeight = overlayHeight;
        }
        else
        {
            _renderCache.UpperBound = 0f;
            _renderCache.LowerBound = height;
            _renderCache.OverlayHeight = 0f;
        }
    }

    /// <summary>
    /// Gets a random velocity from the precomputed lookup table.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private float GetRandomVelocity()
    {
        if (_velocityLookup == null)
            throw new InvalidOperationException("Velocity lookup not initialized");

        var rnd = _threadLocalRandom ??= new Random();
        return _velocityLookup[rnd.Next(Constants.VELOCITY_LOOKUP_SIZE)] * Settings.VelocityMultiplier;
    }

    /// <summary>
    /// Scales spectrum data from source to destination span.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private static void ScaleSpectrum(ReadOnlySpan<float> source, Span<float> dest)
    {
        int srcLen = source.Length, destLen = dest.Length;
        if (srcLen == 0 || destLen == 0) return;

        float scale = srcLen / (float)destLen;
        for (int i = 0; i < destLen; i++)
            dest[i] = source[(int)(i * scale)];
    }

    /// <summary>
    /// Updates particle sizes based on current overlay mode.
    /// </summary>
    private void UpdateParticleSizes()
    {
        if (_particleBuffer == null) return;

        float baseSize = _isOverlayActive ? Settings.ParticleSizeOverlay : Settings.ParticleSizeNormal;
        float oldBaseSize = _isOverlayActive ? Settings.ParticleSizeNormal : Settings.ParticleSizeOverlay;

        foreach (ref var particle in _particleBuffer.GetActiveParticles())
        {
            float relativeSizeFactor = particle.Size / oldBaseSize;
            particle.Size = baseSize * relativeSizeFactor;
        }
    }
    #endregion

    #region Disposal
    /// <summary>
    /// Disposes of resources used by the renderer.
    /// </summary>
    public override void Dispose()
    {
        if (_disposed) return;

        Safe(() =>
        {
            // Stop processing thread
            _processingRunning = false;
            _cts.Cancel();
            _spectrumDataAvailable.Set();
            _processingThread.Join(100);

            // Dispose synchronization primitives
            _cts.Dispose();
            _spectrumDataAvailable.Dispose();
            _processingComplete.Dispose();

            // Return pooled arrays
            if (_spectrumBuffer != null)
            {
                ArrayPool<float>.Shared.Return(_spectrumBuffer);
                _spectrumBuffer = null;
            }

            if (_velocityLookup != null)
            {
                ArrayPool<float>.Shared.Return(_velocityLookup);
                _velocityLookup = null;
            }

            if (_alphaCurve != null)
            {
                ArrayPool<float>.Shared.Return(_alphaCurve);
                _alphaCurve = null;
            }

            // Clear other resources
            _cachedPicture?.Dispose();
            _cachedPicture = null;

            _particleBuffer = null;
            _renderCache = new RenderCache();

            // Call base implementation
            base.Dispose();
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Dispose",
            ErrorMessage = "Error during disposal"
        });

        _disposed = true;
        Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
    }
    #endregion
}

#region Helper Classes
/// <summary>
/// Circular buffer for efficient particle storage and updates.
/// </summary>
internal class CircularParticleBuffer
{
    private readonly Particle[] _buffer;
    private int _head, _tail, _count;
    private readonly int _capacity;
    private readonly float _particleLife, _particleLifeDecay, _velocityMultiplier;
    private readonly object _bufferLock = new();
    private readonly float _sizeDecayFactor = 0.95f;

    /// <summary>
    /// Initializes a new circular buffer for particles.
    /// </summary>
    public CircularParticleBuffer(int capacity, float particleLife, float particleLifeDecay, float velocityMultiplier)
    {
        _capacity = capacity;
        _buffer = new Particle[capacity];
        _particleLife = particleLife;
        _particleLifeDecay = particleLifeDecay;
        _velocityMultiplier = velocityMultiplier;
    }

    /// <summary>
    /// Adds a new particle to the buffer, replacing oldest if full.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    public void Add(Particle particle)
    {
        lock (_bufferLock)
        {
            if (_count >= _capacity)
            {
                // Buffer is full, replace oldest particle
                _buffer[_tail] = particle;
                _tail = (_tail + 1) % _capacity;
                _head = (_head + 1) % _capacity;
            }
            else
            {
                // Add to head and increment count
                _buffer[_head] = particle;
                _head = (_head + 1) % _capacity;
                _count++;
            }
        }
    }

    /// <summary>
    /// Updates all particles' positions and properties.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    public void Update(float upperBound, float[] alphaCurve)
    {
        lock (_bufferLock)
        {
            int currentTail = _tail;

            // Update each active particle
            for (int i = 0; i < _count; i++)
            {
                int index = (currentTail + i) % _capacity;
                ref Particle particle = ref _buffer[index];

                if (!particle.IsActive) continue;

                // Update position based on velocity
                particle.Y -= particle.Velocity * _velocityMultiplier;

                // Deactivate if out of bounds
                if (particle.Y <= upperBound)
                {
                    particle.IsActive = false;
                    continue;
                }

                // Reduce lifetime
                particle.Life -= _particleLifeDecay;
                if (particle.Life <= 0)
                {
                    particle.IsActive = false;
                    continue;
                }

                // Reduce size
                particle.Size *= _sizeDecayFactor;
                if (particle.Size < 0.5f)
                {
                    particle.IsActive = false;
                    continue;
                }

                // Update alpha based on remaining lifetime
                float lifeRatio = particle.Life / _particleLife;
                int alphaIndex = (int)(lifeRatio * (alphaCurve.Length - 1));
                alphaIndex = Clamp(alphaIndex, 0, alphaCurve.Length - 1);
                particle.Alpha = alphaCurve[alphaIndex];
            }

            // Remove inactive particles from the tail
            while (_count > 0 && !_buffer[_tail].IsActive)
            {
                _tail = (_tail + 1) % _capacity;
                _count--;
            }
        }
    }

    /// <summary>
    /// Gets a span of all active particles for rendering.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    public Span<Particle> GetActiveParticles()
    {
        lock (_bufferLock)
        {
            if (_count == 0) return Span<Particle>.Empty;

            Particle[] activeParticles = new Particle[_count];
            int currentTail = _tail;
            int activeCount = 0;

            // Copy only active particles to result array
            for (int i = 0; i < _count; i++)
            {
                int index = (currentTail + i) % _capacity;
                if (_buffer[index].IsActive)
                {
                    activeParticles[activeCount++] = _buffer[index];
                }
            }

            return new Span<Particle>(activeParticles, 0, activeCount);
        }
    }
}

/// <summary>
/// Represents a single particle in the visualization.
/// </summary>
internal struct Particle
{
    public float X;
    public float Y;
    public float Velocity;
    public float Size;
    public float Life;
    public float Alpha;
    public bool IsActive;
}

/// <summary>
/// Cache for render-related calculations and bounds.
/// </summary>
internal class RenderCache
{
    public float Width { get; set; }
    public float Height { get; set; }
    public float UpperBound { get; set; }
    public float LowerBound { get; set; }
    public float OverlayHeight { get; set; }
    public float StepSize { get; set; }
}
#endregion
