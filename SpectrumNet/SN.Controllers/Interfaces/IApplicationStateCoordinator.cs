// SN.Controllers/Interfaces/IApplicationStateCoordinator.cs
#nullable enable

namespace SpectrumNet.SN.Controllers.Interfaces;

public interface IApplicationStateCoordinator
{
    ApplicationState CurrentState { get; }
    event EventHandler<ApplicationState>? StateChanged;
    Task<bool> TransitionToRecordingAsync();
    Task<bool> TransitionToIdleAsync();
}

public enum ApplicationState
{
    Idle,
    Transitioning,
    Recording,
    Error
}