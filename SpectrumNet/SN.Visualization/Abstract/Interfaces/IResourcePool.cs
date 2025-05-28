#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Interfaces;

public interface IResourcePool : IDisposable
{
    SKPath GetPath();
    void ReturnPath(SKPath path);
    SKPaint GetPaint();
    void ReturnPaint(SKPaint paint);
    void CleanupUnused();
}