#nullable enable

namespace SpectrumNet.SN.Shared.Colors.Interfaces;

public interface IPalette : IDisposable
{
    string Name { get; }
    SKColor Color { get; }
    SKPaint Brush { get; }
}