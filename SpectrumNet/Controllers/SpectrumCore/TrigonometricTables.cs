#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

public static class TrigonometricTables
{
    private const string LOG_PREFIX = nameof(TrigonometricTables);
    private static readonly ConcurrentDictionary<int, (float[] Cos, float[] Sin)> _tables = new();

    public static (float[] Cos, float[] Sin) Get(int size)
    {
        if (size <= 0)
            throw new ArgumentException("Size must be positive", nameof(size));

        return _tables.GetOrAdd(size, CreateTrigTables);
    }

    private static (float[] Cos, float[] Sin) CreateTrigTables(int size) =>
        SafeResult(() =>
        {
            var cos = new float[size];
            var sin = new float[size];

            Parallel.For(0, size, i =>
            {
                float angle = Constants.TWO_PI * i / size;
                cos[i] = MathF.Cos(angle);
                sin[i] = MathF.Sin(angle);
            });

            return (cos, sin);
        },
        defaultValue: ([], []),
        options: new ErrorHandlingOptions
        {
            Source = LOG_PREFIX,
            ErrorMessage = "Error creating trig tables"
        });
}