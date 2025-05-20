#nullable enable

namespace SpectrumNet.Controllers.Interfaces.UICore;

public interface IBatchOperationsManager : IDisposable
{
    void EnqueueOperation(Action operation);
    void ExecuteOrEnqueue(Action operation);
}