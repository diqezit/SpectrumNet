#nullable enable

namespace SpectrumNet
{
    public sealed class ParticlesRenderer : ISpectrumRenderer, IDisposable
    {
        private static readonly Lazy<ParticlesRenderer> _lazyInstance = new(() => new ParticlesRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static ParticlesRenderer GetInstance() => _lazyInstance.Value;

        [ThreadStatic] private static Random? _threadLocalRandom;
        private const int VelocityLookupSize = 1024;
        private CircularParticleBuffer? _particleBuffer;
        private bool _isOverlayActive, _isInitialized, _isDisposed;
        private RenderCache _renderCache = new();
        private Settings Settings => Settings.Instance;

        private float[]? _spectrumBuffer, _velocityLookup, _alphaCurve;
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

        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;
        private int _sampleCount = 2;
        private float _pathSimplification = 0.2f;
        private int _maxDetailLevel = 4;
        private float _smoothingFactor = Constants.SmoothingFactorNormal;

        private SKPicture? _cachedPicture;
        private object? _cachedPoints;
        private Dictionary<string, SKPath>? _pathCache;

        private const string LogPrefix = "ParticlesRenderer";

        private static class Constants
        {
            public const float DefaultLineWidth = 3f;
            public const float GlowIntensity = 0.3f;
            public const float HighMagnitudeThreshold = 0.7f;
            public const float Offset = 10f;
            public const float BaselineOffset = 2f;
            public const float SmoothingFactorNormal = 0.3f;
            public const float SmoothingFactorOverlay = 0.5f;
            public const int MaxSamplePoints = 256;
        }

        private ParticlesRenderer()
        {
            PrecomputeAlphaCurve();
            InitializeVelocityLookup(Settings.ParticleVelocityMin);
            _processingThread = new Thread(ProcessSpectrumThreadFunc) { IsBackground = true, Name = "ParticlesProcessor" };
            _processingRunning = true;
            _processingThread.Start();
        }

        public void Initialize()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ParticlesRenderer));
            if (_isInitialized) return;
            _particleBuffer = new CircularParticleBuffer((int)Settings.MaxParticles, Settings.ParticleLife, Settings.ParticleLifeDecay, Settings.VelocityMultiplier);
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            bool configChanged = _isOverlayActive != isOverlayActive || _quality != quality;
            _isOverlayActive = isOverlayActive;
            _smoothingFactor = isOverlayActive ? Constants.SmoothingFactorOverlay : Constants.SmoothingFactorNormal;
            Quality = quality;
            if (configChanged)
            {
                InvalidateCachedResources();
            }
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Configured: Overlay={isOverlayActive}, Quality={quality}");
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount, SKPaint? paint, Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, paint, drawPerformanceInfo))
                return;
            if (canvas.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
                return;
            _renderCache.Width = info.Width;
            _renderCache.Height = info.Height;
            UpdateRenderCacheBounds(info.Height);
            _renderCache.StepSize = barCount > 0 ? info.Width / barCount : 0f;
            float upperBound = _renderCache.UpperBound;
            float lowerBound = _renderCache.LowerBound;
            ProcessSpectrumData(spectrum!, _sampleCount);
            UpdateParticles(upperBound);
            RenderWithQualitySettings(canvas!, info, paint!);
            drawPerformanceInfo!(canvas!, info);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _processingRunning = false;
            _cts.Cancel();
            _spectrumDataAvailable.Set();
            _processingThread.Join(100);
            _cts.Dispose();
            _spectrumDataAvailable.Dispose();
            _processingComplete.Dispose();
            if (_spectrumBuffer != null) { ArrayPool<float>.Shared.Return(_spectrumBuffer); _spectrumBuffer = null; }
            if (_velocityLookup != null) { ArrayPool<float>.Shared.Return(_velocityLookup); _velocityLookup = null; }
            if (_alphaCurve != null) { ArrayPool<float>.Shared.Return(_alphaCurve); _alphaCurve = null; }
            _particleBuffer = null;
            _renderCache = new RenderCache();
            _isInitialized = false;
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }

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
                    _maxDetailLevel = 2;
                    break;
                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.Medium;
                    _useAdvancedEffects = true;
                    _sampleCount = 2;
                    _pathSimplification = 0.2f;
                    _maxDetailLevel = 4;
                    break;
                case RenderQuality.High:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.High;
                    _useAdvancedEffects = true;
                    _sampleCount = 4;
                    _pathSimplification = 0.0f;
                    _maxDetailLevel = 8;
                    break;
            }
            _cachedPicture?.Dispose();
            _cachedPicture = null;
            InvalidateCachedResources();
        }

        private void InvalidateCachedResources()
        {
            _cachedPicture?.Dispose();
            _cachedPicture = null;
            _cachedPoints = null;
            _pathCache?.Clear();
            RebuildShaders();
        }

        private void RebuildShaders()
        {
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Rebuilding shaders.");
        }

        private void ProcessSpectrumData(float[] spectrum, int sampleCount)
        {
            SubmitSpectrumForProcessing(spectrum, _renderCache.LowerBound, (int)_renderCache.Width, _barWidth);
        }

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
            _processingComplete.WaitOne(5);
        }

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
                        if (_spectrumToProcess == null) { _processingComplete.Set(); continue; }
                        spectrumCopy = _spectrumToProcess;
                        spectrumLength = _spectrumLength;
                        spawnY = _spawnY;
                        canvasWidth = _canvasWidth;
                        barWidth = _barWidth;
                    }
                    ProcessSpectrumAndSpawnParticles(spectrumCopy.AsSpan(0, Math.Min(spectrumLength, 2048)), spawnY, canvasWidth, barWidth);
                    _processingComplete.Set();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"ParticlesRenderer: {ex.Message}"); }
        }

        private void ProcessSpectrumAndSpawnParticles(ReadOnlySpan<float> spectrum, float spawnY, int canvasWidth, float barWidth)
        {
            if (_particleBuffer == null) return;
            float threshold = _isOverlayActive ? Settings.SpawnThresholdOverlay : Settings.SpawnThresholdNormal;
            float baseSize = _isOverlayActive ? Settings.ParticleSizeOverlay : Settings.ParticleSizeNormal;
            int targetCount = Math.Min(spectrum.Length, 2048);
            if (_spectrumBuffer == null)
                _spectrumBuffer = ArrayPool<float>.Shared.Rent(targetCount);
            var spectrumBufferSpan = new Span<float>(_spectrumBuffer, 0, targetCount);
            ScaleSpectrum(spectrum, spectrumBufferSpan);
            float xStep = _renderCache.StepSize;
            var rnd = _threadLocalRandom ??= new Random();
            for (int i = 0; i < targetCount; i++)
            {
                float spectrumValue = spectrumBufferSpan[i];
                if (spectrumValue <= threshold) continue;
                float densityFactor = MathF.Min(spectrumValue / threshold, 3f);
                if (rnd.NextDouble() >= densityFactor * Settings.SpawnProbability) continue;
                _particleBuffer.Add(new Particle
                {
                    X = i * xStep + (float)rnd.NextDouble() * _barWidth,
                    Y = spawnY,
                    Velocity = GetRandomVelocity() * densityFactor,
                    Size = baseSize * densityFactor,
                    Life = Settings.ParticleLife,
                    Alpha = 1f,
                    IsActive = true
                });
            }
        }

        private void PrecomputeAlphaCurve()
        {
            if (_alphaCurve == null) _alphaCurve = ArrayPool<float>.Shared.Rent(101);
            float step = 1f / (_alphaCurve.Length - 1);
            for (int i = 0; i < _alphaCurve.Length; i++)
                _alphaCurve[i] = (float)Math.Pow(i * step, Settings.AlphaDecayExponent);
        }

        private void InitializeVelocityLookup(float minVelocity)
        {
            if (_velocityLookup == null) _velocityLookup = ArrayPool<float>.Shared.Rent(VelocityLookupSize);
            float velocityRange = Settings.ParticleVelocityMax - Settings.ParticleVelocityMin;
            for (int i = 0; i < VelocityLookupSize; i++)
                _velocityLookup[i] = minVelocity + velocityRange * i / VelocityLookupSize;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint, Action<SKCanvas, SKImageInfo>? drawPerformanceInfo) =>
            !_isDisposed && _isInitialized && canvas != null && spectrum != null && spectrum.Length >= 2 &&
            paint != null && drawPerformanceInfo != null && info.Width > 0 && info.Height > 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetRandomVelocity()
        {
            if (_velocityLookup == null) throw new InvalidOperationException("Velocity lookup not initialized");
            var rnd = _threadLocalRandom ??= new Random();
            return _velocityLookup[rnd.Next(VelocityLookupSize)] * Settings.VelocityMultiplier;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ScaleSpectrum(ReadOnlySpan<float> source, Span<float> dest)
        {
            int srcLen = source.Length, destLen = dest.Length;
            if (srcLen == 0 || destLen == 0) return;
            float scale = srcLen / (float)destLen;
            for (int i = 0; i < destLen; i++)
                dest[i] = source[(int)(i * scale)];
        }

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

        private void RenderWithQualitySettings(SKCanvas canvas, SKImageInfo info, SKPaint externalPaint)
        {
            using var paint = externalPaint.Clone();
            paint.IsAntialias = _useAntiAlias;
            paint.FilterQuality = _filterQuality;
            RenderBasicVisualization(canvas, paint);
            if (_useAdvancedEffects)
            {
                // Advanced effects disabled.
            }
        }

        private void RenderBasicVisualization(SKCanvas canvas, SKPaint paint)
        {
            RenderParticles(canvas, paint, _renderCache.UpperBound, _renderCache.LowerBound);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderParticles(SKCanvas canvas, SKPaint paint, float upperBound, float lowerBound)
        {
            if (_particleBuffer == null)
                return;
            var activeParticles = _particleBuffer.GetActiveParticles();
            int count = activeParticles.Length;
            if (count == 0)
                return;
            SKColor originalColor = paint.Color;
            paint.Style = SKPaintStyle.Fill;
            paint.StrokeCap = SKStrokeCap.Round;
            for (int i = 0; i < count; i++)
            {
                ref readonly var particle = ref activeParticles[i];
                if (particle.Y < upperBound || particle.Y > lowerBound)
                    continue;
                SKColor particleColor = originalColor.WithAlpha((byte)(particle.Alpha * 255));
                paint.Color = particleColor;
                canvas.DrawCircle(particle.X, particle.Y, particle.Size / 2, paint);
            }
            paint.Color = originalColor;
        }
    }

    internal class CircularParticleBuffer
    {
        private readonly Particle[] _buffer;
        private int _head, _tail, _count;
        private readonly int _capacity;
        private readonly float _particleLife, _particleLifeDecay, _velocityMultiplier;
        private readonly object _bufferLock = new();
        private readonly float _sizeDecayFactor = 0.95f;

        public CircularParticleBuffer(int capacity, float particleLife, float particleLifeDecay, float velocityMultiplier)
        {
            _capacity = capacity;
            _buffer = new Particle[capacity];
            _particleLife = particleLife;
            _particleLifeDecay = particleLifeDecay;
            _velocityMultiplier = velocityMultiplier;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(Particle particle)
        {
            lock (_bufferLock)
            {
                if (_count >= _capacity)
                {
                    _buffer[_tail] = particle;
                    _tail = (_tail + 1) % _capacity;
                    _head = (_head + 1) % _capacity;
                }
                else
                {
                    _buffer[_head] = particle;
                    _head = (_head + 1) % _capacity;
                    _count++;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(float upperBound, float[] alphaCurve)
        {
            lock (_bufferLock)
            {
                int currentTail = _tail;
                for (int i = 0; i < _count; i++)
                {
                    int index = (currentTail + i) % _capacity;
                    ref Particle particle = ref _buffer[index];
                    if (!particle.IsActive) continue;
                    particle.Y -= particle.Velocity * _velocityMultiplier;
                    if (particle.Y <= upperBound)
                    {
                        particle.IsActive = false;
                        continue;
                    }
                    particle.Life -= _particleLifeDecay;
                    if (particle.Life <= 0)
                    {
                        particle.IsActive = false;
                        continue;
                    }
                    particle.Size *= _sizeDecayFactor;
                    if (particle.Size < 0.5f)
                    {
                        particle.IsActive = false;
                        continue;
                    }
                    float lifeRatio = particle.Life / _particleLife;
                    int alphaIndex = (int)(lifeRatio * (alphaCurve.Length - 1));
                    alphaIndex = Math.Clamp(alphaIndex, 0, alphaCurve.Length - 1);
                    particle.Alpha = alphaCurve[alphaIndex];
                }
                while (_count > 0 && !_buffer[_tail].IsActive)
                {
                    _tail = (_tail + 1) % _capacity;
                    _count--;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<Particle> GetActiveParticles()
        {
            lock (_bufferLock)
            {
                if (_count == 0) return Span<Particle>.Empty;
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
                return new Span<Particle>(activeParticles, 0, activeCount);
            }
        }
    }

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

    internal class RenderCache
    {
        public float Width { get; set; }
        public float Height { get; set; }
        public float UpperBound { get; set; }
        public float LowerBound { get; set; }
        public float OverlayHeight { get; set; }
        public float StepSize { get; set; }
    }
}