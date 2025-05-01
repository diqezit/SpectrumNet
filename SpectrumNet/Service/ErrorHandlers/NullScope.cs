#nullable enable

namespace SpectrumNet.Service.ErrorHandlers;

public class NullScope : IDisposable
{
    public static NullScope Instance { get; } = new NullScope();
    private NullScope() { }
    public void Dispose() { }
}
