#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

public sealed class GainParameters : IGainParametersProvider, INotifyPropertyChanged
{
    private const string LogSource = nameof(GainParameters);
    private float _amp = Constants.DefaultAmplificationFactor,
        _min = Constants.DefaultMinDbValue,
        _max = Constants.DefaultMaxDbValue;
    private readonly SynchronizationContext? _context;
    public event PropertyChangedEventHandler? PropertyChanged;

    public GainParameters(
        SynchronizationContext? context = null,
        float minDbValue = Constants.DefaultMinDbValue,
        float maxDbValue = Constants.DefaultMaxDbValue,
        float amplificationFactor = Constants.DefaultAmplificationFactor
    )
    {
        if (minDbValue > maxDbValue)
            throw new ArgumentException("MinDbValue cannot be greater than MaxDbValue.");
        (_context, _min, _max, _amp) =
            (context, minDbValue, maxDbValue, amplificationFactor);
    }

    public float AmplificationFactor
    {
        get => _amp;
        set => UpdateProperty(ref _amp, Max(0.1f, value));
    }

    public float MaxDbValue
    {
        get => _max;
        set => UpdateProperty(ref _max, value < _min ? _min : value);
    }

    public float MinDbValue
    {
        get => _min;
        set => UpdateProperty(ref _min, value > _max ? _max : value);
    }

    private void UpdateProperty(ref float field, float value,
        [CallerMemberName] string? propertyName = null)
    {
        if (Abs(field - value) <= Constants.Epsilon)
            return;
        field = value;
        SafeExecute(() =>
        {
            if (_context != null)
                _context.Post(_ =>
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)),
                    null);
            else
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        },
        new ErrorHandlingOptions
        {
            Source = LogSource,
            ErrorMessage = $"Error notifying property change: {propertyName}"
        });
    }
}
