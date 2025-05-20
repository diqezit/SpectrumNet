// SpectrumNet/Controllers/Interfaces/ViewCore/IAnalyzerManager.cs
#nullable enable

namespace SpectrumNet.Controllers.Interfaces.ViewCore;

public interface IAnalyzerManager
{
    SpectrumAnalyzer Analyzer { get; set; }
}