#nullable enable

using static System.MathF;
using static SpectrumNet.Views.Renderers.ParticlesRenderer.Constants;
using static SpectrumNet.Views.Renderers.ParticlesRenderer.Constants.Quality;

namespace SpectrumNet.Views.Renderers;

public sealed class ParticlesRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<ParticlesRenderer> _instance = new(() => new ParticlesRenderer());
    private const string LOG_PREFIX = nameof(ParticlesRenderer);

    private ParticlesRenderer() { }

    public static ParticlesRenderer GetInstance() => _instance.Value;

    public record Constants
    {
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

            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;

            public const int
                LOW_SAMPLE_COUNT = 1,
                MEDIUM_SAMPLE_COUNT = 2,
                HIGH_SAMPLE_COUNT = 4;
        }
    }

    private readonly struct Particle(
        float x,
        float y,
        float velocity,
        float size,
        float life,
        float alpha,
        bool isActive)
    {
        public readonly float
            X = x,
            Y = y,
            Velocity = velocity,
            Size = size,
            Life = life,
            Alpha = alpha;

        public readonly bool IsActive = isActive;
    }

    private class CircularParticleBuffer(
        int capacity,
        float particleLife,
        float particleLifeDecay,
        float velocityMultiplier)
    {
        private readonly Particle[] _buffer = new Particle[capacity];
        private int _head, _tail, _count;
        private readonly float _sizeDecayFactor = 0.95f;

        private readonly object _bufferLock = new();

        public void Add(Particle particle)
        {
            lock (_bufferLock)
            {
                if (_count >= capacity)
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
            _tail = (_tail + 1) % capacity;
            _head = (_head + 1) % capacity;
        }

        private void AddNewParticle(Particle particle)
        {
            _buffer[_head] = particle;
            _head = (_head + 1) % capacity;
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
                int index = (currentTail + i) % capacity;
                var particle = _buffer[index];

                if (!particle.IsActive) continue;

                var updatedParticle = UpdateParticle(particle, upperBound, alphaCurve);
                _buffer[index] = updatedParticle;
            }
        }

        private Particle UpdateParticle(
            Particle particle,
            float upperBound,
            float[] alphaCurve)
        {
            float newY = particle.Y - particle.Velocity * velocityMultiplier;

            if (newY <= upperBound)
                return new Particle(
                    particle.X,
                    newY,
                    particle.Velocity,
                    particle.Size,
                    particle.Life,
                    particle.Alpha,
                    false);

            float newLife = particle.Life - particleLifeDecay;
            if (newLife <= 0)
                return new Particle(
                    particle.X,
                    newY,
                    particle.Velocity,
                    particle.Size,
                    newLife,
                    particle.Alpha,
                    false);

            float newSize = particle.Size * _sizeDecayFactor;
            if (newSize < 0.5f)
                return new Particle(
                    particle.X,
                    newY,
                    particle.Velocity,
                    newSize,
                    newLife,
                    particle.Alpha,
                    false);

            float lifeRatio = newLife / particleLife;
            int alphaIndex = (int)(lifeRatio * (alphaCurve.Length - 1));
            alphaIndex = Clamp(alphaIndex, 0, alphaCurve.Length - 1);
            float newAlpha = alphaCurve[alphaIndex];

            return new Particle(
                particle.X,
                newY,
                particle.Velocity,
                newSize,
                newLife,
                newAlpha,
                true);
        }

        private void RemoveInactiveParticles()
        {
            while (_count > 0 && !_buffer[_tail].IsActive)
            {
                _tail = (_tail + 1) % capacity;
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
                int index = (currentTail + i) % capacity;
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
    private readonly object _particleSpectrumLock = new();
    private float[]? _spectrumToProcess;
    private int _spectrumLength;
    private bool _processingRunning;
    private float _spawnY;
    private int _canvasWidth;
    private float _barWidth;

    private int _sampleCount = 2;

    private SKPicture? _cachedPicture;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        InitializeResources();
        InitializeQualityParams();
        StartProcessingThread();
        _logger.Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
    }

    private void InitializeResources()
    {
        PrecomputeAlphaCurve();
        InitializeVelocityLookup(Settings.ParticleVelocityMin);
        InitializeParticleBuffer();
        InitializeSynchronizationObjects();
    }

    private void InitializeQualityParams()
    {
        _logger.Safe(
            () => ApplyQualitySettings(),
            LOG_PREFIX,
            "Failed to initialize quality parameters");
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

    protected override void OnConfigurationChanged()
    {
        _smoothingFactor = _isOverlayActive ?
            SMOOTHING_FACTOR_OVERLAY :
            SMOOTHING_FACTOR_NORMAL;

        InvalidateCachedPicture();

        _logger.Log(LogLevel.Information, LOG_PREFIX,
            $"Configuration changed. New Quality: {Quality}, Overlay: {_isOverlayActive}");
    }

    protected override void OnQualitySettingsApplied()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                LowQualitySettings();
                break;

            case RenderQuality.Medium:
                MediumQualitySettings();
                break;

            case RenderQuality.High:
                HighQualitySettings();
                break;
        }

        InvalidateCachedPicture();

        _logger.Log(LogLevel.Debug, LOG_PREFIX,
            $"Quality settings applied. Quality: {Quality}, " +
            $"AntiAlias: {_useAntiAlias}, AdvancedEffects: {_useAdvancedEffects}, " +
            $"SampleCount: {_sampleCount}");
    }

    private void LowQualitySettings()
    {
        _useAntiAlias = LOW_USE_ANTIALIASING;
        _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _sampleCount = LOW_SAMPLE_COUNT;
    }

    private void MediumQualitySettings()
    {
        _useAntiAlias = MEDIUM_USE_ANTIALIASING;
        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _sampleCount = MEDIUM_SAMPLE_COUNT;
    }

    private void HighQualitySettings()
    {
        _useAntiAlias = HIGH_USE_ANTIALIASING;
        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _sampleCount = HIGH_SAMPLE_COUNT;
    }

    private void InvalidateCachedPicture()
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
        if (canvas.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
            return;

        _logger.Safe(
            () =>
            {
                UpdateRenderCache(info, barCount, barWidth);
                ProcessSpectrumData(spectrum);
                UpdateParticles(_renderCache.UpperBound);
                RenderParticles(canvas, paint, _renderCache.UpperBound, _renderCache.LowerBound);
            },
            LOG_PREFIX,
            "Error during rendering");
    }

    private void UpdateRenderCache(SKImageInfo info, int barCount, float barWidth)
    {
        _renderCache.Width = info.Width;
        _renderCache.Height = info.Height;
        UpdateRenderCacheBounds(info.Height);
        _renderCache.StepSize = barCount > 0 ? info.Width / barCount : 0f;
        _barWidth = barWidth;
    }

    private void ProcessSpectrumData(float[] spectrum) =>
        SubmitSpectrumForProcessing(spectrum, _renderCache.LowerBound, (int)_renderCache.Width, _barWidth);

    private void SubmitSpectrumForProcessing(
        float[] spectrum,
        float spawnY,
        int canvasWidth,
        float barWidth)
    {
        lock (_particleSpectrumLock)
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
            _logger.Error(LOG_PREFIX, $"Error in processing thread: {ex.Message}");
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
        lock (_particleSpectrumLock)
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
        int _,
        float barWidth)
    {
        if (_particleBuffer == null) return;

        float threshold = _isOverlayActive ?
            Settings.SpawnThresholdOverlay :
            Settings.SpawnThresholdNormal;

        float baseSize = _isOverlayActive ?
            Settings.ParticleSizeOverlay :
            Settings.ParticleSizeNormal;

        int safeLength = Min(spectrumLength, 2048);
        PrepareSpectrumBuffer(safeLength);

        if (_spectrumBuffer == null) return;

        ScaleSpectrum(
            spectrum.AsSpan(0, safeLength),
            _spectrumBuffer.AsSpan(0, safeLength));

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
            isActive: true));
    }

    private void UpdateParticles(float upperBound)
    {
        if (_particleBuffer == null || _alphaCurve == null) return;
        _particleBuffer.Update(upperBound, _alphaCurve);
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

    protected override void OnInvalidateCachedResources()
    {
        InvalidateCachedPicture();
        _logger.Log(LogLevel.Debug, LOG_PREFIX, "Cached resources invalidated");
    }

    protected override void OnDispose()
    {
        StopProcessingThread();
        DisposeResources();
        base.OnDispose();
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