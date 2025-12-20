namespace SpectrumNet.SN.Controllers;

public sealed class UIManager : ObservableObject, IDisposable
{
    private readonly ISmartLogger _log;
    private readonly AppController _main;
    private readonly IThemes _themes;

    private ControlPanelWindow? _panel;
    private bool _popup;
    private bool _disposed;

    public UIManager(AppController m, IThemes t)
    {
        _main = m ?? throw new ArgumentNullException(nameof(m));
        _themes = t ?? throw new ArgumentNullException(nameof(t));
        _log = Instance;
        _main.Overlay.StateChanged += OnState;
    }

    public bool IsOverlayActive => _main.Overlay.IsActive;

    public bool IsOverlayTopmost
    {
        get => _main.Overlay.IsTopmost;
        set
        {
            _main.Overlay.IsTopmost = value;
            OnPropertyChanged();
        }
    }

    public bool IsControlPanelOpen => _panel is { IsVisible: true };

    public bool IsPopupOpen
    {
        get => _popup;
        set => SetProperty(ref _popup, value);
    }

    private void OnState(object? s, StateChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsOverlayActive));
        OnPropertyChanged(nameof(IsOverlayTopmost));
    }

    public async Task OpenOverlayAsync()
    {
        try
        {
            await _main.Overlay.OpenAsync();
        }
        catch (Exception ex)
        {
            _log.Log(LogLevel.Error, nameof(UIManager), ex.Message);
        }

        OnPropertyChanged(nameof(IsOverlayActive));
    }

    public async Task CloseOverlayAsync()
    {
        try
        {
            await _main.Overlay.CloseAsync();
        }
        catch (Exception ex)
        {
            _log.Log(LogLevel.Error, nameof(UIManager), ex.Message);
        }

        OnPropertyChanged(nameof(IsOverlayActive));
    }

    public Task ToggleOverlayAsync() =>
        IsOverlayActive ? CloseOverlayAsync() : OpenOverlayAsync();

    public void ToggleOverlay() => _ = ToggleOverlayAsync();

    public void OpenControlPanel()
    {
        try
        {
            if (_panel is { IsVisible: true })
            {
                _panel.Activate();

                if (_panel.WindowState == WindowState.Minimized)
                    _panel.WindowState = WindowState.Normal;

                return;
            }

            _panel = new ControlPanelWindow(_main);

            if (Application.Current?.MainWindow is { IsVisible: true } o)
            {
                _panel.Owner = o;
                _panel.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                _panel.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            _panel.Closed += OnClosed;
            _panel.Show();
            OnPropertyChanged(nameof(IsControlPanelOpen));
        }
        catch (Exception ex)
        {
            _log.Log(LogLevel.Error, nameof(UIManager), ex.Message);
        }
    }

    public void CloseControlPanel()
    {
        if (_panel == null) return;

        if (Application.Current?.Dispatcher.CheckAccess() != true)
        {
            Application.Current?.Dispatcher.Invoke(CloseControlPanel);
            return;
        }

        _panel.Closed -= OnClosed;
        _panel.Close();
        _panel = null;

        OnPropertyChanged(nameof(IsControlPanelOpen));
        Application.Current?.MainWindow?.Activate();
    }

    public void ToggleControlPanel()
    {
        if (IsControlPanelOpen)
            CloseControlPanel();
        else
            OpenControlPanel();
    }

    private void OnClosed(object? s, EventArgs e)
    {
        _panel = null;
        _popup = false;

        OnPropertyChanged(nameof(IsPopupOpen));
        OnPropertyChanged(nameof(IsControlPanelOpen));

        Application.Current?.MainWindow?.Activate();
    }

    public void ToggleTheme()
    {
        try
        {
            _themes.ToggleTheme();
        }
        catch (Exception ex)
        {
            _log.Log(LogLevel.Error, nameof(UIManager), ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _main.Overlay.StateChanged -= OnState;
    }
}
