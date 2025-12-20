
namespace SpectrumNet.SN.Visualization;

public sealed class RendererPlaceholder : IDisposable
{
    #region Constants
    const string DefMsg = "Push SPACE to begin...";

    const float
        DefFontSize = 50f,
        GradSpeed = 0.15f,
        MinSpd = 50f,
        MaxSpd = 400f,
        Friction = 3f,
        ColNoise = 30f,
        OutlineW = 2f,
        GlowRad = 10f,
        ShadowX = 4f,
        ShadowY = 4f,
        ParticleSpd = 150f,
        ParticleLife = 0.8f,
        ParticleSize = 4f,
        VelEpsilon = 0.01f,
        MouseSampleInterval = 0.016f,
        ThrowMinDt = 0.001f,
        ThrowMaxDt = 0.3f;

    const int ColorDelta = 40, MaxParticles = 60, GradStops = 7;
    const byte ColorMin = 50, ColorMax = 200;

    static readonly float[] GradPositions = CreateGradPositions();
    #endregion

    #region Static
    static readonly SKColor
        DefCol1 = SKColors.OrangeRed,
        DefCol2 = SKColors.Gold,
        OutlineCol = SKColors.Black,
        ParticleCol = SKColors.LightBlue,
        GlowCol = new(255, 255, 255, 80),
        ShadowCol = new(0, 0, 0, 120);

    static readonly SKPoint InitVel = new(150, 100);

    static float[] CreateGradPositions()
    {
        float[] pos = new float[GradStops];
        for (int i = 0; i < GradStops; i++)
            pos[i] = (float)i / (GradStops - 1);
        return pos;
    }

    static byte MulAlpha(byte baseAlpha, byte factor) =>
        (byte)(baseAlpha * factor / 255);

    static SKColor LerpCol(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t),
        (byte)(a.Green + (b.Green - a.Green) * t),
        (byte)(a.Blue + (b.Blue - a.Blue) * t));

    static byte NextComp(byte v) =>
        (byte)Math.Clamp(v + Random.Shared.Next(-ColorDelta, ColorDelta + 1), ColorMin, ColorMax);

    static float Mag(SKPoint v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    static void ClampVel(ref SKPoint v, float min, float max)
    {
        float spd = Mag(v);
        if (spd < VelEpsilon) return;
        float sc = spd < min ? min / spd : spd > max ? max / spd : 1f;
        if (sc != 1f) { v.X *= sc; v.Y *= sc; }
    }
    #endregion

    #region Fields
    readonly SKFont _font;
    readonly SKPaint _textPaint, _particlePaint, _outline, _glow, _shadow;
    readonly string _msg;
    readonly float _tw, _asc, _desc;
    readonly List<Particle> _particles;
    readonly SKColor[] _gradCols;
    readonly Stopwatch _sw = Stopwatch.StartNew();

    SKPoint _pos, _vel, _prevMouse, _mouseOff;
    double _prevMouseTime, _lastTime;
    bool _disposed, _mouseDown, _gradDirty = true;
    SKColor _col1, _col2;
    float _gradOff, _transparency = 1f;
    #endregion

    #region Properties
    public SKSize CanvasSize { get; private set; }
    public bool IsInteractive { get; set; } = true;
    public float Transparency
    {
        get => _transparency;
        set => _transparency = Math.Clamp(value, 0f, 1f);
    }
    #endregion

    #region Constructor
    public RendererPlaceholder(string? message = null)
    {
        _msg = string.IsNullOrEmpty(message) ? DefMsg : message;
        _font = new SKFont { Size = DefFontSize, Subpixel = true };
        _textPaint = new SKPaint { IsAntialias = true };
        _particlePaint = new SKPaint { IsAntialias = true };

        _outline = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = OutlineW,
            Color = OutlineCol
        };

        _glow = new SKPaint
        {
            IsAntialias = true,
            Color = GlowCol,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, GlowRad)
        };

        _shadow = new SKPaint { IsAntialias = true, Color = ShadowCol };

        _col1 = DefCol1;
        _col2 = DefCol2;
        _gradCols = new SKColor[GradStops];

        _tw = MathF.Max(_font.MeasureText(_msg), 1f);
        SKFontMetrics m = _font.Metrics;
        _asc = MathF.Max(-m.Ascent, 1f);
        _desc = MathF.Max(m.Descent, 0f);

        _particles = new(MaxParticles);
        Reset();
        _lastTime = _sw.Elapsed.TotalSeconds;
    }
    #endregion

    #region Public Methods
    public void Render(SKCanvas canvas, SKImageInfo info)
    {
        if (_disposed) return;

        CanvasSize = new(info.Width, info.Height);
        float dt = CalcDt();

        Update(dt);
        UpdateParticles(dt);
        Draw(canvas);
    }

    public void OnMouseDown(SKPoint pt)
    {
        if (!IsInteractive || _disposed) return;

        if (HitTest(pt))
        {
            _mouseDown = true;
            _mouseOff = new(pt.X - _pos.X, pt.Y - _pos.Y);
            _prevMouse = pt;
            _prevMouseTime = _sw.Elapsed.TotalSeconds;
            SpawnParticles(pt, 5);
        }
    }

    public void OnMouseMove(SKPoint pt)
    {
        if (!IsInteractive || _disposed || !_mouseDown) return;

        _pos = new(pt.X - _mouseOff.X, pt.Y - _mouseOff.Y);
        _vel = SKPoint.Empty;

        double now = _sw.Elapsed.TotalSeconds;
        if (now - _prevMouseTime > MouseSampleInterval)
        {
            _prevMouse = pt;
            _prevMouseTime = now;
        }
    }

    public void OnMouseUp(SKPoint pt)
    {
        if (!IsInteractive || _disposed || !_mouseDown) return;

        _mouseDown = false;
        CalcThrowVelocity(pt);
    }

    public void OnMouseEnter()
    {
        if (IsInteractive && !_disposed)
            _glow.Color = GlowCol.WithAlpha(120);
    }

    public void OnMouseLeave()
    {
        if (!IsInteractive || _disposed) return;
        _glow.Color = GlowCol;

        if (_mouseDown)
        {
            _mouseDown = false;
            EnsureMinVel();
        }
    }

    public bool HitTest(SKPoint pt) =>
        pt.X >= _pos.X && pt.X <= _pos.X + _tw &&
        pt.Y >= _pos.Y - _asc && pt.Y <= _pos.Y + _desc;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _font.Dispose();
        _textPaint.Dispose();
        _particlePaint.Dispose();
        _outline.Dispose();
        _glow.Dispose();
        _shadow.Dispose();
    }
    #endregion

    #region Animation
    void Reset()
    {
        _pos = new(10, _asc + 10);
        _vel = InitVel;
        ClampVel(ref _vel, MinSpd, MaxSpd);
        _col1 = DefCol1;
        _col2 = DefCol2;
        _gradOff = 0;
        _mouseDown = false;
        _particles.Clear();
        _gradDirty = true;
    }

    float CalcDt()
    {
        double t = _sw.Elapsed.TotalSeconds;
        float dt = MathF.Min((float)(t - _lastTime), 0.05f);
        _lastTime = t;
        return dt;
    }

    void Update(float dt)
    {
        if (_mouseDown) return;

        float spd = Mag(_vel);

        if (spd < VelEpsilon) { EnsureMinVel(); spd = MinSpd; }

        if (spd > MinSpd)
        {
            float newSpd = MathF.Max(MinSpd, spd - Friction * spd * dt);
            float sc = newSpd / spd;
            _vel.X *= sc;
            _vel.Y *= sc;
        }

        _pos.X += _vel.X * dt;
        _pos.Y += _vel.Y * dt;

        ProcessCollisions();
        _gradOff = (_gradOff + GradSpeed * dt) % 1f;
    }

    void CalcThrowVelocity(SKPoint pt)
    {
        double now = _sw.Elapsed.TotalSeconds;
        double dt = now - _prevMouseTime;

        if (dt is > ThrowMinDt and < ThrowMaxDt)
        {
            float invDt = (float)(1.0 / dt);
            _vel = new((pt.X - _prevMouse.X) * invDt * 0.25f,
                       (pt.Y - _prevMouse.Y) * invDt * 0.25f);

            float spd = Mag(_vel);
            if (spd < MinSpd) EnsureMinVel();
            else if (spd > MaxSpd) ClampVel(ref _vel, MinSpd, MaxSpd);
        }
        else EnsureMinVel();
    }

    void EnsureMinVel()
    {
        float a = (float)(Random.Shared.NextDouble() * 2 * MathF.PI);
        _vel = new(MathF.Cos(a) * MinSpd, MathF.Sin(a) * MinSpd);
    }

    void ProcessCollisions()
    {
        float th = _asc + _desc;
        bool fitsH = _tw <= CanvasSize.Width;
        bool fitsV = th <= CanvasSize.Height;

        if (!fitsH) _pos.X = (CanvasSize.Width - _tw) / 2;
        if (!fitsV) _pos.Y = _asc + (CanvasSize.Height - th) / 2;
        if (!fitsH && !fitsV) { _vel = SKPoint.Empty; return; }

        SKPoint norm = SKPoint.Empty;
        SKPoint colPt = SKPoint.Empty;

        if (fitsH)
        {
            if (_pos.X < 0)
            {
                _pos.X = 0;
                norm.X = 1;
                colPt = new(0, _pos.Y - _asc + th / 2);
            }
            else if (_pos.X + _tw > CanvasSize.Width)
            {
                _pos.X = CanvasSize.Width - _tw;
                norm.X = -1;
                colPt = new(CanvasSize.Width, _pos.Y - _asc + th / 2);
            }
        }

        if (fitsV)
        {
            if (_pos.Y - _asc < 0)
            {
                _pos.Y = _asc;
                norm.Y = 1;
                colPt = colPt == SKPoint.Empty ? new(_pos.X + _tw / 2, 0) : new(colPt.X, 0);
            }
            else if (_pos.Y + _desc > CanvasSize.Height)
            {
                _pos.Y = CanvasSize.Height - _desc;
                norm.Y = -1;
                colPt = colPt == SKPoint.Empty ? new(_pos.X + _tw / 2, CanvasSize.Height) : new(colPt.X, CanvasSize.Height);
            }
        }

        if (norm == SKPoint.Empty) return;

        float len = Mag(norm);
        if (len > VelEpsilon) { norm.X /= len; norm.Y /= len; }

        Reflect(norm);
        SpawnParticles(colPt, 10);
        ChangeColors();
    }

    void Reflect(SKPoint n)
    {
        float dot = _vel.X * n.X + _vel.Y * n.Y;
        _vel = new(_vel.X - 2 * dot * n.X, _vel.Y - 2 * dot * n.Y);

        float a = (float)(Random.Shared.NextDouble() * 2 * MathF.PI);
        float m = (float)(Random.Shared.NextDouble() * ColNoise);
        _vel.X += MathF.Cos(a) * m;
        _vel.Y += MathF.Sin(a) * m;

        dot = _vel.X * n.X + _vel.Y * n.Y;
        if (dot < 0)
        {
            _vel.X -= 2 * dot * n.X;
            _vel.Y -= 2 * dot * n.Y;
        }

        ClampVel(ref _vel, MinSpd, MaxSpd);
    }
    #endregion

    #region Particles
    void SpawnParticles(SKPoint origin, int cnt)
    {
        for (int i = 0; i < cnt && _particles.Count < MaxParticles; i++)
            _particles.Add(new Particle(origin));
    }

    void UpdateParticles(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            Particle p = _particles[i];
            p.Update(dt);
            if (p.IsDead) _particles.RemoveAt(i);
        }
    }
    #endregion

    #region Drawing
    void Draw(SKCanvas c)
    {
        byte a = (byte)(255 * _transparency);
        if (a == 0) return;

        DrawTextLayer(c, _shadow, ShadowCol, a, ShadowX, ShadowY);
        DrawTextLayer(c, _glow, _glow.Color, a);
        DrawTextLayer(c, _outline, OutlineCol, a);
        DrawGradientText(c, a);
        DrawParticles(c, a);
    }

    void DrawTextLayer(SKCanvas c, SKPaint paint, SKColor baseCol, byte alpha, float dx = 0, float dy = 0)
    {
        paint.Color = baseCol.WithAlpha(MulAlpha(baseCol.Alpha, alpha));

        if (dx != 0 || dy != 0)
        {
            c.Save();
            c.Translate(dx, dy);
            c.DrawText(_msg, _pos.X, _pos.Y, _font, paint);
            c.Restore();
        }
        else
        {
            c.DrawText(_msg, _pos.X, _pos.Y, _font, paint);
        }
    }

    void DrawGradientText(SKCanvas c, byte a)
    {
        EnsureGradCols();

        using var shader = SKShader.CreateLinearGradient(
            new(_pos.X, _pos.Y),
            new(_pos.X + _tw, _pos.Y),
            _gradCols, GradPositions, SKShaderTileMode.Clamp,
            SKMatrix.CreateTranslation(_gradOff * _tw, 0));

        _textPaint.Shader = shader;
        _textPaint.Color = SKColors.White.WithAlpha(a);
        c.DrawText(_msg, _pos.X, _pos.Y, _font, _textPaint);
        _textPaint.Shader = null;
    }

    void DrawParticles(SKCanvas c, byte a)
    {
        foreach (Particle p in _particles)
        {
            if (p.Size <= 0 || p.Alpha <= 0) continue;
            _particlePaint.Color = p.Color.WithAlpha(MulAlpha((byte)(p.Alpha * 255), a));
            c.DrawCircle(p.Pos, p.Size, _particlePaint);
        }
    }
    #endregion

    #region Gradient
    void ChangeColors()
    {
        _col1 = _col2;
        _col2 = new(NextComp(_col1.Red), NextComp(_col1.Green), NextComp(_col1.Blue));
        _gradDirty = true;
    }

    void EnsureGradCols()
    {
        if (!_gradDirty) return;

        for (int i = 0; i < GradStops; i++)
        {
            float t = GradPositions[i];
            float u = t <= 0.5f ? t * 2 : (1 - t) * 2;
            _gradCols[i] = LerpCol(_col1, _col2, u);
        }

        _gradDirty = false;
    }
    #endregion

    #region Particle
    sealed class Particle
    {
        public SKPoint Pos, Vel;
        public float Alpha = 1f, Life = ParticleLife, Size = ParticleSize;
        public SKColor Color = ParticleCol;
        public bool IsDead => Alpha <= 0 || Size <= 0;

        public Particle(SKPoint origin)
        {
            Pos = origin;
            float a = (float)(Random.Shared.NextDouble() * 2 * MathF.PI);
            float s = (float)(Random.Shared.NextDouble() * ParticleSpd);
            Vel = new(MathF.Cos(a) * s, MathF.Sin(a) * s);
        }

        public void Update(float dt)
        {
            Pos.X += Vel.X * dt;
            Pos.Y += Vel.Y * dt;
            Vel.X *= 0.97f;
            Vel.Y *= 0.97f;
            Alpha -= dt / Life;
            Size -= dt / (Life * 2);
            Alpha = MathF.Max(0f, Alpha);
            Size = MathF.Max(0f, Size);
        }
    }
    #endregion
}
