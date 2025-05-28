#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Core;

public abstract class AnimationSpectrumRenderer : BaseSpectrumRenderer
{
    private readonly IAnimationTimer _animationTimer;
    private readonly IValueAnimatorArray _valueAnimator;

    protected AnimationSpectrumRenderer(
        ISpectrumProcessingCoordinator? processingCoordinator = null,
        IQualityManager? qualityManager = null,
        IOverlayStateManager? overlayStateManager = null,
        IRenderingHelpers? renderingHelpers = null,
        IBufferManager? bufferManager = null,
        ISpectrumBandProcessor? bandProcessor = null,
        IAnimationTimer? animationTimer = null) : base(
            processingCoordinator,
            qualityManager,
            overlayStateManager,
            renderingHelpers,
            bufferManager,
            bandProcessor)
    {
        _animationTimer = animationTimer ?? new AnimationTimer();
        _valueAnimator = new ValueAnimatorArray();
    }

    protected override float GetAnimationTime() => _animationTimer.Time;
    protected override float GetAnimationDeltaTime() => _animationTimer.DeltaTime;
    protected void UpdateAnimation() => _animationTimer.Update();
    protected void ResetAnimation() => _animationTimer.Reset();

    protected void AnimateValues(float[] targets, float speed)
    {
        _valueAnimator.EnsureSize(targets.Length);
        _valueAnimator.Update(targets, speed);
    }

    protected float[] GetAnimatedValues() => _valueAnimator.Values;

    protected float AnimateValue(
        float current,
        float target,
        float speed) =>
        Lerp(current, target, speed * GetAnimationDeltaTime());

    protected void AnimateToTarget(
        ref float current,
        float target,
        float speed) =>
        current = AnimateValue(current, target, speed);

    protected float GetOscillation(
        float frequency,
        float amplitude = 1f,
        float phase = 0f) =>
        amplitude * (float)Sin(GetAnimationTime() * frequency + phase);

    protected float GetPulse(
        float frequency,
        float min = 0f,
        float max = 1f) =>
        Lerp(min, max, (float)(Sin(GetAnimationTime() * frequency) * 0.5 + 0.5));

    protected override void OnDispose()
    {
        _animationTimer?.Dispose();
        base.OnDispose();
    }
}