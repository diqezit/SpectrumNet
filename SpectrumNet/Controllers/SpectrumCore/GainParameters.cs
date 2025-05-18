#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

public sealed class GainParameters : IGainParametersProvider, INotifyPropertyChanged
{
    private const string LOG_SOURCE = nameof(GainParameters);

    private float _amplificationFactor = DEFAULT_AMPLIFICATION_FACTOR;
    private float _minDbValue = DEFAULT_MIN_DB_VALUE;
    private float _maxDbValue = DEFAULT_MAX_DB_VALUE;
    private readonly SynchronizationContext? _context;

    public event PropertyChangedEventHandler? PropertyChanged;

    public GainParameters(
        SynchronizationContext? context = null,
        float minDbValue = DEFAULT_MIN_DB_VALUE,
        float maxDbValue = DEFAULT_MAX_DB_VALUE,
        float amplificationFactor = DEFAULT_AMPLIFICATION_FACTOR)
    {
        if (minDbValue > maxDbValue)
            throw new ArgumentException("MinDbValue cannot be greater than MaxDbValue.");

        _context = context;
        _minDbValue = minDbValue;
        _maxDbValue = maxDbValue;
        _amplificationFactor = amplificationFactor;
    }

    public float AmplificationFactor
    {
        get => _amplificationFactor;
        set => UpdateProperty(ref _amplificationFactor, Math.Max(0.1f, value));
    }

    public float MaxDbValue
    {
        get => _maxDbValue;
        set => UpdateProperty(ref _maxDbValue, value < _minDbValue ? _minDbValue : value);
    }

    public float MinDbValue
    {
        get => _minDbValue;
        set => UpdateProperty(ref _minDbValue, value > _maxDbValue ? _maxDbValue : value);
    }

    private void UpdateProperty(
        ref float field,
        float value,
        [CallerMemberName] string? propertyName = null)
    {
        if (Abs(field - value) <= EPSILON)
            return;

        field = value;
        NotifyPropertyChanged(propertyName);
    }

    private void NotifyPropertyChanged(string? propertyName)
    {
        Safe(() =>
        {
            if (_context != null)
                _context.Post(_ => PropertyChanged?.Invoke(
                    this, new PropertyChangedEventArgs(propertyName)), null);
            else
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        },
         LOG_SOURCE,
         $"Error notifying property change: {propertyName}");
    }
}