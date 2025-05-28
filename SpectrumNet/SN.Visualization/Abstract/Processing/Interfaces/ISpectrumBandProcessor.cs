#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing.Interfaces;

public interface ISpectrumBandProcessor
{
    float[] ProcessBands(float[] spectrum, int bandCount);
    float GetBandAverage(float[] spectrum, int start, int end);
}
