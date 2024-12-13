#nullable enable

namespace SpectrumNet
{
    public class HeartbeatRenderer : ISpectrumRenderer, IDisposable
    {
        private static HeartbeatRenderer? _instance;
        private bool _isInitialized;
        private bool _disposed;

        private ObjectPool<SKPath>? _pathPool;
        private ObjectPool<SKPaint>? _paintPool;

        private float _smoothingFactor = 0.3f;
        private float[]? _previousSpectrum;
        private float _globalAnimationTime;

        private const float MinMagnitudeThreshold = 0.05f;
        private const float MaxHeartSize = 120f;
        private const float PulseFrequency = 6f;
        private const float HeartBaseScale = 0.6f;
        private const float GlowIntensity = 0.2f;
        private const float GlowAlphaDivisor = 3f;
        private const float AlphaMultiplier = 1.5f;

        // Вложенный класс ObjectPool
        private class ObjectPool<T> where T : class
        {
            private readonly ConcurrentBag<T> _objects = new ConcurrentBag<T>();
            private readonly Func<T> _objectGenerator;
            private readonly Action<T>? _objectResetter;

            public ObjectPool(Func<T> objectGenerator, Action<T>? objectResetter = null)
            {
                _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
                _objectResetter = objectResetter;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T Get() => _objectGenerator();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Return(T item)
            {
                _objectResetter?.Invoke(item);
                _objects.Add(item);
            }

            public void Dispose()
            {
                while (_objects.TryTake(out _)) { }
            }
        }

        private HeartbeatRenderer()
        {
            InitializePools();
        }

        private void InitializePools()
        {
            _pathPool = new ObjectPool<SKPath>(
                () => new SKPath(),
                path => path.Reset()
            );

            _paintPool = new ObjectPool<SKPaint>(
                () => new SKPaint { IsAntialias = true }
            );
        }

        public static HeartbeatRenderer GetInstance() => _instance ??= new HeartbeatRenderer();

        public void Initialize()
        {
            if (_isInitialized || _disposed) return;
            _isInitialized = true;
            Log.Debug("Enhanced HeartbeatRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            _smoothingFactor = isOverlayActive ? 0.7f : 0.4f;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float unused1, float unused2, int unused3,
                           SKPaint? paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, paint)) return;

            _globalAnimationTime += 0.016f;

            int actualHeartCount = Math.Min(spectrum!.Length / 2, 8);
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualHeartCount);
            float[] smoothedSpectrum = SmoothSpectrum(spectrum, actualHeartCount);

            RenderHeartbeats(canvas!, smoothedSpectrum, info, paint!);
            drawPerformanceInfo(canvas!, info);
        }

        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKPaint? paint)
        {
            if (!_isInitialized || _disposed ||
                canvas == null ||
                spectrum == null ||
                paint == null ||
                _pathPool == null ||
                _paintPool == null)
            {
                Log.Warning("Invalid render parameters or uninitialized HeartbeatRenderer.");
                return false;
            }
            return true;
        }

        private void RenderHeartbeats(SKCanvas canvas, float[] spectrum, SKImageInfo info, SKPaint basePaint)
        {
            float centerX = info.Width / 2f;
            float centerY = info.Height / 2f;
            float maxRadius = Math.Min(info.Width, info.Height) / 3f;

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MinMagnitudeThreshold) continue;

                float angle = i * (360f / spectrum.Length);
                float distance = maxRadius * (1 - magnitude);

                Vector2 position = CalculateHeartPosition(centerX, centerY, angle, distance);

                // Вычисляем альфа-канал на основе магнитуды
                byte alpha = (byte)MathF.Min(magnitude * AlphaMultiplier * 255f, 255f);
                SKColor heartColor = basePaint.Color.WithAlpha(alpha);

                RenderSingleHeart(canvas, position.X, position.Y, magnitude, basePaint, heartColor);
            }
        }

        private static Vector2 CalculateHeartPosition(float centerX, float centerY, float angle, float distance)
        {
            float radians = angle * (MathF.PI / 180f);
            return new Vector2(
                centerX + MathF.Cos(radians) * distance,
                centerY + MathF.Sin(radians) * distance
            );
        }

        private void RenderSingleHeart(SKCanvas canvas, float x, float y, float magnitude, SKPaint basePaint, SKColor heartColor)
        {
            var pathPool = _pathPool ?? throw new InvalidOperationException("Path pool is not initialized");

            // Клонируем базовую кисть для использования в отрисовке сердца
            using var heartPaint = basePaint.Clone();
            heartPaint.Color = heartColor;
            heartPaint.Style = SKPaintStyle.Fill;

            float heartSize = CalculateHeartSize(magnitude);
            SKPath heartPath = pathPool.Get();
            CreateHeartPath(ref heartPath, x, y, heartSize);

            // Calculate alpha directly
            byte alpha = (byte)(magnitude * 255f);
            heartPaint.Color = heartPaint.Color.WithAlpha(alpha);

            AddHeartGlow(canvas, heartPath, heartPaint, magnitude);

            canvas.DrawPath(heartPath, heartPaint);
            pathPool.Return(heartPath);
        }

        private float CalculateHeartSize(float magnitude)
        {
            float pulseEffect = MathF.Sin(_globalAnimationTime * PulseFrequency) * 0.1f + 1f;
            return MaxHeartSize * magnitude * HeartBaseScale * pulseEffect;
        }

        private static void AddHeartGlow(SKCanvas canvas, SKPath heartPath, SKPaint heartPaint, float magnitude)
        {
            byte alpha = heartPaint.Color.Alpha;
            byte glowAlpha = (byte)(alpha / GlowAlphaDivisor);

            using var glowPaint = new SKPaint
            {
                Color = heartPaint.Color.WithAlpha(glowAlpha),
                Style = heartPaint.Style,
                IsAntialias = heartPaint.IsAntialias,
                StrokeWidth = heartPaint.StrokeWidth,
                BlendMode = heartPaint.BlendMode,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, magnitude * GlowIntensity)
            };

            canvas.DrawPath(heartPath, glowPaint);
        }

        private static void CreateHeartPath(ref SKPath path, float x, float y, float size)
        {
            path.Reset();
            path.MoveTo(x, y + size / 2);

            // Левая часть сердца
            path.CubicTo(
                x - size, y,
                x - size, y - size / 2,
                x, y - size
            );

            // Правая часть сердца
            path.CubicTo(
                x + size, y - size / 2,
                x + size, y,
                x, y + size / 2
            );

            path.Close();
        }

        private static float[] ScaleSpectrum(float[] spectrum, int targetCount)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = spectrum.Length / (2f * targetCount);

            for (int i = 0; i < targetCount; i++)
            {
                float sum = 0;
                for (int j = (int)(i * blockSize); j < (int)((i + 1) * blockSize); j++)
                    sum += spectrum[j];

                scaledSpectrum[i] = sum / blockSize;
            }
            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] spectrum, int targetCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
                _previousSpectrum = new float[targetCount];

            float[] smoothedSpectrum = new float[targetCount];

            for (int i = 0; i < targetCount; i++)
            {
                float currentValue = spectrum[i];
                float smoothedValue = _previousSpectrum[i] + (currentValue - _previousSpectrum[i]) * _smoothingFactor;
                smoothedSpectrum[i] = smoothedValue;
                _previousSpectrum[i] = smoothedValue;
            }

            return smoothedSpectrum;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _previousSpectrum = null;
                _isInitialized = false;
                _disposed = true;

                // Безопасное освобождение пулов с проверкой на null
                _pathPool?.Dispose();
                _paintPool?.Dispose();

                _pathPool = null;
                _paintPool = null;

                Log.Debug("HeartbeatRenderer disposed");
                GC.SuppressFinalize(this);
            }
        }
    }
}