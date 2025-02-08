#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Provides settings and constants for the raindrops visual effect.
    /// </summary>
    #region RaindropsSettings
    public static class RaindropsSettings
    {
        /// <summary>
        /// The maximum number of raindrops that can be rendered simultaneously.
        /// </summary>
        public const int MaxRaindrops = 1000;
        // public const int MaxRipples = 150; // Commented out for ripple removal
        /// <summary>
        /// The maximum number of particles that can be rendered simultaneously.
        /// </summary>
        public const int MaxParticles = 5000;

        /// <summary>
        /// The base falling speed of raindrops.
        /// </summary>
        public const float BaseFallSpeed = 2f;
        /// <summary>
        /// The expansion speed of ripple effects (currently not used).
        /// </summary>
        public const float RippleExpandSpeed = 2f; // Not used
        /// <summary>
        /// The threshold for spectrum intensity to trigger visual effects.
        /// </summary>
        public const float SpectrumThreshold = 0.1f;
        /// <summary>
        /// The stroke width for rendering ripple effects (currently not used).
        /// </summary>
        public const float RippleStrokeWidth = 2f; // Not used
        /// <summary>
        /// The initial radius of ripple effects (currently not used).
        /// </summary>
        public const float InitialRadius = 3f; // Not used
        /// <summary>
        /// The initial alpha (opacity) of ripple effects (currently not used).
        /// </summary>
        public const float InitialAlpha = 1f; // Not used
        /// <summary>
        /// The alpha threshold for ripple effects to be considered faded out (currently not used).
        /// </summary>
        public const float RippleAlphaThreshold = 0.1f; // Not used
        /// <summary>
        /// The decay factor for ripple alpha over time (currently not used).
        /// </summary>
        public const float RippleAlphaDecay = 0.95f; // Not used
        /// <summary>
        /// Multiplier for calculating the bottom boundary of the overlay effect.
        /// </summary>
        public const float OverlayBottomMultiplier = 3.75f;

        /// <summary>
        /// The probability of a new raindrop spawning based on spectrum intensity.
        /// </summary>
        public const double SpawnProbability = 0.15;
    }
    #endregion

    /// <summary>
    /// Implements a spectrum renderer that simulates raindrops and particles reacting to audio spectrum data.
    /// This renderer is designed to be efficient and utilizes SkiaSharp for rendering.
    /// </summary>
    #region RaindropsRenderer
    public sealed class RaindropsRenderer : ISpectrumRenderer, IDisposable
    {
        #region Nested Types
        /// <summary>
        /// Represents a single raindrop with its position and falling speed.
        /// </summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private readonly struct Raindrop
        {
            /// <summary>
            /// The horizontal position of the raindrop.
            /// </summary>
            public readonly float X;
            /// <summary>
            /// The vertical position of the raindrop.
            /// </summary>
            public readonly float Y;
            /// <summary>
            /// The falling speed of the raindrop.
            /// </summary>
            public readonly float FallSpeed;

            /// <summary>
            /// Initializes a new instance of the <see cref="Raindrop"/> struct.
            /// </summary>
            /// <param name="x">The horizontal position.</param>
            /// <param name="y">The vertical position.</param>
            /// <param name="fallSpeed">The falling speed.</param>
            public Raindrop(float x, float y, float fallSpeed) =>
                (X, Y, FallSpeed) = (x, y, fallSpeed);

            /// <summary>
            /// Creates a new <see cref="Raindrop"/> with an updated vertical position.
            /// </summary>
            /// <param name="newY">The new vertical position.</param>
            /// <returns>A new <see cref="Raindrop"/> struct with the updated Y coordinate.</returns>
            public Raindrop WithNewY(float newY) => new Raindrop(X, newY, FallSpeed);
        }

        // [StructLayout(LayoutKind.Sequential)]
        // private readonly struct Ripple // Ripple struct is commented out for removal
        // {
        //     //    public readonly float X, Y, Radius, Alpha;
        //     //    public Ripple(float x, float y, float radius, float alpha) =>
        //     //         (X, Y, Radius, Alpha) = (x, y, radius, alpha);
        //     //    public Ripple WithUpdatedValues(float newRadius, float newAlpha) =>
        //     //         new Ripple(X, Y, newRadius, newAlpha);
        // }

        /// <summary>
        /// Represents a particle with its position and velocity.
        /// </summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Particle
        {
            /// <summary>
            /// The horizontal position of the particle.
            /// </summary>
            public float X;
            /// <summary>
            /// The vertical position of the particle.
            /// </summary>
            public float Y;
            /// <summary>
            /// The horizontal velocity of the particle.
            /// </summary>
            public float VelocityX;
            /// <summary>
            /// The vertical velocity of the particle.
            /// </summary>
            public float VelocityY;

            /// <summary>
            /// Initializes a new instance of the <see cref="Particle"/> struct.
            /// </summary>
            /// <param name="x">The horizontal position.</param>
            /// <param name="y">The vertical position.</param>
            /// <param name="velocityX">The horizontal velocity.</param>
            /// <param name="velocityY">The vertical velocity.</param>
            public Particle(float x, float y, float velocityX, float velocityY) =>
                (X, Y, VelocityX, VelocityY) = (x, y, velocityX, velocityY);
        }

        /// <summary>
        /// Manages a buffer of particles for efficient rendering.
        /// </summary>
        private class ParticleBuffer
        {
            private readonly Particle[] _particles;
            private int _count;

            /// <summary>
            /// Initializes a new instance of the <see cref="ParticleBuffer"/> class.
            /// </summary>
            /// <param name="capacity">The maximum number of particles the buffer can hold.</param>
            public ParticleBuffer(int capacity)
            {
                _particles = new Particle[capacity];
                _count = 0;
            }

            /// <summary>
            /// Adds a particle to the buffer if there is capacity.
            /// </summary>
            /// <param name="particle">The particle to add.</param>
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            public void AddParticle(in Particle particle)
            {
                if (_count < _particles.Length)
                    _particles[_count++] = particle;
            }

            /// <summary>
            /// Clears the particle buffer, resetting the particle count to zero.
            /// </summary>
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            public void Clear() => _count = 0;

            /// <summary>
            /// Updates the position of each particle in the buffer based on its velocity and elapsed time.
            /// </summary>
            /// <param name="deltaTime">The time elapsed since the last update, typically a small fraction of a second.</param>
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            public void UpdateParticles(float deltaTime)
            {
                for (int i = 0; i < _count; i++)
                {
                    ref Particle p = ref _particles[i];
                    p.X += p.VelocityX * deltaTime;
                    p.Y += p.VelocityY * deltaTime;
                }
            }

            /// <summary>
            /// Renders the particles in the buffer to the specified SkiaSharp canvas.
            /// </summary>
            /// <param name="canvas">The SkiaSharp canvas to render to. Can be null, in which case the method does nothing.</param>
            /// <param name="paint">The SkiaSharp paint to use for rendering. Can be null, in which case the method does nothing.</param>
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            public void RenderParticles(SKCanvas? canvas, SKPaint? paint)
            {
                if (canvas == null || paint == null)
                    return;

                paint.Style = SKPaintStyle.Fill;
                int count = _count;
                for (int i = 0; i < count; i++)
                {
                    ref Particle p = ref _particles[i];
                    canvas.DrawCircle(p.X, p.Y, 2f, paint);
                }
            }
        }
        #endregion

        #region Fields
        private static readonly Lazy<RaindropsRenderer> _instance = new(() => new RaindropsRenderer());
        private RenderCache _renderCache;
        private readonly Raindrop[] _raindrops;
        // private readonly Ripple[] _ripples; // Commented out for ripple removal
        private int _raindropCount, /*_rippleCount*/ _rippleCount_removed; // _rippleCount commented out for removal, renamed to _rippleCount_removed to preserve code using it
        private readonly SKPath _dropsPath;
        private readonly Random _random;
        private readonly float[] _scaledSpectrumCache;
        private bool _isInitialized, _isOverlayActive, _overlayStatusChanged, _isDisposed;
        private readonly ParticleBuffer _particleBuffer;
        private const float DeltaTime = 0.016f;
        private readonly SKPaint _raindropPaint;
        // private readonly SKPaint _ripplePaint; // Commented out for ripple removal
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes a new instance of the <see cref="RaindropsRenderer"/> class (private constructor for singleton pattern).
        /// </summary>
        private RaindropsRenderer()
        {
            _raindrops = new Raindrop[RaindropsSettings.MaxRaindrops];
            // _ripples = new Ripple[RaindropsSettings.MaxRipples]; // Commented out for ripple removal
            _dropsPath = new SKPath();
            _random = new Random();
            _scaledSpectrumCache = new float[RaindropsSettings.MaxRaindrops];
            _overlayStatusChanged = false;
            _particleBuffer = new ParticleBuffer(RaindropsSettings.MaxParticles);
            _renderCache = new RenderCache();

            _raindropPaint = new SKPaint { Style = SKPaintStyle.Fill }; // Initialize reusable paint for raindrops
            // _ripplePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = RaindropsSettings.RippleStrokeWidth }; // Initialize reusable paint for ripples - not used
        }

        /// <summary>
        /// Gets the singleton instance of the <see cref="RaindropsRenderer"/> class.
        /// </summary>
        /// <returns>The singleton instance of <see cref="RaindropsRenderer"/>.</returns>
        public static RaindropsRenderer GetInstance() => _instance.Value;

        /// <summary>
        /// Initializes the renderer. This method must be called before rendering can occur.
        /// </summary>
        public void Initialize()
        {
            EnsureNotDisposed();
            if (!_isInitialized)
            {
                _isInitialized = true;
                Log.Debug("RaindropsRenderer initialized");
            }
        }
        #endregion

        #region Configuration
        /// <summary>
        /// Configures the renderer based on whether overlay mode is active.
        /// </summary>
        /// <param name="isOverlayActive">True if overlay mode is active, otherwise false.</param>
        public void Configure(bool isOverlayActive)
        {
            EnsureNotDisposed();
            if (_isOverlayActive != isOverlayActive)
            {
                _isOverlayActive = isOverlayActive;
                _overlayStatusChanged = true;
            }
        }
        #endregion

        #region Rendering
        /// <inheritdoc />
        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth,
            float barSpacing, int barCount, SKPaint? paint, System.Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            EnsureNotDisposed();
            if (canvas == null || spectrum == null || paint == null || spectrum.Length == 0)
                return;

            if (_overlayStatusChanged || _renderCache.Width != info.Width || _renderCache.Height != info.Height)
            {
                UpdateRenderCache(info);
                _overlayStatusChanged = false;
            }

            int actualBarCount = Math.Min(spectrum.Length / 2, barCount);
            ScaleSpectrum(spectrum, _scaledSpectrumCache.AsSpan(0, actualBarCount), actualBarCount);
            UpdateSimulation(_scaledSpectrumCache.AsSpan(0, actualBarCount));
            RenderScene(canvas, paint);
            drawPerformanceInfo?.Invoke(canvas, info);
        }

        /// <summary>
        /// Renders the raindrops, ripples (currently removed), and particles to the canvas.
        /// </summary>
        /// <param name="canvas">The SkiaSharp canvas to render to.</param>
        /// <param name="paint">The SkiaSharp paint to use for rendering.</param>
        private void RenderScene(SKCanvas canvas, SKPaint paint)
        {
            _dropsPath.Reset();
            for (int i = 0; i < _raindropCount; i++)
                _dropsPath.AddCircle(_raindrops[i].X, _raindrops[i].Y, 2f);

            _raindropPaint.Color = paint.Color; // Update color from the main paint
            canvas.DrawPath(_dropsPath, _raindropPaint);

            // _ripplePaint.Style = SKPaintStyle.Stroke; // Ensure style is Stroke - not needed as it's set in constructor and not changed
            // _ripplePaint.StrokeWidth = RaindropsSettings.RippleStrokeWidth; // Ensure StrokeWidth is set - not needed as it's set in constructor and not changed
            // for (int i = 0; i < _rippleCount; i++) // Ripple rendering is commented out for removal
            // {
            //     //    var ripple = _ripples[i];
            //     //    _ripplePaint.Color = paint.Color.WithAlpha((byte)(ripple.Alpha * 255));
            //     //    canvas.DrawCircle(ripple.X, ripple.Y, ripple.Radius, _ripplePaint);
            // }

            _particleBuffer.RenderParticles(canvas, paint);
        }
        #endregion

        #region Simulation Logic
        /// <summary>
        /// Updates the render cache with the latest image information and overlay status.
        /// </summary>
        /// <param name="info">The image information.</param>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void UpdateRenderCache(SKImageInfo info)
        {
            _renderCache = new RenderCache
            {
                Width = info.Width,
                Height = info.Height,
                LowerBound = _isOverlayActive ? info.Height * RaindropsSettings.OverlayBottomMultiplier : info.Height,
                UpperBound = _isOverlayActive ? info.Height * 0.1f : 0f,
                StepSize = info.Width / (float)RaindropsSettings.MaxRaindrops
            };
        }

        /// <summary>
        /// Updates the simulation state, including raindrops, ripples (currently removed), and particles.
        /// </summary>
        /// <param name="spectrum">The audio spectrum data.</param>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void UpdateSimulation(System.ReadOnlySpan<float> spectrum)
        {
            UpdateRaindrops(spectrum, _renderCache.Width, _renderCache.LowerBound, _renderCache.UpperBound);
            // UpdateRipples(); // Ripple update is commented out for removal
            _particleBuffer.UpdateParticles(DeltaTime);
        }

        /// <summary>
        /// Scales the audio spectrum data to a smaller size for efficient processing.
        /// </summary>
        /// <param name="src">The source spectrum data.</param>
        /// <param name="dst">The destination span to store the scaled spectrum data.</param>
        /// <param name="count">The number of scaled spectrum values to compute.</param>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void ScaleSpectrum(System.ReadOnlySpan<float> src, System.Span<float> dst, int count)
        {
            if (src.IsEmpty || dst.IsEmpty || count <= 0)
                return;
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

        /// <summary>
        /// Updates the position of raindrops, removes raindrops that have fallen off-screen, and spawns new raindrops based on spectrum intensity.
        /// Optimizes raindrop management using a two-pointer algorithm to compact the raindrop array.
        /// </summary>
        /// <param name="spectrum">The audio spectrum data.</param>
        /// <param name="width">The width of the rendering area.</param>
        /// <param name="lower">The lower bound (bottom edge) of the rendering area.</param>
        /// <param name="upper">The upper bound (top edge) of the rendering area.</param>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void UpdateRaindrops(System.ReadOnlySpan<float> spectrum, float width, float lower, float upper)
        {
            int writeIdx = 0;
            for (int i = 0; i < _raindropCount; i++)
            {
                ref Raindrop drop = ref _raindrops[i];
                float newY = drop.Y + drop.FallSpeed;
                if (newY < lower)
                    _raindrops[writeIdx++] = drop.WithNewY(newY);
            }
            _raindropCount = writeIdx;
            SpawnNewDrops(spectrum, width / spectrum.Length, upper);
        }

        /// <summary>
        /// Spawns new raindrops based on the intensity of the audio spectrum.
        /// </summary>
        /// <param name="spectrum">The audio spectrum data.</param>
        /// <param name="step">The horizontal step size for spawning raindrops across the width.</param>
        /// <param name="upper">The upper bound (top edge) where new raindrops should spawn.</param>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void SpawnNewDrops(System.ReadOnlySpan<float> spectrum, float step, float upper)
        {
            int len = spectrum.Length;
            Random rnd = _random; // Кэширование генератора случайных чисел
            for (int i = 0; i < len && _raindropCount < RaindropsSettings.MaxRaindrops; i++)
            {
                float intensity = Math.Clamp(spectrum[i], 0f, 1f);
                if (rnd.NextDouble() < intensity * RaindropsSettings.SpawnProbability)
                {
                    _raindrops[_raindropCount++] = new Raindrop(
                        i * step + rnd.NextSingle() * _renderCache.StepSize,
                        upper,
                        RaindropsSettings.BaseFallSpeed * (1f + intensity)
                    );
                }
            }
            UpdateParticles();
        }

        /// <summary>
        /// Updates the particle buffer by creating new particles for each raindrop.
        /// </summary>
        private void UpdateParticles()
        {
            _particleBuffer.Clear();
            int dropsCount = _raindropCount;
            Random rnd = _random; // Локальное кэширование генератора случайных чисел
            float twoPi = MathF.PI * 2f; // Кэширование константы
            for (int i = 0; i < dropsCount; i++)
            {
                Raindrop drop = _raindrops[i];
                float angle = rnd.NextSingle() * twoPi;
                float speed = rnd.NextSingle() * 2f + 1f;
                _particleBuffer.AddParticle(new Particle(
                    drop.X,
                    drop.Y,
                    MathF.Cos(angle) * speed,
                    MathF.Sin(angle) * speed
                ));
            }
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Ensures that the renderer has not been disposed, throwing an <see cref="ObjectDisposedException"/> if it has.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void EnsureNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RaindropsRenderer));
        }
        #endregion

        #region IDisposable Implementation
        /// <inheritdoc />
        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            _dropsPath.Dispose();
            // _ripplePaint.Dispose(); // Ripple paint disposal is commented out for removal
            _raindropPaint.Dispose();
            System.GC.SuppressFinalize(this);
        }
        #endregion

        #region RenderCache Struct
        /// <summary>
        /// A struct to cache rendering-related values that depend on the image info and overlay status,
        /// to avoid recalculating them every frame.
        /// </summary>
        private struct RenderCache
        {
            /// <summary>
            /// The width of the rendering area.
            /// </summary>
            public int Width;
            /// <summary>
            /// The height of the rendering area.
            /// </summary>
            public int Height;
            /// <summary>
            /// The lower vertical bound for rendering, used to determine when raindrops fall off-screen.
            /// </summary>
            public float LowerBound;
            /// <summary>
            /// The upper vertical bound for rendering, used as the starting Y position for new raindrops.
            /// </summary>
            public float UpperBound;
            /// <summary>
            /// The horizontal step size for spawning raindrops, calculated based on the width and maximum raindrops.
            /// </summary>
            public float StepSize;
        }
        #endregion
    }
    #endregion
}
