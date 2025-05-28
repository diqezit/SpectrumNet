// SN.Visualization/Abstract/Rendering/IResourceManager.cs
#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Rendering.Interfaces;

// управление пулами ресурсов
public interface IResourceManager : IDisposable
{
    SKPath GetPath();
    void ReturnPath(SKPath path);
    SKPaint GetPaint();
    void ReturnPaint(SKPaint paint);
}
