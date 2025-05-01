#nullable enable 

namespace SpectrumNet.Service.Colors;

/// <summary>
/// Manages palette resources with lifecycle control and thread-safe access.
/// </summary>
/// <remarks>
/// Automatically registers predefined palettes from Palettes.ColorDefinitions upon initialization.
/// Provides safe access to color resources and implements IDisposable for proper cleanup.
/// </remarks>
public sealed class SpectrumBrushes : IDisposable
{
    private static readonly Lazy<SpectrumBrushes> _instance = new Lazy<SpectrumBrushes>(() => new SpectrumBrushes());
    private readonly ConcurrentDictionary<string, Palette> _palettes = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public static SpectrumBrushes Instance => _instance.Value;

    /// <summary>
    /// Gets a read-only view of registered palettes.
    /// </summary>
    public IReadOnlyDictionary<string, Palette> RegisteredPalettes => _palettes;

    /// <summary>
    /// Initializes a new instance and registers palettes from Palettes.ColorDefinitions.
    /// </summary>
    public SpectrumBrushes()
    {
        RegisterFromDefinitions();
    }

    private void RegisterFromDefinitions()
    {
        foreach (var kvp in Colors.ColorDefinitions)
        {
            var palette = new Palette(kvp.Key, kvp.Value);
            Register(palette);
        }
    }

    /// <summary>
    /// Registers a custom palette instance.
    /// </summary>
    /// <param name="palette">Palette to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when palette is null.</exception>
    /// <exception cref="ArgumentException">Thrown when palette name is invalid.</exception>
    public void Register(Palette palette)
    {
        if (palette == null)
            throw new ArgumentNullException(nameof(palette));

        if (string.IsNullOrWhiteSpace(palette.Name))
            throw new ArgumentException("Palette name cannot be empty", nameof(palette));

        _palettes[palette.Name] = palette;
    }

    /// <summary>
    /// Retrieves color and paint resources for the specified palette.
    /// </summary>
    /// <param name="paletteName">Case-insensitive palette identifier.</param>
    /// <returns>Tuple containing color value and associated paint brush.</returns>
    /// <exception cref="ArgumentException">Thrown for invalid palette names.</exception>
    /// <exception cref="KeyNotFoundException">Thrown for unregistered palettes.</exception>
    public (SKColor Color, SKPaint Brush) GetColorAndBrush(string paletteName)
    {
        EnsureNotDisposed();

        if (string.IsNullOrWhiteSpace(paletteName))
            throw new ArgumentException("Palette name cannot be empty", nameof(paletteName));

        if (_palettes.TryGetValue(paletteName, out var palette))
        {
            return (palette.Color, palette.Brush);
        }
        throw new KeyNotFoundException($"Palette '{paletteName}' not registered");
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <summary>
    /// Releases all managed resources and clears registered palettes.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        foreach (var palette in _palettes.Values)
            palette.Dispose();

        _palettes.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
