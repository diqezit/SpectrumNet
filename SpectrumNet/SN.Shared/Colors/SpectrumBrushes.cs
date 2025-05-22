#nullable enable

namespace SpectrumNet.SN.Shared.Colors;

public sealed class SpectrumBrushes : IBrushProvider, IDisposable
{
    private static readonly Lazy<SpectrumBrushes> _instance =
        new(() => new SpectrumBrushes());

    private readonly ConcurrentDictionary<string, Palette> _palettes = new(StringComparer.OrdinalIgnoreCase);
    private readonly IColorDefinitionProvider _colorDefinitionProvider;
    private bool _disposed;

    public static SpectrumBrushes Instance => _instance.Value;

    public IReadOnlyDictionary<string, Palette> RegisteredPalettes => _palettes;

    public SpectrumBrushes(IColorDefinitionProvider? colorDefinitionProvider = null)
    {
        _colorDefinitionProvider = colorDefinitionProvider ?? new ColorDefinitionProvider();
        RegisterFromDefinitions();
    }

    public (SKColor Color, SKPaint Brush) GetColorAndBrush(string paletteName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(paletteName))
            throw new ArgumentException("Palette name cannot be empty", nameof(paletteName));

        if (!_palettes.TryGetValue(paletteName, out var palette))
        {
            var colorDefinitions = _colorDefinitionProvider.GetColorDefinitions();
            if (colorDefinitions.TryGetValue(paletteName, out var color))
            {
                palette = new Palette(paletteName, color);
                _palettes[paletteName] = palette;
            }
            else
                throw new KeyNotFoundException($"Palette '{paletteName}' not registered");
        }
        return (palette.Color, palette.Brush);
    }

    public void Register(Palette palette)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ArgumentNullException.ThrowIfNull(palette);

        if (string.IsNullOrWhiteSpace(palette.Name))
            throw new ArgumentException("Palette name cannot be empty", nameof(palette));

        _palettes[palette.Name] = palette;
    }

    public void Dispose()
    {
        if (_disposed) return;

        Parallel.ForEach(_palettes.Values,
            palette => palette.Dispose());
        _palettes.Clear();
        _disposed = true;
    }

    private void RegisterFromDefinitions()
    {
        foreach (var kvp in _colorDefinitionProvider.GetColorDefinitions())
        {
            var palette = new Palette(kvp.Key, kvp.Value);
            Register(palette);
        }
    }
}