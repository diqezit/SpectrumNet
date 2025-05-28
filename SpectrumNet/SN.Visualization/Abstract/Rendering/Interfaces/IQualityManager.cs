// SN.Visualization/Abstract/Rendering/IQualityManager.cs
namespace SpectrumNet.SN.Visualization.Abstract.Rendering.Interfaces;

// Отвечает за настройки качества
public interface IQualityManager
{
    RenderQuality Quality { get; set; }
    bool UseAntiAlias { get; }
    bool UseAdvancedEffects { get; }
    SKSamplingOptions SamplingOptions { get; }
    void ApplyQuality(RenderQuality quality);
}
