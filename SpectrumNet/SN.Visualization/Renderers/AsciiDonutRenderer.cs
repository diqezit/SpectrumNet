#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class AsciiDonutRenderer : EffectSpectrumRenderer<AsciiDonutRenderer.QualitySettings>
{
    private static readonly Lazy<AsciiDonutRenderer> _instance =
        new(() => new AsciiDonutRenderer());

    public static AsciiDonutRenderer GetInstance() => _instance.Value;

    private const float ROTATION_SPEED = 0.02f,
        ROTATION_INFLUENCE = 0.5f,
        RADIUS = 2f,
        TUBE_RADIUS = 0.5f,
        SCALE = 0.4f,
        SCALE_OVERLAY = 0.35f,
        FONT_SIZE = 18f,
        FONT_SIZE_OVERLAY = 14f,
        DEPTH_OFFSET = 3f,
        MIN_ALPHA_FACTOR = 0.2f,
        MAX_ALPHA_FACTOR = 0.8f,
        CHAR_OFFSET = 4f,
        CHAR_OFFSET_OVERLAY = 3f;

    private const string ASCII_CHARS = " .,-~:;=!*#$@";
    private const int SEGMENTS = 64;

    private static readonly Vector3 LIGHT_DIR = Vector3.Normalize(new(0.6f, 0.6f, -1f));
    private static readonly float[] _cosTable = GenerateCosTable();
    private static readonly float[] _sinTable = GenerateSinTable();

    private readonly SKFont _font = new() { Size = FONT_SIZE, Hinting = SKFontHinting.None };
    private Vertex[] _vertices = [];

    private float 
        _rotationX,
        _rotationY,
        _rotationZ,
        _rotationIntensity = 1f;

    public sealed class QualitySettings
    {
        public int SkipFactor { get; init; }
        public bool UseLight { get; init; }
        public bool UseColorVariation { get; init; }
        public float AnimationSpeed { get; init; }
        public float LerpAmount { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            SkipFactor = 3,
            UseLight = false,
            UseColorVariation = false,
            AnimationSpeed = 0.8f,
            LerpAmount = 0.15f
        },
        [RenderQuality.Medium] = new()
        {
            SkipFactor = 1,
            UseLight = true,
            UseColorVariation = true,
            AnimationSpeed = 1f,
            LerpAmount = 0.2f
        },
        [RenderQuality.High] = new()
        {
            SkipFactor = 0,
            UseLight = true,
            UseColorVariation = true,
            AnimationSpeed = 1.2f,
            LerpAmount = 0.25f
        }
    };

    protected override void OnInitialize()
    {
        base.OnInitialize();
        InitializeDonut();
    }

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
            Min(renderParams.EffectiveBarCount, 16),
            applyTemporalSmoothing: true);

        if (!isValid || processedSpectrum == null)
            return;

        var donutData = CalculateDonutData(
            processedSpectrum,
            info);

        if (!ValidateDonutData(donutData))
            return;

        RenderDonutVisualization(
            canvas,
            donutData,
            renderParams,
            passedInPaint);
    }

    private DonutData CalculateDonutData(
        float[] spectrum,
        SKImageInfo info)
    {
        var settings = CurrentQualitySettings!;

        UpdateRotation(spectrum, settings);

        var center = new SKPoint(info.Width * 0.5f, info.Height * 0.5f);
        float scale = CalculateScale(info);

        var rotation = CalculateRotationMatrix();
        var projected = ProjectVertices(rotation, scale, center, settings);

        return new DonutData(
            ProjectedVertices: projected,
            RotationIntensity: _rotationIntensity,
            Scale: scale);
    }

    private static bool ValidateDonutData(DonutData data) =>
        data.ProjectedVertices.Length > 0 &&
        data.Scale > 0;

    private void RenderDonutVisualization(
        SKCanvas canvas,
        DonutData data,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;
        var boundingBox = CalculateBoundingBox(data.ProjectedVertices);

        if (!IsAreaVisible(canvas, boundingBox))
            return;

        RenderWithOverlay(canvas, () =>
        {
            RenderDonutCharacters(canvas, data.ProjectedVertices, basePaint, settings);
        });
    }

    private void InitializeDonut()
    {
        _vertices = new Vertex[SEGMENTS * SEGMENTS];
        int idx = 0;

        for (int i = 0; i < SEGMENTS; i++)
        {
            for (int j = 0; j < SEGMENTS; j++)
            {
                float r = RADIUS + TUBE_RADIUS * _cosTable[j];
                _vertices[idx++] = new Vertex(
                    r * _cosTable[i],
                    r * _sinTable[i],
                    TUBE_RADIUS * _sinTable[j]);
            }
        }
    }

    private void UpdateRotation(float[] spectrum, QualitySettings settings)
    {
        float avgIntensity = CalculateAverageIntensity(spectrum);

        _rotationIntensity = Lerp(
            _rotationIntensity,
            1f + avgIntensity * ROTATION_INFLUENCE,
            settings.LerpAmount);

        float speed = ROTATION_SPEED * _rotationIntensity * settings.AnimationSpeed;
        _rotationX += speed * 0.5f;
        _rotationY += speed;
        _rotationZ += speed * 0.25f;
    }

    private Matrix4x4 CalculateRotationMatrix() =>
        Matrix4x4.CreateRotationX(_rotationX) *
        Matrix4x4.CreateRotationY(_rotationY) *
        Matrix4x4.CreateRotationZ(_rotationZ);

    private VertexProjection[] ProjectVertices(
        Matrix4x4 rotation,
        float scale,
        SKPoint center,
        QualitySettings settings)
    {
        int step = settings.SkipFactor + 1;
        int count = _vertices.Length / step;
        var result = new VertexProjection[count];

        for (int i = 0; i < count; i++)
        {
            result[i] = ProjectSingleVertex(
                _vertices[i * step],
                rotation,
                scale,
                center,
                settings.UseLight);
        }

        return SortVerticesByDepth(result);
    }

    private static VertexProjection ProjectSingleVertex(
        Vertex vertex,
        Matrix4x4 rotation,
        float scale,
        SKPoint center,
        bool useLight)
    {
        var position = new Vector3(vertex.X, vertex.Y, vertex.Z);
        var rotated = Vector3.Transform(position, rotation);

        float depth = rotated.Z + DEPTH_OFFSET;
        float invDepth = 1f / depth;
        float light = CalculateVertexLight(rotated, useLight);

        return new VertexProjection(
            rotated.X * scale * invDepth + center.X,
            rotated.Y * scale * invDepth + center.Y,
            depth,
            light);
    }

    private static float CalculateVertexLight(Vector3 position, bool useLight)
    {
        if (!useLight)
            return 1f;

        var normal = Vector3.Normalize(position);
        return MathF.Max(0f, Vector3.Dot(normal, LIGHT_DIR));
    }

    private static VertexProjection[] SortVerticesByDepth(VertexProjection[] vertices) =>
        [.. vertices.OrderByDescending(v => v.Depth)];

    private void RenderDonutCharacters(
        SKCanvas canvas,
        VertexProjection[] vertices,
        SKPaint basePaint,
        QualitySettings settings)
    {
        var textPaint = CreatePaint(basePaint.Color, SKPaintStyle.Fill);

        try
        {
            foreach (var vertex in vertices)
            {
                if (!IsVertexVisible(canvas, vertex))
                    continue;

                RenderVertexCharacter(canvas, vertex, textPaint);
            }
        }
        finally
        {
            ReturnPaint(textPaint);
        }
    }

    private bool IsVertexVisible(SKCanvas canvas, VertexProjection vertex)
    {
        float offset = GetCharOffset();
        var charRect = new SKRect(
            vertex.X - offset * 2,
            vertex.Y - offset * 2,
            vertex.X + offset * 2,
            vertex.Y + offset * 2);

        return IsAreaVisible(canvas, charRect);
    }

    private void RenderVertexCharacter(
        SKCanvas canvas,
        VertexProjection vertex,
        SKPaint paint)
    {
        var chars = ASCII_CHARS.ToCharArray();
        int charIndex = CalculateCharIndex(vertex.Light, chars.Length);

        byte alpha = CalculateVertexAlpha(vertex.Depth);
        paint.Color = paint.Color.WithAlpha(alpha);

        float offset = GetCharOffset();
        canvas.DrawText(
            chars[charIndex].ToString(),
            vertex.X - offset,
            vertex.Y + offset,
            GetFont(),
            paint);
    }

    private SKRect CalculateBoundingBox(VertexProjection[] vertices)
    {
        if (vertices.Length == 0)
            return SKRect.Empty;

        float minX = vertices[0].X;
        float minY = vertices[0].Y;
        float maxX = vertices[0].X;
        float maxY = vertices[0].Y;

        float charOffset = GetCharOffset();

        for (int i = 1; i < vertices.Length; i++)
        {
            minX = MathF.Min(minX, vertices[i].X - charOffset);
            minY = MathF.Min(minY, vertices[i].Y - charOffset);
            maxX = MathF.Max(maxX, vertices[i].X + charOffset);
            maxY = MathF.Max(maxY, vertices[i].Y + charOffset);
        }

        return new SKRect(minX, minY, maxX, maxY);
    }

    private static byte CalculateVertexAlpha(float depth)
    {
        float normalizedDepth = Normalize(depth, 2f, 4f);
        return CalculateAlpha(MIN_ALPHA_FACTOR + MAX_ALPHA_FACTOR * (1f - normalizedDepth));
    }

    private static int CalculateCharIndex(float light, int charCount) =>
        Clamp((int)(light * (charCount - 1)), 0, charCount - 1);

    private SKFont GetFont()
    {
        _font.Size = IsOverlayActive ? FONT_SIZE_OVERLAY : FONT_SIZE;
        return _font;
    }

    private float GetCharOffset() =>
        IsOverlayActive ? CHAR_OFFSET_OVERLAY : CHAR_OFFSET;

    private float CalculateScale(SKImageInfo info)
    {
        float baseScale = IsOverlayActive ? SCALE_OVERLAY : SCALE;
        return MathF.Min(info.Width, info.Height) * baseScale;
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

    private static float[] GenerateCosTable()
    {
        var table = new float[SEGMENTS];
        for (int i = 0; i < SEGMENTS; i++)
        {
            float angle = i * MathF.PI * 2f / SEGMENTS;
            table[i] = MathF.Cos(angle);
        }
        return table;
    }

    private static float[] GenerateSinTable()
    {
        var table = new float[SEGMENTS];
        for (int i = 0; i < SEGMENTS; i++)
        {
            float angle = i * MathF.PI * 2f / SEGMENTS;
            table[i] = MathF.Sin(angle);
        }
        return table;
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 32,
        RenderQuality.Medium => 64,
        RenderQuality.High => 128,
        _ => 64
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
            smoothingFactor *= 1.2f;
        
        SetProcessingSmoothingFactor(smoothingFactor);
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _font.Dispose();
        _vertices = [];
        _rotationX = 0f;
        _rotationY = 0f;
        _rotationZ = 0f;
        _rotationIntensity = 1f;
        base.OnDispose();
    }

    private readonly record struct Vertex(float X, float Y, float Z);

    private readonly record struct VertexProjection(
        float X,
        float Y,
        float Depth,
        float Light);

    private record DonutData(
        VertexProjection[] ProjectedVertices,
        float RotationIntensity,
        float Scale);
}