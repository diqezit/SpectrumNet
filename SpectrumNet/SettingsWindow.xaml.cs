namespace SpectrumNet;

public sealed partial class SliderSetting(
    string key,
    string? desc,
    double min,
    double max,
    TypeCode tc,
    double val) : ObservableObject
{
    public string Key { get; } = key;
    public string Label => Key;
    public string? Description { get; } = desc;
    public double Min { get; } = min;
    public double Max { get; } = max;
    public TypeCode TypeCode { get; } = tc;

    [ObservableProperty]
    private double _value = val;
}

public sealed partial class KeyBindingItem : ObservableObject
{
    private static readonly ImmutableHashSet<Key> Invalid =
    [
        Key.None, Key.LWin, Key.RWin, Key.Apps,
        Key.Sleep, Key.System, Key.LeftShift,
        Key.RightShift, Key.LeftCtrl, Key.RightCtrl,
        Key.LeftAlt, Key.RightAlt
    ];

    private static readonly SolidColorBrush CapBr =
        Freeze(new SolidColorBrush(Color.FromRgb(255, 165, 0)));

    private static readonly SolidColorBrush CapBorderBr =
        Freeze(new SolidColorBrush(Color.FromRgb(255, 200, 100)));

    private static KeyBindingItem? _active;

    public static bool IsCapturing => _active is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyDisplay))]
    private Key _currentKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ButtonText),
        nameof(ButtonBackground),
        nameof(BorderBrush),
        nameof(BorderThickness))]
    private bool _capturing;

    public string ActionName { get; init; } = "";

    public string KeyDisplay =>
        CurrentKey == Key.None ? "None" : CurrentKey.ToString();

    public string ButtonText => Capturing ? "Cancel" : "Change";

    public Brush ButtonBackground =>
        Capturing ? CapBr : Brushes.Transparent;

    public Brush BorderBrush =>
        Capturing
            ? CapBorderBr
            : Application.Current.Resources["BorderBrush"] as Brush
              ?? Brushes.Gray;

    public Thickness BorderThickness => new(Capturing ? 2 : 1);

    public event EventHandler<Key>? KeyChanged;

    partial void OnCurrentKeyChanged(Key value) =>
        KeyChanged?.Invoke(this, value);

    [RelayCommand]
    private void ToggleCapture()
    {
        if (Capturing)
        {
            if (_active == this) _active = null;
            Capturing = false;
            return;
        }

        _active?.ToggleCapture();
        _active = this;
        Capturing = true;
    }

    public static void ProcessKey(Key k)
    {
        KeyBindingItem? a = _active;
        if (a == null) return;

        if (k == Key.Escape)
        {
            a.ToggleCapture();
            return;
        }

        if (Invalid.Contains(k)) return;

        a.CurrentKey = k;
        a.ToggleCapture();
    }

    private static T Freeze<T>(T b) where T : Freezable
    {
        if (b.CanFreeze) b.Freeze();
        return b;
    }
}

public partial class SettingsWindow : Window
{
    private readonly IThemes _themes = Theme.Instance;
    private readonly ISettingsService _cfg = SettingsService.Instance;
    private readonly IRendererFactory _rf = RendererFactory.Instance;

    private sealed record Meta(
        PropertyInfo P,
        TypeCode T,
        double Min,
        double Max,
        int Ord,
        string? Desc);

    private static class MetaCache<T> where T : class, new()
    {
        public static readonly IReadOnlyList<Meta> Items = Build();

        private static IReadOnlyList<Meta> Build()
        {
            var d = new T();
            var list = new List<Meta>();

            foreach (PropertyInfo p in typeof(T).GetProperties(
                BindingFlags.Instance | BindingFlags.Public))
            {
                TypeCode tc = Type.GetTypeCode(p.PropertyType);

                if (tc is not (TypeCode.Int32 or TypeCode.Single or TypeCode.Double))
                    continue;

                SliderAttribute? a = p.GetCustomAttribute<SliderAttribute>();
                if (a == null) continue;

                double def = Convert.ToDouble(p.GetValue(d));
                double min = double.IsNaN(a.Min) ? 0 : a.Min;
                double max = double.IsNaN(a.Max)
                    ? (def <= 0 ? 1 : def * a.AutoMaxMultiplier)
                    : a.Max;

                list.Add(new(p, tc, min, max, a.Order, a.Description));
            }

            return list
                .OrderBy(x => x.Ord)
                .ThenBy(x => x.P.Name)
                .ToList();
        }
    }

    public ObservableCollection<SliderSetting> ParticleSettings { get; } = [];
    public ObservableCollection<SliderSetting> RaindropSettings { get; } = [];
    public ObservableCollection<KeyBindingItem> KeyBindings { get; } = [];

    public SettingsWindow()
    {
        CommonResources.InitialiseResources();
        InitializeComponent();

        DataContext = this;
        Topmost = true;

        _themes.RegisterWindow(this);
        Rebuild();
    }

    [RelayCommand]
    private void Apply()
    {
        ApplyAll();

        MessageBox.Show(
            "Applied!",
            "Settings",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private void Reset()
    {
        if (MessageBox.Show(
            "Reset?",
            "Reset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _cfg.Reset();
        Rebuild();

        MessageBox.Show(
            "Reset.",
            "Reset",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private void CloseWindow() => Close();

    private void Rebuild()
    {
        AppSettingsConfig c = _cfg.Current;

        ParticleSettings.Clear();
        foreach (SliderSetting s in BuildS(c.Particles))
            ParticleSettings.Add(s);

        RaindropSettings.Clear();
        foreach (SliderSetting s in BuildS(c.Raindrops))
            RaindropSettings.Add(s);

        KeyBindings.Clear();
        foreach (KeyValuePair<string, Key> kv in c.KeyBindings.Bindings)
        {
            var i = new KeyBindingItem
            {
                ActionName = kv.Key,
                CurrentKey = kv.Value
            };

            i.KeyChanged += OnKC;
            KeyBindings.Add(i);
        }
    }

    private static IEnumerable<SliderSetting> BuildS<T>(T cur)
        where T : class, new()
    {
        foreach (Meta m in MetaCache<T>.Items)
        {
            double v = Clamp(
                Convert.ToDouble(m.P.GetValue(cur)),
                m.Min,
                m.Max);

            yield return new(m.P.Name, m.Desc, m.Min, m.Max, m.T, v);
        }
    }

    private void ApplyAll()
    {
        AppSettingsConfig c = _cfg.Current;

        ParticlesConfig np = Patch(c.Particles, ParticleSettings);
        RaindropsConfig nr = Patch(c.Raindrops, RaindropSettings);

        KeyBindingsConfig kb = KeyBindings.Aggregate(
            new KeyBindingsConfig(),
            (x, i) => x.SetKey(i.ActionName, i.CurrentKey));

        _cfg.Apply(c with
        {
            Particles = np,
            Raindrops = nr,
            KeyBindings = kb
        });

        _themes.SetTheme(_cfg.Current.General.IsDarkTheme);

        foreach (ISpectrumRenderer r in _rf.GetAllRenderers())
            r.Configure(
                r.IsOverlayActive,
                _cfg.Current.Visualization.SelectedRenderQuality);
    }

    private static T Patch<T>(T c, IEnumerable<SliderSetting> sl)
        where T : class
    {
        var jo = JObject.FromObject(c);

        foreach (SliderSetting s in sl)
        {
            double v = Clamp(s.Value, s.Min, s.Max);

            jo[s.Key] = s.TypeCode switch
            {
                TypeCode.Int32 => (int)Round(v),
                TypeCode.Single => (float)v,
                _ => v
            };
        }

        return jo.ToObject<T>() ?? c;
    }

    private void OnKC(object? sender, Key k)
    {
        if (sender is not KeyBindingItem i) return;

        KeyBindingItem? c = KeyBindings.FirstOrDefault(
            x => x.CurrentKey == k && x.ActionName != i.ActionName);

        if (c == null) return;

        if (MessageBox.Show(
            $"'{k}' used for '{c.ActionName}'. Reassign?",
            "Conflict",
            MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            c.CurrentKey = Key.None;
        else
            i.CurrentKey = Key.None;
    }

    private void OnWindowKeyDown(object s, KeyEventArgs e)
    {
        if (!KeyBindingItem.IsCapturing) return;

        KeyBindingItem.ProcessKey(
            e.Key == Key.System ? e.SystemKey : e.Key);

        e.Handled = true;
    }

    private void OnDrag(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _themes.UnregisterWindow(this);
    }
}
