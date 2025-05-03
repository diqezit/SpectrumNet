#nullable enable

namespace SpectrumNet.Views.Renderers;

public sealed class CircularBarsRenderer : EffectSpectrumRenderer
{
    private static class Constants
    {
        public const string
            LOG_PREFIX = "CircularBarsRenderer";

        public const float
            RADIUS_PROPORTION = 0.8f,
            INNER_RADIUS_FACTOR = 0.9f,
            BAR_SPACING_FACTOR = 0.7f,
            MIN_STROKE_WIDTH = 2f,
            SPECTRUM_MULTIPLIER = 0.5f,
            MAX_BAR_HEIGHT = 1.5f,
            MIN_BAR_HEIGHT = 0.01f,
            GLOW_RADIUS = 3f,
            GLOW_INTENSITY = 0.4f,
            GLOW_THRESHOLD = 0.6f,
            HIGHLIGHT_ALPHA = 0.7f,
            HIGHLIGHT_POSITION = 0.7f,
            HIGHLIGHT_INTENSITY = 0.5f,
            HIGHLIGHT_THRESHOLD = 0.4f;

        public const byte
            INNER_CIRCLE_ALPHA = 80;

        public const int
            PARALLEL_BATCH_SIZE = 32,
            DEFAULT_PATH_POOL_SIZE = 8;
    }

    public readonly record struct RenderConfig(float BarWidth, float BarSpacing, int BarCount);

    private static readonly Lazy<CircularBarsRenderer> _instance = new(() => new CircularBarsRenderer());

    private Vector2[]? _barVectors;
    private int _previousBarCount;

    private readonly SKPathPool _barPathPool;

    private CircularBarsRenderer()
    {
        _barPathPool = new SKPathPool(Constants.DEFAULT_PATH_POOL_SIZE);
    }

    public static CircularBarsRenderer GetInstance() => _instance.Value;

    protected override void OnQualitySettingsApplied()
    {
        Safe(
           () =>
           {
               base.OnQualitySettingsApplied();
               // Specific quality settings for CircularBarsRenderer if needed
               Log(LogLevel.Debug, Constants.LOG_PREFIX, $"Quality set to {base.Quality}");
           },
           new ErrorHandlingOptions
           {
               Source = $"{GetType().Name}.OnQualitySettingsApplied",
               ErrorMessage = "Failed to apply circular bars specific quality settings"
           }
       );
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
        float centerX = info.Width / 2f;
        float centerY = info.Height / 2f;
        float mainRadius = MathF.Min(centerX, centerY) * Constants.RADIUS_PROPORTION;
        float adjustedBarWidth = AdjustBarWidthForBarCount(
            barWidth,
            barCount,
            MathF.Min(info.Width, info.Height));

        EnsureBarVectors(barCount);

        RenderCircularBars(
            canvas,
            spectrum,
            barCount,
            centerX,
            centerY,
            mainRadius,
            adjustedBarWidth,
            paint);
    }

    private float AdjustBarWidthForBarCount(float barWidth, int barCount, float minDimension)
    {
        float maxPossibleWidth = 2 * MathF.PI * Constants.RADIUS_PROPORTION * minDimension / 2 /
                                  barCount * Constants.BAR_SPACING_FACTOR;

        return MathF.Max(MathF.Min(barWidth, maxPossibleWidth), Constants.MIN_STROKE_WIDTH);
    }

    private void RenderCircularBars(
        SKCanvas canvas,
        float[] spectrum,
        int barCount,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint basePaint)
    {
        using var innerCirclePaint = new SKPaint
        {
            IsAntialias = UseAntiAlias,
            Style = Stroke,
            Color = basePaint.Color.WithAlpha(Constants.INNER_CIRCLE_ALPHA),
            StrokeWidth = barWidth * 0.5f
        };

        canvas.DrawCircle(
            centerX,
            centerY,
            mainRadius * Constants.INNER_RADIUS_FACTOR,
            innerCirclePaint);

        EnsureBarVectors(barCount);

        if (UseAdvancedEffects)
        {
            RenderGlowEffects(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
        }

        RenderMainBars(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);

        if (UseAdvancedEffects)
        {
            RenderHighlights(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
        }
    }

    private void EnsureBarVectors(int barCount)
    {
        Safe(() =>
        {
            if (_barVectors == null || _barVectors.Length != barCount || _previousBarCount != barCount)
            {
                _barVectors = new Vector2[barCount];
                float angleStep = 2 * MathF.PI / barCount;

                for (int i = 0; i < barCount; i++)
                {
                    _barVectors[i] = new Vector2(
                        MathF.Cos(angleStep * i),
                        MathF.Sin(angleStep * i));
                }

                _previousBarCount = barCount;
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.EnsureBarVectors",
            ErrorMessage = "Error calculating bar vectors"
        });
    }

    private void RenderGlowEffects(
        SKCanvas canvas,
        float[] spectrum,
        int barCount,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint basePaint)
    {
        using var batchPath = new SKPath();

        for (int i = 0; i < barCount; i++)
        {
            if (spectrum[i] <= Constants.GLOW_THRESHOLD)
                continue;

            float radius = mainRadius + spectrum[i] * mainRadius * Constants.SPECTRUM_MULTIPLIER;
            var path = _barPathPool.Get();

            AddBarToPath(path, i, centerX, centerY, mainRadius, radius);
            batchPath.AddPath(path);

            _barPathPool.Return(path);
        }

        if (!batchPath.IsEmpty)
        {
            using var glowPaint = new SKPaint
            {
                IsAntialias = UseAntiAlias,
                Style = Stroke,
                Color = basePaint.Color.WithAlpha((byte)(255 * Constants.GLOW_INTENSITY)),
                StrokeWidth = barWidth * 1.2f,
                MaskFilter = SKMaskFilter.CreateBlur(Normal, Constants.GLOW_RADIUS)
            };

            canvas.DrawPath(batchPath, glowPaint);
        }
    }

    private void RenderMainBars(
        SKCanvas canvas,
        float[] spectrum,
        int barCount,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint basePaint)
    {
        using var batchPath = new SKPath();

        for (int i = 0; i < barCount; i++)
        {
            if (spectrum[i] < MIN_MAGNITUDE_THRESHOLD)
                continue;

            float radius = mainRadius + spectrum[i] * mainRadius * Constants.SPECTRUM_MULTIPLIER;
            var path = _barPathPool.Get();

            AddBarToPath(path, i, centerX, centerY, mainRadius, radius);
            batchPath.AddPath(path);

            _barPathPool.Return(path);
        }

        if (!batchPath.IsEmpty)
        {
            using var barPaint = new SKPaint
            {
                IsAntialias = UseAntiAlias,
                Style = Stroke,
                StrokeCap = SKStrokeCap.Round,
                Color = basePaint.Color,
                StrokeWidth = barWidth
            };

            canvas.DrawPath(batchPath, barPaint);
        }
    }

    private void RenderHighlights(
        SKCanvas canvas,
        float[] spectrum,
        int barCount,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint basePaint)
    {
        using var batchPath = new SKPath();

        for (int i = 0; i < barCount; i++)
        {
            if (spectrum[i] <= Constants.HIGHLIGHT_THRESHOLD)
                continue;

            float radius = mainRadius + spectrum[i] * mainRadius * Constants.SPECTRUM_MULTIPLIER;
            float innerPoint = mainRadius + (radius - mainRadius) * Constants.HIGHLIGHT_POSITION;

            var path = _barPathPool.Get();
            AddBarToPath(path, i, centerX, centerY, innerPoint, radius);
            batchPath.AddPath(path);

            _barPathPool.Return(path);
        }

        if (!batchPath.IsEmpty)
        {
            using var highlightPaint = new SKPaint
            {
                IsAntialias = UseAntiAlias,
                Style = Stroke,
                Color = SKColors.White.WithAlpha((byte)(255 * Constants.HIGHLIGHT_INTENSITY)),
                StrokeWidth = barWidth * 0.6f
            };

            canvas.DrawPath(batchPath, highlightPaint);
        }
    }

    private void AddBarToPath(
        SKPath path,
        int index,
        float centerX,
        float centerY,
        float innerRadius,
        float outerRadius)
    {
        if (_barVectors == null)
            return;

        Vector2 vector = _barVectors[index];

        path.MoveTo(
            centerX + innerRadius * vector.X,
            centerY + innerRadius * vector.Y);

        path.LineTo(
            centerX + outerRadius * vector.X,
            centerY + outerRadius * vector.Y);
    }

    private class SKPathPool : IDisposable
    {
        private readonly List<SKPath> _paths = new();
        private readonly List<SKPath> _inUse = new();
        private readonly object _lockObject = new();
        private bool _disposed;

        public SKPathPool(int capacity)
        {
            for (int i = 0; i < capacity; i++)
                _paths.Add(new SKPath());
        }

        public SKPath Get()
        {
            lock (_lockObject)
            {
                ObjectDisposedException.ThrowIf(_disposed, nameof(SKPathPool));

                SKPath path;
                if (_paths.Count > 0)
                {
                    path = _paths[_paths.Count - 1];
                    _paths.RemoveAt(_paths.Count - 1);
                }
                else
                {
                    path = new SKPath();
                }

                path.Reset();
                _inUse.Add(path);
                return path;
            }
        }

        public void Return(SKPath path)
        {
            lock (_lockObject)
            {
                if (_disposed)
                    return;

                if (_inUse.Remove(path))
                    _paths.Add(path);
            }
        }

        public void Dispose()
        {
            lock (_lockObject)
            {
                if (!_disposed)
                {
                    foreach (var path in _paths)
                        path.Dispose();

                    foreach (var path in _inUse)
                        path.Dispose();

                    _paths.Clear();
                    _inUse.Clear();
                    _disposed = true;
                }
            }
        }
    }

    protected override void OnDispose()
    {
        Safe(() =>
        {
            _barPathPool?.Dispose();
            base.OnDispose();
        },
        new ErrorHandlingOptions
        {
            Source = $"{GetType().Name}.OnDispose",
            ErrorMessage = "Error during CircularBarsRenderer disposal"
        });
    }
}