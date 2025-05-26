#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.BarsRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class BarsRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<BarsRenderer> _instance =
        new(() => new BarsRenderer());

    public static BarsRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            DEFAULT_CORNER_RADIUS_FACTOR = 0.5f,
            MIN_BAR_HEIGHT = 1f,
            MAX_CORNER_RADIUS = 125f,
            GLOW_EFFECT_ALPHA = 0.25f,
            ALPHA_MULTIPLIER = 1.5f,
            HIGH_INTENSITY_THRESHOLD = 0.6f;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                GlowRadius: 0f,  // Отключаем свечение для низкого качества
                EdgeStrokeWidth: 0f,
                EdgeBlurRadius: 0f
            ),
            [RenderQuality.Medium] = new(
                GlowRadius: 2.0f,
                EdgeStrokeWidth: 1.5f,
                EdgeBlurRadius: 1f
            ),
            [RenderQuality.High] = new(
                GlowRadius: 3.0f,
                EdgeStrokeWidth: 2.5f,
                EdgeBlurRadius: 2f
            )
        };

        public record QualitySettings(
            float GlowRadius,
            float EdgeStrokeWidth,
            float EdgeBlurRadius
        );
    }

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];

    protected override void OnInitialize() =>
        UpdatePaintConfigs();

    protected override void OnQualitySettingsApplied() =>
        UpdatePaintConfigs();

    private void UpdatePaintConfigs()
    {
        _currentSettings = QualityPresets[Quality];

        // Регистрируем все конфигурации кистей
        RegisterPaintConfig(
            "glow",
            CreateGlowPaintConfig(SKColors.White, _currentSettings.GlowRadius));

        RegisterPaintConfig(
            "edge",
            CreateEdgePaintConfig(
                SKColors.White,
                _currentSettings.EdgeStrokeWidth,
                _currentSettings.EdgeBlurRadius));

        RegisterPaintConfig(
            "main",
            CreateDefaultPaintConfig(SKColors.White));
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
        float cornerRadius = Min(
            barWidth * DEFAULT_CORNER_RADIUS_FACTOR,
            MAX_CORNER_RADIUS);

        int spectrumLength = Min(barCount, spectrum.Length);

        // Порядок отрисовки: свечение -> основные бары -> края
        if (UseAdvancedEffects && _currentSettings.GlowRadius > 0)
        {
            RenderGlowEffects(canvas, spectrum, info,
                barWidth, barSpacing, cornerRadius, spectrumLength);
        }

        RenderMainBars(canvas, spectrum, info, barWidth,
            barSpacing, paint, cornerRadius, spectrumLength);

        if (UseAdvancedEffects && _currentSettings.EdgeStrokeWidth > 0)
        {
            RenderEdgeEffects(canvas, spectrum, info, barWidth,
                barSpacing, cornerRadius, spectrumLength);
        }
    }

    private void RenderGlowEffects(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float cornerRadius,
        int spectrumLength)
    {
        using var glowPaint = CreatePaint("glow");

        // Используем пакетный рендеринг для скругленных прямоугольников
        if (cornerRadius > 0)
        {
            // Для каждого бара отдельно, т.к. у них разные цвета
            for (int i = 0; i < spectrumLength; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < HIGH_INTENSITY_THRESHOLD) continue;

                float x = i * (barWidth + barSpacing);
                var rect = GetBarRect(x, magnitude, barWidth, info.Height, MIN_BAR_HEIGHT);

                if (IsRectVisible(canvas, rect))
                {
                    glowPaint.Color = ApplyAlpha(SKColors.White, magnitude * GLOW_EFFECT_ALPHA);
                    canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, glowPaint);
                }
            }
        }
        else
        {
            // Для прямоугольников без скругления используем батчинг по цветам
            var glowGroups = new Dictionary<byte, List<SKRect>>();

            // Группируем по альфа-значению для пакетной отрисовки
            for (int i = 0; i < spectrumLength; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < HIGH_INTENSITY_THRESHOLD) continue;

                float x = i * (barWidth + barSpacing);
                var rect = GetBarRect(x, magnitude, barWidth, info.Height, MIN_BAR_HEIGHT);

                if (IsRectVisible(canvas, rect))
                {
                    byte alpha = CalculateAlpha(magnitude * GLOW_EFFECT_ALPHA);
                    if (!glowGroups.TryGetValue(alpha, out var rects))
                    {
                        rects = new List<SKRect>();
                        glowGroups[alpha] = rects;
                    }
                    rects.Add(rect);
                }
            }

            // Отрисовка по группам
            foreach (var (alpha, rects) in glowGroups)
            {
                glowPaint.Color = SKColors.White.WithAlpha(alpha);
                RenderRects(canvas, rects, glowPaint);
            }
        }
    }

    private void RenderMainBars(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        SKPaint paint,
        float cornerRadius,
        int spectrumLength)
    {
        // Выбираем оптимальную стратегию отрисовки в зависимости от наличия скругления
        if (cornerRadius == 0)
        {
            // Без скругления - используем батчинг для производительности
            RenderBatch(canvas, path =>
            {
                for (int i = 0; i < spectrumLength; i++)
                {
                    float magnitude = spectrum[i];
                    if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;

                    float x = i * (barWidth + barSpacing);
                    var rect = GetBarRect(x, magnitude, barWidth, info.Height, MIN_BAR_HEIGHT);

                    if (IsRectVisible(canvas, rect))
                    {
                        path.AddRect(rect);
                    }
                }
            }, paint);
        }
        else
        {
            // Со скруглением - группируем по цветам для оптимизации
            var barGroups = new Dictionary<byte, List<SKRect>>();

            // Группируем бары по альфа-значению
            for (int i = 0; i < spectrumLength; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;

                float x = i * (barWidth + barSpacing);
                var rect = GetBarRect(x, magnitude, barWidth, info.Height, MIN_BAR_HEIGHT);

                if (IsRectVisible(canvas, rect))
                {
                    byte alpha = CalculateAlpha(magnitude, ALPHA_MULTIPLIER * 255);
                    if (!barGroups.TryGetValue(alpha, out var rects))
                    {
                        rects = new List<SKRect>();
                        barGroups[alpha] = rects;
                    }
                    rects.Add(rect);
                }
            }

            // Используем копию цвета из основной кисти
            var baseColor = paint.Color;

            // Отрисовка по группам
            foreach (var (alpha, rects) in barGroups)
            {
                paint.Color = baseColor.WithAlpha(alpha);

                // Для каждой группы используем batch рендеринг скругленных прямоугольников
                RenderBatch(canvas, path =>
                {
                    foreach (var rect in rects)
                    {
                        path.AddRoundRect(rect, cornerRadius, cornerRadius);
                    }
                }, paint);
            }

            // Восстанавливаем исходный цвет
            paint.Color = baseColor;
        }
    }

    private void RenderEdgeEffects(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float cornerRadius,
        int spectrumLength)
    {
        using var edgePaint = CreatePaint("edge");

        // Группируем по цветам для оптимизации при большом количестве баров
        if (spectrumLength > 50)
        {
            var edgeGroups = new Dictionary<byte, List<SKRect>>();

            // Группируем по альфа-значению
            for (int i = 0; i < spectrumLength; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;

                float x = i * (barWidth + barSpacing);
                var rect = GetBarRect(x, magnitude, barWidth, info.Height, MIN_BAR_HEIGHT);

                if (IsRectVisible(canvas, rect))
                {
                    byte alpha = CalculateAlpha(Lerp(0.3f, 1f, magnitude));
                    if (!edgeGroups.TryGetValue(alpha, out var rects))
                    {
                        rects = new List<SKRect>();
                        edgeGroups[alpha] = rects;
                    }
                    rects.Add(rect);
                }
            }

            // Отрисовка по группам
            foreach (var (alpha, rects) in edgeGroups)
            {
                edgePaint.Color = SKColors.White.WithAlpha(alpha);

                RenderBatch(canvas, path =>
                {
                    foreach (var rect in rects)
                    {
                        path.AddRoundRect(rect, cornerRadius, cornerRadius);
                    }
                }, edgePaint);
            }
        }
        else
        {
            // Для небольшого количества баров рендерим напрямую
            for (int i = 0; i < spectrumLength; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;

                float x = i * (barWidth + barSpacing);
                var rect = GetBarRect(x, magnitude, barWidth, info.Height, MIN_BAR_HEIGHT);

                if (IsRectVisible(canvas, rect))
                {
                    edgePaint.Color = InterpolateColor(SKColors.White, magnitude, 0.3f, 1f);
                    canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, edgePaint);
                }
            }
        }
    }
}