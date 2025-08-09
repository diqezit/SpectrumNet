#nullable enable

namespace SpectrumNet.SN.Spectrum.Models;

public record FftEventArgs(Complex[] Result, int SampleRate);
public record SpectralDataEventArgs(SpectralData Data);
public record SpectralData(float[] Spectrum, DateTime Timestamp);