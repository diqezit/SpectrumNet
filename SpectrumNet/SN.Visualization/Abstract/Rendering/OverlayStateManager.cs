// SN.Visualization/Abstract/Rendering/OverlayStateManager.cs
namespace SpectrumNet.SN.Visualization.Abstract.Rendering;

// Отвечает за состояние оверлея
public interface IOverlayStateManager
{
    bool IsOverlayActive { get; }
    float OverlayAlphaFactor { get; }
    bool StateChanged { get; }
    bool StateChangeRequested { get; }

    void SetOverlayActive(bool isActive);
    void SetOverlayTransparency(float level);
    void ResetStateFlags();
}

public class OverlayStateManager : IOverlayStateManager
{
    private const float 
        DEFAULT_OVERLAY_ALPHA_FACTOR = 0.3f,
        HOVER_OVERLAY_ALPHA_FACTOR = 0.8f,
        MIN_ALPHA_FACTOR = 0.0f,
        MAX_ALPHA_FACTOR = 1.0f,
        EPSILON = 0.0001f;

    private bool _isOverlayActive;
    private float _overlayAlphaFactor = DEFAULT_OVERLAY_ALPHA_FACTOR;
    private bool _overlayStateChanged;
    private bool _overlayStateChangeRequested;

    public bool IsOverlayActive => _isOverlayActive;
    public float OverlayAlphaFactor => _overlayAlphaFactor;
    public bool StateChanged => _overlayStateChanged;
    public bool StateChangeRequested => _overlayStateChangeRequested;

    public void SetOverlayActive(bool isActive)
    {
        if (HasOverlayStateChanged(isActive))
            ApplyOverlayStateChange(isActive);
    }

    public void SetOverlayTransparency(float level)
    {
        float clampedLevel = ClampAlphaFactor(level);

        if (HasTransparencyChanged(clampedLevel))
            ApplyTransparencyChange(clampedLevel);
    }

    public void ResetStateFlags()
    {
        ClearStateChangedFlag();
        ClearStateChangeRequestedFlag();
    }

    private bool HasOverlayStateChanged(bool newState) =>
        _isOverlayActive != newState;

    private void ApplyOverlayStateChange(bool isActive)
    {
        UpdateOverlayActiveState(isActive);
        UpdateAlphaFactorForState(isActive);
        MarkStateAsChanged();
    }

    private void UpdateOverlayActiveState(bool isActive) =>
        _isOverlayActive = isActive;

    private void UpdateAlphaFactorForState(bool isActive) =>
        _overlayAlphaFactor = DetermineAlphaFactorForState(isActive);

    private static float DetermineAlphaFactorForState(bool isActive) =>
        isActive ? HOVER_OVERLAY_ALPHA_FACTOR : DEFAULT_OVERLAY_ALPHA_FACTOR;

    private static float ClampAlphaFactor(float value) =>
        Math.Clamp(value, MIN_ALPHA_FACTOR, MAX_ALPHA_FACTOR);

    private bool HasTransparencyChanged(float newLevel) =>
        MathF.Abs(_overlayAlphaFactor - newLevel) > EPSILON;

    private void ApplyTransparencyChange(float level)
    {
        UpdateAlphaFactor(level);
        MarkStateAsChanged();
    }

    private void UpdateAlphaFactor(float level) =>
        _overlayAlphaFactor = level;

    private void MarkStateAsChanged()
    {
        SetStateChangedFlag();
        SetStateChangeRequestedFlag();
    }

    private void SetStateChangedFlag() =>
        _overlayStateChanged = true;

    private void SetStateChangeRequestedFlag() =>
        _overlayStateChangeRequested = true;

    private void ClearStateChangedFlag() =>
        _overlayStateChanged = false;

    private void ClearStateChangeRequestedFlag() =>
        _overlayStateChangeRequested = false;
}