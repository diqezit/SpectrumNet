#nullable enable

namespace SpectrumNet.Views.Utils;

public sealed class RendererPlaceholder : IDisposable
{
    private const string
        DEFAULT_MESSAGE = "Push SPACE to begin...";

    private static readonly SKColor DefaultColor1 = SKColors.OrangeRed;
    private static readonly SKColor DefaultColor2 = SKColors.Gold;
    private static readonly float DefaultFontSize = 50;
    private const float GRADIENT_SPEED = 0.2f;
    private static readonly float[] GradientPositions = [0.0f, 0.5f, 1.0f];

    private readonly SKFont _font;
    private readonly SKPaint _paint;
    private readonly string _message;
    private readonly float _textWidth;
    private readonly float _textHeight;

    private SKPoint _position;
    private SKPoint _velocity;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private double _lastElapsedTime = 0;
    private bool _isDisposed;
    private SKColor _gradientColor1;
    private SKColor _gradientColor2;
    private float _gradientOffset;

    public required SKSize CanvasSize { get; set; }

    public RendererPlaceholder(string? message = null)
    {
        _message = message ?? DEFAULT_MESSAGE;
        _velocity = new(150, 100);
        _font = new() { Size = DefaultFontSize, Subpixel = true };
        _paint = new() { IsAntialias = true };
        _gradientColor1 = DefaultColor1;
        _gradientColor2 = DefaultColor2;
        _textWidth = _font.MeasureText(_message, _paint);
        _textHeight = _font.Size;
        _position = new(0, DefaultFontSize);
        _lastElapsedTime = _stopwatch.Elapsed.TotalSeconds;
    }

    public void Render(SKCanvas canvas, SKImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        CanvasSize = new(info.Width, info.Height);

        var deltaTime = CalculateDeltaTime();

        UpdatePosition(deltaTime);
        HandleBoundsCollision(info.Width, info.Height);
        UpdateGradientOffset(deltaTime);

        DrawContent(canvas);
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        _font?.Dispose();
        _paint?.Dispose();

        _isDisposed = true;
    }

    private float CalculateDeltaTime()
    {
        var currentTime = _stopwatch.Elapsed.TotalSeconds;
        var deltaTime = currentTime - _lastElapsedTime;
        _lastElapsedTime = currentTime;
        return (float)deltaTime;
    }

    private void UpdatePosition(float deltaTime)
    {
        _position = new(
          _position.X + _velocity.X * deltaTime,
          _position.Y + _velocity.Y * deltaTime);
    }

    private void UpdateGradientOffset(float deltaTime) =>
        _gradientOffset = (_gradientOffset + GRADIENT_SPEED * deltaTime) % 1.0f;

    private void HandleBoundsCollision(float width, float height)
    {
        bool collision = false;

        collision |= HandleHorizontalCollision(width);
        collision |= HandleVerticalCollision(height);

        if (collision)
        {
            ChangeGradientColors();
        }
    }

    private bool HandleHorizontalCollision(float width)
    {
        bool collision = false;
        collision |= HandleLeftCollision();
        collision |= HandleRightCollision(width);
        return collision;
    }

    private bool HandleVerticalCollision(float height)
    {
        bool collision = false;
        collision |= HandleTopCollision();
        collision |= HandleBottomCollision(height);
        return collision;
    }

    private bool HandleLeftCollision()
    {
        if (_position.X < 0)
        {
            _velocity = _velocity with { X = -_velocity.X };
            _position = _position with { X = 0 };
            return true;
        }
        return false;
    }

    private bool HandleRightCollision(float width)
    {
        float rightEdge = width - _textWidth;
        if (_position.X > rightEdge)
        {
            _velocity = _velocity with { X = -_velocity.X };
            _position = _position with { X = rightEdge };
            return true;
        }
        return false;
    }

    private bool HandleTopCollision()
    {
        if (_position.Y - _textHeight < 0)
        {
            _velocity = _velocity with { Y = -_velocity.Y };
            _position = _position with { Y = _textHeight };
            return true;
        }
        return false;
    }

    private bool HandleBottomCollision(float height)
    {
        if (_position.Y > height)
        {
            _velocity = _velocity with { Y = -_velocity.Y };
            _position = _position with { Y = height };
            return true;
        }
        return false;
    }

    private void DrawContent(SKCanvas canvas)
    {
        using var shader = SKShader.CreateLinearGradient(
          new SKPoint(_position.X, _position.Y),
          new SKPoint(_position.X + _textWidth, _position.Y),
          [_gradientColor1, _gradientColor2, _gradientColor1],
          GradientPositions,
          SKShaderTileMode.Clamp,
          SKMatrix.CreateTranslation(_gradientOffset * _textWidth, 0)
        );

        _paint.Shader = shader;
        canvas.DrawText(_message, _position.X, _position.Y, _font, _paint);
        _paint.Shader = null;
    }

    private void ChangeGradientColors()
    {
        _gradientColor1 = _gradientColor2;
        _gradientColor2 = new SKColor(
          (byte)Clamp(
            _gradientColor1.Red + Random.Shared.Next(-20, 20),
            100,
            255),
          (byte)Clamp(
            _gradientColor1.Green + Random.Shared.Next(-20, 20),
            100,
            255),
          (byte)Clamp(
            _gradientColor1.Blue + Random.Shared.Next(-20, 20),
            100,
            255)
        );
    }
}