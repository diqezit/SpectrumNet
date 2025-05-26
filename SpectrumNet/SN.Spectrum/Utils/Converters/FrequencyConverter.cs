#nullable enable

namespace SpectrumNet.SN.Spectrum.Utils.Converters;

public static class FrequencyConverter
{
    // Преобразование в шкалу Mel
    public static float FreqToMel(float freq) =>
        2595f * MathF.Log10(1 + freq / 700f);

    public static float MelToFreq(float mel) =>
        700f * (MathF.Pow(10, mel / 2595f) - 1);

    // Преобразование в шкалу Bark
    public static float FreqToBark(float freq) =>
        13f * MathF.Atan(0.00076f * freq) +
        3.5f * MathF.Atan(MathF.Pow(freq / 7500f, 2));

    public static float BarkToFreq(float bark) =>
        1960f * (bark + 0.53f) / (26.28f - bark);

    // Преобразование в шкалу ERB
    public static float FreqToERB(float freq) =>
        21.4f * MathF.Log10(0.00437f * freq + 1);

    public static float ERBToFreq(float erb) =>
        (MathF.Pow(10, erb / 21.4f) - 1) / 0.00437f;
}