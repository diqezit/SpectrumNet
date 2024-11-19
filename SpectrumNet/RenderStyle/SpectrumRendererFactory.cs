using Serilog;
using SkiaSharp;

#nullable enable

namespace SpectrumNet
{
    #region Перечисление стилей рендеринга

    public enum RenderStyle
    {
        Bars,
        Dots,
        Cubes,
        Waveform,
        Loudness,
        CircularBars,
        Particles,
        SphereRenderer,
        GradientWave,
        Starburst,
        CircularWave,
        Fire,
        Raindrops
    }

    #endregion

    #region Интерфейс ISpectrumRenderer

    public interface ISpectrumRenderer : IDisposable
    {
        void Initialize();
        void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount, SKPaint paint);
        void Configure(bool isOverlayActive);
    }

    #endregion

    #region Класс SpectrumRendererFactory

    public static class SpectrumRendererFactory
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<RenderStyle, ISpectrumRenderer> _rendererCache = new();
        private static readonly HashSet<RenderStyle> _initializedRenderers = new();

        public static ISpectrumRenderer CreateRenderer(RenderStyle style, bool isOverlayActive)
        {
            if (_rendererCache.TryGetValue(style, out var cachedRenderer))
            {
                cachedRenderer.Configure(isOverlayActive);
                return cachedRenderer;
            }

            lock (_lock)
            {
                if (_rendererCache.TryGetValue(style, out cachedRenderer))
                {
                    cachedRenderer.Configure(isOverlayActive);
                    return cachedRenderer;
                }

                var renderer = CreateNewRenderer(style);

                try
                {
                    if (!_initializedRenderers.Contains(style))
                    {
                        renderer.Initialize();
                        _initializedRenderers.Add(style);
                    }

                    renderer.Configure(isOverlayActive);
                    _rendererCache[style] = renderer;

                    Log.Debug($"Создан и инициализирован новый экземпляр рендерера для стиля: {style}, компонент: {nameof(SpectrumRendererFactory)}");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Не удалось инициализировать рендерер для стиля: {style}, компонент: {nameof(SpectrumRendererFactory)}");
                    throw new InvalidOperationException($"Не удалось инициализировать рендерер для стиля: {style}", ex);
                }

                return renderer;
            }
        }

        private static ISpectrumRenderer CreateNewRenderer(RenderStyle style)
        {
            try
            {
                return style switch
                {
                    RenderStyle.Bars => BarsRenderer.GetInstance(),
                    RenderStyle.Dots => DotsRenderer.GetInstance(),
                    RenderStyle.Cubes => CubesRenderer.GetInstance(),
                    RenderStyle.Waveform => WaveformRenderer.GetInstance(),
                    RenderStyle.Loudness => LoudnessMeterRenderer.GetInstance(),
                    RenderStyle.CircularBars => CircularBarsRenderer.GetInstance(),
                    RenderStyle.Particles => ParticlesRenderer.GetInstance(),
                    RenderStyle.SphereRenderer => SphereRenderer.GetInstance(),
                    RenderStyle.GradientWave => GradientWaveRenderer.GetInstance(),
                    RenderStyle.Starburst => StarburstRenderer.GetInstance(),
                    RenderStyle.CircularWave => CircularWaveRenderer.GetInstance(),
                    RenderStyle.Fire => FireRenderer.GetInstance(),
                    RenderStyle.Raindrops => RaindropsRenderer.GetInstance(),
                    _ => throw new ArgumentException($"Неизвестный стиль рендеринга: {style}")
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Не удалось создать экземпляр рендерера для стиля: {style}, компонент: {nameof(SpectrumRendererFactory)}");
                throw new InvalidOperationException($"Не удалось создать рендерер для стиля: {style}", ex);
            }
        }

        public static void Cleanup()
        {
            lock (_lock)
            {
                List<Exception> disposalExceptions = new();

                foreach (var (style, renderer) in _rendererCache)
                {
                    try
                    {
                        renderer.Dispose();
                        Log.Debug($"Успешно удален и освобожден рендерер для стиля: {style}, компонент: {nameof(SpectrumRendererFactory)}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Ошибка при удалении рендерера для стиля: {style}, компонент: {nameof(SpectrumRendererFactory)}");
                        disposalExceptions.Add(ex);
                    }
                }

                _rendererCache.Clear();
                _initializedRenderers.Clear();
                Log.Information("Кэш рендереров очищен");

                if (disposalExceptions.Count > 0)
                {
                    throw new AggregateException("Одна или несколько ошибок произошли во время очистки рендереров",
                                               disposalExceptions);
                }
            }
        }

        public static void RemoveRenderer(RenderStyle style)
        {
            lock (_lock)
            {
                if (_rendererCache.TryGetValue(style, out var renderer))
                {
                    try
                    {
                        renderer.Dispose();
                        _rendererCache.Remove(style);
                        _initializedRenderers.Remove(style);
                        Log.Debug($"Успешно удален и освобожден рендерер для стиля: {style}, компонент: {nameof(SpectrumRendererFactory)}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Ошибка при удалении рендерера для стиля: {style}, компонент: {nameof(SpectrumRendererFactory)}");
                        throw new InvalidOperationException($"Не удалось удалить рендерер для стиля: {style}", ex);
                    }
                }
            }
        }
    }

    #endregion
}