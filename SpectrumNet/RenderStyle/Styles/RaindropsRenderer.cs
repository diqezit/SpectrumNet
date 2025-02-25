#nullable enable

namespace SpectrumNet
{
    #region Renderers Implementations

    public static class RaindropsSettings
    {
        public const int MaxRaindrops = 1000;
        public const int MaxParticles = 5000;
        public const float BaseFallSpeed = 5f;
        public const float SpectrumThreshold = 0.1f;
        public const float OverlayBottomMultiplier = 3.75f;
        public const double SpawnProbability = 0.15;
        public const float DeltaTime = 0.016f;
        public const float ParticleSize = 2f;
        public const float RaindropSize = 2f;
        public const float ParticleVelocityMultiplier = 5f;
        public const float SpeedVariation = 3f;
        public const float IntensitySpeedMultiplier = 4f;
        public const float InitialDropsCount = 10;
        public const int MaxSplashParticles = 15; // Максимальное количество частиц в брызгах
        public const float SplashUpwardForce = 8f; // Сила отталкивания вверх
    }

    public sealed class RaindropsRenderer : ISpectrumRenderer, IDisposable
    {
        #region Nested Types

        private readonly struct RenderCache
        {
            public readonly float Width;
            public readonly float Height;
            public readonly float LowerBound;
            public readonly float UpperBound;
            public readonly float StepSize;

            public RenderCache(float width, float height, bool isOverlay)
            {
                Width = width;
                Height = height;
                LowerBound = isOverlay ? height / RaindropsSettings.OverlayBottomMultiplier : height;
                UpperBound = isOverlay ? height * 0.1f : 0f;
                StepSize = width / RaindropsSettings.MaxRaindrops;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Raindrop
        {
            public readonly float X;
            public readonly float Y;
            public readonly float FallSpeed;
            public readonly int SpectrumIndex;

            public Raindrop(float x, float y, float fallSpeed, int spectrumIndex) =>
                (X, Y, FallSpeed, SpectrumIndex) = (x, y, fallSpeed, spectrumIndex);

            public Raindrop WithNewY(float newY) => new(X, newY, FallSpeed, SpectrumIndex);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Particle
        {
            public float X;
            public float Y;
            public float VelocityX;
            public float VelocityY;
            public float Lifetime; // Время жизни частицы
            public bool IsSplash; // Флаг, указывающий, является ли частица брызгами

            public Particle(float x, float y, float velocityX, float velocityY, bool isSplash)
            {
                X = x;
                Y = y;
                VelocityX = velocityX;
                VelocityY = velocityY;
                Lifetime = 1.0f; // Начальное время жизни
                IsSplash = isSplash;
            }

            public bool Update(float deltaTime, float lowerBound)
            {
                X += VelocityX * deltaTime;
                Y += VelocityY * deltaTime;

                // Применяем гравитацию
                VelocityY += deltaTime * 9.8f;

                // Уменьшаем время жизни
                Lifetime -= deltaTime * 0.5f;

                // Если частица - брызги и достигла нижней границы, отражаем её
                if (IsSplash && Y >= lowerBound)
                {
                    Y = lowerBound;
                    VelocityY = -VelocityY * 0.6f; // Отражение с потерей энергии

                    // Если скорость слишком мала, прекращаем отражение
                    if (Math.Abs(VelocityY) < 1.0f)
                    {
                        VelocityY = 0;
                    }
                }

                // Возвращаем true, если частица еще жива
                return Lifetime > 0;
            }
        }

        private sealed class ParticleBuffer
        {
            private readonly Particle[] _particles;
            private int _count;
            private float _lowerBound; // Убрано readonly

            public ParticleBuffer(int capacity, float lowerBound)
            {
                _particles = new Particle[capacity];
                _count = 0;
                _lowerBound = lowerBound;
            }

            public void UpdateLowerBound(float lowerBound)
            {
                _lowerBound = lowerBound;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddParticle(in Particle particle)
            {
                if (_count < _particles.Length)
                    _particles[_count++] = particle;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Clear() => _count = 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void UpdateParticles(float deltaTime)
            {
                int writeIndex = 0;

                for (int i = 0; i < _count; i++)
                {
                    ref Particle p = ref _particles[i];

                    // Обновляем частицу и проверяем, жива ли она еще
                    if (p.Update(deltaTime, _lowerBound))
                    {
                        // Если частица еще жива, сохраняем её
                        if (writeIndex != i)
                            _particles[writeIndex] = p;
                        writeIndex++;
                    }
                }

                _count = writeIndex;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RenderParticles(SKCanvas canvas, SKPaint paint)
            {
                for (int i = 0; i < _count; i++)
                {
                    ref Particle p = ref _particles[i];

                    // Устанавливаем прозрачность в зависимости от времени жизни
                    byte alpha = (byte)(p.Lifetime * 255);
                    paint.Color = paint.Color.WithAlpha(alpha);

                    // Размер частицы зависит от того, брызги это или нет
                    float size = p.IsSplash ? RaindropsSettings.ParticleSize * 1.5f : RaindropsSettings.ParticleSize;

                    canvas.DrawCircle(p.X, p.Y, size, paint);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void CreateSplashParticles(float x, float y, Random random)
            {
                int particleCount = random.Next(5, RaindropsSettings.MaxSplashParticles);

                for (int i = 0; i < particleCount; i++)
                {
                    float angle = (float)(random.NextDouble() * Math.PI * 2);
                    float speed = (float)(random.NextDouble() * RaindropsSettings.ParticleVelocityMultiplier);

                    // Создаем частицы брызг с большей вертикальной скоростью (отталкивание)
                    AddParticle(new Particle(
                        x,
                        y,
                        MathF.Cos(angle) * speed,
                        MathF.Sin(angle) * speed - RaindropsSettings.SplashUpwardForce, // Сильное начальное движение вверх
                        true // Это брызги
                    ));
                }
            }
        }

        #endregion

        #region Fields

        private static readonly Lazy<RaindropsRenderer> _instance = new(() => new RaindropsRenderer());
        private RenderCache _renderCache;
        private readonly Raindrop[] _raindrops = new Raindrop[RaindropsSettings.MaxRaindrops];
        private int _raindropCount;
        private readonly SKPath _dropsPath = new();
        private readonly Random _random = new();
        private readonly float[] _scaledSpectrumCache = new float[RaindropsSettings.MaxRaindrops];
        private bool _isInitialized, _isOverlayActive, _cacheNeedsUpdate, _isDisposed;
        private ParticleBuffer _particleBuffer;
        private readonly SKPaint _raindropPaint = new() { Style = SKPaintStyle.Fill };
        private float _timeSinceLastSpawn;
        private bool _firstRender = true;

        #endregion

        #region Constructor and Instance Management

        private RaindropsRenderer()
        {
            _particleBuffer = new ParticleBuffer(RaindropsSettings.MaxParticles, 1);
            _renderCache = new RenderCache(1, 1, false);
            _timeSinceLastSpawn = 0;
        }

        public static RaindropsRenderer GetInstance() => _instance.Value;

        #endregion

        #region ISpectrumRenderer Implementation

        public void Initialize()
        {
            if (_isDisposed)
            {
                _dropsPath.Reset();
                _raindropCount = 0;
                _particleBuffer.Clear();
                _isDisposed = false;
            }

            _isInitialized = true;
            _firstRender = true;
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive == isOverlayActive) return;

            _isOverlayActive = isOverlayActive;
            _cacheNeedsUpdate = true;
            _firstRender = true;

            _raindropCount = 0;
            _particleBuffer.Clear();
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                          float barWidth, float barSpacing, int barCount,
                          SKPaint? paint, Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, paint))
            {
                Log.Warning("RaindropsRenderer: Invalid render parameters");
                return;
            }

            try
            {
                // Обновляем кэш при необходимости
                if (_cacheNeedsUpdate || _renderCache.Width != info.Width || _renderCache.Height != info.Height)
                {
                    _renderCache = new RenderCache(info.Width, info.Height, _isOverlayActive);
                    _particleBuffer.UpdateLowerBound(_renderCache.LowerBound);
                    _cacheNeedsUpdate = false;
                }

                int actualBarCount = Math.Min(spectrum!.Length / 2, barCount);
                ScaleSpectrum(spectrum, _scaledSpectrumCache.AsSpan(0, actualBarCount), actualBarCount);

                // Если это первый рендер, создаем начальные капли
                if (_firstRender)
                {
                    InitializeInitialDrops(actualBarCount);
                    _firstRender = false;
                }

                // Увеличиваем таймер
                _timeSinceLastSpawn += RaindropsSettings.DeltaTime;

                // Обновляем симуляцию
                UpdateSimulation(_scaledSpectrumCache.AsSpan(0, actualBarCount), actualBarCount);

                using var localPaint = paint!.Clone();
                RenderScene(canvas!, localPaint);
                drawPerformanceInfo?.Invoke(canvas!, info);
            }
            catch (Exception ex)
            {
                Log.Error($"RaindropsRenderer: Exception during render: {ex.Message}");
            }
        }

        #endregion

        #region Simulation Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKPaint? paint) =>
            _isInitialized && !_isDisposed && canvas != null && spectrum != null &&
            spectrum.Length > 0 && paint != null;

        private void InitializeInitialDrops(int barCount)
        {
            // Создаем начальные капли при первом рендере
            _raindropCount = 0;

            float width = _renderCache.Width;
            float height = _renderCache.Height;

            // Создаем несколько капель в случайных позициях
            for (int i = 0; i < RaindropsSettings.InitialDropsCount; i++)
            {
                int spectrumIndex = _random.Next(barCount);
                float x = width * (float)_random.NextDouble();
                float y = height * (float)_random.NextDouble() * 0.5f; // В верхней половине экрана
                float speedVariation = (float)(_random.NextDouble() * RaindropsSettings.SpeedVariation);
                float fallSpeed = RaindropsSettings.BaseFallSpeed + speedVariation;

                _raindrops[_raindropCount++] = new Raindrop(x, y, fallSpeed, spectrumIndex);
            }

            // Создаем несколько начальных брызг
            for (int i = 0; i < 20; i++)
            {
                float x = width * (float)_random.NextDouble();
                float y = _renderCache.LowerBound - height * 0.1f * (float)_random.NextDouble();

                _particleBuffer.CreateSplashParticles(x, y, _random);
            }

            Log.Debug($"RaindropsRenderer: Initialized {_raindropCount} initial drops and splash particles");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateSimulation(ReadOnlySpan<float> spectrum, int barCount)
        {
            // Обновляем капли с увеличенной скоростью
            UpdateRaindrops(spectrum);
            _particleBuffer.UpdateParticles(RaindropsSettings.DeltaTime);

            // Спавним новые капли с контролем частоты
            if (_timeSinceLastSpawn >= 0.05f)
            {
                SpawnNewDrops(spectrum, barCount);
                _timeSinceLastSpawn = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ScaleSpectrum(ReadOnlySpan<float> src, Span<float> dst, int count)
        {
            if (src.IsEmpty || dst.IsEmpty || count <= 0) return;

            float blockSize = src.Length / (2f * count);
            int halfLen = src.Length / 2;

            for (int i = 0; i < count; i++)
            {
                int start = (int)(i * blockSize);
                int end = Math.Min((int)((i + 1) * blockSize), halfLen);
                float sum = 0;

                for (int j = start; j < end; j++)
                    sum += src[j];

                dst[i] = (end > start) ? sum / (end - start) : 0f;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateRaindrops(ReadOnlySpan<float> spectrum)
        {
            int writeIdx = 0;
            for (int i = 0; i < _raindropCount; i++)
            {
                Raindrop drop = _raindrops[i];

                // Используем больший множитель для скорости падения
                float speedMultiplier = 1.5f;
                float newY = drop.Y + drop.FallSpeed * RaindropsSettings.DeltaTime * speedMultiplier;

                if (newY < _renderCache.LowerBound)
                {
                    _raindrops[writeIdx++] = drop.WithNewY(newY);
                }
                else
                {
                    // Капля достигла нижней границы - создаем брызги
                    _particleBuffer.CreateSplashParticles(drop.X, _renderCache.LowerBound, _random);
                }
            }
            _raindropCount = writeIdx;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SpawnNewDrops(ReadOnlySpan<float> spectrum, int barCount)
        {
            float stepWidth = _renderCache.Width / barCount;

            for (int i = 0; i < spectrum.Length; i++)
            {
                float intensity = spectrum[i];

                if (intensity > RaindropsSettings.SpectrumThreshold &&
                    _random.NextDouble() < RaindropsSettings.SpawnProbability * intensity)
                {
                    // Точная позиция X соответствует спектральному индексу
                    float x = i * stepWidth + stepWidth * 0.5f + (float)(_random.NextDouble() * stepWidth * 0.5f - stepWidth * 0.25f);

                    // Увеличиваем скорость падения и добавляем вариацию
                    float speedVariation = (float)(_random.NextDouble() * RaindropsSettings.SpeedVariation);
                    float fallSpeed = RaindropsSettings.BaseFallSpeed *
                                     (1f + intensity * RaindropsSettings.IntensitySpeedMultiplier) +
                                     speedVariation;

                    if (_raindropCount < RaindropsSettings.MaxRaindrops)
                        _raindrops[_raindropCount++] = new Raindrop(x, _renderCache.UpperBound, fallSpeed, i);
                }
            }
        }

        #endregion

        #region Rendering Methods

        private void RenderScene(SKCanvas canvas, SKPaint paint)
        {
            // Рендерим капли
            _dropsPath.Reset();

            for (int i = 0; i < _raindropCount; i++)
                _dropsPath.AddCircle(_raindrops[i].X, _raindrops[i].Y, RaindropsSettings.RaindropSize);

            paint.Style = SKPaintStyle.Fill;
            canvas.DrawPath(_dropsPath, paint);

            // Рендерим частицы (включая брызги)
            _particleBuffer.RenderParticles(canvas, paint);
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            if (_isDisposed) return;

            _dropsPath.Dispose();
            _raindropPaint.Dispose();

            _isInitialized = false;
            _isDisposed = true;

            Log.Debug("RaindropsRenderer disposed");
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    #endregion
}