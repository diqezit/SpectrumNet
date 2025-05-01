#nullable enable 

namespace SpectrumNet.Service.Colors;

/// <summary>
/// Represents a named color palette with managed drawing resources.
/// </summary>
/// <remarks>
/// Implements IDisposable to ensure proper cleanup of SKPaint resources.
/// Provides both color value and pre-configured paint brush for drawing operations.
/// </remarks>
public sealed record Palette(string Name, SKColor Color) : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Gets the pre-configured paint brush for this palette.
    /// </summary>
    public SKPaint Brush { get; } = new SKPaint
    {
        Color = Color,
        Style = Fill,
        IsAntialias = true
    };

    /// <summary>
    /// Releases all resources used by the Palette.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        Brush.Dispose();
        _disposed = true;
    }
}
