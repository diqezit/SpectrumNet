#nullable enable

namespace SpectrumNet
{
    public sealed class ParticlesRenderer : ISpectrumRenderer, IDisposable
    {
        #region Instance
        private static readonly Lazy<ParticlesRenderer> _lazyInstance = new(() => new ParticlesRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static ParticlesRenderer GetInstance() => _lazyInstance.Value;
        #endregion

        #region Fields
        [ThreadStatic] private static Random? _threadLocalRandom;
        private const int VelocityLookupSize = 1024;
        private CircularParticleBuffer? _particleBuffer;
        private bool _isOverlayActive, _isInitialized, _isDisposed;
        private RenderCache _renderCache = new();

        // Доступ к настройкам через синглтон
        private Settings Settings => Settings.Instance;

        private float[]? _spectrumBuffer, _velocityLookup, _alphaCurve;

        // Многопоточная обработка
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
        #endregion

        #region Constructor
        private ParticlesRenderer()
        {
            PrecomputeAlphaCurve();
            InitializeVelocityLookup(Settings.ParticleVelocityMin);

            _processingThread = new Thread(ProcessSpectrumThreadFunc) { IsBackground = true, Name = "ParticlesProcessor" };
            _processingRunning = true;
            _processingThread.Start();
        }
        #endregion

        #region ISpectrumRenderer Implementation
        public void Initialize()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ParticlesRenderer));
            if (_isInitialized) return;
            _particleBuffer = new CircularParticleBuffer(
                (int)Settings.MaxParticles,
                Settings.ParticleLife,
                Settings.ParticleLifeDecay,
                Settings.VelocityMultiplier);
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive != isOverlayActive)
            {
                _isOverlayActive = isOverlayActive;
                UpdateParticleSizes();
            }
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth, float barSpacing,
                          int barCount, SKPaint? paint, Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, paint, drawPerformanceInfo)) return;

            int width = info.Width, height = info.Height;
            _renderCache.Width = width;
            _renderCache.Height = height;
            UpdateRenderCacheBounds(height);
            _renderCache.StepSize = barCount > 0 ? width / barCount : 0f;

            float upperBound = _renderCache.UpperBound;
            float lowerBound = _renderCache.LowerBound;

            // Отправляем спектр на обработку в фоновый поток
            SubmitSpectrumForProcessing(spectrum!, lowerBound, width, barWidth);

            // Обновляем и рендерим частицы в основном потоке
            UpdateParticles(upperBound);
            RenderParticles(canvas!, paint!, upperBound, lowerBound);
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
        #endregion

        #region Background Processing
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
            catch (OperationCanceledException) { /* Нормальное завершение */ }
            catch (Exception ex) { Log.Error($"ParticlesRenderer: {ex.Message}"); }
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
        #endregion

        #region Helper Methods
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
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint,
                                             Action<SKCanvas, SKImageInfo>? drawPerformanceInfo) =>
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderParticles(SKCanvas canvas, SKPaint paint, float upperBound, float lowerBound)
        {
            if (_particleBuffer == null) return;
            var activeParticles = _particleBuffer.GetActiveParticles();
            int count = activeParticles.Length;
            if (count == 0) return;

            paint.Style = SKPaintStyle.Fill;
            paint.StrokeCap = SKStrokeCap.Round;

            for (int i = 0; i < count; i++)
            {
                ref readonly var particle = ref activeParticles[i];
                if (particle.Y < upperBound || particle.Y > lowerBound) continue;

                paint.Color = paint.Color.WithAlpha((byte)(particle.Alpha * 255));
                canvas.DrawCircle(particle.X, particle.Y, particle.Size / 2, paint);
            }
        }
        #endregion

        #region Nested Classes
        private class CircularParticleBuffer
        {
            private readonly Particle[] _buffer;
            private int _head, _tail, _count;
            private readonly int _capacity;
            private readonly float _particleLife, _particleLifeDecay, _velocityMultiplier;
            private readonly object _bufferLock = new();
            private readonly float _sizeDecayFactor = 0.95f; // Фактор уменьшения размера со временем

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

                        // Обновляем позицию
                        particle.Y -= particle.Velocity * _velocityMultiplier;

                        // Проверяем, не вышла ли частица за верхнюю границу
                        if (particle.Y <= upperBound)
                        {
                            particle.IsActive = false;
                            continue;
                        }

                        // Обновляем время жизни и прозрачность
                        particle.Life -= _particleLifeDecay;
                        if (particle.Life <= 0)
                        {
                            particle.IsActive = false;
                            continue;
                        }

                        // Уменьшаем размер частицы со временем
                        particle.Size *= _sizeDecayFactor;

                        // Если частица стала слишком маленькой, деактивируем её
                        if (particle.Size < 0.5f)
                        {
                            particle.IsActive = false;
                            continue;
                        }

                        // Вычисляем альфа на основе оставшегося времени жизни
                        float lifeRatio = particle.Life / _particleLife;
                        int alphaIndex = (int)(lifeRatio * (alphaCurve.Length - 1));
                        alphaIndex = Math.Clamp(alphaIndex, 0, alphaCurve.Length - 1);
                        particle.Alpha = alphaCurve[alphaIndex];
                    }

                    // Удаляем неактивные частицы из начала буфера
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

                    // Создаем временный массив для активных частиц
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

        private struct Particle
        {
            public float X;
            public float Y;
            public float Velocity;
            public float Size;
            public float Life;
            public float Alpha;
            public bool IsActive;
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
        #endregion
    }
}