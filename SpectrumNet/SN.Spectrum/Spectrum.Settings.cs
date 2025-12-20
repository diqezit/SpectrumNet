namespace SpectrumNet.SN.Spectrum;

public sealed class GainParameters(SynchronizationContext? ctx = null)
    : IGainParametersProvider, INotifyPropertyChanged
{
    private float _amp = 0.5f, _min = -130f, _max = 0f;

    public event PropertyChangedEventHandler? PropertyChanged;

    public float AmplificationFactor
    {
        get => _amp;
        set => Set(ref _amp, Max(0.1f, value));
    }

    public float MaxDbValue
    {
        get => _max;
        set => Set(ref _max, Max(value, _min));
    }

    public float MinDbValue
    {
        get => _min;
        set => Set(ref _min, Min(value, _max));
    }

    private void Set(ref float field, float val, [CallerMemberName] string? name = null)
    {
        if (Abs(field - val) <= float.Epsilon) return;
        field = val;
        var args = new PropertyChangedEventArgs(name);
        if (ctx != null && SynchronizationContext.Current != ctx)
            ctx.Post(_ => PropertyChanged?.Invoke(this, args), null);
        else
            PropertyChanged?.Invoke(this, args);
    }
}
