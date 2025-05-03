#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

public static class Constants
{
    // Общие константы
    public const float
        DEFAULT_AMPLIFICATION_FACTOR = 0.5f,
        DEFAULT_MAX_DB_VALUE = 0f,
        DEFAULT_MIN_DB_VALUE = -130f,
        EPSILON = float.Epsilon,
        TWO_PI = 2f * MathF.PI,
        KAISER_BETA = 5f,
        BESSEL_EPSILON = 1e-10f,
        INV_LOG10 = 0.43429448190325182765f;

    public const int
        DEFAULT_FFT_SIZE = 2048,
        DEFAULT_CHANNEL_CAPACITY = 10;

    // Константы для параллельной обработки
    public const int
        MIN_PARALLEL_SIZE = 100,
        BATCH_SIZE = 1024;

    // Константы для FFT обработки
    public const int
        MAX_RETRY_COUNT = 3,
        RETRY_DELAY_MS = 100;
}