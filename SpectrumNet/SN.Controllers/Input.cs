namespace SpectrumNet.SN.Controllers;

public sealed class InputHandler : IDisposable
{
    private readonly ISmartLogger _log;
    private readonly AppController _ctrl;
    private readonly ISettingsService _cfg;
    private readonly ITransparencyManager _tm;
    private readonly Dictionary<string, Action> _acts;

    private bool _disposed;

    public InputHandler(
        AppController c,
        ISettingsService s,
        ITransparencyManager t)
    {
        _ctrl = c ?? throw new ArgumentNullException(nameof(c));
        _cfg = s ?? throw new ArgumentNullException(nameof(s));
        _tm = t ?? throw new ArgumentNullException(nameof(t));
        _log = Instance;

        _acts = new()
        {
            ["ToggleOverlay"] = _ctrl.UI.ToggleOverlay,
            ["ToggleControlPanel"] = _ctrl.UI.ToggleControlPanel,
            ["ToggleRecording"] = () => _ = ToggleRec(),
            ["QualityLow"] = () => _ctrl.View.RenderQuality = RenderQuality.Low,
            ["QualityMedium"] = () => _ctrl.View.RenderQuality = RenderQuality.Medium,
            ["QualityHigh"] = () => _ctrl.View.RenderQuality = RenderQuality.High,
            ["PreviousRenderer"] = _ctrl.View.SelectPreviousRenderer,
            ["NextRenderer"] = _ctrl.View.SelectNextRenderer,
            ["ClosePopup"] = () => _ctrl.UI.IsPopupOpen = false,
            ["IncreaseBarCount"] = () => AdjBars(Limits.BarStep),
            ["DecreaseBarCount"] = () => AdjBars(-Limits.BarStep),
            ["IncreaseBarSpacing"] = () => AdjSpacing(Limits.SpacingStep),
            ["DecreaseBarSpacing"] = () => AdjSpacing(-Limits.SpacingStep)
        };
    }

    public void RegisterWindow(Window w)
    {
        if (_disposed) return;

        w.KeyDown += HandleKeyDown;

        if (!ReferenceEquals(w, Application.Current?.MainWindow))
            return;

        w.MouseDown += OnDown;
        w.MouseMove += OnMove;
        w.MouseUp += OnUp;
        w.MouseEnter += OnEnter;
        w.MouseLeave += OnLeave;
        w.MouseDoubleClick += OnDouble;
    }

    private string? Act(Key k) =>
        k == Key.None
            ? null
            : _cfg.Current.KeyBindings.Bindings
                .FirstOrDefault(x => x.Value == k).Key;

    private RendererPlaceholder? Ph => _ctrl.View.Renderer?.GetPlaceholder();
    private bool ShowPh => _ctrl.View.Renderer?.ShouldShowPlaceholder == true;

    private SKPoint Pt(MouseEventArgs e)
    {
        System.Windows.Point p = e.GetPosition(_ctrl.View.SpectrumCanvas);
        return new((float)p.X, (float)p.Y);
    }

    public void HandleKeyDown(object? s, KeyEventArgs e)
    {
        if (_disposed || e.IsRepeat)
            return;

        if (Keyboard.FocusedElement is TextBox or PasswordBox or ComboBox)
            return;

        string? a = Act(e.Key);
        if (a != null && _acts.TryGetValue(a, out Action? f))
        {
            f();
            e.Handled = true;
        }
    }

    private void AdjBars(int d) =>
        _ctrl.View.BarCount = Clamp(
            _ctrl.View.BarCount + d,
            Limits.MinBars,
            Limits.MaxBars);

    private void AdjSpacing(double d) =>
        _ctrl.View.BarSpacing = Clamp(
            _ctrl.View.BarSpacing + d,
            Limits.MinSpacing,
            Limits.MaxSpacing);

    private async Task ToggleRec()
    {
        try
        {
            if (_ctrl.IsRecording)
                await _ctrl.Audio.StopCaptureAsync();
            else
                await _ctrl.Audio.StartCaptureAsync();
        }
        catch (Exception ex)
        {
            _log.Log(LogLevel.Error, nameof(InputHandler), ex.Message);
        }
    }

    private void OnDown(object? s, MouseButtonEventArgs e)
    {
        if (_disposed || e.ChangedButton != MouseButton.Left || !ShowPh)
            return;

        SKPoint p = Pt(e);
        if (Ph?.HitTest(p) == true)
        {
            Ph.OnMouseDown(p);
            e.Handled = true;
        }
    }

    private void OnMove(object? s, MouseEventArgs e)
    {
        if (_disposed) return;

        _tm.OnMouseMove();

        if (ShowPh)
            Ph?.OnMouseMove(Pt(e));
    }

    private void OnUp(object? s, MouseButtonEventArgs e)
    {
        if (_disposed || e.ChangedButton != MouseButton.Left || !ShowPh)
            return;

        Ph?.OnMouseUp(Pt(e));
    }

    private void OnDouble(object? s, MouseButtonEventArgs e)
    {
        if (_disposed || e.ChangedButton != MouseButton.Left)
            return;

        if (e.OriginalSource is CheckBox)
        {
            e.Handled = true;
            return;
        }

        if (ShowPh && Ph?.HitTest(Pt(e)) == true)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;

        if (Application.Current?.MainWindow is Window w)
        {
            w.WindowState = w.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }

    private void OnEnter(object? s, MouseEventArgs e)
    {
        if (_disposed) return;

        _tm.OnMouseEnter();
        Ph?.OnMouseEnter();
    }

    private void OnLeave(object? s, MouseEventArgs e)
    {
        if (_disposed) return;

        _tm.OnMouseLeave();
        Ph?.OnMouseLeave();
    }

    public void Dispose() => _disposed = true;
}
