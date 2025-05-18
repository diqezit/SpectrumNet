#nullable enable

namespace SpectrumNet.Service.Colors.Interfaces;

public interface IPalette : IDisposable
{
    string Name { get; }
    SKColor Color { get; }
    SKPaint Brush { get; }
}