#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CubeRenderer : EffectSpectrumRenderer<CubeRenderer.QualitySettings>
{
    private static readonly Lazy<CubeRenderer> _instance =
        new(() => new CubeRenderer());

    public static CubeRenderer GetInstance() => _instance.Value;

    private const float
        CUBE_SIZE = 0.5f,
        BASE_ROTATION = 0.02f,
        ROTATION_INFLUENCE = 0.015f,
        AMBIENT_LIGHT = 0.4f,
        DIFFUSE_LIGHT = 0.6f,
        BASE_ALPHA = 0.9f,
        BAR_COUNT_SCALE_FACTOR = 0.01f,
        BAR_WIDTH_SCALE_FACTOR = 0.05f,
        MIN_SCALE = 0.5f,
        MAX_SCALE = 2.5f,
        INTENSITY_SCALE_FACTOR = 0.4f,
        EDGE_ALPHA_MULTIPLIER = 0.8f,
        LIGHTNESS_VARIATION = 0.1f,
        MIN_LIGHTNESS = 0.2f,
        MAX_LIGHTNESS = 0.8f,
        HUE_SHIFT_PER_FACE = 60f;

    private const float
        EDGE_WIDTH = 2f,
        EDGE_WIDTH_OVERLAY = 1.5f,
        SCALE_FACTOR = 0.3f,
        SCALE_FACTOR_OVERLAY = 0.25f,
        BLUR_FACTOR = 1f,
        BLUR_FACTOR_OVERLAY = 0.7f;

    private static readonly Vector3 LIGHT_DIRECTION =
        Vector3.Normalize(new(0.5f, 0.7f, -1f));

    private static readonly Vertex[] _vertices = CreateVertices();
    private static readonly Face[] _faces = CreateFaces();

    private float _rotationX;
    private float _rotationY;
    private float _rotationZ;
    private float _currentIntensity;

    public sealed class QualitySettings
    {
        public bool UseGlow { get; init; }
        public bool UseEdgeHighlight { get; init; }
        public byte EdgeBlur { get; init; }
        public float EdgeWidth { get; init; }
        public float RotationSpeed { get; init; }
        public float ResponseFactor { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseGlow = false,
            UseEdgeHighlight = false,
            EdgeBlur = 0,
            EdgeWidth = 1f,
            RotationSpeed = 0.8f,
            ResponseFactor = 0.2f
        },
        [RenderQuality.Medium] = new()
        {
            UseGlow = true,
            UseEdgeHighlight = true,
            EdgeBlur = 2,
            EdgeWidth = 1.5f,
            RotationSpeed = 1f,
            ResponseFactor = 0.3f
        },
        [RenderQuality.High] = new()
        {
            UseGlow = true,
            UseEdgeHighlight = true,
            EdgeBlur = 4,
            EdgeWidth = 2f,
            RotationSpeed = 1.2f,
            ResponseFactor = 0.4f
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
            Math.Min(renderParams.EffectiveBarCount, 3),
            applyTemporalSmoothing: true);

        if (!isValid || processedSpectrum == null)
            return;

        var cubeData = CalculateCubeData(
            processedSpectrum,
            info,
            renderParams.EffectiveBarCount,
            passedInPaint.Color);

        if (!ValidateCubeData(cubeData))
            return;

        var boundingBox = CalculateBoundingBox(cubeData.ProjectedVertices);
        if (!IsAreaVisible(canvas, boundingBox))
            return;

        RenderCubeVisualization(canvas, cubeData);
    }

    private CubeRenderData CalculateCubeData(
        float[] spectrum,
        SKImageInfo info,
        int barCount,
        SKColor baseColor)
    {
        var settings = CurrentQualitySettings!;

        float avgIntensity = CalculateAverageIntensity(spectrum);
        _currentIntensity = Lerp(_currentIntensity, avgIntensity, settings.ResponseFactor);

        UpdateRotationAngles(spectrum, settings.RotationSpeed);

        var center = new SKPoint(info.Width * 0.5f, info.Height * 0.5f);
        float scale = CalculateScale(info, barCount, _currentIntensity);

        var rotation = CalculateRotationMatrix();
        var projected = ProjectVertices(rotation, scale, center);
        var facesWithDepth = CalculateFaceDepths(rotation);

        return new CubeRenderData(
            ProjectedVertices: projected,
            FacesWithDepth: facesWithDepth,
            BaseColor: baseColor,
            Intensity: _currentIntensity,
            Scale: scale);
    }

    private static bool ValidateCubeData(CubeRenderData data) =>
        data.ProjectedVertices.Length > 0 &&
        data.FacesWithDepth.Length > 0 &&
        data.Scale > 0;

    private static SKRect CalculateBoundingBox(SKPoint[] points)
    {
        if (points.Length == 0)
            return SKRect.Empty;

        float minX = points[0].X;
        float minY = points[0].Y;
        float maxX = points[0].X;
        float maxY = points[0].Y;

        for (int i = 1; i < points.Length; i++)
        {
            minX = Math.Min(minX, points[i].X);
            minY = Math.Min(minY, points[i].Y);
            maxX = Math.Max(maxX, points[i].X);
            maxY = Math.Max(maxY, points[i].Y);
        }

        return new SKRect(minX, minY, maxX, maxY);
    }

    private void UpdateRotationAngles(float[] spectrum, float speedMultiplier)
    {
        float speedX = BASE_ROTATION + (spectrum.Length > 0 ? spectrum[0] : 0f) * ROTATION_INFLUENCE;
        float speedY = BASE_ROTATION + (spectrum.Length > 1 ? spectrum[1] : 0f) * ROTATION_INFLUENCE;
        float speedZ = BASE_ROTATION + (spectrum.Length > 2 ? spectrum[2] : 0f) * ROTATION_INFLUENCE;

        float deltaTime = 0.016f;
        _rotationX = (_rotationX + speedX * speedMultiplier * deltaTime) % MathF.Tau;
        _rotationY = (_rotationY + speedY * speedMultiplier * deltaTime) % MathF.Tau;
        _rotationZ = (_rotationZ + speedZ * speedMultiplier * deltaTime) % MathF.Tau;
    }

    private Matrix4x4 CalculateRotationMatrix() =>
        Matrix4x4.CreateRotationX(_rotationX) *
        Matrix4x4.CreateRotationY(_rotationY) *
        Matrix4x4.CreateRotationZ(_rotationZ);

    private static SKPoint[] ProjectVertices(
        Matrix4x4 rotation,
        float scale,
        SKPoint center)
    {
        var projected = new SKPoint[_vertices.Length];

        for (int i = 0; i < _vertices.Length; i++)
        {
            var v = _vertices[i];
            var rotated = Vector3.Transform(
                new Vector3(v.X, v.Y, v.Z),
                rotation);

            projected[i] = new SKPoint(
                rotated.X * scale + center.X,
                rotated.Y * scale + center.Y);
        }

        return projected;
    }

    private static (Face face, float depth, float light)[] CalculateFaceDepths(
        Matrix4x4 rotation)
    {
        var result = new (Face face, float depth, float light)[_faces.Length];

        for (int i = 0; i < _faces.Length; i++)
        {
            var face = _faces[i];
            var normal = GetFaceNormal(face.ColorIndex);
            var rotatedNormal = Vector3.TransformNormal(normal, rotation);

            float depth = rotatedNormal.Z;
            float light = CalculateFaceLight(rotatedNormal);

            result[i] = (face, depth, light);
        }

        return [.. result.OrderBy(f => f.depth)];
    }

    private static float CalculateFaceLight(Vector3 normal) =>
        AMBIENT_LIGHT +
        DIFFUSE_LIGHT * Math.Max(0, Vector3.Dot(
            Vector3.Normalize(normal),
            LIGHT_DIRECTION));

    private void RenderCubeVisualization(
        SKCanvas canvas,
        CubeRenderData data)
    {
        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            RenderCubeFaces(canvas, data, settings);
        });
    }

    private void RenderCubeFaces(
        SKCanvas canvas,
        CubeRenderData data,
        QualitySettings settings)
    {
        var facePaint = CreatePaint(SKColors.Black, SKPaintStyle.Fill);
        var edgePaint = CreatePaint(SKColors.White, SKPaintStyle.Stroke);

        try
        {
            foreach (var (face, depth, light) in data.FacesWithDepth)
            {
                if (depth >= 0) continue;

                RenderFaceFill(canvas, data, face, light, facePaint);

                if (UseAdvancedEffects && settings.UseEdgeHighlight)
                {
                    RenderFaceEdge(canvas, data, face, settings, edgePaint);
                }
            }
        }
        finally
        {
            ReturnPaint(facePaint);
            ReturnPaint(edgePaint);
        }
    }

    private void RenderFaceFill(
        SKCanvas canvas,
        CubeRenderData data,
        Face face,
        float light,
        SKPaint paint)
    {
        var faceColor = GetFaceColor(data.BaseColor, face.ColorIndex);
        byte alpha = CalculateAlpha(BASE_ALPHA + data.Intensity * 0.1f);

        paint.Color = new SKColor(
            (byte)(faceColor.Red * light),
            (byte)(faceColor.Green * light),
            (byte)(faceColor.Blue * light),
            alpha);

        RenderPath(canvas, path =>
        {
            CreateFacePath(path, data.ProjectedVertices, face);
        }, paint);
    }

    private void RenderFaceEdge(
        SKCanvas canvas,
        CubeRenderData data,
        Face face,
        QualitySettings settings,
        SKPaint paint)
    {
        byte alpha = CalculateAlpha(BASE_ALPHA * EDGE_ALPHA_MULTIPLIER);
        paint.Color = SKColors.White.WithAlpha(alpha);
        paint.StrokeWidth = settings.EdgeWidth * (IsOverlayActive ? EDGE_WIDTH_OVERLAY : EDGE_WIDTH);

        if (settings.UseGlow && settings.EdgeBlur > 0)
        {
            float blurFactor = IsOverlayActive ? BLUR_FACTOR_OVERLAY : BLUR_FACTOR;
            byte edgeBlur = (byte)(settings.EdgeBlur * blurFactor);

            using var blurFilter = SKMaskFilter.CreateBlur(
                SKBlurStyle.Normal,
                edgeBlur);
            paint.MaskFilter = blurFilter;
        }

        RenderPath(canvas, path =>
        {
            CreateFacePath(path, data.ProjectedVertices, face);
        }, paint);
    }

    private static void CreateFacePath(
        SKPath path,
        SKPoint[] projected,
        Face face)
    {
        path.MoveTo(projected[face.V1]);
        path.LineTo(projected[face.V2]);
        path.LineTo(projected[face.V3]);
        path.LineTo(projected[face.V4]);
        path.Close();
    }

    private static SKColor GetFaceColor(SKColor baseColor, int faceIndex)
    {
        baseColor.ToHsl(out float hue, out float saturation, out float lightness);

        float hueShift = faceIndex * HUE_SHIFT_PER_FACE;
        hue = (hue + hueShift) % 360f;

        lightness = Clamp(
            lightness + (faceIndex % 2 == 0 ? LIGHTNESS_VARIATION : -LIGHTNESS_VARIATION),
            MIN_LIGHTNESS,
            MAX_LIGHTNESS);

        return SKColor.FromHsl(hue, saturation * 100f, lightness * 100f);
    }

    private float CalculateScale(
        SKImageInfo info,
        int barCount,
        float intensity)
    {
        float barCountScale = 1f + (barCount - 50) * BAR_COUNT_SCALE_FACTOR;
        float barWidthScale = 1f;
        float intensityScale = 0.8f + intensity * INTENSITY_SCALE_FACTOR;

        float totalScale = Clamp(
            barCountScale * barWidthScale * intensityScale,
            MIN_SCALE,
            MAX_SCALE);

        float baseFactor = IsOverlayActive ? SCALE_FACTOR_OVERLAY : SCALE_FACTOR;
        return Math.Min(info.Width, info.Height) * baseFactor * totalScale;
    }

    private static float CalculateAverageIntensity(float[] spectrum)
    {
        if (spectrum.Length == 0) return 0f;

        float sum = 0f;
        for (int i = 0; i < spectrum.Length; i++)
        {
            sum += spectrum[i];
        }

        return sum / spectrum.Length;
    }

    private static Vertex[] CreateVertices() =>
    [
        new(-CUBE_SIZE, -CUBE_SIZE,  CUBE_SIZE),
        new( CUBE_SIZE, -CUBE_SIZE,  CUBE_SIZE),
        new( CUBE_SIZE,  CUBE_SIZE,  CUBE_SIZE),
        new(-CUBE_SIZE,  CUBE_SIZE,  CUBE_SIZE),
        new(-CUBE_SIZE, -CUBE_SIZE, -CUBE_SIZE),
        new( CUBE_SIZE, -CUBE_SIZE, -CUBE_SIZE),
        new( CUBE_SIZE,  CUBE_SIZE, -CUBE_SIZE),
        new(-CUBE_SIZE,  CUBE_SIZE, -CUBE_SIZE)
    ];

    private static Face[] CreateFaces() =>
    [
        new(0, 1, 2, 3, 0),
        new(5, 4, 7, 6, 1),
        new(3, 2, 6, 7, 2),
        new(4, 5, 1, 0, 3),
        new(1, 5, 6, 2, 4),
        new(4, 0, 3, 7, 5)
    ];

    private static Vector3 GetFaceNormal(int faceIndex) => faceIndex switch
    {
        0 => Vector3.UnitZ,
        1 => -Vector3.UnitZ,
        2 => Vector3.UnitY,
        3 => -Vector3.UnitY,
        4 => Vector3.UnitX,
        5 => -Vector3.UnitX,
        _ => Vector3.Zero
    };

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 50,
        RenderQuality.Medium => 100,
        RenderQuality.High => 150,
        _ => 100
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.4f,
            RenderQuality.Medium => 0.3f,
            RenderQuality.High => 0.25f,
            _ => 0.3f
        };

        if (IsOverlayActive)
        {
            smoothingFactor *= 1.2f;
        }

        SetProcessingSmoothingFactor(smoothingFactor);
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _rotationX = 0f;
        _rotationY = 0f;
        _rotationZ = 0f;
        _currentIntensity = 0f;
        base.OnDispose();
    }

    private readonly record struct Vertex(float X, float Y, float Z);

    private readonly record struct Face(
        int V1,
        int V2,
        int V3,
        int V4,
        int ColorIndex);

    private record CubeRenderData(
        SKPoint[] ProjectedVertices,
        (Face face, float depth, float light)[] FacesWithDepth,
        SKColor BaseColor,
        float Intensity,
        float Scale);
}