#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.AsciiDonutRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class AsciiDonutRenderer() : EffectSpectrumRenderer
{
    private static readonly Lazy<AsciiDonutRenderer> _instance =
        new(() => new AsciiDonutRenderer());

    public static AsciiDonutRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            ROTATION_SPEED = 0.02f,
            ROTATION_INFLUENCE = 0.5f,
            RADIUS = 2f,
            TUBE_RADIUS = 0.5f,
            SCALE = 0.4f,
            FONT_SIZE = 18f,
            LERP_AMOUNT = 0.2f,
            DEPTH_OFFSET = 3f,
            MIN_ALPHA_FACTOR = 0.2f,
            MAX_ALPHA_FACTOR = 0.8f,
            CHAR_OFFSET = 4f;

        public const string ASCII_CHARS = " .,-~:;=!*#$@";
        public const int SEGMENTS = 64;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(SkipFactor: 3, UseLight: false),
            [RenderQuality.Medium] = new(SkipFactor: 1, UseLight: true),
            [RenderQuality.High] = new(SkipFactor: 0, UseLight: true)
        };

        public record QualitySettings(int SkipFactor, bool UseLight);
    }

    private static readonly Vector3 LIGHT_DIR = Vector3.Normalize(new(0.6f, 0.6f, -1f));
    private static readonly float[] _cosTable = new float[SEGMENTS];
    private static readonly float[] _sinTable = new float[SEGMENTS];

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private readonly record struct Vertex(float X, float Y, float Z);
    private Vertex[] _vertices = [];
    private readonly SKFont _font = new() { Size = FONT_SIZE, Hinting = SKFontHinting.None };
    private float _rotationX, _rotationY, _rotationZ;
    private float _rotationIntensity = 1f;

    static AsciiDonutRenderer()
    {
        for (int i = 0; i < SEGMENTS; i++)
        {
            float angle = i * MathF.PI * 2f / SEGMENTS;
            _cosTable[i] = Cos(angle);
            _sinTable[i] = Sin(angle);
        }
    }

    protected override void OnInitialize()
    {
        base.OnInitialize();
        InitializeDonut();
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
    }

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        UpdateRotation(spectrum);

        var center = new SKPoint(info.Width * 0.5f, info.Height * 0.5f);
        float scale = MathF.Min(center.X, center.Y) * SCALE;

        var rotation = CreateRotationMatrix();
        var projected = ProjectVertices(rotation, scale, center);

        DrawAsciiDonut(canvas, projected, paint);
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

    private void UpdateRotation(float[] spectrum)
    {
        float avgIntensity = spectrum.Length > 0
            ? GetAverageInRange(spectrum, 0, spectrum.Length)
            : 0f;

        _rotationIntensity = Lerp(
            _rotationIntensity,
            1f + avgIntensity * ROTATION_INFLUENCE,
            LERP_AMOUNT);

        float speed = ROTATION_SPEED * _rotationIntensity;
        _rotationX += speed * 0.5f;
        _rotationY += speed;
        _rotationZ += speed * 0.25f;
    }

    private Matrix4x4 CreateRotationMatrix() =>
        Matrix4x4.CreateRotationX(_rotationX) *
        Matrix4x4.CreateRotationY(_rotationY) *
        Matrix4x4.CreateRotationZ(_rotationZ);

    private (float x, float y, float depth, float light)[] ProjectVertices(
        Matrix4x4 rotation,
        float scale,
        SKPoint center)
    {
        int step = _currentSettings.SkipFactor + 1;
        int count = _vertices.Length / step;
        var result = new (float x, float y, float depth, float light)[count];

        for (int i = 0; i < count; i++)
        {
            var v = _vertices[i * step];
            var rotated = Vector3.Transform(new Vector3(v.X, v.Y, v.Z), rotation);

            float depth = rotated.Z + DEPTH_OFFSET;
            float invDepth = 1f / depth;

            float light = 1f;
            if (_currentSettings.UseLight)
            {
                var normal = Vector3.Normalize(rotated);
                light = MathF.Max(0f, Vector3.Dot(normal, LIGHT_DIR));
            }

            result[i] = (
                rotated.X * scale * invDepth + center.X,
                rotated.Y * scale * invDepth + center.Y,
                depth,
                light);
        }

        return [.. result.OrderByDescending(v => v.depth)];
    }

    private void DrawAsciiDonut(
        SKCanvas canvas,
        (float x, float y, float depth, float light)[] vertices,
        SKPaint basePaint)
    {
        var chars = ASCII_CHARS.ToCharArray();

        foreach (var (x, y, depth, light) in vertices)
        {
            if (!IsRenderAreaVisible(canvas, x - CHAR_OFFSET, y - CHAR_OFFSET,
                CHAR_OFFSET * 2, CHAR_OFFSET * 2))
                continue;

            int charIndex = Clamp((int)(light * (chars.Length - 1)), 0, chars.Length - 1);
            float normalizedDepth = Normalize(depth, 2f, 4f);
            byte alpha = CalculateAlpha(
                MIN_ALPHA_FACTOR + MAX_ALPHA_FACTOR * (1f - normalizedDepth));

            basePaint.Color = basePaint.Color.WithAlpha(alpha);
            canvas.DrawText(
                chars[charIndex].ToString(),
                x - CHAR_OFFSET,
                y + CHAR_OFFSET,
                _font,
                basePaint);
        }
    }

    protected override void OnDispose()
    {
        _font.Dispose();
        base.OnDispose();
    }
}