#nullable enable

namespace SpectrumNet.SN.Controllers.View;

public class StylesProvider(
    IMainController mainController,
    ISettings settings,
    IBrushProvider? brushProvider = null) : IStylesProvider
{
    private readonly ISettings _settings = settings ??
        throw new ArgumentNullException(nameof(settings));

    private readonly IMainController _mainController = mainController ??
        throw new ArgumentNullException(nameof(mainController));

    private readonly IBrushProvider _brushProvider = brushProvider ??
        SpectrumBrushes.Instance;

    public IReadOnlyDictionary<string, Palette> AvailablePalettes => _brushProvider.RegisteredPalettes;
    public IEnumerable<RenderStyle> AvailableDrawingTypes { get; } = [.. Enum.GetValues<RenderStyle>()
                                                                     .OrderBy(s => s.ToString())];
    public IEnumerable<FftWindowType> AvailableFftWindowTypes { get; } = [.. Enum.GetValues<FftWindowType>()
                                                                         .OrderBy(wt => wt.ToString())];
    public IEnumerable<SpectrumScale> AvailableScaleTypes { get; } = [.. Enum.GetValues<SpectrumScale>()
                                                                     .OrderBy(s => s.ToString())];
    public IEnumerable<RenderQuality> AvailableRenderQualities { get; } = [.. Enum.GetValues<RenderQuality>()
                                                                          .OrderBy(q => (int)q)];

    public IEnumerable<RenderStyle> OrderedDrawingTypes =>
        AvailableDrawingTypes.OrderBy(x =>
            _settings.General.FavoriteRenderers.Contains(x) ? 0 : 1).ThenBy(x => x.ToString());

    public Palette? SelectedPalette
    {
        get => AvailablePalettes.TryGetValue(_mainController.SelectedStyle, out var palette) ? palette : null;
        set
        {
            if (value is null) return;
            _mainController.SelectedStyle = value.Name;
            _mainController.OnPropertyChanged(nameof(SelectedPalette));
        }
    }
}