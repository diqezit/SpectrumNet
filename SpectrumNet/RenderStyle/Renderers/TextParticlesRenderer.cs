#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SkiaSharp;
using static System.Math;
using static System.Numerics.Vector;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace SpectrumNet
{
    /// <summary>
    /// Renderer that visualizes spectrum data as text particles with 3D perspective effects.
    /// Particles are spawned based on spectrum intensity, move with physics-based motion, and fade over time.
    /// </summary>
    public sealed class TextParticlesRenderer : BaseSpectrumRenderer
    {
        #region Singleton Pattern
        private static readonly Lazy<TextParticlesRenderer> _instance = new(() => new TextParticlesRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Gets the singleton instance of the renderer.
        /// </summary>
        public static TextParticlesRenderer GetInstance() => _instance.Value;
        #endregion

        #region Constants
        private static class Constants
        {
            public const string LOG_PREFIX = "TextParticlesRenderer";

            public static class Rendering
            {
                public const float FOCAL_LENGTH = 1000f;      // Distance to projection plane
                public const float BASE_TEXT_SIZE = 12f;      // Base size for text particles
                public const float ALPHA_MAX = 255f;          // Maximum alpha value
            }

            public static class Particles
            {
                public const int VELOCITY_LOOKUP_SIZE = 1024; // Size of pre-computed velocity array
                public const float GRAVITY = 9.81f;           // Downward acceleration
                public const float AIR_RESISTANCE = 0.98f;    // Velocity dampening factor
                public const float MAX_VELOCITY = 15f;        // Maximum particle velocity
                public const float RANDOM_DIRECTION_CHANCE = 0.05f; // Probability of direction change
                public const float DIRECTION_VARIANCE = 0.5f;       // Magnitude of direction change
                public const float SPAWN_VARIANCE = 5f;             // Spawn position variance
                public const float SPAWN_HALF_VARIANCE = SPAWN_VARIANCE / 2f; // Half variance for centering
                public const string DEFAULT_CHARACTERS = "01";      // Default character set
                public const float PHYSICS_TIMESTEP = 0.016f;       // Physics update timestep (~60fps)
                public const float MIN_SPAWN_INTENSITY = 1f;        // Minimum spawn intensity multiplier
                public const float MAX_SPAWN_INTENSITY = 3f;        // Maximum spawn intensity multiplier
                public const float LIFE_VARIANCE_MIN = 0.8f;        // Minimum life variance
                public const float LIFE_VARIANCE_MAX = 1.2f;        // Maximum life variance
            }

            public static class Boundaries
            {
                public const float BOUNDARY_MARGIN = 50f;     // Off-screen margin for culling
            }

            public static class Performance
            {
                public const int MAX_BATCH_SIZE = 1000;       // Maximum particles per batch
                public const int PARTICLE_BUFFER_GROWTH = 128;// Buffer growth increment
                public const float MIN_ALPHA_THRESHOLD = 0.05f;// Minimum alpha for visibility
                public const int SPECTRUM_BUFFER_SIZE = 2048; // Default spectrum buffer size
            }
        }
        #endregion

        #region Fields
        private readonly Random _random = new();
        private CircularParticleBuffer? _particleBuffer;
        private RenderCache _renderCache = new();
        private float[]? _spectrumBuffer;
        private float[]? _velocityLookup;
        private float[]? _alphaCurve;
        private readonly string _characters;
        private readonly float _velocityRange;
        private readonly float _particleLife;
        private readonly float _particleLifeDecay;
        private readonly float _alphaDecayExponent;
        private readonly float _spawnThresholdOverlay;
        private readonly float _spawnThresholdNormal;
        private readonly float _spawnProbability;
        private readonly float _particleSizeOverlay;
        private readonly float _particleSizeNormal;
        private readonly float _velocityMultiplier;
        private readonly float _zRange;
        private bool _isOverlayActive;
        private SKFont? _font;
        private SKPicture? _cachedBackground;
        private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
        private readonly object _particleLock = new();
        private bool _disposed;

        // Quality-dependent settings
        private bool _useBatching = true;
        private int _cullingLevel = 1;
        private bool _useHardwareAcceleration = true;
        #endregion

        #region Constructor
        private TextParticlesRenderer()
        {
            Settings s = Settings.Instance;
            _characters = Constants.Particles.DEFAULT_CHARACTERS;
            _velocityRange = s.ParticleVelocityMax - s.ParticleVelocityMin;
            _particleLife = s.ParticleLife;
            _particleLifeDecay = s.ParticleLifeDecay;
            _alphaDecayExponent = s.AlphaDecayExponent;
            _spawnThresholdOverlay = s.SpawnThresholdOverlay;
            _spawnThresholdNormal = s.SpawnThresholdNormal;
            _spawnProbability = s.SpawnProbability;
            _particleSizeOverlay = s.ParticleSizeOverlay;
            _particleSizeNormal = s.ParticleSizeNormal;
            _velocityMultiplier = s.VelocityMultiplier;
            _zRange = s.MaxZDepth - s.MinZDepth;

            InitializeFont();
            PrecomputeAlphaCurve();
            InitializeVelocityLookup(s.ParticleVelocityMin);
        }
        #endregion

        #region Initialization and Configuration
        /// <summary>
        /// Initializes the renderer and prepares resources for rendering.
        /// </summary>
        public override void Initialize()
        {
            SmartLogger.Safe(() =>
            {
                if (_disposed) throw new ObjectDisposedException(nameof(TextParticlesRenderer));
                if (_isInitialized) return;

                base.Initialize();
                _particleBuffer = new CircularParticleBuffer(
                    Settings.Instance.MaxParticles,
                    _particleLife,
                    _particleLifeDecay,
                    _velocityMultiplier,
                    this);
                _renderCache = new RenderCache();
                ApplyQualitySettings();
                _isInitialized = true;
                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
            }, new SmartLogger.ErrorHandlingOptions
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
            SmartLogger.Safe(() =>
            {
                base.Configure(isOverlayActive, quality);
                if (_isOverlayActive != isOverlayActive)
                {
                    _isOverlayActive = isOverlayActive;
                    UpdateParticleSizes();
                    InvalidateCachedBackground();
                }
            }, new SmartLogger.ErrorHandlingOptions
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
            SmartLogger.Safe(() =>
            {
                base.ApplyQualitySettings();
                switch (_quality)
                {
                    case RenderQuality.Low:
                        _useBatching = false;
                        _cullingLevel = 2;
                        _useHardwareAcceleration = false;
                        _useAntiAlias = false;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
                        break;
                    case RenderQuality.Medium:
                        _useBatching = true;
                        _cullingLevel = 1;
                        _useHardwareAcceleration = true;
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        break;
                    case RenderQuality.High:
                        _useBatching = true;
                        _cullingLevel = 0;
                        _useHardwareAcceleration = true;
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        break;
                }
                if (_font != null)
                {
                    _font.Edging = _useAntiAlias ? SKFontEdging.SubpixelAntialias : SKFontEdging.Alias;
                }
                InvalidateCachedBackground();
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            });
        }
        #endregion

        #region Rendering
        /// <summary>
        /// Renders the text particle visualization on the canvas using spectrum data.
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
            if (!QuickValidate(canvas, spectrum, info, paint) || _particleBuffer == null)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, "Invalid render parameters or uninitialized buffer");
                drawPerformanceInfo?.Invoke(canvas!, info);
                return;
            }

            SmartLogger.Safe(() =>
            {
                int actualBarCount = Min(spectrum!.Length, barCount);
                float[] processedSpectrum = PrepareSpectrum(spectrum, actualBarCount, spectrum.Length);

                bool semaphoreAcquired = false;
                try
                {
                    semaphoreAcquired = _renderSemaphore.Wait(0);
                    if (semaphoreAcquired)
                    {
                        UpdateRenderCache(info, actualBarCount);
                        _particleBuffer.Update(_renderCache.UpperBound, _renderCache.LowerBound, _alphaDecayExponent);
                        SpawnNewParticles(processedSpectrum, _renderCache.LowerBound, info.Width, barWidth);
                    }

                    DrawCachedBackground(canvas!, info);
                    if (_useBatching)
                        RenderParticlesBatched(canvas!, paint!);
                    else
                        RenderParticles(canvas!, paint!);
                }
                finally
                {
                    if (semaphoreAcquired) _renderSemaphore.Release();
                }

                drawPerformanceInfo?.Invoke(canvas!, info);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Render",
                ErrorMessage = "Error during rendering"
            });
        }

        /// <summary>
        /// Draws the cached background if available and applicable.
        /// </summary>
        private void DrawCachedBackground(SKCanvas canvas, SKImageInfo info)
        {
            if (!_useAdvancedEffects || _quality == RenderQuality.Low || canvas == null) return;

            if (_cachedBackground == null)
            {
                using var recorder = new SKPictureRecorder();
                using var recordCanvas = recorder.BeginRecording(new SKRect(0, 0, info.Width, info.Height));
                // Placeholder for static background elements
                _cachedBackground = recorder.EndRecording();
            }
            canvas.DrawPicture(_cachedBackground);
        }

        /// <summary>
        /// Renders particles individually without batching.
        /// </summary>
        private void RenderParticles(SKCanvas canvas, SKPaint paint)
        {
            if (_particleBuffer == null || _font == null) return;

            var activeParticles = _particleBuffer.GetActiveParticles();
            if (activeParticles.IsEmpty) return;

            using var particlePaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = _useAntiAlias,
                Color = paint.Color
            };

            Vector2 center = new(_renderCache.Width / 2f, _renderCache.Height / 2f);
            float focalLength = Constants.Rendering.FOCAL_LENGTH;

            foreach (ref readonly var p in activeParticles)
            {
                if (!ShouldRenderParticle(p)) continue;

                float depth = focalLength + p.Z;
                float scale = focalLength / depth;
                Vector2 position = new(p.X, p.Y);
                Vector2 screenPos = center + (position - center) * scale;

                if (!IsRenderAreaVisible(canvas, screenPos.X, screenPos.Y, p.Size, p.Size)) continue;

                byte alpha = (byte)(p.Alpha * (depth / (focalLength + p.Z)) * Constants.Rendering.ALPHA_MAX);
                particlePaint.Color = paint.Color.WithAlpha(alpha);
                canvas.DrawText(p.Character.ToString(), screenPos.X, screenPos.Y, _font, particlePaint);
            }
        }

        /// <summary>
        /// Renders particles in batches grouped by character for improved performance.
        /// </summary>
        private void RenderParticlesBatched(SKCanvas canvas, SKPaint paint)
        {
            if (_particleBuffer == null || _font == null) return;

            var activeParticles = _particleBuffer.GetActiveParticles();
            if (activeParticles.IsEmpty) return;

            using var particlePaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = _useAntiAlias,
                Color = paint.Color
            };

            Vector2 center = new(_renderCache.Width / 2f, _renderCache.Height / 2f);
            float focalLength = Constants.Rendering.FOCAL_LENGTH;
            var batches = new Dictionary<char, List<(Vector2 position, byte alpha)>>();

            foreach (ref readonly var p in activeParticles)
            {
                if (!ShouldRenderParticle(p)) continue;

                float depth = focalLength + p.Z;
                float scale = focalLength / depth;
                Vector2 position = new(p.X, p.Y);
                Vector2 screenPos = center + (position - center) * scale;

                if (!IsRenderAreaVisible(canvas, screenPos.X, screenPos.Y, p.Size, p.Size)) continue;

                byte alpha = (byte)(p.Alpha * (depth / (focalLength + p.Z)) * Constants.Rendering.ALPHA_MAX);
                if (!batches.TryGetValue(p.Character, out var list))
                {
                    list = new List<(Vector2, byte)>(Constants.Performance.MAX_BATCH_SIZE);
                    batches[p.Character] = list;
                }
                list.Add((screenPos, alpha));
            }

            foreach (var batch in batches)
            {
                string charStr = batch.Key.ToString();
                foreach (var (pos, alpha) in batch.Value)
                {
                    particlePaint.Color = paint.Color.WithAlpha(alpha);
                    canvas.DrawText(charStr, pos.X, pos.Y, _font, particlePaint);
                }
            }
        }
        #endregion

        #region Particle Management
        /// <summary>
        /// Spawns new particles based on processed spectrum data.
        /// </summary>
        private void SpawnNewParticles(float[] spectrum, float spawnY, float canvasWidth, float barWidth)
        {
            if (_particleBuffer == null || spectrum.Length == 0) return;

            float threshold = _isOverlayActive ? _spawnThresholdOverlay : _spawnThresholdNormal;
            float baseSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;

            for (int i = 0; i < spectrum.Length; i++)
            {
                float value = spectrum[i];
                if (value <= threshold) continue;

                float spawnChance = Min(value / threshold, Constants.Particles.MAX_SPAWN_INTENSITY) * _spawnProbability;
                if (_random.NextDouble() >= spawnChance) continue;

                float intensity = Min(value / threshold, Constants.Particles.MAX_SPAWN_INTENSITY);
                _particleBuffer.Add(CreateParticle(i, spawnY, barWidth, baseSize, intensity));
            }
        }

        /// <summary>
        /// Creates a new particle with randomized properties.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private Particle CreateParticle(int index, float spawnY, float barWidth, float baseSize, float intensity)
        {
            float x = index * _renderCache.StepSize + (float)_random.NextDouble() * barWidth;
            float y = spawnY + (float)_random.NextDouble() * Constants.Particles.SPAWN_VARIANCE - Constants.Particles.SPAWN_HALF_VARIANCE;
            float z = Settings.Instance.MinZDepth + (float)_random.NextDouble() * _zRange;
            float lifeVariance = Constants.Particles.LIFE_VARIANCE_MIN + (float)_random.NextDouble() * (Constants.Particles.LIFE_VARIANCE_MAX - Constants.Particles.LIFE_VARIANCE_MIN);

            return new Particle
            {
                X = x,
                Y = y,
                Z = z,
                VelocityY = -GetRandomVelocity() * intensity,
                VelocityX = ((float)_random.NextDouble() - 0.5f) * 2f,
                Size = baseSize * intensity,
                Life = _particleLife * lifeVariance,
                Alpha = 1f,
                IsActive = true,
                Character = _characters[_random.Next(_characters.Length)]
            };
        }

        private struct Particle
        {
            public float X, Y, Z;
            public float VelocityX, VelocityY;
            public float Size, Life, Alpha;
            public bool IsActive;
            public char Character;
        }

        private sealed class CircularParticleBuffer
        {
            private readonly TextParticlesRenderer _renderer;
            private Particle[] _buffer;
            private int _head, _tail, _count, _capacity;
            private readonly float _particleLife, _particleLifeDecay, _velocityMultiplier;
            private readonly object _bufferLock = new();

            public CircularParticleBuffer(int capacity, float particleLife, float particleLifeDecay, float velocityMultiplier, TextParticlesRenderer renderer)
            {
                _capacity = capacity;
                _buffer = new Particle[capacity];
                _particleLife = particleLife;
                _particleLifeDecay = particleLifeDecay;
                _velocityMultiplier = velocityMultiplier;
                _renderer = renderer;
            }

            public void Add(Particle particle)
            {
                lock (_bufferLock)
                {
                    if (_count >= _capacity) ResizeBuffer();
                    _buffer[_tail] = particle;
                    _tail = (_tail + 1) % _capacity;
                    _count++;
                }
            }

            public Span<Particle> GetActiveParticles()
            {
                lock (_bufferLock)
                {
                    if (_count == 0) return Span<Particle>.Empty;
                    return _head < _tail
                        ? _buffer.AsSpan(_head, _count)
                        : BuildContiguousSpan();
                }
            }

            public void Update(float upperBound, float lowerBound, float alphaDecayExponent)
            {
                if (_count == 0) return;

                lock (_bufferLock)
                {
                    int writeIndex = 0;
                    float lifeRatioScale = 1f / _particleLife;

                    for (int i = 0, readIndex = _head; i < _count; i++, readIndex = (readIndex + 1) % _capacity)
                    {
                        ref var particle = ref _buffer[readIndex];
                        if (!particle.IsActive || !UpdateParticle(ref particle, upperBound, lowerBound, lifeRatioScale, alphaDecayExponent)) continue;
                        if (writeIndex != readIndex) _buffer[writeIndex] = particle;
                        writeIndex++;
                    }

                    _count = writeIndex;
                    _head = 0;
                    _tail = writeIndex % _capacity;
                }
            }

            private bool UpdateParticle(ref Particle p, float upperBound, float lowerBound, float lifeRatioScale, float alphaDecayExponent)
            {
                p.Life -= _particleLifeDecay;
                if (p.Life <= 0 || p.Y < upperBound - Constants.Boundaries.BOUNDARY_MARGIN || p.Y > lowerBound + Constants.Boundaries.BOUNDARY_MARGIN)
                {
                    p.IsActive = false;
                    return false;
                }

                p.VelocityY = Clamp(
                    (p.VelocityY + Constants.Particles.GRAVITY * Constants.Particles.PHYSICS_TIMESTEP) * Constants.Particles.AIR_RESISTANCE,
                    -Constants.Particles.MAX_VELOCITY * _velocityMultiplier,
                    Constants.Particles.MAX_VELOCITY * _velocityMultiplier);

                if (_renderer._random.NextDouble() < Constants.Particles.RANDOM_DIRECTION_CHANCE)
                    p.VelocityX += ((float)_renderer._random.NextDouble() - 0.5f) * Constants.Particles.DIRECTION_VARIANCE;

                p.VelocityX *= Constants.Particles.AIR_RESISTANCE;
                p.Y += p.VelocityY;
                p.X += p.VelocityX;
                p.Alpha = CalculateAlpha(p.Life * lifeRatioScale, alphaDecayExponent);
                return true;
            }

            private float CalculateAlpha(float lifeRatio, float alphaDecayExponent)
            {
                if (lifeRatio <= 0f) return 0f;
                if (lifeRatio >= 1f) return 1f;
                return _renderer._alphaCurve?[(int)(lifeRatio * 100)] ?? (float)Pow(lifeRatio, alphaDecayExponent);
            }

            private void ResizeBuffer()
            {
                int newCapacity = _capacity + Constants.Performance.PARTICLE_BUFFER_GROWTH;
                var newBuffer = new Particle[newCapacity];
                if (_tail > _head)
                {
                    Array.Copy(_buffer, _head, newBuffer, 0, _count);
                }
                else
                {
                    int firstPart = _capacity - _head;
                    Array.Copy(_buffer, _head, newBuffer, 0, firstPart);
                    Array.Copy(_buffer, 0, newBuffer, firstPart, _tail);
                }
                _head = 0;
                _tail = _count;
                _buffer = newBuffer;
                _capacity = newCapacity;
            }

            private Span<Particle> BuildContiguousSpan()
            {
                var result = new Particle[_count];
                int firstPartSize = _capacity - _head;
                Array.Copy(_buffer, _head, result, 0, firstPartSize);
                Array.Copy(_buffer, 0, result, firstPartSize, _tail);
                return result.AsSpan();
            }
        }

        private sealed record RenderCache
        {
            public float Width, Height, StepSize, OverlayHeight, UpperBound, LowerBound;
            public int BarCount;
        }
        #endregion

        #region Helper Methods
        private void InitializeFont()
        {
            _font = new SKFont { Size = Constants.Rendering.BASE_TEXT_SIZE };
            if (_useAntiAlias) _font.Edging = SKFontEdging.SubpixelAntialias;
        }

        private void PrecomputeAlphaCurve()
        {
            _alphaCurve = ArrayPool<float>.Shared.Rent(101);
            float step = 1f / (_alphaCurve.Length - 1);
            for (int i = 0; i < _alphaCurve.Length; i++)
                _alphaCurve[i] = (float)Pow(i * step, _alphaDecayExponent);
        }

        private void InitializeVelocityLookup(float minVelocity)
        {
            _velocityLookup = ArrayPool<float>.Shared.Rent(Constants.Particles.VELOCITY_LOOKUP_SIZE);
            for (int i = 0; i < _velocityLookup.Length; i++)
                _velocityLookup[i] = minVelocity + _velocityRange * i / Constants.Particles.VELOCITY_LOOKUP_SIZE;
        }

        [MethodImpl(AggressiveInlining)]
        private float GetRandomVelocity() => _velocityLookup?[_random.Next(Constants.Particles.VELOCITY_LOOKUP_SIZE)] ?? throw new InvalidOperationException("Velocity lookup not initialized");

        private void UpdateParticleSizes()
        {
            if (_particleBuffer == null) return;

            float newSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            float oldSize = _isOverlayActive ? _particleSizeNormal : _particleSizeOverlay;

            lock (_particleLock)
            {
                foreach (ref var particle in _particleBuffer.GetActiveParticles())
                    if (particle.IsActive)
                        particle.Size = newSize * (particle.Size / oldSize);
            }
        }

        private void UpdateRenderCache(SKImageInfo info, int barCount)
        {
            _renderCache.Width = info.Width;
            _renderCache.Height = info.Height;
            _renderCache.StepSize = barCount > 0 ? info.Width / (float)barCount : 0f;
            float overlayHeight = info.Height * Settings.Instance.OverlayHeightMultiplier;
            _renderCache.OverlayHeight = _isOverlayActive ? overlayHeight : 0f;
            _renderCache.UpperBound = _isOverlayActive ? info.Height - overlayHeight : 0f;
            _renderCache.LowerBound = info.Height;
            _renderCache.BarCount = barCount;
        }

        [MethodImpl(AggressiveInlining)]
        private bool ShouldRenderParticle(in Particle p)
        {
            if (!p.IsActive) return false;
            if (_cullingLevel > 0 && p.Alpha < Constants.Performance.MIN_ALPHA_THRESHOLD) return false;
            if (_cullingLevel > 1 && (p.Y < _renderCache.UpperBound || p.Y > _renderCache.LowerBound)) return false;
            return true;
        }

        private void InvalidateCachedBackground()
        {
            _cachedBackground?.Dispose();
            _cachedBackground = null;
        }
        #endregion

        #region Disposal
        /// <summary>
        /// Disposes of resources used by the renderer.
        /// </summary>
        public override void Dispose()
        {
            if (_disposed) return;

            SmartLogger.Safe(() =>
            {
                _renderSemaphore.Dispose();
                if (_spectrumBuffer != null) ArrayPool<float>.Shared.Return(_spectrumBuffer);
                if (_velocityLookup != null) ArrayPool<float>.Shared.Return(_velocityLookup);
                if (_alphaCurve != null) ArrayPool<float>.Shared.Return(_alphaCurve);
                _font?.Dispose();
                _cachedBackground?.Dispose();
                _particleBuffer = null;
                _spectrumBuffer = null;
                _velocityLookup = null;
                _alphaCurve = null;
                _font = null;
                _cachedBackground = null;
                base.Dispose();
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Dispose",
                ErrorMessage = "Error during disposal"
            });

            _disposed = true;
            SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
        }
        #endregion
    }
}