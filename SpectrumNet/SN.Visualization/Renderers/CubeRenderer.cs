#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.CubeRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CubeRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(CubeRenderer);

    private static readonly Lazy<CubeRenderer> _instance =
        new(() => new CubeRenderer());

    private CubeRenderer()
    {
        _vertices = CreateVertices();
        _faces = CreateFaces();
    }

    public static CubeRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            CUBE_SIZE = 0.5f,
            BASE_ROTATION = 0.02f,
            ROTATION_INFLUENCE = 0.015f,
            AMBIENT_LIGHT = 0.4f,
            DIFFUSE_LIGHT = 0.6f,
            BASE_ALPHA = 0.9f,
            BAR_COUNT_SCALE_FACTOR = 0.01f,
            BAR_WIDTH_SCALE_FACTOR = 0.05f,
            MIN_SCALE = 0.5f,
            MAX_SCALE = 2.5f;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                UseGlow: false,
                EdgeBlur: 0,
                EdgeWidth: 1
            ),
            [RenderQuality.Medium] = new(
                UseGlow: true,
                EdgeBlur: 2,
                EdgeWidth: 1
            ),
            [RenderQuality.High] = new(
                UseGlow: true,
                EdgeBlur: 4,
                EdgeWidth: 2
            )
        };

        public record QualitySettings(
            bool UseGlow,
            byte EdgeBlur,
            byte EdgeWidth
        );
    }

    private static readonly Vector3 LIGHT_DIRECTION = Vector3.Normalize(new(0.5f, 0.7f, -1f));

    private readonly record struct Vertex(float X, float Y, float Z);
    private readonly record struct Face(int V1, int V2, int V3, int V4, int ColorIndex);

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private readonly Vertex[] _vertices;
    private readonly Face[] _faces;

    private float _rotationX, _rotationY, _rotationZ;
    private float _speedX = BASE_ROTATION;
    private float _speedY = BASE_ROTATION * 1.2f;
    private float _speedZ = BASE_ROTATION * 0.8f;
    private float _cubeScale = 1f;
    private SKColor _baseColor = SKColors.White;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        _logger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {Quality}");
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
        _baseColor = paint.Color;
        UpdateRotation(spectrum);

        var center = new SKPoint(info.Width * 0.5f, info.Height * 0.5f);

        float barCountScale = 1f + (barCount - 50) * BAR_COUNT_SCALE_FACTOR;
        float barWidthScale = 1f + (barWidth - 5) * BAR_WIDTH_SCALE_FACTOR;
        float totalScale = Clamp(barCountScale * barWidthScale * _cubeScale, MIN_SCALE, MAX_SCALE);

        float scale = Min(info.Width, info.Height) * 0.3f * totalScale;

        var rotation = Matrix4x4.CreateRotationX(_rotationX) *
                      Matrix4x4.CreateRotationY(_rotationY) *
                      Matrix4x4.CreateRotationZ(_rotationZ);

        var projected = ProjectVertices(rotation, scale, center);
        var facesWithDepth = CalculateFaceDepths(projected, rotation);

        DrawFaces(canvas, projected, facesWithDepth, spectrum);
    }

    private void UpdateRotation(float[] spectrum)
    {
        float deltaTime = _animationTimer.DeltaTime;

        if (spectrum.Length >= 3)
        {
            _speedX = BASE_ROTATION + spectrum[0] * ROTATION_INFLUENCE;
            _speedY = BASE_ROTATION + spectrum[spectrum.Length / 2] * ROTATION_INFLUENCE;
            _speedZ = BASE_ROTATION + spectrum[^1] * ROTATION_INFLUENCE;

            float avgIntensity = spectrum.Average();
            _cubeScale = Lerp(_cubeScale, 0.8f + avgIntensity * 0.4f, 0.1f);
        }

        _rotationX = (_rotationX + _speedX * deltaTime) % MathF.Tau;
        _rotationY = (_rotationY + _speedY * deltaTime) % MathF.Tau;
        _rotationZ = (_rotationZ + _speedZ * deltaTime) % MathF.Tau;
    }

    private static float Lerp(
        float current,
        float target,
        float amount) =>
        current * (1f - amount) + target * amount;

    private SKPoint[] ProjectVertices(
        Matrix4x4 rotation,
        float scale,
        SKPoint center)
    {
        var projected = new SKPoint[_vertices.Length];

        for (int i = 0; i < _vertices.Length; i++)
        {
            var v = _vertices[i];
            var rotated = Vector3.Transform(new Vector3(v.X, v.Y, v.Z), rotation);

            projected[i] = new SKPoint(
                rotated.X * scale + center.X,
                rotated.Y * scale + center.Y
            );
        }

        return projected;
    }

    private (Face face, float depth, float light)[] CalculateFaceDepths(
        SKPoint[] _,
        Matrix4x4 rotation)
    {
        var result = new (Face face, float depth, float light)[_faces.Length];

        for (int i = 0; i < _faces.Length; i++)
        {
            var face = _faces[i];
            var normal = GetFaceNormal(face.ColorIndex);
            var rotatedNormal = Vector3.TransformNormal(normal, rotation);

            float depth = rotatedNormal.Z;
            float light = AMBIENT_LIGHT + DIFFUSE_LIGHT *
                         MathF.Max(0, Vector3.Dot(Vector3.Normalize(rotatedNormal), LIGHT_DIRECTION));

            result[i] = (face, depth, light);
        }

        return [.. result.OrderBy(f => f.depth)];
    }

    private void DrawFaces(
        SKCanvas canvas,
        SKPoint[] projected,
        (Face face, float depth, float light)[] faces,
        float[] spectrum)
    {
        float maxSpectrum = spectrum.Length > 0 ? spectrum.Max() : 0f;

        foreach (var (face, depth, light) in faces)
        {
            if (depth >= 0) continue;

            var path = _resourceManager.GetPath();
            try
            {
                CreateFacePath(path, projected, face);

                var faceColor = GetFaceColor(face.ColorIndex);

                byte r = (byte)(faceColor.Red * light);
                byte g = (byte)(faceColor.Green * light);
                byte b = (byte)(faceColor.Blue * light);
                byte a = (byte)((BASE_ALPHA + maxSpectrum * 0.1f) * 255f);

                using var facePaint = CreateStandardPaint(new SKColor(r, g, b, a));
                canvas.DrawPath(path, facePaint);

                if (UseAdvancedEffects && _currentSettings.UseGlow)
                {
                    DrawGlow(canvas, path, a);
                }
            }
            finally
            {
                _resourceManager.ReturnPath(path);
            }
        }
    }

    private SKColor GetFaceColor(int faceIndex)
    {
        _baseColor.ToHsl(out float hue, out float saturation, out float lightness);

        float hueShift = faceIndex * 60f;
        hue = (hue + hueShift) % 360f;

        lightness = Clamp(lightness + (faceIndex % 2 == 0 ? 0.1f : -0.1f), 0.2f, 0.8f);

        return SKColor.FromHsl(hue, saturation * 100f, lightness * 100f);
    }

    private void DrawGlow(
        SKCanvas canvas,
        SKPath path,
        byte alpha)
    {
        using var edgePaint = CreateStandardPaint(SKColors.White.WithAlpha((byte)(alpha * 0.8f)));
        edgePaint.Style = SKPaintStyle.Stroke;
        edgePaint.StrokeWidth = _currentSettings.EdgeWidth;

        if (_currentSettings.EdgeBlur > 0)
        {
            edgePaint.MaskFilter = SKMaskFilter.CreateBlur(
                SKBlurStyle.Normal,
                _currentSettings.EdgeBlur);
        }

        canvas.DrawPath(path, edgePaint);
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

    protected override void OnDispose()
    {
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}