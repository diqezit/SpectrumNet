// SN.Controllers/ApplicationStateCoordinator.cs
#nullable enable

namespace SpectrumNet.SN.Controllers;

public sealed class ApplicationStateCoordinator : IApplicationStateCoordinator
{
    private const string LogPrefix = nameof(ApplicationStateCoordinator);
    private readonly ISmartLogger _logger = Instance;

    private readonly IMainController _controller;
    private readonly ISettings _settings;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private ApplicationState _currentState = ApplicationState.Idle;

    public ApplicationState CurrentState => _currentState;

    public event EventHandler<ApplicationState>? StateChanged;

    public ApplicationStateCoordinator(IMainController controller, ISettings settings)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<bool> TransitionToRecordingAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_currentState == ApplicationState.Recording)
                return true;

            if (_currentState != ApplicationState.Idle)
                return false;

            ChangeState(ApplicationState.Transitioning);

            await _controller.AudioController.StartCaptureAsync();
            ChangeState(ApplicationState.Recording);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(LogPrefix, "Failed to transition to recording", ex);
            ChangeState(ApplicationState.Error);
            await Task.Delay(1000);
            ChangeState(ApplicationState.Idle);
            return false;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<bool> TransitionToIdleAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_currentState == ApplicationState.Idle)
                return true;

            ChangeState(ApplicationState.Transitioning);

            await _controller.AudioController.StopCaptureAsync();
            ChangeState(ApplicationState.Idle);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(LogPrefix, "Failed to transition to idle", ex);
            ChangeState(ApplicationState.Idle);
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void ChangeState(ApplicationState newState)
    {
        if (_currentState == newState) return;

        _currentState = newState;
        StateChanged?.Invoke(this, newState);

        _logger.Log(LogLevel.Information, LogPrefix, $"State changed to: {newState}");
    }
}