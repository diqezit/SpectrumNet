#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Helpers.Interfaces;

public interface IBufferManager : IDisposable
{
    T[] GetBuffer<T>(string key, int size);
    void Clear();
}
