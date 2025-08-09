#nullable enable

namespace SpectrumNet.SN.Shared.Controls;

public partial class KeyBindingControl : UserControl
{
    private const string LogPrefix = nameof(KeyBindingControl);
    private readonly ISmartLogger _logger = SmartLogger.Instance;

    public static readonly DependencyProperty ActionNameProperty =
        DependencyProperty.Register(
            nameof(ActionName),
            typeof(string),
            typeof(KeyBindingControl));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(
            nameof(Description),
            typeof(string),
            typeof(KeyBindingControl),
            new PropertyMetadata(string.Empty, OnDescriptionChanged));

    public static readonly DependencyProperty CurrentKeyProperty =
        DependencyProperty.Register(
            nameof(CurrentKey),
            typeof(Key),
            typeof(KeyBindingControl),
            new PropertyMetadata(Key.None, OnCurrentKeyChanged));

    public string ActionName
    {
        get => (string)GetValue(ActionNameProperty);
        set => SetValue(ActionNameProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public Key CurrentKey
    {
        get => (Key)GetValue(CurrentKeyProperty);
        set => SetValue(CurrentKeyProperty, value);
    }

    public event EventHandler<Key>? KeyChanged;
    public event EventHandler? RequestInitialization;

    private bool _isCapturing;
    private readonly Brush _captureBrush = new SolidColorBrush(Color.FromRgb(255, 165, 0));
    private Brush? _originalButtonBackground;

    public KeyBindingControl()
    {
        InitializeComponent();
        Loaded += OnControlLoaded;
    }

    private void OnControlLoaded(object sender, RoutedEventArgs e)
    {
        InitializeFromTag();
        UpdateDisplay();
    }

    private static void OnDescriptionChanged(
        DependencyObject d, 
        DependencyPropertyChangedEventArgs e)
    {
        if (d is KeyBindingControl control)
            control.UpdateDescription();
    }

    private static void OnCurrentKeyChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is KeyBindingControl control)
            control.UpdateKeyDisplay();
    }

    private void ChangeKey_Click(object sender, RoutedEventArgs e)
    {
        RequestInitialization?.Invoke(this, EventArgs.Empty);

        if (string.IsNullOrEmpty(ActionName))
        {
            InitializeFromTag();
            if (string.IsNullOrEmpty(ActionName))
                return;
        }

        if (_isCapturing)
            StopKeyCapture();
        else
            StartKeyCapture();
    }

    private void InitializeFromTag()
    {
        if (string.IsNullOrEmpty(ActionName)
            && Tag is string tagValue
            && !string.IsNullOrEmpty(tagValue))
        {
            ActionName = tagValue;
        }
    }

    private void UpdateDisplay()
    {
        UpdateDescription();
        UpdateKeyDisplay();
    }

    private void StartKeyCapture()
    {
        _isCapturing = true;
        SetupCaptureUI();
        SubscribeToInputEvents();
        Focus();
        Keyboard.Focus(this);
    }

    private void StopKeyCapture()
    {
        _isCapturing = false;
        RestoreUI();
        UnsubscribeFromInputEvents();
    }

    private void SetupCaptureUI()
    {
        if (KeyDisplay != null)
            KeyDisplay.Text = "Press any key...";

        if (ChangeButton != null)
        {
            _originalButtonBackground = ChangeButton.Background;
            ChangeButton.Content = "Cancel";
            ChangeButton.Background = _captureBrush;
        }
    }

    private void RestoreUI()
    {
        UpdateKeyDisplay();

        if (ChangeButton != null)
        {
            ChangeButton.Content = "Change";
            ChangeButton.Background = _originalButtonBackground;
        }
    }

    private void SubscribeToInputEvents()
    {
        PreviewKeyDown += OnKeyCapture;
        PreviewMouseDown += OnMouseDown;
        LostKeyboardFocus += OnLostFocus;
    }

    private void UnsubscribeFromInputEvents()
    {
        PreviewKeyDown -= OnKeyCapture;
        PreviewMouseDown -= OnMouseDown;
        LostKeyboardFocus -= OnLostFocus;
    }

    private void OnKeyCapture(object sender, KeyEventArgs e)
    {
        if (!_isCapturing) return;

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            StopKeyCapture();
            return;
        }

        var capturedKey = GetActualKey(e);

        if (IsValidKey(capturedKey))
        {
            StopKeyCapture();
            KeyChanged?.Invoke(this, capturedKey);
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isCapturing && e.OriginalSource == ChangeButton)
        {
            e.Handled = true;
            StopKeyCapture();
        }
    }

    private void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_isCapturing)
            StopKeyCapture();
    }

    private void UpdateDescription()
    {
        if (DescriptionText != null)
            DescriptionText.Text = Description;
    }

    private void UpdateKeyDisplay()
    {
        if (KeyDisplay != null && !_isCapturing)
            KeyDisplay.Text = CurrentKey == Key.None ? "None" : CurrentKey.ToString();
    }

    private static Key GetActualKey(KeyEventArgs e)
    {
        return e.Key == Key.System ? e.SystemKey : e.Key;
    }

    private static bool IsValidKey(Key key)
    {
        var invalidKeys = new[]
        {
            Key.None, Key.LWin, Key.RWin, Key.Apps, Key.Sleep,
            Key.System, Key.LeftShift, Key.RightShift,
            Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt
        };

        return !invalidKeys.Contains(key);
    }
}