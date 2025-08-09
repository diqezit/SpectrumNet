// SpectrumNet/Controllers/ViewCore/AnalyzerManager.cs
#nullable enable

using SpectrumNet.SN.Spectrum.Core;

namespace SpectrumNet.SN.Controllers.View;

public class AnalyzerManager : IAnalyzerManager, IDisposable
{
    private const string LogPrefix = nameof(AnalyzerManager);
    private readonly ISmartLogger _logger = Instance;
    private SpectrumAnalyzer? _analyzer;
    private bool _isDisposed;

    public SpectrumAnalyzer Analyzer
    {
        get => _analyzer ?? throw new InvalidOperationException("Analyzer not initialized");
        set => _analyzer = value ?? throw new ArgumentNullException(nameof(value));
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _analyzer = null;
    }
}