#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Rendering.Interfaces;

public interface IResourceManager : IDisposable
{
    SKPath GetPath();
    void ReturnPath(SKPath path);
    SKPaint GetPaint();
    void ReturnPaint(SKPaint paint);
    void CleanupUnused();
}