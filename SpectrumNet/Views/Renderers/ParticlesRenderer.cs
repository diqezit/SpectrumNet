#nullable enable

using static System.MathF;
using static SpectrumNet.Views.Renderers.ParticlesRenderer.Constants;

namespace SpectrumNet.Views.Renderers;

public sealed class ParticlesRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<ParticlesRenderer> _instance = new(() => new ParticlesRenderer());

    private struct Particle(
        float x,
        float y,
        float velocity,
        float size,
        float life,
        float alpha,
        bool isActive)
    {
        public float X = x;
        public float Y = y;
        public float Velocity = velocity;
        public float Size = size;
        public float Life = life;
        public float Alpha = alpha;
        public bool IsActive = isActive;
    }

    private class CircularParticleBuffer(
        int capacity,
        float particleLife,
        float particleLifeDecay,
        float velocityMultiplier)
    {
        private readonly Particle[] _buffer = new Particle[capacity];
        private int _head, _tail, _count;
        private readonly int _capacity = capacity;

        private readonly float 
            _particleLife = particleLife,
            _particleLifeDecay = particleLifeDecay,
            _velocityMultiplier = velocityMultiplier,
            _sizeDecayFactor = 0.95f;

        private readonly object _bufferLock = new();

        public void Add(Particle particle)
        {
            lock (_bufferLock)
            {
                if (_count >= _capacity)
                {
                    ReplaceOldestParticle(particle);
                }
                else
                {
                    AddNewParticle(particle);
                }
            }
        }

        private void ReplaceOldestParticle(Particle particle)
        {
            _buffer[_tail] = particle;
            _tail = (_tail + 1) % _capacity;
            _head = (_head + 1) % _capacity;
        }

        private void AddNewParticle(Particle particle)
        {
            _buffer[_head] = particle;
            _head = (_head + 1) % _capacity;
            _count++;
        }

        public void Update(float upperBound, float[] alphaCurve)
        {
            lock (_bufferLock)
            {
                UpdateActiveParticles(upperBound, alphaCurve);
                RemoveInactiveParticles();
            }
        }

        private void UpdateActiveParticles(float upperBound, float[] alphaCurve)
        {
            int currentTail = _tail;

            for (int i = 0; i < _count; i++)
            {
                int index = (currentTail + i) % _capacity;
                ref Particle particle = ref _buffer[index];

                if (!particle.IsActive) continue;

                UpdateParticlePosition(ref particle, upperBound);

                if (!particle.IsActive) continue;

                UpdateParticleLifetime(ref particle);

                if (!particle.IsActive) continue;

                UpdateParticleSize(ref particle);

                if (!particle.IsActive) continue;

                UpdateParticleAlpha(ref particle, alphaCurve);
            }
        }

        private void UpdateParticlePosition(ref Particle particle, float upperBound)
        {
            particle.Y -= particle.Velocity * _velocityMultiplier;

            if (particle.Y <= upperBound)
            {
                particle.IsActive = false;
            }
        }

        private void UpdateParticleLifetime(ref Particle particle)
        {
            particle.Life -= _particleLifeDecay;
            if (particle.Life <= 0)
            {
                particle.IsActive = false;
            }
        }

        private void UpdateParticleSize(ref Particle particle)
        {
            particle.Size *= _sizeDecayFactor;
            if (particle.Size < 0.5f)
            {
                particle.IsActive = false;
            }
        }

        private void UpdateParticleAlpha(ref Particle particle, float[] alphaCurve)
        {
            float lifeRatio = particle.Life / _particleLife;
            int alphaIndex = (int)(lifeRatio * (alphaCurve.Length - 1));
            alphaIndex = Clamp(alphaIndex, 0, alphaCurve.Length - 1);
            particle.Alpha = alphaCurve[alphaIndex];
        }

        private void RemoveInactiveParticles()
        {
            while (_count > 0 && !_buffer[_tail].IsActive)
            {
                _tail = (_tail + 1) % _capacity;
                _count--;
            }
        }

        public Span<Particle> GetActiveParticles()
        {
            lock (_bufferLock)
            {
                return CollectActiveParticles();
            }
        }

        private Span<Particle> CollectActiveParticles()
        {
            if (_count == 0) return [];

            Particle[] activeParticles = new Particle[_count];
            int currentTail = _tail;
            int activeCount = 0;

            for (int i = 0; i < _count; i++)
            {
                int index = (currentTail + i) % _capacity;
                if (_buffer[index].IsActive)
                {
                    activeParticles[activeCount++] = _buffer[index];
                }
            }

            return activeParticles.AsSpan(0, activeCount);
        }
    }

    private class RenderCache
    {
        public float Width { get; set; }
        public float Height { get; set; }
        public float UpperBound { get; set; }
        public float LowerBound { get; set; }
        public float OverlayHeight { get; set; }
        public float StepSize { get; set; }
    }

    private readonly struct ProcessingParameters(
        float[] spectrum,
        int spectrumLength,
        float spawnY,
        int canvasWidth,
        float barWidth)
    {
        public readonly float[] Spectrum = spectrum;
        public readonly int SpectrumLength = spectrumLength;
        public readonly float SpawnY = spawnY;
        public readonly int CanvasWidth = canvasWidth;
        public readonly float BarWidth = barWidth;
    }

    [ThreadStatic] private static Random? _threadLocalRandom;

    private CircularParticleBuffer? _particleBuffer;
    private readonly RenderCache _renderCache = new();
    private static Settings Settings => Settings.Instance;

    private float[]? _spectrumBuffer;
    private float[]? _velocityLookup;
    private float[]? _alphaCurve;
    private Thread? _processingThread;
    private CancellationTokenSource? _cts;
    private AutoResetEvent? _spectrumDataAvailable;
    private AutoResetEvent? _processingComplete;
    private readonly new object _spectrumLock = new();
    private float[]? _spectrumToProcess;
    private int _spectrumLength;
    private bool _processingRunning;
    private float _spawnY;
    private int _canvasWidth;
    private float _barWidth;

    private int _sampleCount = 2;
    private new bool _isOverlayActive;

    private SKPicture? _cachedPicture;

    private ParticlesRenderer() { }

    public static ParticlesRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "ParticlesRenderer";

        public const float
            DEFAULT_LINE_WIDTH = 3f,
            GLOW_INTENSITY = 0.3f,
            HIGH_MAGNITUDE_THRESHOLD = 0.7f,
            OFFSET = 10f,
            BASELINE_OFFSET = 2f;

        public const float
            SMOOTHING_FACTOR_NORMAL = 0.3f,
            SMOOTHING_FACTOR_OVERLAY = 0.5f;

        public const int
            MAX_SAMPLE_POINTS = 256,
            VELOCITY_LOOKUP_SIZE = 1024;

        public static class Quality
        {
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const int
                LOW_SAMPLE_COUNT = 1,
                MEDIUM_SAMPLE_COUNT = 2,
                HIGH_SAMPLE_COUNT = 4;
        }
    }

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                InitializeResources();
                StartProcessingThread();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            "OnInitialize",
            "Failed during renderer initialization"
        );
    }

    private void InitializeResources()
    {
        PrecomputeAlphaCurve();
        InitializeVelocityLookup(Settings.ParticleVelocityMin);
        InitializeParticleBuffer();
        InitializeSynchronizationObjects();
    }

    private void InitializeParticleBuffer()
    {
        _particleBuffer = new CircularParticleBuffer(
            Settings.MaxParticles,
            Settings.ParticleLife,
            Settings.ParticleLifeDecay,
            Settings.VelocityMultiplier);
    }

    private void InitializeSynchronizationObjects()
    {
        _cts = new CancellationTokenSource();
        _spectrumDataAvailable = new AutoResetEvent(false);
        _processingComplete = new AutoResetEvent(false);
    }

    private void StartProcessingThread()
    {
        _processingRunning = true;
        _processingThread = new Thread(ProcessSpectrumThreadFunc)
        {
            IsBackground = true,
            Name = "ParticlesProcessor"
        };
        _processingThread.Start();
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        ExecuteSafely(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;
                base.Configure(isOverlayActive, quality);

                _isOverlayActive = isOverlayActive;
                _smoothingFactor = isOverlayActive ? SMOOTHING_FACTOR_OVERLAY : SMOOTHING_FACTOR_NORMAL;

                if (configChanged)
                {
                    Log(LogLevel.Debug,
                        LOG_PREFIX,
                        $"Configuration changed. New Quality: {Quality}");
                    OnConfigurationChanged();
                }
            },
            "Configure",
            "Failed to configure renderer"
        );
    }

    protected override void OnQualitySettingsApplied()
    {
        ExecuteSafely(
            () =>
            {
                base.OnQualitySettingsApplied();
                ApplyQualitySpecificSettings();
                Log(LogLevel.Debug,
                    LOG_PREFIX,
                    $"Quality settings applied. New Quality: {Quality}");
            },
            "OnQualitySettingsApplied",
            "Failed to apply specific quality settings"
        );
    }

    private void ApplyQualitySpecificSettings()
    {
        SetQualityBasedParameters();
        InvalidateCachedResources();
    }

    private void SetQualityBasedParameters()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                _useAntiAlias = false;
                _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
                _useAdvancedEffects = Constants.Quality.LOW_USE_ADVANCED_EFFECTS;
                _sampleCount = Constants.Quality.LOW_SAMPLE_COUNT;
                break;

            case RenderQuality.Medium:
                _useAntiAlias = true;
                _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
                _sampleCount = Constants.Quality.MEDIUM_SAMPLE_COUNT;
                break;

            case RenderQuality.High:
                _useAntiAlias = true;
                _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
                _sampleCount = Constants.Quality.HIGH_SAMPLE_COUNT;
                break;
        }
    }

    private new void InvalidateCachedResources()
    {
        _cachedPicture?.Dispose();
        _cachedPicture = null;
    }

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        if (!ValidateRenderParameters(canvas, spectrum, info, paint)) return;

        ExecuteSafely(
            () =>
            {
                UpdateRenderCache(info, barCount, barWidth);
                ProcessSpectrumData(spectrum);
                UpdateParticles(_renderCache.UpperBound);
                RenderParticles(canvas, paint, _renderCache.UpperBound, _renderCache.LowerBound);
            },
            "RenderEffect",
            "Error during rendering"
        );
    }

    private void UpdateRenderCache(SKImageInfo info, int barCount, float barWidth)
    {
        _renderCache.Width = info.Width;
        _renderCache.Height = info.Height;
        UpdateRenderCacheBounds(info.Height);
        _renderCache.StepSize = barCount > 0 ? info.Width / barCount : 0f;
        _barWidth = barWidth;
    }

    private bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint)
    {
        if (!IsCanvasValid(canvas)) return false;
        if (!IsSpectrumValid(spectrum)) return false;
        if (!IsPaintValid(paint)) return false;
        if (!AreDimensionsValid(info)) return false;
        if (IsDisposed()) return false;
        return true;
    }

    private static bool IsCanvasValid(SKCanvas? canvas)
    {
        if (canvas != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Canvas is null");
        return false;
    }

    private static bool IsSpectrumValid(float[]? spectrum)
    {
        if (spectrum != null && spectrum.Length > 1) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Spectrum is null or too small");
        return false;
    }

    private static bool IsPaintValid(SKPaint? paint)
    {
        if (paint != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Paint is null");
        return false;
    }

    private static bool AreDimensionsValid(SKImageInfo info)
    {
        if (info.Width > 0 && info.Height > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
        return false;
    }

    private bool IsDisposed()
    {
        if (!_disposed) return false;
        Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
        return true;
    }

    private void ProcessSpectrumData(float[] spectrum) => 
        SubmitSpectrumForProcessing(spectrum, _renderCache.LowerBound, (int)_renderCache.Width, _barWidth);

    private void SubmitSpectrumForProcessing(
        float[] spectrum,
        float spawnY,
        int canvasWidth,
        float barWidth)
    {
        lock (_spectrumLock)
        {
            _spectrumToProcess = spectrum;
            _spectrumLength = spectrum.Length;
            _spawnY = spawnY;
            _canvasWidth = canvasWidth;
            _barWidth = barWidth;
        }

        _spectrumDataAvailable?.Set();
        _processingComplete?.WaitOne(5);
    }

    private void ProcessSpectrumThreadFunc()
    {
        try
        {
            RunProcessingLoop();
        }
        catch (OperationCanceledException)
        {
            // Expected when canceling the thread
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LOG_PREFIX, $"Error in processing thread: {ex.Message}");
        }
    }

    private void RunProcessingLoop()
    {
        while (_processingRunning && _cts != null && !_cts.Token.IsCancellationRequested)
        {
            _spectrumDataAvailable?.WaitOne();

            if (TryGetProcessingParameters(out var parameters))
            {
                ProcessSpectrumAndSpawnParticles(
                    parameters.Spectrum,
                    parameters.SpectrumLength,
                    parameters.SpawnY,
                    parameters.CanvasWidth,
                    parameters.BarWidth);
            }

            _processingComplete?.Set();
        }
    }

    private bool TryGetProcessingParameters(out ProcessingParameters parameters)
    {
        lock (_spectrumLock)
        {
            if (_spectrumToProcess == null)
            {
                parameters = default;
                return false;
            }

            parameters = new ProcessingParameters(
                _spectrumToProcess,
                _spectrumLength,
                _spawnY,
                _canvasWidth,
                _barWidth);

            return true;
        }
    }

    private void ProcessSpectrumAndSpawnParticles(
        float[] spectrum,
        int spectrumLength,
        float spawnY,
        int _, // canvasWidth
        float barWidth)
    {
        if (_particleBuffer == null) return;

        float threshold = _isOverlayActive ? Settings.SpawnThresholdOverlay : Settings.SpawnThresholdNormal;
        float baseSize = _isOverlayActive ? Settings.ParticleSizeOverlay : Settings.ParticleSizeNormal;

        int safeLength = Min(spectrumLength, 2048);
        PrepareSpectrumBuffer(safeLength);

        if (_spectrumBuffer == null) return;

        ScaleSpectrum(spectrum.AsSpan(0, safeLength), _spectrumBuffer.AsSpan(0, safeLength));

        float xStep = _renderCache.StepSize;
        var rnd = GetThreadLocalRandom();

        SpawnParticlesFromSpectrum(
            _spectrumBuffer.AsSpan(0, safeLength),
            spawnY,
            xStep,
            barWidth,
            threshold,
            baseSize,
            rnd);
    }

    private void PrepareSpectrumBuffer(int targetLength)
    {
        if (_spectrumBuffer == null || _spectrumBuffer.Length < targetLength)
            _spectrumBuffer = ArrayPool<float>.Shared.Rent(targetLength);
    }

    private void SpawnParticlesFromSpectrum(
        Span<float> spectrumBuffer,
        float spawnY,
        float xStep,
        float barWidth,
        float threshold,
        float baseSize,
        Random rnd)
    {
        for (int i = 0; i < spectrumBuffer.Length; i++)
        {
            TrySpawnParticleForValue(
                spectrumBuffer[i],
                i,
                xStep,
                barWidth,
                spawnY,
                threshold,
                baseSize,
                rnd);
        }
    }

    private void TrySpawnParticleForValue(
        float spectrumValue,
        int index,
        float xStep,
        float barWidth,
        float spawnY,
        float threshold,
        float baseSize,
        Random rnd)
    {
        if (spectrumValue <= threshold) return;

        float densityFactor = MathF.Min(spectrumValue / threshold, 3f);
        if (rnd.NextDouble() >= densityFactor * Settings.SpawnProbability) return;

        CreateParticle(index, xStep, barWidth, spawnY, densityFactor, baseSize, rnd);
    }

    private void CreateParticle(
        int index,
        float xStep,
        float barWidth,
        float spawnY,
        float densityFactor,
        float baseSize,
        Random rnd)
    {
        if (_particleBuffer == null) return;

        _particleBuffer.Add(new Particle(
            x: index * xStep + (float)rnd.NextDouble() * barWidth,
            y: spawnY,
            velocity: GetRandomVelocity() * densityFactor,
            size: baseSize * densityFactor,
            life: Settings.ParticleLife,
            alpha: 1f,
            isActive: true
        ));
    }

    private void PrecomputeAlphaCurve()
    {
        _alphaCurve ??= ArrayPool<float>.Shared.Rent(101);

        float step = 1f / (_alphaCurve.Length - 1);
        for (int i = 0; i < _alphaCurve.Length; i++)
            _alphaCurve[i] = (float)Pow(i * step, Settings.AlphaDecayExponent);
    }

    private void InitializeVelocityLookup(float minVelocity)
    {
        _velocityLookup ??= ArrayPool<float>.Shared.Rent(VELOCITY_LOOKUP_SIZE);

        float velocityRange = Settings.ParticleVelocityMax - Settings.ParticleVelocityMin;
        for (int i = 0; i < VELOCITY_LOOKUP_SIZE; i++)
            _velocityLookup[i] = minVelocity + velocityRange * i / VELOCITY_LOOKUP_SIZE;
    }

    private void UpdateParticles(float upperBound)
    {
        if (_particleBuffer == null || _alphaCurve == null) return;
        _particleBuffer.Update(upperBound, _alphaCurve);
    }

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

    private float GetRandomVelocity()
    {
        if (_velocityLookup == null)
            throw new InvalidOperationException("Velocity lookup not initialized");

        var rnd = GetThreadLocalRandom();
        return _velocityLookup[rnd.Next(VELOCITY_LOOKUP_SIZE)] * Settings.VelocityMultiplier;
    }

    private static Random GetThreadLocalRandom() =>
        _threadLocalRandom ??= new Random();

    private static void ScaleSpectrum(ReadOnlySpan<float> source, Span<float> dest)
    {
        int srcLen = source.Length, destLen = dest.Length;
        if (srcLen == 0 || destLen == 0) return;

        float scale = srcLen / (float)destLen;
        for (int i = 0; i < destLen; i++)
            dest[i] = source[(int)(i * scale)];
    }

    private void RenderParticles(
        SKCanvas canvas,
        SKPaint paint,
        float upperBound,
        float lowerBound)
    {
        if (_particleBuffer == null) return;

        var activeParticles = _particleBuffer.GetActiveParticles();
        if (activeParticles.Length == 0) return;

        DrawParticles(canvas, paint, upperBound, lowerBound, activeParticles);
    }

    private static void DrawParticles(
        SKCanvas canvas,
        SKPaint paint,
        float upperBound,
        float lowerBound,
        Span<Particle> particles)
    {
        SKColor originalColor = paint.Color;
        ConfigurePaintForParticles(paint);

        foreach (var particle in particles)
        {
            DrawParticleIfVisible(canvas, paint, upperBound, lowerBound, particle, originalColor);
        }

        paint.Color = originalColor;
    }

    private static void ConfigurePaintForParticles(SKPaint paint)
    {
        paint.Style = SKPaintStyle.Fill;
        paint.StrokeCap = SKStrokeCap.Round;
    }

    private static void DrawParticleIfVisible(
        SKCanvas canvas,
        SKPaint paint,
        float upperBound,
        float lowerBound,
        Particle particle,
        SKColor originalColor)
    {
        if (particle.Y < upperBound || particle.Y > lowerBound) return;

        paint.Color = originalColor.WithAlpha((byte)(particle.Alpha * 255));
        canvas.DrawCircle(particle.X, particle.Y, particle.Size / 2, paint);
    }

    public override void Dispose()
    {
        if (_disposed) return;
        ExecuteSafely(
            () =>
            {
                OnDispose();
            },
            "Dispose",
            "Error during disposal"
        );
        _disposed = true;
        GC.SuppressFinalize(this);
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }

    protected override void OnDispose()
    {
        ExecuteSafely(
            () =>
            {
                StopProcessingThread();
                DisposeResources();
                base.OnDispose();
            },
            "OnDispose",
            "Error during specific disposal"
        );
    }

    private void StopProcessingThread()
    {
        _processingRunning = false;
        _cts?.Cancel();
        _spectrumDataAvailable?.Set();
        _processingThread?.Join(100);

        DisposeSynchronizationObjects();
    }

    private void DisposeSynchronizationObjects()
    {
        _cts?.Dispose();
        _cts = null;

        _spectrumDataAvailable?.Dispose();
        _spectrumDataAvailable = null;

        _processingComplete?.Dispose();
        _processingComplete = null;
    }

    private void DisposeResources()
    {
        ReturnPooledArrays();

        _cachedPicture?.Dispose();
        _cachedPicture = null;

        _particleBuffer = null;
    }

    private void ReturnPooledArrays()
    {
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
    }
}