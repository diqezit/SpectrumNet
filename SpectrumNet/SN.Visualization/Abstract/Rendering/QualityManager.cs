// SN.Visualization/Abstract/Rendering/QualityManager.cs
namespace SpectrumNet.SN.Visualization.Abstract.Rendering;

// Отвечает за настройки качества
public interface IQualityManager
{
    RenderQuality Quality { get; set; }
    bool UseAntiAlias { get; }
    bool UseAdvancedEffects { get; }
    SKSamplingOptions SamplingOptions { get; }
    void ApplyQuality(RenderQuality quality);
}

public class QualityManager : IQualityManager
{
    private RenderQuality _quality = RenderQuality.Medium;
    private bool _useAntiAlias = true;
    private bool _useAdvancedEffects = true;
    private SKSamplingOptions _samplingOptions = CreateDefaultSamplingOptions();

    public RenderQuality Quality
    {
        get => _quality;
        set => ApplyQualityIfChanged(value);
    }

    public bool UseAntiAlias => _useAntiAlias;
    public bool UseAdvancedEffects => _useAdvancedEffects;
    public SKSamplingOptions SamplingOptions => _samplingOptions;

    public void ApplyQuality(RenderQuality quality)
    {
        Quality = quality;
    }

    private static SKSamplingOptions CreateDefaultSamplingOptions() =>
        new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private void ApplyQualityIfChanged(RenderQuality newQuality)
    {
        if (HasQualityChanged(newQuality))
            UpdateQualitySettings(newQuality);
    }

    private bool HasQualityChanged(RenderQuality newQuality) =>
        _quality != newQuality;

    private void UpdateQualitySettings(RenderQuality newQuality)
    {
        SetQuality(newQuality);
        ApplyQualitySpecificSettings();
    }

    private void SetQuality(RenderQuality quality) =>
        _quality = quality;

    private void ApplyQualitySpecificSettings()
    {
        UpdateRenderingFlags();
        UpdateSamplingOptions();
    }

    private void UpdateRenderingFlags()
    {
        var (antiAlias, advancedEffects) = DetermineRenderingFlags(_quality);
        SetRenderingFlags(antiAlias, advancedEffects);
    }

    private static (bool antiAlias, bool advancedEffects) DetermineRenderingFlags(RenderQuality quality) =>
        quality switch
        {
            RenderQuality.Low => (false, false),
            RenderQuality.Medium => (true, true),
            RenderQuality.High => (true, true),
            _ => (true, true)
        };

    private void SetRenderingFlags(bool antiAlias, bool advancedEffects)
    {
        SetAntiAliasFlag(antiAlias);
        SetAdvancedEffectsFlag(advancedEffects);
    }

    private void SetAntiAliasFlag(bool value) =>
        _useAntiAlias = value;

    private void SetAdvancedEffectsFlag(bool value) =>
        _useAdvancedEffects = value;

    private void UpdateSamplingOptions() =>
        _samplingOptions = CreateSamplingOptionsForQuality(_quality);

    private static SKSamplingOptions CreateSamplingOptionsForQuality(RenderQuality quality) =>
        quality switch
        {
            RenderQuality.Low => CreateLowQualitySamplingOptions(),
            RenderQuality.Medium => CreateMediumQualitySamplingOptions(),
            RenderQuality.High => CreateHighQualitySamplingOptions(),
            _ => CreateMediumQualitySamplingOptions()
        };

    private static SKSamplingOptions CreateLowQualitySamplingOptions() =>
        new(SKFilterMode.Nearest, SKMipmapMode.None);

    private static SKSamplingOptions CreateMediumQualitySamplingOptions() =>
        new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private static SKSamplingOptions CreateHighQualitySamplingOptions() =>
        new(SKFilterMode.Linear, SKMipmapMode.Linear);
}