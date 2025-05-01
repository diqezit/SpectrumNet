#nullable enable

namespace SpectrumNet.Views.Utils;
public sealed class RendererPlaceholder : IDisposable
{
    private const string DEFAULT_MESSAGE = "Push SPACE to begin...";
    private static readonly SKColor DefaultColor1 = SKColors.OrangeRed;
    private static readonly SKColor DefaultColor2 = SKColors.Gold;
    private static readonly float DefaultFontSize = 50;
    private SKPoint _position;
    private SKPoint _velocity;
    private float _textWidth;
    private float _textHeight;
    private readonly SKFont _font;
    private readonly SKPaint _paint;
    private readonly string _message;
    private DateTime _lastUpdateTime = Now;
    private bool _isDisposed;
    private SKColor _gradientColor1;
    private SKColor _gradientColor2;
    private float _gradientOffset;
    private const float GRADIENT_SPEED = 0.2f;

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
    }

    public void Render(SKCanvas canvas, SKImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        CanvasSize = new(info.Width, info.Height);

        var deltaTime = (float)(Now - _lastUpdateTime).TotalSeconds;
        _lastUpdateTime = Now;

        _position = new(_position.X + _velocity.X * deltaTime, _position.Y + _velocity.Y * deltaTime);
        HandleBoundsCollision(info);

        // Update gradient offset for animation
        _gradientOffset = (_gradientOffset + GRADIENT_SPEED * deltaTime) % 1.0f;

        // Create gradient shader for text
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(_position.X, _position.Y),
            new SKPoint(_position.X + _textWidth, _position.Y),
            new[] { _gradientColor1, _gradientColor2, _gradientColor1 },
            new[] { 0.0f, 0.5f, 1.0f },
            SKShaderTileMode.Clamp,
            SKMatrix.CreateTranslation(_gradientOffset * _textWidth, 0)
        );

        _paint.Shader = shader;
        canvas.DrawText(_message, _position.X, _position.Y, _font, _paint);
        _paint.Shader = null;
    }

    private void HandleBoundsCollision(SKImageInfo info)
    {
        bool collision = false;

        // Проверка левой границы
        if (_position.X < 0)
        {
            _velocity = _velocity with { X = -_velocity.X };
            _position = _position with { X = 0 };
            collision = true;
        }

        // Проверка правой границы
        float rightEdge = info.Width - _textWidth;
        if (_position.X > rightEdge)
        {
            _velocity = _velocity with { X = -_velocity.X };
            _position = _position with { X = rightEdge };
            collision = true;
        }

        // Проверка верхней границы
        if (_position.Y - _textHeight < 0)
        {
            _velocity = _velocity with { Y = -_velocity.Y };
            _position = _position with { Y = _textHeight };
            collision = true;
        }

        // Проверка нижней границы
        if (_position.Y > info.Height)
        {
            _velocity = _velocity with { Y = -_velocity.Y };
            _position = _position with { Y = info.Height };
            collision = true;
        }

        // Change colors on any collision
        if (collision)
        {
            // Smooth transition - shift current colors and generate a new secondary color
            _gradientColor1 = _gradientColor2;
            _gradientColor2 = new SKColor(
                (byte)Math.Clamp(_gradientColor1.Red + Random.Shared.Next(-20, 20), 100, 255),
                (byte)Math.Clamp(_gradientColor1.Green + Random.Shared.Next(-20, 20), 100, 255),
                (byte)Math.Clamp(_gradientColor1.Blue + Random.Shared.Next(-20, 20), 100, 255)
            );
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _font?.Dispose();
        _paint?.Dispose();
        _isDisposed = true;
    }
}