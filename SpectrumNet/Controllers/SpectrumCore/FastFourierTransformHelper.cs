#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

public static class FastFourierTransformHelper
{
    public static void PerformFFT(Complex[] buffer, int size)
    {
        if (!BitOperations.IsPow2(size))
            throw new ArgumentException("FFT size must be a power of 2", nameof(size));

        FastFourierTransform.FFT(true, (int)Log2(size), buffer);
    }

    public static float Magnitude(Complex c) => c.X * c.X + c.Y * c.Y;

    public static void ApplyWindowInPlaceVectorized(
        Complex[] buffer,
        ReadOnlyMemory<float> data,
        float[] window,
        int offset,
        int vecSize)
    {
        Span<float> temp = stackalloc float[vecSize];
        int length = data.Length;
        int vecEnd = length - length % vecSize;

        for (int i = 0; i < vecEnd; i += vecSize)
        {
            data.Span.Slice(i, vecSize).CopyTo(temp);
            Vector<float> s = new(temp);
            Vector<float> w = new(window, offset + i);
            (s * w).CopyTo(temp);

            for (int j = 0; j < vecSize; j++)
                buffer[offset + i + j] = new Complex { X = temp[j] };
        }

        for (int i = vecEnd; i < length; i++)
            buffer[offset + i] = new Complex { X = data.Span[i] * window[offset + i] };
    }
}