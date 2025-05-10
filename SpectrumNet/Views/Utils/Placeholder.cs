#nullable enable

using static SpectrumNet.Views.Utils.RendererPlaceholder.Constants;

namespace SpectrumNet.Views.Utils;

public sealed class RendererPlaceholder : IPlaceholder
{
    public record Constants
    {
        public static readonly string DEFAULT_MESSAGE = "Push SPACE to begin...";

        public static readonly SKColor
            DEFAULT_COLOR1 = SKColors.OrangeRed,
            DEFAULT_COLOR2 = SKColors.Gold,
            OUTLINE_COLOR = SKColors.Black,
            SPLASH_PARTICLE_COLOR = SKColors.LightBlue,
            GLOW_COLOR = new(255, 255, 255, 80),
            SHADOW_COLOR = new(0, 0, 0, 120),
            TRAIL_COLOR = new(255, 255, 255, 40);

        public static readonly int
            COLOR_RANDOM_DELTA_RANGE = 40,
            RANDOM_NUDE_INTERVAL_FRAMES = 90,
            SPLASH_PARTICLE_COUNT = 20,
            MAX_TRAIL_POINTS = 15,
            MAX_PARTICLE_POOL_SIZE = 50,
            MAX_ENERGY_PARTICLES = 30;

        public static readonly byte
            COLOR_CLAMP_MIN = 50,
            COLOR_CLAMP_MAX = 200;

        public static readonly float
            DEFAULT_FONT_SIZE = 50,
            GRADIENT_SPEED = 0.2f,
            GRADIENT_SPEED_MULTIPLIER = 0.001f,
            MIN_SPEED = 100,
            MAX_SPEED = 400,
            COLLISION_VELOCITY_NOISE = 50,
            PERIODIC_VELOCITY_NUDE_MAGNITUDE = 20,
            DAMPING_FACTOR = 0.99f,
            OUTLINE_STROKE_WIDTH = 2f,
            WAVE_FREQUENCY = 2f,
            WAVE_AMPLITUDE = 10f,
            REFLECT_SMOOTHING = 0.2f,
            SPLASH_PARTICLE_SPEED = 200f,
            SPLASH_PARTICLE_LIFETIME = 0.6f,
            SPLASH_PARTICLE_SIZE = 4f,
            MAX_SQUASH_AMOUNT = 0.1f,
            SQUASH_DURATION = 0.3f,
            GLOW_RADIUS = 10f,
            SHADOW_OFFSET_X = 4f,
            SHADOW_OFFSET_Y = 4f,
            ENERGY_PARTICLE_SPEED = 60f,
            ENERGY_PARTICLE_LIFETIME = 1.5f,
            ENERGY_PARTICLE_SPAWN_RATE = 0.1f;

        public static readonly SKPoint INITIAL_VELOCITY = new(150, 100);
    }

    private readonly ObjectPool<SplashParticle> _particlePool;
    private readonly ObjectPool<EnergyParticle> _energyParticlePool;
    private readonly SKFont _font;
    private readonly SKPaint _paint;
    private readonly SKPaint _outlinePaint;
    private readonly SKPaint _glowPaint;
    private readonly SKPaint _shadowPaint;
    private readonly SKPaint _trailPaint;
    private readonly string _message;
    private readonly float _textWidth;
    private readonly float _textHeight;
    private readonly List<SKPoint> _trailPoints;
    private readonly List<SplashParticle> _activeSplashParticles;
    private readonly List<EnergyParticle> _activeEnergyParticles;

    private SKPoint _position;
    private SKPoint _velocity;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private double _lastElapsedTime;
    private bool _isDisposed;
    private SKColor _gradientColor1;
    private SKColor _gradientColor2;
    private float _gradientOffset;

    private int _frameCounter;
    private float _waveOffset;
    private float _currentWavePhase;

    private readonly SKColor[] _shaderColors;
    private readonly float[] _shaderPositions;
    private const int GRADIENT_STOP_COUNT = 15;

    private float _squashAmount;
    private SKPoint _squashNormal = SKPoint.Empty;
    private float _squashTimer;
    private float _energyParticleSpawnTimer;
    private bool _isMouseDown;
    private SKPoint _lastMousePosition;
    private SKPoint _mouseOffset;

    private SKShader? _cachedShader;
    private float _lastGradientOffset = -1;
    private SKColor _lastColor1 = SKColors.Transparent;
    private SKColor _lastColor2 = SKColors.Transparent;

    public required SKSize CanvasSize { get; set; }
    public bool IsInteractive { get; set; } = true;
    public float Transparency { get; set; } = 1f;

    public RendererPlaceholder(string? message = null)
    {
        _message = message ?? DEFAULT_MESSAGE;
        _font = new() { Size = DEFAULT_FONT_SIZE, Subpixel = true };
        _paint = new() { IsAntialias = true };
        _outlinePaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = OUTLINE_STROKE_WIDTH,
            Color = OUTLINE_COLOR
        };

        _glowPaint = new()
        {
            IsAntialias = true,
            Color = GLOW_COLOR,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, GLOW_RADIUS)
        };

        _shadowPaint = new()
        {
            IsAntialias = true,
            Color = SHADOW_COLOR
        };

        _trailPaint = new()
        {
            IsAntialias = true,
            Color = TRAIL_COLOR,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f,
            PathEffect = SKPathEffect.CreateDash([5f, 5f], 0f)
        };

        _gradientColor1 = DEFAULT_COLOR1;
        _gradientColor2 = DEFAULT_COLOR2;

        _shaderColors = new SKColor[GRADIENT_STOP_COUNT];
        _shaderPositions = [.. Enumerable.Range(0, GRADIENT_STOP_COUNT)
            .Select(i => (float)i / (GRADIENT_STOP_COUNT - 1))];

        _textWidth = _font.MeasureText(_message);
        _textHeight = _font.Size;

        _trailPoints = new(MAX_TRAIL_POINTS);
        _activeSplashParticles = new(MAX_PARTICLE_POOL_SIZE);
        _activeEnergyParticles = new(MAX_ENERGY_PARTICLES);

        _particlePool = new(() => new SplashParticle(), p => p.Reset(), MAX_PARTICLE_POOL_SIZE);
        _energyParticlePool = new(() => new EnergyParticle(), p => p.Reset(), MAX_ENERGY_PARTICLES);

        ResetState();
        _lastElapsedTime = _stopwatch.Elapsed.TotalSeconds;
    }

    public void Render(SKCanvas canvas, SKImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        UpdateCanvasSize(info);
        float deltaTime = CalculateDeltaTime();
        UpdateAnimation(deltaTime);
        UpdateSquashEffect(deltaTime);
        UpdateSplashParticles(deltaTime);
        UpdateEnergyParticles(deltaTime);
        UpdateTrail();
        DrawContent(canvas);
        DrawSplashParticles(canvas);
        DrawEnergyParticles(canvas);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _font?.Dispose();
        _paint?.Dispose();
        _outlinePaint?.Dispose();
        _glowPaint?.Dispose();
        _shadowPaint?.Dispose();
        _trailPaint?.Dispose();
        _cachedShader?.Dispose();
        _particlePool?.Dispose();
        _energyParticlePool?.Dispose();
        _isDisposed = true;
    }

    public void Reset()
    {
        ResetState();
        ResetElapsedTime();
        ClearParticles();
        ResetSquashEffect();
        ClearTrail();
        _isMouseDown = false;
        _mouseOffset = SKPoint.Empty;
    }

    public void OnMouseDown(SKPoint point)
    {
        if (!IsInteractive || _isDisposed) return;

        if (HitTest(point))
        {
            _isMouseDown = true;
            _lastMousePosition = point;
            _mouseOffset = new SKPoint(point.X - _position.X, point.Y - _position.Y);
            GenerateEnergyParticles(point);
        }
    }

    public void OnMouseMove(SKPoint point)
    {
        if (!IsInteractive || _isDisposed) return;
        _lastMousePosition = point;

        if (_isMouseDown)
        {
            _position = new SKPoint(point.X - _mouseOffset.X, point.Y - _mouseOffset.Y);
            _velocity = SKPoint.Empty;
            GenerateEnergyParticles(point);
        }
    }

    public void OnMouseUp(SKPoint point)
    {
        if (!IsInteractive || _isDisposed) return;

        if (_isMouseDown)
        {
            _isMouseDown = false;

            if (_lastMousePosition != SKPoint.Empty)
            {
                var delta = new SKPoint(point.X - _lastMousePosition.X, point.Y - _lastMousePosition.Y);
                _velocity = new SKPoint(delta.X * 10, delta.Y * 10);
                LimitVelocityMagnitude(ref _velocity, MIN_SPEED, MAX_SPEED);
            }
        }
    }

    public void OnMouseEnter()
    {
        if (!IsInteractive || _isDisposed) return;
        _glowPaint.Color = GLOW_COLOR.WithAlpha(120);
    }

    public void OnMouseLeave()
    {
        if (!IsInteractive || _isDisposed) return;
        _glowPaint.Color = GLOW_COLOR;
        _isMouseDown = false;
    }

    public bool HitTest(SKPoint point)
    {
        var bounds = new SKRect(_position.X, _position.Y - _textHeight,
            _position.X + _textWidth, _position.Y);
        return bounds.Contains(point);
    }

    private void ResetState()
    {
        ResetPosition();
        ResetVelocity();
        ResetGradientColors();
        ResetGradientOffset();
        ResetFrameCounter();
        ResetWaveOffset();
        ResetWavePhase();
        GenerateShaderColors();
    }

    private void ResetPosition() => _position = new(0, DEFAULT_FONT_SIZE);
    private void ResetVelocity() =>
        _velocity = LimitMagnitude(INITIAL_VELOCITY, MIN_SPEED, MAX_SPEED);
    private void ResetGradientColors()
    {
        _gradientColor1 = DEFAULT_COLOR1;
        _gradientColor2 = DEFAULT_COLOR2;
        GenerateShaderColors();
    }
    private void ResetGradientOffset() => _gradientOffset = 0;
    private void ResetFrameCounter() => _frameCounter = 0;
    private void ResetWaveOffset() => _waveOffset = 0;
    private void ResetWavePhase() => _currentWavePhase = 0;

    private void ClearParticles()
    {
        foreach (var particle in _activeSplashParticles)
            _particlePool.Return(particle);
        _activeSplashParticles.Clear();

        foreach (var particle in _activeEnergyParticles)
            _energyParticlePool.Return(particle);
        _activeEnergyParticles.Clear();
    }

    private void ResetSquashEffect()
    {
        _squashAmount = 0f;
        _squashTimer = 0f;
        _squashNormal = SKPoint.Empty;
    }

    private void ResetElapsedTime() => _lastElapsedTime = _stopwatch.Elapsed.TotalSeconds;
    private void ClearTrail() => _trailPoints.Clear();

    private void UpdateCanvasSize(SKImageInfo info) =>
        CanvasSize = new(info.Width, info.Height);

    private float CalculateDeltaTime()
    {
        var currentTime = _stopwatch.Elapsed.TotalSeconds;
        var deltaTime = currentTime - _lastElapsedTime;
        _lastElapsedTime = currentTime;
        return MathF.Min((float)deltaTime, 0.05f);
    }

    private void UpdateAnimation(float deltaTime)
    {
        if (!_isMouseDown)
        {
            ApplyDamping();
            UpdatePosition(deltaTime);
            ApplyWaveMotion(deltaTime);
            ProcessCollisions();
        }

        UpdateGradientOffset(deltaTime);

        if (_isMouseDown)
        {
            _cachedShader?.Dispose();
            _cachedShader = null;
        }

        UpdateSquashEffect(deltaTime);
        ApplyPeriodicRandomNudge();
        SpawnEnergyParticles(deltaTime);
    }

    private void ApplyDamping()
    {
        _velocity.X *= DAMPING_FACTOR;
        _velocity.Y *= DAMPING_FACTOR;
        LimitVelocityMagnitude(ref _velocity, MIN_SPEED, MAX_SPEED);
    }

    private void ApplyWaveMotion(float deltaTime)
    {
        _currentWavePhase += deltaTime * WAVE_FREQUENCY;
        float currentWave = MathF.Sin(_currentWavePhase) * WAVE_AMPLITUDE;
        float deltaWave = currentWave - _waveOffset;
        _position.Y += deltaWave;
        _waveOffset = currentWave;
    }

    private void UpdateSquashEffect(float deltaTime)
    {
        if (_squashTimer < SQUASH_DURATION)
        {
            _squashTimer += deltaTime;
            float progress = _squashTimer / SQUASH_DURATION;
            float easedProgress = EaseOut(progress);
            _squashAmount = MAX_SQUASH_AMOUNT * (1f - easedProgress);
            _squashAmount = MathF.Max(0f, _squashAmount);
        }
    }

    private static float EaseOut(float t) => 1 - (1 - t) * (1 - t);

    private void UpdatePosition(float deltaTime)
    {
        _position.X += _velocity.X * deltaTime;
        _position.Y += _velocity.Y * deltaTime;
    }

    private void UpdateGradientOffset(float deltaTime) =>
        _gradientOffset = (_gradientOffset + GRADIENT_SPEED * GetMagnitude(_velocity) *
            GRADIENT_SPEED_MULTIPLIER * deltaTime) % 1.0f;

    private void ProcessCollisions()
    {
        bool collisionOccurred = CheckAndHandleCollisions();
        if (collisionOccurred) ChangeGradientColors();
    }

    private bool CheckAndHandleCollisions()
    {
        bool collisionOccurred = false;
        collisionOccurred |= CheckAndHandleHorizontalCollisions();
        collisionOccurred |= CheckAndHandleVerticalCollisions();
        return collisionOccurred;
    }

    private bool CheckAndHandleHorizontalCollisions()
    {
        bool collision = false;
        collision |= CheckAndHandleLeftCollision();
        collision |= CheckAndHandleRightCollision();
        return collision;
    }

    private bool CheckAndHandleVerticalCollisions()
    {
        bool collision = false;
        collision |= CheckAndHandleTopCollision();
        collision |= CheckAndHandleBottomCollision();
        return collision;
    }

    private bool CheckAndHandleLeftCollision()
    {
        if (_position.X < 0)
        {
            SKPoint collisionPoint = new(0, _position.Y);
            _position.X = 0;
            SKPoint normal = new(1, 0);
            ReflectAndHandleCollision(normal, collisionPoint);
            return true;
        }
        return false;
    }

    private bool CheckAndHandleRightCollision()
    {
        if (_position.X + _textWidth > CanvasSize.Width)
        {
            SKPoint collisionPoint = new(CanvasSize.Width, _position.Y);
            _position.X = CanvasSize.Width - _textWidth;
            SKPoint normal = new(-1, 0);
            ReflectAndHandleCollision(normal, collisionPoint);
            return true;
        }
        return false;
    }

    private bool CheckAndHandleTopCollision()
    {
        if (_position.Y - _textHeight < 0)
        {
            SKPoint collisionPoint = new(_position.X + _textWidth / 2, 0);
            _position.Y = _textHeight;
            SKPoint normal = new(0, 1);
            ReflectAndHandleCollision(normal, collisionPoint);
            return true;
        }
        return false;
    }

    private bool CheckAndHandleBottomCollision()
    {
        if (_position.Y > CanvasSize.Height)
        {
            SKPoint collisionPoint = new(_position.X + _textWidth / 2, CanvasSize.Height);
            _position.Y = CanvasSize.Height;
            SKPoint normal = new(0, -1);
            ReflectAndHandleCollision(normal, collisionPoint);
            return true;
        }
        return false;
    }

    private void ReflectAndHandleCollision(SKPoint normal, SKPoint collisionPoint)
    {
        _velocity = SmoothReflect(_velocity, normal, REFLECT_SMOOTHING);
        AddRandomVelocityNoise(COLLISION_VELOCITY_NOISE);
        LimitVelocityMagnitude(ref _velocity, MIN_SPEED, MAX_SPEED);
        GenerateSplash(collisionPoint);
        StartSquashEffect(normal);
    }

    private void StartSquashEffect(SKPoint normal)
    {
        _squashNormal = normal;
        _squashAmount = MAX_SQUASH_AMOUNT;
        _squashTimer = 0f;
    }

    private void ApplyPeriodicRandomNudge()
    {
        _frameCounter++;
        if (_frameCounter >= RANDOM_NUDE_INTERVAL_FRAMES)
        {
            AddRandomVelocityNoise(PERIODIC_VELOCITY_NUDE_MAGNITUDE);
            LimitVelocityMagnitude(ref _velocity, MIN_SPEED, MAX_SPEED);
            _frameCounter = 0;
        }
    }

    private void AddRandomVelocityNoise(float maxMagnitude)
    {
        float angle = (float)(Random.Shared.NextDouble() * 2 * MathF.PI);
        float magnitude = (float)(Random.Shared.NextDouble() * maxMagnitude);
        _velocity.X += MathF.Cos(angle) * magnitude;
        _velocity.Y += MathF.Sin(angle) * magnitude;
    }

    private void SpawnEnergyParticles(float deltaTime)
    {
        _energyParticleSpawnTimer += deltaTime;
        if (_energyParticleSpawnTimer >= ENERGY_PARTICLE_SPAWN_RATE)
        {
            _energyParticleSpawnTimer = 0f;
            if (_activeEnergyParticles.Count < MAX_ENERGY_PARTICLES)
            {
                var particle = _energyParticlePool.Get();
                particle.Initialize(new(_position.X + _textWidth / 2, _position.Y - _textHeight / 2));
                _activeEnergyParticles.Add(particle);
            }
        }
    }

    private void GenerateSplash(SKPoint collisionPoint)
    {
        for (int i = 0; i < SPLASH_PARTICLE_COUNT && _activeSplashParticles.Count < MAX_PARTICLE_POOL_SIZE; i++)
        {
            var particle = _particlePool.Get();
            particle.Initialize(collisionPoint);
            _activeSplashParticles.Add(particle);
        }
    }

    private void GenerateEnergyParticles(SKPoint origin)
    {
        for (int i = 0; i < 3 && _activeEnergyParticles.Count < MAX_ENERGY_PARTICLES; i++)
        {
            var particle = _energyParticlePool.Get();
            particle.Initialize(origin);
            _activeEnergyParticles.Add(particle);
        }
    }

    private void UpdateSplashParticles(float deltaTime)
    {
        for (int i = _activeSplashParticles.Count - 1; i >= 0; i--)
        {
            var particle = _activeSplashParticles[i];
            particle.Update(deltaTime);
            if (particle.IsDead)
            {
                _particlePool.Return(particle);
                _activeSplashParticles.RemoveAt(i);
            }
        }
    }

    private void UpdateEnergyParticles(float deltaTime)
    {
        for (int i = _activeEnergyParticles.Count - 1; i >= 0; i--)
        {
            var particle = _activeEnergyParticles[i];
            particle.Update(deltaTime);
            if (particle.IsDead)
            {
                _energyParticlePool.Return(particle);
                _activeEnergyParticles.RemoveAt(i);
            }
        }
    }

    private void UpdateTrail()
    {
        if (_trailPoints.Count >= MAX_TRAIL_POINTS)
            _trailPoints.RemoveAt(0);
        _trailPoints.Add(new(_position.X + _textWidth / 2, _position.Y - _textHeight / 2));
    }

    private void DrawContent(SKCanvas canvas)
    {
        if (_font is null || _paint is null || _outlinePaint is null) return;

        canvas.Save();

        using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * Transparency)) };
        canvas.SaveLayer(layer);

        DrawTrail(canvas);
        DrawShadow(canvas);

        if (_squashAmount > 0)
        {
            var matrix = CalculateSquashMatrix();
            canvas.Concat(matrix);
        }

        DrawGlow(canvas);
        DrawOutline(canvas);
        DrawMainTextWithShader(canvas);

        canvas.Restore();
        canvas.Restore();
    }

    private void DrawTrail(SKCanvas canvas)
    {
        if (_trailPoints.Count < 2) return;

        using var path = new SKPath();
        for (int i = 0; i < _trailPoints.Count; i++)
        {
            if (i == 0) path.MoveTo(_trailPoints[i]);
            else path.LineTo(_trailPoints[i]);
        }

        var trailPaint = _trailPaint.Clone();
        trailPaint.Color = TRAIL_COLOR.WithAlpha((byte)(TRAIL_COLOR.Alpha * Transparency));
        canvas.DrawPath(path, trailPaint);
        trailPaint.Dispose();
    }

    private void DrawShadow(SKCanvas canvas)
    {
        canvas.Save();
        canvas.Translate(SHADOW_OFFSET_X, SHADOW_OFFSET_Y);
        canvas.DrawText(_message, _position.X, _position.Y, _font, _shadowPaint);
        canvas.Restore();
    }

    private void DrawGlow(SKCanvas canvas)
    {
        var glowPaint = _glowPaint.Clone();
        glowPaint.Color = _glowPaint.Color.WithAlpha((byte)(_glowPaint.Color.Alpha * Transparency));
        canvas.DrawText(_message, _position.X, _position.Y, _font, glowPaint);
        glowPaint.Dispose();
    }

    private SKMatrix CalculateSquashMatrix()
    {
        float squashScaleX = 1f, squashScaleY = 1f;

        if (MathF.Abs(_squashNormal.X) > 0.1f)
        {
            squashScaleX = 1f - _squashAmount;
            squashScaleY = 1f + _squashAmount * (_textWidth / _textHeight);
        }
        else if (MathF.Abs(_squashNormal.Y) > 0.1f)
        {
            squashScaleY = 1f - _squashAmount;
            squashScaleX = 1f + _squashAmount * (_textHeight / _textWidth);
        }

        SKPoint pivot = new(_position.X + _textWidth / 2, _position.Y - _textHeight / 2);
        return SKMatrix.CreateScale(squashScaleX, squashScaleY, pivot.X, pivot.Y);
    }

    private void DrawOutline(SKCanvas canvas)
    {
        var outlinePaint = _outlinePaint.Clone();
        outlinePaint.Color = OUTLINE_COLOR.WithAlpha((byte)(255 * Transparency));
        canvas.DrawText(_message, _position.X, _position.Y, _font, outlinePaint);
        outlinePaint.Dispose();
    }

    private void DrawMainTextWithShader(SKCanvas canvas)
    {
        using var shader = GetOrCreateShader();
        var paint = _paint.Clone();
        paint.Shader = shader;
        paint.Color = SKColors.White.WithAlpha((byte)(255 * Transparency));
        canvas.DrawText(_message, _position.X, _position.Y, _font, paint);
        paint.Dispose();
    }

    private SKShader GetOrCreateShader()
    {
        if (_cachedShader != null &&
            _lastGradientOffset == _gradientOffset &&
            _lastColor1 == _gradientColor1 &&
            _lastColor2 == _gradientColor2 &&
            !_isMouseDown)
        {
            return _cachedShader;
        }

        _cachedShader?.Dispose();
        _cachedShader = CreateTextShader();
        _lastGradientOffset = _gradientOffset;
        _lastColor1 = _gradientColor1;
        _lastColor2 = _gradientColor2;
        return _cachedShader;
    }

    private SKShader CreateTextShader()
    {
        GenerateShaderColors();

        var startPoint = new SKPoint(_position.X, _position.Y - _textHeight / 2);
        var endPoint = new SKPoint(_position.X + _textWidth, _position.Y - _textHeight / 2);

        return SKShader.CreateLinearGradient(
            startPoint,
            endPoint,
            [.. _shaderColors],
            [.. _shaderPositions],
            SKShaderTileMode.Clamp,
            SKMatrix.CreateTranslation(_gradientOffset * _textWidth, 0));
    }

    private void DrawSplashParticles(SKCanvas canvas)
    {
        using var particlePaint = new SKPaint { IsAntialias = true };
        foreach (var particle in _activeSplashParticles)
        {
            if (particle.Size > 0 && particle.Alpha > 0)
            {
                particlePaint.Color = particle.Color.WithAlpha((byte)(particle.Alpha * 255 * Transparency));
                canvas.DrawCircle(particle.Position, particle.Size, particlePaint);
            }
        }
    }

    private void DrawEnergyParticles(SKCanvas canvas)
    {
        using var particlePaint = new SKPaint { IsAntialias = true };
        foreach (var particle in _activeEnergyParticles)
        {
            if (particle.Size > 0 && particle.Alpha > 0)
            {
                particlePaint.Color = particle.Color.WithAlpha((byte)(particle.Alpha * 255 * Transparency));
                canvas.DrawCircle(particle.Position, particle.Size, particlePaint);
            }
        }
    }

    private static SKPoint SmoothReflect(SKPoint vector, SKPoint normal, float smoothing)
    {
        float dotProduct = vector.X * normal.X + vector.Y * normal.Y;
        var perfectReflection = new SKPoint(
            vector.X - 2 * dotProduct * normal.X,
            vector.Y - 2 * dotProduct * normal.Y);
        return new(
            vector.X + (perfectReflection.X - vector.X) * smoothing,
            vector.Y + (perfectReflection.Y - vector.Y) * smoothing);
    }

    private static void LimitVelocityMagnitude(ref SKPoint velocity, float minSpeed, float maxSpeed)
    {
        float currentSpeed = GetMagnitude(velocity);

        if (currentSpeed < minSpeed && currentSpeed > 0)
        {
            float scale = minSpeed / currentSpeed;
            velocity.X *= scale;
            velocity.Y *= scale;
        }
        else if (currentSpeed > maxSpeed)
        {
            float scale = maxSpeed / currentSpeed;
            velocity.X *= scale;
            velocity.Y *= scale;
        }
        else if (currentSpeed == 0 && minSpeed > 0)
        {
            float angle = (float)(Random.Shared.NextDouble() * 2 * MathF.PI);
            velocity.X = MathF.Cos(angle) * minSpeed;
            velocity.Y = MathF.Sin(angle) * minSpeed;
        }
    }

    private static SKPoint LimitMagnitude(SKPoint vector, float minSpeed, float maxSpeed)
    {
        var result = vector;
        LimitVelocityMagnitude(ref result, minSpeed, maxSpeed);
        return result;
    }

    private static float GetMagnitude(SKPoint vector) =>
        MathF.Sqrt(vector.X * vector.X + vector.Y * vector.Y);

    private void ChangeGradientColors()
    {
        _gradientColor1 = _gradientColor2;
        _gradientColor2 = GetNextRandomColor(_gradientColor1);
        GenerateShaderColors();
    }

    private void GenerateShaderColors()
    {
        _gradientColor1.ToHsl(out var h1, out var s1, out var l1);
        _gradientColor2.ToHsl(out var h2, out var s2, out var l2);

        for (int i = 0; i < GRADIENT_STOP_COUNT; i++)
        {
            float position = _shaderPositions[i];
            (float h, float s, float l) interpolatedHsl;

            if (position <= 0.5f)
            {
                float t = position / 0.5f;
                interpolatedHsl = LerpHsl((h1, s1, l1), (h2, s2, l2), t);
            }
            else
            {
                float t = (position - 0.5f) / 0.5f;
                interpolatedHsl = LerpHsl((h2, s2, l2), (h1, s1, l1), t);
            }

            _shaderColors[i] = SKColor.FromHsl(interpolatedHsl.h, interpolatedHsl.s, interpolatedHsl.l);
        }
    }

    private static (float h, float s, float l) LerpHsl((float h, float s, float l) hsl1,
        (float h, float s, float l) hsl2, float t)
    {
        float s = hsl1.s + (hsl2.s - hsl1.s) * t;
        float l = hsl1.l + (hsl2.l - hsl1.l) * t;

        float h1 = hsl1.h;
        float h2 = hsl2.h;
        float hueDelta = h2 - h1;

        if (hueDelta > 180) hueDelta -= 360;
        if (hueDelta < -180) hueDelta += 360;

        float h = h1 + hueDelta * t;
        h %= 360;
        if (h < 0) h += 360;

        return (h, Math.Clamp(s, 0, 100), Math.Clamp(l, 0, 100));
    }

    private static SKColor GetNextRandomColor(SKColor baseColor) =>
        new(GetNextColorComponent(baseColor.Red),
            GetNextColorComponent(baseColor.Green),
            GetNextColorComponent(baseColor.Blue));

    private static byte GetNextColorComponent(byte baseColorComponent) =>
        (byte)Math.Clamp((float)baseColorComponent + Random.Shared.Next(-COLOR_RANDOM_DELTA_RANGE,
            COLOR_RANDOM_DELTA_RANGE + 1), (float)COLOR_CLAMP_MIN, (float)COLOR_CLAMP_MAX);

    private class SplashParticle
    {
        public SKPoint Position;
        public SKPoint Velocity;
        public float Alpha;
        public float Lifetime;
        public float Size;
        public SKColor Color;
        public bool IsDead => Alpha <= 0 || Size <= 0;

        public void Initialize(SKPoint origin)
        {
            Position = origin;
            float angle = (float)(Random.Shared.NextDouble() * 2 * MathF.PI);
            float speed = (float)(Random.Shared.NextDouble() * SPLASH_PARTICLE_SPEED);
            Velocity = new(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed);
            Alpha = 1.0f;
            Lifetime = SPLASH_PARTICLE_LIFETIME;
            Size = SPLASH_PARTICLE_SIZE;
            Color = SPLASH_PARTICLE_COLOR;
        }

        public void Update(float deltaTime)
        {
            Position.X += Velocity.X * deltaTime;
            Position.Y += Velocity.Y * deltaTime;
            Alpha -= deltaTime / Lifetime;
            Size -= deltaTime / (Lifetime * 2);
            Alpha = MathF.Max(0f, Alpha);
            Size = MathF.Max(0f, Size);
        }

        public void Reset()
        {
            Position = SKPoint.Empty;
            Velocity = SKPoint.Empty;
            Alpha = 0;
            Lifetime = 0;
            Size = 0;
            Color = SKColors.Transparent;
        }
    }

    private class EnergyParticle
    {
        public SKPoint Position;
        public SKPoint Velocity;
        public float Alpha;
        public float Lifetime;
        public float Size;
        public SKColor Color;
        public bool IsDead => Alpha <= 0 || Size <= 0;

        public void Initialize(SKPoint origin)
        {
            Position = origin;
            float angle = (float)(Random.Shared.NextDouble() * 2 * MathF.PI);
            float speed = (float)(20 + Random.Shared.NextDouble() * ENERGY_PARTICLE_SPEED);
            Velocity = new(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed);
            Alpha = 1.0f;
            Lifetime = ENERGY_PARTICLE_LIFETIME;
            Size = 2f + (float)(Random.Shared.NextDouble() * 3f);
            Color = SKColors.LightBlue.WithAlpha(180);
        }

        public void Update(float deltaTime)
        {
            Position.X += Velocity.X * deltaTime;
            Position.Y += Velocity.Y * deltaTime;
            Velocity.X *= 0.98f;
            Velocity.Y *= 0.98f;
            Alpha -= deltaTime / Lifetime;
            Size -= deltaTime / (Lifetime * 3);
            Alpha = MathF.Max(0f, Alpha);
            Size = MathF.Max(0f, Size);
        }

        public void Reset()
        {
            Position = SKPoint.Empty;
            Velocity = SKPoint.Empty;
            Alpha = 0;
            Lifetime = 0;
            Size = 0;
            Color = SKColors.Transparent;
        }
    }
}