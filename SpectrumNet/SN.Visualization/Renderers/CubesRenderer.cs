#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CubesRenderer : EffectSpectrumRenderer<CubesRenderer.QualitySettings>
{
    private static readonly Lazy<CubesRenderer> _instance =
        new(() => new CubesRenderer());

    public static CubesRenderer GetInstance() => _instance.Value;

    public sealed class QualitySettings
    {
        public bool UseTopFace { get; init; }
        public bool UseSideFace { get; init; }
        public float TopWidthRatio { get; init; }
        public float TopHeightRatio { get; init; }
        public float TopFaceBrightness { get; init; }
        public float SideFaceBrightness { get; init; }
        public bool UseShadow { get; init; }
        public float ShadowOffset { get; init; }
        public byte ShadowAlpha { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseTopFace = false,
            UseSideFace = false,
            TopWidthRatio = 0.7f,
            TopHeightRatio = 0.2f,
            TopFaceBrightness = 1.0f,
            SideFaceBrightness = 0.7f,
            UseShadow = false,
            ShadowOffset = 0f,
            ShadowAlpha = 0
        },
        [RenderQuality.Medium] = new()
        {
            UseTopFace = true,
            UseSideFace = true,
            TopWidthRatio = 0.75f,
            TopHeightRatio = 0.25f,
            TopFaceBrightness = 1.2f,
            SideFaceBrightness = 0.6f,
            UseShadow = true,
            ShadowOffset = 2f,
            ShadowAlpha = 20
        },
        [RenderQuality.High] = new()
        {
            UseTopFace = true,
            UseSideFace = true,
            TopWidthRatio = 0.8f,
            TopHeightRatio = 0.3f,
            TopFaceBrightness = 1.3f,
            SideFaceBrightness = 0.5f,
            UseShadow = true,
            ShadowOffset = 3f,
            ShadowAlpha = 30
        }
    };

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams,
        SKPaint passedInPaint)
    {
        if (CurrentQualitySettings == null || renderParams.EffectiveBarCount <= 0)
            return;

        var (isValid, processedSpectrum) = ProcessSpectrum(
            spectrum,
            renderParams.EffectiveBarCount,
            applyTemporalSmoothing: true);

        if (!isValid || processedSpectrum == null)
            return;

        var cubes = CalculateCubeGeometry(
            processedSpectrum,
            info,
            renderParams);

        if (cubes.Count == 0)
            return;

        RenderCubeVisualization(
            canvas,
            cubes,
            renderParams,
            passedInPaint);
    }

    private static List<CubeData> CalculateCubeGeometry(
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        var cubes = new List<CubeData>(renderParams.EffectiveBarCount);
        float xPosition = renderParams.StartOffset;
        const float MIN_MAGNITUDE = 0.01f;

        for (int i = 0; i < renderParams.EffectiveBarCount && i < spectrum.Length; i++)
        {
            float magnitude = Math.Max(spectrum[i], 0f);

            if (magnitude >= MIN_MAGNITUDE)
            {
                var cube = CreateCubeData(
                    xPosition,
                    magnitude,
                    renderParams.BarWidth,
                    info.Height);

                cubes.Add(cube);
            }

            xPosition += renderParams.BarWidth + renderParams.BarSpacing;
        }

        return cubes;
    }

    private static CubeData CreateCubeData(
        float x,
        float magnitude,
        float width,
        float canvasHeight)
    {
        float height = magnitude * canvasHeight * 0.8f;
        float y = canvasHeight - height;

        return new CubeData(x, y, width, height, magnitude);
    }

    private void RenderCubeVisualization(
        SKCanvas canvas,
        List<CubeData> cubes,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            if (UseAdvancedEffects && settings.UseShadow)
                RenderShadowLayer(canvas, cubes, settings);

            RenderFrontFaces(canvas, cubes, basePaint);

            if (UseAdvancedEffects && settings.UseSideFace)
                RenderSideFaces(canvas, cubes, basePaint, settings);

            if (UseAdvancedEffects && settings.UseTopFace)
                RenderTopFaces(canvas, cubes, basePaint, settings);
        });
    }

    private void RenderShadowLayer(
        SKCanvas canvas,
        List<CubeData> cubes,
        QualitySettings settings)
    {
        if (settings.ShadowAlpha == 0) return;

        var shadowPaint = CreatePaint(
            new SKColor(0, 0, 0, settings.ShadowAlpha),
            SKPaintStyle.Fill);

        try
        {
            var shadowRects = cubes
                .Select(cube => CreateShadowRect(cube, settings.ShadowOffset))
                .ToList();

            RenderRects(canvas, shadowRects, shadowPaint);
        }
        finally
        {
            ReturnPaint(shadowPaint);
        }
    }

    private void RenderFrontFaces(
        SKCanvas canvas,
        List<CubeData> cubes,
        SKPaint basePaint)
    {
        var frontPaint = CreatePaint(basePaint.Color, SKPaintStyle.Fill);

        try
        {
            foreach (var cube in cubes)
            {
                byte alpha = CalculateAlpha(cube.Magnitude);
                frontPaint.Color = basePaint.Color.WithAlpha(alpha);

                var frontRect = new SKRect(
                    cube.X,
                    cube.Y,
                    cube.X + cube.Width,
                    cube.Y + cube.Height);

                canvas.DrawRect(frontRect, frontPaint);
            }
        }
        finally
        {
            ReturnPaint(frontPaint);
        }
    }

    private void RenderSideFaces(
        SKCanvas canvas,
        List<CubeData> cubes,
        SKPaint basePaint,
        QualitySettings settings)
    {
        var sideColor = AdjustBrightness(
            basePaint.Color,
            settings.SideFaceBrightness);

        var sidePaint = CreatePaint(sideColor, SKPaintStyle.Fill);

        try
        {
            RenderPath(canvas, path =>
            {
                foreach (var cube in cubes)
                {
                    AddSideFacePath(path, cube, settings);
                }
            }, sidePaint);
        }
        finally
        {
            ReturnPaint(sidePaint);
        }
    }

    private void RenderTopFaces(
        SKCanvas canvas,
        List<CubeData> cubes,
        SKPaint basePaint,
        QualitySettings settings)
    {
        var topColor = AdjustBrightness(
            basePaint.Color,
            settings.TopFaceBrightness);

        var topPaint = CreatePaint(topColor, SKPaintStyle.Fill);

        try
        {
            RenderPath(canvas, path =>
            {
                foreach (var cube in cubes)
                {
                    AddTopFacePath(path, cube, settings);
                }
            }, topPaint);
        }
        finally
        {
            ReturnPaint(topPaint);
        }
    }

    private static void AddSideFacePath(
        SKPath path,
        CubeData cube,
        QualitySettings settings)
    {
        float topOffset = cube.Width * settings.TopWidthRatio;
        float topHeight = cube.Width * settings.TopHeightRatio;

        path.MoveTo(cube.X + cube.Width, cube.Y);
        path.LineTo(cube.X + cube.Width, cube.Y + cube.Height);
        path.LineTo(cube.X + topOffset, cube.Y - topHeight + cube.Height);
        path.LineTo(cube.X + topOffset, cube.Y - topHeight);
        path.Close();
    }

    private static void AddTopFacePath(
        SKPath path,
        CubeData cube,
        QualitySettings settings)
    {
        float topOffset = cube.Width * settings.TopWidthRatio;
        float topHeight = cube.Width * settings.TopHeightRatio;
        float backOffset = cube.Width - topOffset;

        path.MoveTo(cube.X, cube.Y);
        path.LineTo(cube.X + cube.Width, cube.Y);
        path.LineTo(cube.X + topOffset, cube.Y - topHeight);
        path.LineTo(cube.X - backOffset, cube.Y - topHeight);
        path.Close();
    }

    private static SKRect CreateShadowRect(CubeData cube, float offset) =>
        new(
            cube.X + offset,
            cube.Y + offset,
            cube.X + cube.Width + offset,
            cube.Y + cube.Height);

    private static SKColor AdjustBrightness(SKColor color, float factor)
    {
        byte r = (byte)Math.Min(255, color.Red * factor);
        byte g = (byte)Math.Min(255, color.Green * factor);
        byte b = (byte)Math.Min(255, color.Blue * factor);

        return new SKColor(r, g, b, color.Alpha);
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 80,
        RenderQuality.Medium => 150,
        RenderQuality.High => 250,
        _ => 150
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.35f,
            RenderQuality.Medium => 0.25f,
            RenderQuality.High => 0.2f,
            _ => 0.25f
        };

        SetProcessingSmoothingFactor(smoothingFactor);
        RequestRedraw();
    }

    private record CubeData(
        float X,
        float Y,
        float Width,
        float Height,
        float Magnitude);
}