#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

public static class Constants
{
    public const float DefaultAmplificationFactor = 0.5f,
        DefaultMaxDbValue = 0f,
        DefaultMinDbValue = -130f,
        Epsilon = float.Epsilon,
        TwoPi = 2f * MathF.PI,
        KaiserBeta = 5f,
        BesselEpsilon = 1e-10f,
        InvLog10 = 0.43429448190325182765f;
    public const int DefaultFftSize = 2048,
        DefaultChannelCapacity = 10;
}
