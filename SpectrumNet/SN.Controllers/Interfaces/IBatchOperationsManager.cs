#nullable enable

namespace SpectrumNet.SN.Controllers.Interfaces;

public interface IBatchOperationsManager : IDisposable
{
    void EnqueueOperation(Action operation);
    void ExecuteOrEnqueue(Action operation);
}