#nullable enable

using static System.MathF;
using static SpectrumNet.Views.Renderers.ParticlesRenderer.Constants;
using static SpectrumNet.Views.Renderers.ParticlesRenderer.Constants.Quality;

namespace SpectrumNet.Views.Renderers;

public sealed class ParticlesRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<ParticlesRenderer> _instance = new(() => new ParticlesRenderer());
    private const string LogPrefix = nameof(ParticlesRenderer);

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

    private readonly record struct Particle(
        float X,
        float Y,
        float Velocity,
        float Size,
        float Life,
        float Alpha,
        bool IsActive);

    private sealed class CircularParticleBuffer(
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
                return particle with { Y = newY, IsActive = false };

            float newLife = particle.Life - particleLifeDecay;
            if (newLife <= 0)
                return particle with { Y = newY, Life = newLife, IsActive = false };

            float newSize = particle.Size * _sizeDecayFactor;
            if (newSize < 0.5f)
                return particle with { Y = newY, Size = newSize, Life = newLife, IsActive = false };

            float lifeRatio = newLife / particleLife;
            int alphaIndex = (int)(lifeRatio * (alphaCurve.Length - 1));
            alphaIndex = Clamp(alphaIndex, 0, alphaCurve.Length - 1);
            float newAlpha = alphaCurve[alphaIndex];

            return particle with
            {
                Y = newY,
                Size = newSize,
                Life = newLife,
                Alpha = newAlpha,
                IsActive = true
            };
        }

        private void RemoveInactiveParticles()
        {
            while (_count > 0 && !_buffer[_tail].IsActive)
            {
                _tail = (_tail + 1) % capacity;
                _count--;
            }
        }

        public List<Particle> GetActiveParticles()
        {
            lock (_bufferLock)
            {
                return CollectActiveParticles();
            }
        }

        private List<Particle> CollectActiveParticles()
        {
            if (_count == 0) return [];

            List<Particle> activeParticles = new(_count);
            int currentTail = _tail;

            for (int i = 0; i < _count; i++)
            {
                int index = (currentTail + i) % capacity;
                if (_buffer[index].IsActive)
                {
                    activeParticles.Add(_buffer[index]);
                }
            }

            return activeParticles;
        }
    }

    private sealed class RenderCache(float width, float height, bool isOverlay)
    {
        public float Width { get; set; } = width;
        public float Height { get; set; } = height;
        public float StepSize { get; set; }

        public float OverlayHeight { get; set; } = isOverlay ? height : 0f;
        public float UpperBound { get; set; } = 0f;
        public float LowerBound { get; set; } = height;

        public void UpdateBounds(float height, bool isOverlay, float overlayHeightMultiplier)
        {
            if (isOverlay)
            {
                UpperBound = 0f;
                LowerBound = height;
                OverlayHeight = height;
            }
            else
            {
                UpperBound = 0f;
                LowerBound = height;
                OverlayHeight = 0f;
            }
        }
    }

    private readonly record struct ProcessingParameters(
        float[] Spectrum,
        int SpectrumLength,
        float SpawnY,
        int CanvasWidth,
        float BarWidth);

    [ThreadStatic] private static Random? _threadLocalRandom;

    private CircularParticleBuffer? _particleBuffer;
    private readonly RenderCache _renderCache = new(1, 1, false);
    private readonly Settings _settings = SpectrumNet.DataSettings.Settings.Instance;

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
        _logger.Safe(() => {
            base.OnInitialize();
            InitializeResources();
            InitializeQualityParams();
            StartProcessingThread();
            _logger.Debug(LogPrefix, "Initialized");
        }, LogPrefix, "Error initializing ParticlesRenderer");
    }

    private void InitializeResources()
    {
        PrecomputeAlphaCurve();
        InitializeVelocityLookup(_settings.ParticleVelocityMin);
        InitializeParticleBuffer();
        InitializeSynchronizationObjects();
    }

    private void InitializeQualityParams()
    {
        _logger.Safe(ApplyQualitySettings, LogPrefix, "Failed to initialize quality parameters");
    }

    private void InitializeParticleBuffer()
    {
        _particleBuffer = new CircularParticleBuffer(
            _settings.MaxParticles,
            _settings.ParticleLife,
            _settings.ParticleLifeDecay,
            _settings.VelocityMultiplier);
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
        _logger.Safe(() => {
            _smoothingFactor = _isOverlayActive ?
                SMOOTHING_FACTOR_OVERLAY :
                SMOOTHING_FACTOR_NORMAL;

            InvalidateCachedPicture();

            _logger.Info(LogPrefix, $"Configuration changed. New Quality: {Quality}, Overlay: {_isOverlayActive}");
        }, LogPrefix, "Error handling configuration change");
    }

    protected override void OnQualitySettingsApplied()
    {
        _logger.Safe(() => {
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

            _logger.Debug(LogPrefix,
                $"Quality settings applied. Quality: {Quality}, " +
                $"AntiAlias: {_useAntiAlias}, AdvancedEffects: {_useAdvancedEffects}, " +
                $"SampleCount: {_sampleCount}");
        }, LogPrefix, "Error applying quality settings");
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
        _logger.Safe(() => {
            if (canvas.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
                return;

            UpdateRenderCache(info, barCount, barWidth);
            ProcessSpectrumData(spectrum);
            UpdateParticles(_renderCache.UpperBound);
            RenderParticles(canvas, paint, _renderCache.UpperBound, _renderCache.LowerBound);
        }, LogPrefix, "Error during rendering");
    }

    private void UpdateRenderCache(SKImageInfo info, int barCount, float barWidth)
    {
        _renderCache.Width = info.Width;
        _renderCache.Height = info.Height;
        _renderCache.UpdateBounds(info.Height, _isOverlayActive, _settings.OverlayHeightMultiplier);
        _renderCache.StepSize = barCount > 0 ? info.Width / barCount : 0f;
        _barWidth = barWidth;
    }

    private void ProcessSpectrumData(float[] spectrum)
    {
        SubmitSpectrumForProcessing(spectrum, _renderCache.LowerBound, (int)_renderCache.Width, _barWidth);
    }

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
            _logger.Error(LogPrefix, $"Error in processing thread: {ex.Message}");
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
            _settings.SpawnThresholdOverlay :
            _settings.SpawnThresholdNormal;

        float baseSize = _isOverlayActive ?
            _settings.ParticleSizeOverlay :
            _settings.ParticleSizeNormal;

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
        if (rnd.NextDouble() >= densityFactor * _settings.SpawnProbability) return;

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
            X: index * xStep + (float)rnd.NextDouble() * barWidth,
            Y: spawnY,
            Velocity: GetRandomVelocity(rnd) * densityFactor,
            Size: baseSize * densityFactor,
            Life: _settings.ParticleLife,
            Alpha: 1f,
            IsActive: true));
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
        _logger.Safe(() => {
            if (_particleBuffer == null) return;

            var activeParticles = _particleBuffer.GetActiveParticles();
            if (activeParticles.Count == 0) return;

            DrawParticles(canvas, paint, upperBound, lowerBound, activeParticles);
        }, LogPrefix, "Error rendering particles");
    }

    private static void DrawParticles(
        SKCanvas canvas,
        SKPaint paint,
        float upperBound,
        float lowerBound,
        List<Particle> particles)
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
        _logger.Safe(() => {
            _alphaCurve ??= ArrayPool<float>.Shared.Rent(101);

            float step = 1f / (_alphaCurve.Length - 1);
            for (int i = 0; i < _alphaCurve.Length; i++)
                _alphaCurve[i] = (float)Pow(i * step, _settings.AlphaDecayExponent);
        }, LogPrefix, "Error precomputing alpha curve");
    }

    private void InitializeVelocityLookup(float minVelocity)
    {
        _logger.Safe(() => {
            _velocityLookup ??= ArrayPool<float>.Shared.Rent(VELOCITY_LOOKUP_SIZE);

            float velocityRange = _settings.ParticleVelocityMax - _settings.ParticleVelocityMin;
            for (int i = 0; i < VELOCITY_LOOKUP_SIZE; i++)
                _velocityLookup[i] = minVelocity + velocityRange * i / VELOCITY_LOOKUP_SIZE;
        }, LogPrefix, "Error initializing velocity lookup");
    }

    private float GetRandomVelocity(Random rnd)
    {
        if (_velocityLookup == null)
            return _settings.ParticleVelocityMin * _settings.VelocityMultiplier;

        return _velocityLookup[rnd.Next(VELOCITY_LOOKUP_SIZE)] * _settings.VelocityMultiplier;
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
        _logger.Safe(() => {
            base.OnInvalidateCachedResources();
            InvalidateCachedPicture();
            _logger.Debug(LogPrefix, "Cached resources invalidated");
        }, LogPrefix, "Error invalidating cached resources");
    }

    protected override void OnDispose()
    {
        _logger.Safe(() => {
            StopProcessingThread();
            DisposeResources();
            _logger.Debug(LogPrefix, "Disposed");
            base.OnDispose();
        }, LogPrefix, "Error disposing ParticlesRenderer");
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