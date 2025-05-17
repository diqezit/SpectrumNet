#nullable enable

namespace SpectrumNet.Controllers.RenderCore.Overlay;

public interface IOverlayManager : IAsyncDisposable
{
    // Состояние оверлея
    bool IsActive { get; }
    bool IsTopmost { get; set; }

    // Основные операции
    Task OpenAsync();
    Task CloseAsync();
    Task ToggleAsync();

    // Настройка внешнего вида
    void SetTransparency(float level);
    void UpdateRenderDimensions(int width, int height);
    void ForceRedraw();

    // Конфигурация
    void Configure(OverlayConfiguration configuration);

    // События
    event EventHandler<bool>? StateChanged;
}