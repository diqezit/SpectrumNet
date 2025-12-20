namespace SpectrumNet.SN.Shared.Colors;

public interface IBrushProvider
{
    (SKColor Color, SKPaint Brush) GetColorAndBrush(string paletteName);
    IReadOnlyDictionary<string, Palette> RegisteredPalettes { get; }
}

public interface IPalette : IDisposable
{
    string Name { get; }
    SKColor Color { get; }
    SKPaint Brush { get; }
}

public sealed class Palette : IPalette
{
    private bool _disposed;

    public string Name { get; }
    public SKColor Color { get; }
    public SKPaint Brush { get; }

    public Palette(string name, SKColor color)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Color = color;
        Brush = new() { Color = color, Style = Fill, IsAntialias = true };
    }

    public void Dispose()
    {
        if (_disposed) return;
        Brush.Dispose();
        _disposed = true;
    }
}

public sealed class SpectrumBrushes : IBrushProvider, IDisposable
{
    private static readonly Lazy<SpectrumBrushes> _instance = new(() => new SpectrumBrushes());
    private readonly ConcurrentDictionary<string, Palette> _palettes = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public static SpectrumBrushes Instance => _instance.Value;
    public IReadOnlyDictionary<string, Palette> RegisteredPalettes => _palettes;

    private SpectrumBrushes() => RegFromDefs();

    public (SKColor Color, SKPaint Brush) GetColorAndBrush(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_palettes.TryGetValue(name, out Palette? p))
        {
            if (Colors.Definitions.TryGetValue(name, out var c))
            {
                p = new Palette(name, c);
                _palettes[name] = p;
            }
            else
                throw new KeyNotFoundException($"Palette '{name}' not found");
        }
        return (p.Color, p.Brush);
    }

    public void Register(Palette p)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(p);
        _palettes[p.Name] = p;
    }

    private void RegFromDefs()
    {
        foreach (var (n, c) in Colors.Definitions)
            Register(new Palette(n, c));
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (Palette p in _palettes.Values) p.Dispose();
        _palettes.Clear();
        _disposed = true;
    }
}
