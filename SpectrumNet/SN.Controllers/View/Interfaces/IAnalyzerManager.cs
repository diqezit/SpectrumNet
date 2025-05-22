// SpectrumNet/Controllers/Interfaces/ViewCore/IAnalyzerManager.cs
#nullable enable

namespace SpectrumNet.SN.Controllers.View.Interfaces;

public interface IAnalyzerManager
{
    SpectrumAnalyzer Analyzer { get; set; }
}