#nullable enable

namespace SpectrumNet.Service.Colors;

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
        Brush = new SKPaint
        {
            Color = color,
            Style = Fill,
            IsAntialias = true
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        Brush.Dispose();
        _disposed = true;
    }
}