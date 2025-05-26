// SpectrumNet/Controllers/Interfaces/ViewCore/IAnalyzerManager.cs
#nullable enable

using SpectrumNet.SN.Spectrum.Core;

namespace SpectrumNet.SN.Controllers.View.Interfaces;

public interface IAnalyzerManager
{
    SpectrumAnalyzer Analyzer { get; set; }
}