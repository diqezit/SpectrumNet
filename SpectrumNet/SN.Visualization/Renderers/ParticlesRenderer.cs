#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.ParticlesRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class ParticlesRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(ParticlesRenderer);

    private static readonly Lazy<ParticlesRenderer> _instance =
        new(() => new ParticlesRenderer());

    private ParticlesRenderer() { }

    public static ParticlesRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            DEFAULT_LINE_WIDTH = 3f,
            GLOW_INTENSITY = 0.3f,
            HIGH_MAGNITUDE_THRESHOLD = 0.7f,
            OFFSET = 10f,
            BASELINE_OFFSET = 2f,
            SMOOTHING_FACTOR_NORMAL = 0.3f,
            SMOOTHING_FACTOR_OVERLAY = 0.5f,
            SIZE_DECAY_FACTOR = 0.95f,
            MIN_PARTICLE_SIZE = 0.5f,
            MAX_DENSITY_FACTOR = 3f;

        public const int
            MAX_SAMPLE_POINTS = 256,
            VELOCITY_LOOKUP_SIZE = 1024,
            ALPHA_CURVE_SIZE = 101,
            SAFE_SPECTRUM_LENGTH = 2048,
            PROCESSING_TIMEOUT_MS = 5,
            THREAD_JOIN_TIMEOUT_MS = 100;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                UseAntiAlias: false,
                UseAdvancedEffects: false,
                SampleCount: 1,
                SamplingOptions: new SKSamplingOptions(
                    SKFilterMode.Nearest,
                    SKMipmapMode.None)
            ),
            [RenderQuality.Medium] = new(
                UseAntiAlias: true,
                UseAdvancedEffects: true,
                SampleCount: 2,
                SamplingOptions: new SKSamplingOptions(
                    SKFilterMode.Linear,
                    SKMipmapMode.Linear)
            ),
            [RenderQuality.High] = new(
                UseAntiAlias: true,
                UseAdvancedEffects: true,
                SampleCount: 4,
                SamplingOptions: new SKSamplingOptions(
                    SKFilterMode.Linear,
                    SKMipmapMode.Linear)
            )
        };

        public record QualitySettings(
            bool UseAntiAlias,
            bool UseAdvancedEffects,
            int SampleCount,
            SKSamplingOptions SamplingOptions
        );
    }

    private readonly record struct Particle(
        float X,
        float Y,
        float Velocity,
        float Size,
        float Life,
        float Alpha,
        bool IsActive
    );

    private sealed class CircularParticleBuffer(
        int capacity,
        float particleLife,
        float particleLifeDecay,
        float velocityMultiplier)
    {
        private readonly Particle[] _buffer = new Particle[capacity];
        private int _head, _tail, _count;
        private readonly object _bufferLock = new();

        public void Add(Particle particle)
        {
            lock (_bufferLock)
            {
                if (_count >= capacity)
                {
                    _buffer[_tail] = particle;
                    _tail = (_tail + 1) % capacity;
                    _head = (_head + 1) % capacity;
                }
                else
                {
                    _buffer[_head] = particle;
                    _head = (_head + 1) % capacity;
                    _count++;
                }
            }
        }

        public void Update(float upperBound, float[] alphaCurve)
        {
            lock (_bufferLock)
            {
                int currentTail = _tail;

                for (int i = 0; i < _count; i++)
                {
                    int index = (currentTail + i) % capacity;
                    var particle = _buffer[index];

                    if (!particle.IsActive) continue;

                    _buffer[index] = UpdateParticle(
                        particle,
                        upperBound,
                        alphaCurve,
                        particleLife,
                        particleLifeDecay,
                        velocityMultiplier);
                }

                while (_count > 0 && !_buffer[_tail].IsActive)
                {
                    _tail = (_tail + 1) % capacity;
                    _count--;
                }
            }
        }

        private static Particle UpdateParticle(
            Particle particle,
            float upperBound,
            float[] alphaCurve,
            float particleLife,
            float particleLifeDecay,
            float velocityMultiplier)
        {
            float newY = particle.Y - particle.Velocity * velocityMultiplier;

            if (newY <= upperBound)
                return particle with { Y = newY, IsActive = false };

            float newLife = particle.Life - particleLifeDecay;
            if (newLife <= 0)
                return particle with { Y = newY, Life = newLife, IsActive = false };

            float newSize = particle.Size * SIZE_DECAY_FACTOR;
            if (newSize < MIN_PARTICLE_SIZE)
                return particle with
                {
                    Y = newY,
                    Size = newSize,
                    Life = newLife,
                    IsActive = false
                };

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

        public List<Particle> GetActiveParticles()
        {
            lock (_bufferLock)
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
    }

    private sealed class RenderCache(float width, float height, bool isOverlay)
    {
        public float Width { get; set; } = width;
        public float Height { get; set; } = height;
        public float StepSize { get; set; }
        public float OverlayHeight { get; set; } = isOverlay ? height : 0f;
        public float UpperBound { get; set; } = 0f;
        public float LowerBound { get; set; } = height;

        public void UpdateBounds(
            float height,
            bool isOverlay,
            float overlayHeightMultiplier)
        {
            UpperBound = 0f;
            LowerBound = height;
            OverlayHeight = isOverlay ? height : 0f;
        }
    }

    private readonly record struct ProcessingParameters(
        float[] Spectrum,
        int SpectrumLength,
        float SpawnY,
        int CanvasWidth,
        float BarWidth
    );

    [ThreadStatic] private static Random? _threadLocalRandom;

    private CircularParticleBuffer? _particleBuffer;
    private readonly RenderCache _renderCache = new(1, 1, false);
    private readonly ISettings _settings = Settings.Settings.Instance;
    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];

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
        StartProcessingThread();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    private void InitializeResources()
    {
        PrecomputeAlphaCurve();
        InitializeVelocityLookup(_settings.ParticleVelocityMin);
        InitializeParticleBuffer();
        InitializeSynchronizationObjects();
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
        _smoothingFactor = _isOverlayActive ?
            SMOOTHING_FACTOR_OVERLAY :
            SMOOTHING_FACTOR_NORMAL;

        InvalidateCachedPicture();
        _logger.Info(LogPrefix,
            $"Configuration changed. New Quality: {Quality}, Overlay: {_isOverlayActive}");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        _useAntiAlias = _currentSettings.UseAntiAlias;
        _useAdvancedEffects = _currentSettings.UseAdvancedEffects;
        _samplingOptions = _currentSettings.SamplingOptions;
        _sampleCount = _currentSettings.SampleCount;

        InvalidateCachedPicture();
        _logger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {Quality}");
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
        _logger.Safe(
            () => RenderParticles(canvas, spectrum, info, barWidth, barCount, paint),
            LogPrefix,
            "Error during rendering"
        );
    }

    private void RenderParticles(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        int barCount,
        SKPaint paint)
    {
        if (canvas.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
            return;

        UpdateRenderCache(info, barCount, barWidth);
        ProcessSpectrumData(spectrum);
        UpdateParticles(_renderCache.UpperBound);
        RenderActiveParticles(
            canvas,
            paint,
            _renderCache.UpperBound,
            _renderCache.LowerBound);
    }

    private void UpdateRenderCache(SKImageInfo info, int barCount, float barWidth)
    {
        _renderCache.Width = info.Width;
        _renderCache.Height = info.Height;
        _renderCache.UpdateBounds(
            info.Height,
            _isOverlayActive,
            _settings.OverlayHeightMultiplier);
        _renderCache.StepSize = barCount > 0 ? info.Width / barCount : 0f;
        _barWidth = barWidth;
    }

    private void ProcessSpectrumData(float[] spectrum)
    {
        SubmitSpectrumForProcessing(
            spectrum,
            _renderCache.LowerBound,
            (int)_renderCache.Width,
            _barWidth);
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
        _processingComplete?.WaitOne(PROCESSING_TIMEOUT_MS);
    }

    private void ProcessSpectrumThreadFunc()
    {
        try
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
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogPrefix, $"Error in processing thread: {ex.Message}");
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
        int canvasWidth,
        float barWidth)
    {
        if (_particleBuffer == null) return;

        float threshold = _isOverlayActive ?
            _settings.SpawnThresholdOverlay :
            _settings.SpawnThresholdNormal;

        float baseSize = _isOverlayActive ?
            _settings.ParticleSizeOverlay :
            _settings.ParticleSizeNormal;

        int safeLength = Min(spectrumLength, SAFE_SPECTRUM_LENGTH);
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
            if (spectrumBuffer[i] <= threshold) continue;

            float densityFactor = MathF.Min(spectrumBuffer[i] / threshold, MAX_DENSITY_FACTOR);
            if (rnd.NextDouble() >= densityFactor * _settings.SpawnProbability) continue;

            CreateParticle(
                i,
                xStep,
                barWidth,
                spawnY,
                densityFactor,
                baseSize,
                rnd);
        }
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

    private void RenderActiveParticles(
        SKCanvas canvas,
        SKPaint paint,
        float upperBound,
        float lowerBound)
    {
        if (_particleBuffer == null) return;

        var activeParticles = _particleBuffer.GetActiveParticles();
        if (activeParticles.Count == 0) return;

        SKColor originalColor = paint.Color;
        paint.Style = SKPaintStyle.Fill;
        paint.StrokeCap = SKStrokeCap.Round;

        foreach (var particle in activeParticles)
        {
            if (particle.Y < upperBound || particle.Y > lowerBound) continue;

            paint.Color = originalColor.WithAlpha((byte)(particle.Alpha * 255));
            canvas.DrawCircle(particle.X, particle.Y, particle.Size / 2, paint);
        }

        paint.Color = originalColor;
    }

    private void PrecomputeAlphaCurve()
    {
        _alphaCurve ??= ArrayPool<float>.Shared.Rent(ALPHA_CURVE_SIZE);

        float step = 1f / (_alphaCurve.Length - 1);
        for (int i = 0; i < _alphaCurve.Length; i++)
            _alphaCurve[i] = (float)Pow(i * step, _settings.AlphaDecayExponent);
    }

    private void InitializeVelocityLookup(float minVelocity)
    {
        _velocityLookup ??= ArrayPool<float>.Shared.Rent(VELOCITY_LOOKUP_SIZE);

        float velocityRange = _settings.ParticleVelocityMax - _settings.ParticleVelocityMin;
        for (int i = 0; i < VELOCITY_LOOKUP_SIZE; i++)
            _velocityLookup[i] = minVelocity + velocityRange * i / VELOCITY_LOOKUP_SIZE;
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
        base.OnInvalidateCachedResources();
        InvalidateCachedPicture();
        _logger.Log(LogLevel.Debug, LogPrefix, "Cached resources invalidated");
    }

    protected override void OnDispose()
    {
        StopProcessingThread();
        DisposeResources();
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }

    private void StopProcessingThread()
    {
        _processingRunning = false;
        _cts?.Cancel();
        _spectrumDataAvailable?.Set();
        _processingThread?.Join(THREAD_JOIN_TIMEOUT_MS);

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