#nullable enable

using PrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;
using BufferTarget = OpenTK.Graphics.OpenGL.BufferTarget;
using BufferUsageHint = OpenTK.Graphics.OpenGL.BufferUsageHint;
using VertexAttribPointerType = OpenTK.Graphics.OpenGL.VertexAttribPointerType;

namespace SpectrumNet
{
    public sealed unsafe class RaindropsRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants
        private static class Constants
        {
            // Render Settings
            public const float TARGET_DELTA_TIME = 0.016f;   // Целевое время кадра (сек)
            public const float SMOOTH_FACTOR = 0.2f;           // Коэффициент сглаживания спектра
            public const float TRAIL_LENGTH_MULTIPLIER = 0.15f;  // Множитель для расчёта длины следа
            public const float TRAIL_LENGTH_SIZE_FACTOR = 5f;    // Фактор максимальной длины следа относительно размера капли
            public const float TRAIL_STROKE_MULTIPLIER = 0.6f;   // Множитель для ширины линии следа
            public const float TRAIL_OPACITY_MULTIPLIER = 150f;  // Множитель для прозрачности следа
            public const float TRAIL_INTENSITY_THRESHOLD = 0.3f; // Порог интенсивности для отрисовки следа

            // Simulation Settings
            public const int INITIAL_DROP_COUNT = 30;
            public const float GRAVITY = 9.8f;
            public const float LIFETIME_DECAY = 0.4f;
            public const float SPLASH_REBOUND = 0.5f;
            public const float SPLASH_VELOCITY_THRESHOLD = 1.0f;
            public const float SPAWN_INTERVAL = 0.05f;
            public const float FALLSPEED_THRESHOLD_MULTIPLIER = 1.5f;
            public const float RAINDROP_SIZE_THRESHOLD_MULTIPLIER = 0.9f;
            public const float RAINDROP_SIZE_HIGHLIGHT_THRESHOLD = 0.8f;
            public const float INTENSITY_HIGHLIGHT_THRESHOLD = 0.4f;
            public const float HIGHLIGHT_SIZE_MULTIPLIER = 0.4f;
            public const float HIGHLIGHT_OFFSET_MULTIPLIER = 0.2f;

            // Particle Creation Settings
            public const int SPLASH_PARTICLE_COUNT_MIN = 3;
            public const int SPLASH_PARTICLE_COUNT_MAX = 8;
            public const float PARTICLE_VELOCITY_BASE_MULTIPLIER = 0.7f;
            public const float PARTICLE_VELOCITY_INTENSITY_MULTIPLIER = 0.3f;
            public const float SPLASH_UPWARD_BASE_MULTIPLIER = 0.8f;
            public const float SPLASH_UPWARD_INTENSITY_MULTIPLIER = 0.2f;
            public const float SPLASH_PARTICLE_SIZE_BASE_MULTIPLIER = 0.7f;
            public const float SPLASH_PARTICLE_SIZE_RANDOM_MULTIPLIER = 0.6f;
            public const float SPLASH_PARTICLE_INTENSITY_MULTIPLIER = 0.5f;
            public const float SPLASH_PARTICLE_SIZE_INTENSITY_OFFSET = 0.8f;

            // Logger Settings
            public const string LOGGER_PREFIX = "[RaindropsRenderer] ";
        }
        #endregion

        #region Nested Types

        private readonly struct RenderCache
        {
            public readonly float Width, Height, LowerBound, UpperBound, StepSize;
            public RenderCache(float width, float height, bool isOverlay)
            {
                Width = width;
                Height = height;
                LowerBound = isOverlay ? height * Settings.Instance.OverlayHeightMultiplier : height;
                UpperBound = 0f;
                StepSize = width / Settings.Instance.MaxRaindrops;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Raindrop
        {
            public readonly float X, Y, FallSpeed, Size, Intensity;
            public readonly int SpectrumIndex;
            public Raindrop(float x, float y, float fallSpeed, float size, float intensity, int spectrumIndex) =>
                (X, Y, FallSpeed, Size, Intensity, SpectrumIndex) = (x, y, fallSpeed, size, intensity, spectrumIndex);
            public Raindrop WithNewY(float newY) => new Raindrop(X, newY, FallSpeed, Size, Intensity, SpectrumIndex);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Particle
        {
            public float X, Y, VelocityX, VelocityY, Lifetime, Size;
            public bool IsSplash;
            public Particle(float x, float y, float velocityX, float velocityY, float size, bool isSplash) =>
                (X, Y, VelocityX, VelocityY, Lifetime, Size, IsSplash) = (x, y, velocityX, velocityY, 1.0f, size, isSplash);
            public bool Update(float deltaTime, float lowerBound)
            {
                X += VelocityX * deltaTime;
                Y += VelocityY * deltaTime;
                VelocityY += deltaTime * Constants.GRAVITY;
                Lifetime -= deltaTime * Constants.LIFETIME_DECAY;
                if (IsSplash && Y >= lowerBound)
                {
                    Y = lowerBound;
                    VelocityY = -VelocityY * Constants.SPLASH_REBOUND;
                    if (Math.Abs(VelocityY) < Constants.SPLASH_VELOCITY_THRESHOLD)
                        VelocityY = 0;
                }
                return Lifetime > 0;
            }
        }

        private sealed class ParticleBuffer
        {
            private Particle[] _particles;
            private int _count;
            private float _lowerBound;
            private readonly Random _random;
            public ParticleBuffer(int capacity, float lowerBound)
            {
                _particles = new Particle[capacity];
                _count = 0;
                _lowerBound = lowerBound;
                _random = new Random();
            }
            public void UpdateLowerBound(float lowerBound) => _lowerBound = lowerBound;
            public void ResizeBuffer(int newCapacity)
            {
                if (newCapacity <= 0) return;
                var newParticles = new Particle[newCapacity];
                int copyCount = Math.Min(_count, newCapacity);
                if (copyCount > 0)
                    Array.Copy(_particles, newParticles, copyCount);
                _particles = newParticles;
                _count = copyCount;
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
                    if (p.Update(deltaTime, _lowerBound))
                    {
                        if (writeIndex != i)
                            _particles[writeIndex] = p;
                        writeIndex++;
                    }
                }
                _count = writeIndex;
            }
            public void RenderParticles(Color4 baseColor, Action<float, float, float, Color4> drawCircle)
            {
                for (int i = 0; i < _count; i++)
                {
                    Particle p = _particles[i];
                    float clampedLifetime = Math.Clamp(p.Lifetime, 0f, 1f);
                    float alphaMultiplier = clampedLifetime * clampedLifetime;
                    float alpha = 255 * alphaMultiplier;
                    alpha = Math.Clamp(alpha, 0, 255);
                    // Конвертируем компоненты в диапазон 0..1
                    Color4 particleColor = new Color4(baseColor.R, baseColor.G, baseColor.B, alpha / 255f);
                    float sizeMultiplier = 0.8f + 0.2f * clampedLifetime;
                    drawCircle(p.X, p.Y, p.Size * sizeMultiplier, particleColor);
                }
            }
            public void CreateSplashParticles(float x, float y, float intensity)
            {
                int count = Math.Min(_random.Next(Constants.SPLASH_PARTICLE_COUNT_MIN, Constants.SPLASH_PARTICLE_COUNT_MAX),
                                     Settings.Instance.MaxParticles - _count);
                if (count <= 0) return;
                float particleVelocityMax = Settings.Instance.ParticleVelocityMax *
                    (Constants.PARTICLE_VELOCITY_BASE_MULTIPLIER + intensity * Constants.PARTICLE_VELOCITY_INTENSITY_MULTIPLIER);
                float upwardForce = Settings.Instance.SplashUpwardForce *
                    (Constants.SPLASH_UPWARD_BASE_MULTIPLIER + intensity * Constants.SPLASH_UPWARD_INTENSITY_MULTIPLIER);
                for (int i = 0; i < count; i++)
                {
                    float angle = (float)(_random.NextDouble() * Math.PI * 2);
                    float speed = (float)(_random.NextDouble() * particleVelocityMax);
                    float size = Settings.Instance.SplashParticleSize *
                        (Constants.SPLASH_PARTICLE_SIZE_BASE_MULTIPLIER + (float)_random.NextDouble() * Constants.SPLASH_PARTICLE_SIZE_RANDOM_MULTIPLIER) *
                        (intensity * Constants.SPLASH_PARTICLE_INTENSITY_MULTIPLIER + Constants.SPLASH_PARTICLE_SIZE_INTENSITY_OFFSET);
                    AddParticle(new Particle(
                        x, y,
                        MathF.Cos(angle) * speed,
                        MathF.Sin(angle) * speed - upwardForce,
                        size,
                        true));
                }
            }
        }
        #endregion

        #region Fields
        private static RaindropsRenderer? _instance;
        private RenderCache _renderCache;
        private Raindrop[] _raindrops;
        private int _raindropCount;
        private readonly Random _random = new();
        private float[] _smoothedSpectrumCache;
        private bool _isInitialized, _isOverlayActive, _cacheNeedsUpdate, _disposed;
        private readonly ParticleBuffer _particleBuffer;
        private float _timeSinceLastSpawn;
        private bool _firstRender = true;
        private readonly Stopwatch _frameTimer = new();
        private float _actualDeltaTime = Constants.TARGET_DELTA_TIME;
        private float _averageLoudness = 0f;
        private readonly object _spectrumLock = new();
        private float[]? _processedSpectrum;
        private int _frameCounter = 0;
        private int _particleUpdateSkip = 1;
        private int _effectsThreshold = 3;

        // Шейдерные программы и буферы
        private int _circleVAO;
        private int _circleVBO;
        private int _lineVAO;
        private int _lineVBO;
        private ShaderProgram? _shaderProgram;

        // Quality settings fields
        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useAntiAlias = true;
        private bool _useAdvancedEffects = true;

        private const string LogPrefix = Constants.LOGGER_PREFIX;

        // Цвет отрисовки по умолчанию
        private Color4 _baseColor = Color4.CornflowerBlue;
        #endregion

        #region Constructor and Instance Management
        private RaindropsRenderer()
        {
            _raindrops = new Raindrop[Settings.Instance.MaxRaindrops];
            _smoothedSpectrumCache = new float[Settings.Instance.MaxRaindrops];
            _particleBuffer = new ParticleBuffer(Settings.Instance.MaxParticles, 1);
            _renderCache = new RenderCache(1, 1, false);
            _frameTimer.Start();
            Settings.Instance.PropertyChanged += OnSettingsChanged;
            InitializeOpenGLResources();
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "RaindropsRenderer initialized");
        }

        private void InitializeOpenGLResources()
        {
            // Инициализация VAO и VBO для кругов
            _circleVAO = GL.GenVertexArray();
            _circleVBO = GL.GenBuffer();

            // Инициализация VAO и VBO для линий
            _lineVAO = GL.GenVertexArray();
            _lineVBO = GL.GenBuffer();
        }

        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.MaxRaindrops))
            {
                var newRaindrops = new Raindrop[Settings.Instance.MaxRaindrops];
                var newSmoothedCache = new float[Settings.Instance.MaxRaindrops];
                int copyCount = Math.Min(_raindropCount, Settings.Instance.MaxRaindrops);
                if (copyCount > 0)
                    Array.Copy(_raindrops, newRaindrops, copyCount);
                _raindrops = newRaindrops;
                _smoothedSpectrumCache = newSmoothedCache;
                _raindropCount = copyCount;
                _cacheNeedsUpdate = true;
            }
            else if (e.PropertyName == nameof(Settings.MaxParticles))
            {
                _particleBuffer.ResizeBuffer(Settings.Instance.MaxParticles);
            }
            else if (e.PropertyName == nameof(Settings.OverlayHeightMultiplier))
            {
                _cacheNeedsUpdate = true;
            }
        }

        public static RaindropsRenderer GetInstance() => _instance ??= new RaindropsRenderer();
        #endregion

        #region ISpectrumRenderer Implementation
        public RenderQuality Quality
        {
            get => _quality;
            set
            {
                if (_quality != value)
                {
                    _quality = value;
                    ApplyQualitySettings();
                }
            }
        }

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            if (_isOverlayActive != isOverlayActive)
            {
                _isOverlayActive = isOverlayActive;
                _cacheNeedsUpdate = true;
                _firstRender = true;
                _raindropCount = 0;
                _particleBuffer.Clear();
            }
            Quality = quality;
        }

        public void Initialize()
        {
            if (_disposed)
            {
                _raindropCount = 0;
                _particleBuffer.Clear();
                _disposed = false;
                InitializeOpenGLResources();
            }
            _isInitialized = true;
            _firstRender = true;
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "RaindropsRenderer initialized");
        }

        // Отрисовка с использованием OpenTK. Параметры viewport задают область рендеринга,
        public void Render(float[]? spectrum, Viewport viewport, float barWidth,
                           float barSpacing, int barCount, ShaderProgram? shader,
                           Action<Viewport> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(spectrum, viewport))
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Invalid render parameters for RaindropsRenderer");
                return;
            }

            // Сохраняем шейдер для использования в методах рисования
            _shaderProgram = shader;

            if (shader != null)
            {
                shader.Use();
            }

            float[] renderSpectrum;
            int spectrumLength = spectrum!.Length;
            int actualBarCount = Math.Min(spectrumLength, barCount);

            lock (_spectrumLock)
            {
                _processedSpectrum = ProcessSpectrumSynchronously(spectrum, actualBarCount);
                renderSpectrum = _processedSpectrum;
            }

            float targetDeltaTime = Constants.TARGET_DELTA_TIME;
            float elapsed = (float)_frameTimer.Elapsed.TotalSeconds;
            _frameTimer.Restart();
            float speedMultiplier = elapsed / targetDeltaTime;
            _actualDeltaTime = Math.Clamp(
                targetDeltaTime * speedMultiplier,
                Settings.Instance.MinTimeStep,
                Settings.Instance.MaxTimeStep
            );

            _frameCounter = (_frameCounter + 1) % (_particleUpdateSkip + 1);

            UpdateAndRenderScene(renderSpectrum, viewport, actualBarCount);

            drawPerformanceInfo?.Invoke(viewport);

            if (shader != null)
            {
                GL.UseProgram(0);
            }
        }
        #endregion

        #region Spectrum Processing
        private float[] ProcessSpectrumSynchronously(float[] spectrum, int barCount)
        {
            if (barCount <= 0) return Array.Empty<float>();
            Span<float> result = stackalloc float[barCount];
            ProcessSpectrumData(spectrum.AsSpan(0, Math.Min(spectrum.Length, barCount)), result);
            float[] output = new float[barCount];
            result.CopyTo(output.AsSpan(0, barCount));
            return output;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessSpectrumData(ReadOnlySpan<float> src, Span<float> dst)
        {
            if (src.IsEmpty || dst.IsEmpty) return;
            float sum = 0f;
            float blockSize = src.Length / (float)dst.Length;
            float smoothFactor = Constants.SMOOTH_FACTOR;

            for (int i = 0; i < dst.Length; i++)
            {
                int start = (int)(i * blockSize);
                int end = Math.Min((int)((i + 1) * blockSize), src.Length);
                if (end <= start)
                {
                    dst[i] = dst[i] * (1 - smoothFactor);
                    continue;
                }
                int count = end - start;
                float value = 0f;
                if (count >= Vector<float>.Count)
                {
                    int simdLength = count - (count % Vector<float>.Count);
                    Vector<float> vSum = Vector<float>.Zero;
                    for (int j = 0; j < simdLength; j += Vector<float>.Count)
                    {
                        // Для простоты создаём временный массив из span
                        float[] temp = src.Slice(start + j, Vector<float>.Count).ToArray();
                        vSum += new Vector<float>(temp);
                    }
                    for (int k = 0; k < Vector<float>.Count; k++)
                    {
                        value += vSum[k];
                    }
                    for (int j = simdLength; j < count; j++)
                    {
                        value += src[start + j];
                    }
                }
                else
                {
                    for (int j = start; j < end; j++)
                    {
                        value += src[j];
                    }
                }
                value /= count;
                dst[i] = dst[i] * (1 - smoothFactor) + value * smoothFactor;
                sum += dst[i];
            }
            _averageLoudness = Math.Clamp(sum / dst.Length * 4.0f, 0f, 1f);
        }
        #endregion

        #region Simulation Methods
        private bool ValidateRenderParameters(float[]? spectrum, Viewport viewport) =>
            _isInitialized && !_disposed &&
            spectrum != null && spectrum.Length > 0 &&
            viewport.Width > 0 && viewport.Height > 0;

        private void UpdateAndRenderScene(float[] spectrum, Viewport viewport, int barCount)
        {
            if (_cacheNeedsUpdate || _renderCache.Width != viewport.Width || _renderCache.Height != viewport.Height)
            {
                _renderCache = new RenderCache(viewport.Width, viewport.Height, _isOverlayActive);
                _particleBuffer.UpdateLowerBound(_renderCache.LowerBound);
                _cacheNeedsUpdate = false;
            }
            if (_firstRender)
            {
                InitializeInitialDrops(barCount);
                _firstRender = false;
            }
            _timeSinceLastSpawn += _actualDeltaTime;
            UpdateSimulation(spectrum, barCount);

            // Отрисовка частиц (фон)
            _particleBuffer.RenderParticles(_baseColor, DrawCircle);

            // Отрисовка следов капель (передний план)
            RenderRaindropTrails(spectrum);

            // Отрисовка самих капель (передний план)
            RenderRaindrops(spectrum);
        }

        private void InitializeInitialDrops(int barCount)
        {
            _raindropCount = 0;
            float width = _renderCache.Width;
            float height = _renderCache.Height;
            int initialCount = Math.Min(Constants.INITIAL_DROP_COUNT, Settings.Instance.MaxRaindrops);
            for (int i = 0; i < initialCount; i++)
            {
                int spectrumIndex = _random.Next(barCount);
                float x = width * (float)_random.NextDouble();
                float y = height * (float)_random.NextDouble() * 0.5f;
                float speedVariation = (float)(_random.NextDouble() * Settings.Instance.SpeedVariation);
                float fallSpeed = Settings.Instance.BaseFallSpeed + speedVariation;
                float size = Settings.Instance.RaindropSize * (0.7f + (float)_random.NextDouble() * 0.6f);
                float intensity = 0.3f + (float)_random.NextDouble() * 0.3f;
                _raindrops[_raindropCount++] = new Raindrop(x, y, fallSpeed, size, intensity, spectrumIndex);
            }
        }

        private void UpdateSimulation(float[] spectrum, int barCount)
        {
            UpdateRaindrops(spectrum);
            if (_frameCounter == 0)
            {
                float adjustedDelta = _actualDeltaTime * (_particleUpdateSkip + 1);
                _particleBuffer.UpdateParticles(adjustedDelta);
            }
            if (_timeSinceLastSpawn >= Constants.SPAWN_INTERVAL)
            {
                SpawnNewDrops(spectrum, barCount);
                _timeSinceLastSpawn = 0;
            }
        }

        private void UpdateRaindrops(float[] spectrum)
        {
            int writeIdx = 0;
            for (int i = 0; i < _raindropCount; i++)
            {
                Raindrop drop = _raindrops[i];
                float newY = drop.Y + drop.FallSpeed * _actualDeltaTime;
                if (newY < _renderCache.LowerBound)
                {
                    _raindrops[writeIdx++] = drop.WithNewY(newY);
                }
                else
                {
                    float intensity = drop.SpectrumIndex < spectrum.Length ? spectrum[drop.SpectrumIndex] : drop.Intensity;
                    if (intensity > 0.2f)
                    {
                        _particleBuffer.CreateSplashParticles(drop.X, _renderCache.LowerBound, intensity);
                    }
                }
            }
            _raindropCount = writeIdx;
        }

        private void SpawnNewDrops(float[] spectrum, int barCount)
        {
            if (barCount <= 0 || spectrum.Length == 0) return;
            float stepWidth = _renderCache.Width / barCount;
            float threshold = _isOverlayActive ? Settings.Instance.SpawnThresholdOverlay : Settings.Instance.SpawnThresholdNormal;
            float spawnBoost = 1.0f + _averageLoudness * 2.0f;
            int maxSpawnsPerFrame = 3;
            int spawnsThisFrame = 0;
            for (int i = 0; i < barCount && i < spectrum.Length && spawnsThisFrame < maxSpawnsPerFrame; i++)
            {
                float intensity = spectrum[i];
                if (intensity > threshold && _random.NextDouble() < Settings.Instance.SpawnProbability * intensity * spawnBoost)
                {
                    float x = i * stepWidth + stepWidth * 0.5f +
                              (float)(_random.NextDouble() * stepWidth * 0.5f - stepWidth * 0.25f);
                    float speedVariation = (float)(_random.NextDouble() * Settings.Instance.SpeedVariation);
                    float fallSpeed = Settings.Instance.BaseFallSpeed *
                                      (1f + intensity * Settings.Instance.IntensitySpeedMultiplier) +
                                      speedVariation;
                    float size = Settings.Instance.RaindropSize * (0.8f + intensity * 0.4f) * (0.9f + (float)_random.NextDouble() * 0.2f);
                    if (_raindropCount < _raindrops.Length)
                    {
                        _raindrops[_raindropCount++] = new Raindrop(x, _renderCache.UpperBound, fallSpeed, size, intensity, i);
                        spawnsThisFrame++;
                    }
                }
            }
        }
        #endregion

        #region Rendering Methods
        private void RenderRaindropTrails(float[] spectrum)
        {
            for (int i = 0; i < _raindropCount; i++)
            {
                Raindrop drop = _raindrops[i];
                if (drop.FallSpeed < Settings.Instance.BaseFallSpeed * Constants.FALLSPEED_THRESHOLD_MULTIPLIER ||
                    drop.Size < Settings.Instance.RaindropSize * Constants.RAINDROP_SIZE_THRESHOLD_MULTIPLIER)
                    continue;
                float intensity = drop.SpectrumIndex < spectrum.Length ? spectrum[drop.SpectrumIndex] : drop.Intensity;
                if (intensity < Constants.TRAIL_INTENSITY_THRESHOLD)
                    continue;
                float trailLength = Math.Min(
                    drop.FallSpeed * Constants.TRAIL_LENGTH_MULTIPLIER * intensity,
                    drop.Size * Constants.TRAIL_LENGTH_SIZE_FACTOR
                );
                if (trailLength < Constants.TRAIL_INTENSITY_THRESHOLD)
                    continue;
                float alpha = Constants.TRAIL_OPACITY_MULTIPLIER * intensity;
                alpha = Math.Clamp(alpha, 0, 255);
                Color4 trailColor = new Color4(_baseColor.R, _baseColor.G, _baseColor.B, alpha / 255f);
                DrawLine(drop.X, drop.Y, drop.X, drop.Y - trailLength, drop.Size * Constants.TRAIL_STROKE_MULTIPLIER, trailColor);
            }
        }

        private void RenderRaindrops(float[] spectrum)
        {
            for (int i = 0; i < _raindropCount; i++)
            {
                Raindrop drop = _raindrops[i];
                float intensity = drop.SpectrumIndex < spectrum.Length
                    ? spectrum[drop.SpectrumIndex] * 0.7f + drop.Intensity * 0.3f
                    : drop.Intensity;
                float alpha = Math.Min(0.7f + intensity * 0.3f, 1.0f);
                Color4 dropColor = new Color4(_baseColor.R, _baseColor.G, _baseColor.B, alpha);
                DrawCircle(drop.X, drop.Y, drop.Size, dropColor);
                if (_effectsThreshold < 2 &&
                    drop.Size > Settings.Instance.RaindropSize * Constants.RAINDROP_SIZE_HIGHLIGHT_THRESHOLD &&
                    intensity > Constants.INTENSITY_HIGHLIGHT_THRESHOLD)
                {
                    float highlightSize = drop.Size * Constants.HIGHLIGHT_SIZE_MULTIPLIER;
                    float highlightX = drop.X - drop.Size * Constants.HIGHLIGHT_OFFSET_MULTIPLIER;
                    float highlightY = drop.Y - drop.Size * Constants.HIGHLIGHT_OFFSET_MULTIPLIER;
                    float hAlpha = 150 * intensity;
                    hAlpha = Math.Clamp(hAlpha, 0, 255);
                    Color4 highlightColor = new Color4(1f, 1f, 1f, hAlpha / 255f);
                    DrawCircle(highlightX, highlightY, highlightSize, highlightColor);
                }
            }
        }
        #endregion

        #region Quality Settings
        private void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _useAdvancedEffects = false;
                    _effectsThreshold = 4;
                    break;
                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _useAdvancedEffects = true;
                    _effectsThreshold = 3;
                    break;
                case RenderQuality.High:
                    _useAntiAlias = true;
                    _useAdvancedEffects = true;
                    _effectsThreshold = 2;
                    break;
            }
        }
        #endregion

        #region OpenTK Drawing Helpers

        private void DrawCircle(float centerX, float centerY, float radius, Color4 color)
        {
            DrawCircle(centerX, centerY, radius, color, 32);
        }

        private void DrawCircle(float centerX, float centerY, float radius, Color4 color, int segments)
        {
            if (_shaderProgram != null)
            {
                _shaderProgram.Use();
                // Предполагаем, что у вас есть uniform-переменная "uColor" в шейдере
                int colorLocation = GL.GetUniformLocation(_shaderProgram.ProgramId, "uColor");
                if (colorLocation != -1)
                {
                    GL.Uniform4(colorLocation, color);
                }
            }

            // Используем традиционный метод рисования для совместимости
            GL.Color4(color);
            GL.Begin(PrimitiveType.TriangleFan);
            GL.Vertex2(centerX, centerY);
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * MathF.PI * 2 / segments;
                float x = centerX + MathF.Cos(angle) * radius;
                float y = centerY + MathF.Sin(angle) * radius;
                GL.Vertex2(x, y);
            }
            GL.End();
        }

        private void DrawLine(float x1, float y1, float x2, float y2, float lineWidth, Color4 color)
        {
            if (_shaderProgram != null)
            {
                _shaderProgram.Use();
                // Предполагаем, что у вас есть uniform-переменная "uColor" в шейдере
                int colorLocation = GL.GetUniformLocation(_shaderProgram.ProgramId, "uColor");
                if (colorLocation != -1)
                {
                    GL.Uniform4(colorLocation, color);
                }
            }

            // Используем традиционный метод рисования для совместимости
            GL.LineWidth(lineWidth);
            GL.Color4(color);
            GL.Begin(PrimitiveType.Lines);
            GL.Vertex2(x1, y1);
            GL.Vertex2(x2, y2);
            GL.End();
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            if (_disposed) return;

            // Освобождаем ресурсы OpenGL
            GL.DeleteVertexArray(_circleVAO);
            GL.DeleteBuffer(_circleVBO);
            GL.DeleteVertexArray(_lineVAO);
            GL.DeleteBuffer(_lineVBO);

            Settings.Instance.PropertyChanged -= OnSettingsChanged;
            _processedSpectrum = null;
            _isInitialized = false;
            _disposed = true;
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "RaindropsRenderer disposed");
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}