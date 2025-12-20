namespace SpectrumNet;

public partial class ControlPanelWindow : Window
{
    private readonly AppController _ctrl;

    public ControlPanelWindow(AppController c)
    {
        _ctrl = c ?? throw new ArgumentNullException(nameof(c));

        InitializeComponent();
        DataContext = _ctrl;

        _ctrl.Input.RegisterWindow(this);
        Theme.Instance.RegisterWindow(this);
    }

    private void OnButtonClick(object s, RoutedEventArgs e)
    {
        if (s is not Button { Name: var n }) return;

        switch (n)
        {
            case nameof(CloseButton):
                Close();
                break;

            case nameof(StartCaptureButton):
                _ = _ctrl.Audio.StartCaptureAsync();
                break;

            case nameof(StopCaptureButton):
                _ = _ctrl.Audio.StopCaptureAsync();
                break;

            case nameof(OverlayButton):
                _ctrl.UI.ToggleOverlay();
                break;

            case nameof(OpenSettingsButton):
                new SettingsWindow().ShowDialog();
                break;

            case nameof(OpenPopupButton):
                _ctrl.UI.IsPopupOpen = true;
                break;
        }
    }

    private void OnTitleBarMouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        Rect b = CloseButton
            .TransformToAncestor(TitleBar)
            .TransformBounds(new Rect(
                0,
                0,
                CloseButton.ActualWidth,
                CloseButton.ActualHeight));

        if (!b.Contains(e.GetPosition(TitleBar)))
            DragMove();
    }

    private void OnFavoriteButtonClick(object s, RoutedEventArgs e)
    {
        if (s is not Button { Tag: RenderStyle st }) return;

        ImmutableArray<RenderStyle> f = SettingsService.Instance.Current.General.FavoriteRenderers;

        SettingsService.Instance.UpdateGeneral(g => g with
        {
            FavoriteRenderers = f.Contains(st) ? f.Remove(st) : f.Add(st)
        });

        _ctrl.View.RefreshOrderedDrawingTypes();
    }

    private void ClosePopup() => _ctrl.UI.IsPopupOpen = false;

    private void OnClosePopup(object s, RoutedEventArgs e) => ClosePopup();

    private void OnOverlayMouseDown(object s, MouseButtonEventArgs e) =>
        ClosePopup();

    private void OnPopupMouseDown(object s, MouseButtonEventArgs e) =>
        e.Handled = true;

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Theme.Instance.UnregisterWindow(this);
    }
}
