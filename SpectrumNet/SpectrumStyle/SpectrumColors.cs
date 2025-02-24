#nullable enable

namespace SpectrumNet
{
    public static class ColorUtilities
    {
        private const float AlphaMultiplier = 255f;
        public static SKColor MixColors(SKColor c1, SKColor c2, float amount)
        {
            var inv = 1 - amount;
            return new SKColor(
                (byte)(inv * c1.Red + amount * c2.Red),
                (byte)(inv * c1.Green + amount * c2.Green),
                (byte)(inv * c1.Blue + amount * c2.Blue),
                (byte)(inv * c1.Alpha + amount * c2.Alpha)
            );
        }

        public static SKColor ApplyOpacity(SKColor color, float opacity) =>
            new SKColor(color.Red, color.Green, color.Blue, (byte)(AlphaMultiplier * opacity));
    }

    public static class ColorConstants
    {
        public static readonly SKColor Primary = new(0x21, 0x96, 0xF3, 0xFF);
        public static readonly SKColor PrimaryDark = new(0x19, 0x76, 0xD2, 0xFF);
        public static readonly SKColor Secondary = new(0xFF, 0x40, 0x81, 0xFF);
        public static readonly SKColor Background = new(0x1E, 0x1E, 0x1E, 0xFF);
        public static readonly SKColor Surface = new(0x25, 0x25, 0x25, 0xFF);
        public static readonly SKColor Accent = new(0xFF, 0xC1, 0x07, 0xFF);
        public static readonly SKColor Success = new(0x4C, 0xAF, 0x50, 0xFF);
        public static readonly SKColor Warning = new(0xFF, 0xA7, 0x26, 0xFF);
        public static readonly SKColor Error = new(0xF4, 0x43, 0x36, 0xFF);
        public static readonly SKColor Info = new(0x29, 0xB6, 0xF6, 0xFF);
        public static readonly SKColor Purple = new(0x9C, 0x27, 0xB0, 0xFF);
        public static readonly SKColor Pink = new(0xE9, 0x1E, 0x63, 0xFF);
        public static readonly SKColor Lime = new(0xCD, 0xDC, 0x39, 0xFF);
        public static readonly SKColor Teal = new(0x00, 0x96, 0x88, 0xFF);
        public static readonly SKColor Indigo = new(0x3F, 0x51, 0xB5, 0xFF);
        public static readonly SKColor Brown = new(0x79, 0x55, 0x48, 0xFF);
        public static readonly SKColor Olive = new(0x80, 0x80, 0x00, 0xFF);
        public static readonly SKColor Gold = new(0xFF, 0xD7, 0x00, 0xFF);
        public static readonly SKColor Silver = new(0xC0, 0xC0, 0xC0, 0xFF);
        public static readonly SKColor Coral = new(0xFF, 0x7F, 0x50, 0xFF);
        public static readonly SKColor Turquoise = new(0x40, 0xE0, 0xD0, 0xFF);
        public static readonly SKColor Magenta = new(0xFF, 0x00, 0xFF, 0xFF);
        public static readonly SKColor Cyan = new(0x00, 0xFF, 0xFF, 0xFF);
    }

    public abstract class BaseStyleCommand : IStyleCommand
    {
        public abstract string Name { get; }
        protected static SKPaint BasePaint() => new SKPaint { IsAntialias = true };

        protected static SKPaint CreateLinearGradient(SKColor start, SKColor end, SKPoint startPt, SKPoint endPt, SKShaderTileMode tileMode = SKShaderTileMode.Clamp)
        {
            var paint = BasePaint();
            paint.Shader = SKShader.CreateLinearGradient(startPt, endPt, new[] { start, end }, null, tileMode);
            return paint;
        }

        protected static SKPaint CreateRadialGradient(SKColor start, SKColor end, SKPoint center, float radius)
        {
            var paint = BasePaint();
            paint.Shader = SKShader.CreateRadialGradient(center, radius, new[] { start, end }, new[] { 0f, 1f }, SKShaderTileMode.Clamp);
            return paint;
        }

        public abstract StyleDefinition CreateStyle();
    }

    public sealed class GradientStyleCommand : BaseStyleCommand
    {
        public override string Name => "Gradient";
        public override StyleDefinition CreateStyle() =>
            new(ColorConstants.Primary, ColorConstants.PrimaryDark,
                (start, end) => CreateLinearGradient(start, end, new SKPoint(0, 0), new SKPoint(0, 1)));
    }

    public sealed class SolidStyleCommand : BaseStyleCommand
    {
        public override string Name => "Solid";
        public override StyleDefinition CreateStyle() =>
            new(new SKColor(0xFF, 0x63, 0x47), new SKColor(0xFF, 0x63, 0x47),
                (color, _) => new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true });
    }

    public sealed class RadialStyleCommand : BaseStyleCommand
    {
        public override string Name => "Radial";
        public override StyleDefinition CreateStyle() =>
            new(new SKColor(0x8A, 0x2B, 0xE2), new SKColor(0xFF, 0x14, 0x93),
                (start, end) => CreateRadialGradient(start, end, new SKPoint(0.5f, 0.5f), 0.5f));
    }

    public sealed class NeonStyleCommand : BaseStyleCommand
    {
        public override string Name => "Neon";
        public override StyleDefinition CreateStyle() =>
            new(new SKColor(0x00, 0xFF, 0x00), new SKColor(0x66, 0xFF, 0x66),
                (start, end) => CreateLinearGradient(start, ColorUtilities.MixColors(end, ColorConstants.Primary, 0.5f),
                    new SKPoint(0, 0), new SKPoint(0, 1)));
    }

    public sealed class RadiantGlassStyleCommand : BaseStyleCommand
    {
        public override string Name => "RadiantGlass";
        public override StyleDefinition CreateStyle() =>
            new(new SKColor(0xE0, 0xF7, 0xFA), new SKColor(0xB3, 0xE5, 0xFC),
                (start, end) =>
                {
                    var paint = BasePaint();
                    paint.Shader = SKShader.CreateRadialGradient(
                        new SKPoint(0.5f, 0.5f), 1.0f,
                        new[] { ColorUtilities.ApplyOpacity(start, 0.7f), ColorUtilities.ApplyOpacity(end, 0.4f) },
                        null, SKShaderTileMode.Clamp);
                    return paint;
                });
    }

    public sealed class MetallicStyleCommand : BaseStyleCommand
    {
        public override string Name => "Metallic";
        public override StyleDefinition CreateStyle() =>
            new(new SKColor(0xC0, 0xC0, 0xC0), new SKColor(0xA0, 0xA0, 0xA0),
                (start, end) =>
                {
                    var paint = CreateLinearGradient(start, end, new SKPoint(0, 0), new SKPoint(1, 1));
                    paint.Style = SKPaintStyle.Fill;
                    paint.StrokeCap = SKStrokeCap.Round;
                    paint.StrokeJoin = SKStrokeJoin.Round;
                    return paint;
                });
    }

    public sealed class NeonOutlineStyleCommand : BaseStyleCommand
    {
        private static readonly SKPoint GStart = new(0, 0), GEnd = new(0, 1);
        private static readonly SKMaskFilter NeonBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Solid, 4.0f);
        public override string Name => "NeonOutline";
        public override StyleDefinition CreateStyle() =>
            new(SKColors.Green, SKColors.Magenta,
                (start, end) =>
                {
                    var paint = CreateLinearGradient(start, end, GStart, GEnd);
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 4;
                    paint.StrokeCap = SKStrokeCap.Round;
                    paint.StrokeJoin = SKStrokeJoin.Round;
                    paint.MaskFilter = NeonBlur;
                    return paint;
                });
    }

    public sealed class MinimalistPatternStyleCommand : BaseStyleCommand
    {
        public override string Name => "MinimalistPattern";
        public override StyleDefinition CreateStyle() =>
            new(SKColors.DarkSlateGray, SKColors.LightSteelBlue,
                (primary, secondary) =>
                {
                    const int size = 20;
                    using var bmp = new SKBitmap(size, size);
                    using var canvas = new SKCanvas(bmp);
                    canvas.Clear(SKColors.White);
                    using var p1 = new SKPaint { Color = primary, Style = SKPaintStyle.Fill };
                    using var p2 = new SKPaint { Color = secondary, Style = SKPaintStyle.Fill };
                    canvas.DrawRect(new SKRect(0, 0, size / 2, size / 2), p1);
                    canvas.DrawRect(new SKRect(size / 2, size / 2, size, size), p1);
                    canvas.DrawCircle(size / 2, size / 2, size / 4, p2);
                    var paint = BasePaint();
                    paint.Shader = SKShader.CreateBitmap(bmp, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
                    return paint;
                });
    }

    public sealed class EnhancedGradientStyleCommand : BaseStyleCommand
    {
        public override string Name => "EnhancedGradient";
        public override StyleDefinition CreateStyle()
        {
            var start = SKColors.MidnightBlue;
            var end = SKColors.Firebrick;
            return new StyleDefinition(start, end, CreateEnhancedGradient);
        }

        private static SKPaint CreateEnhancedGradient(SKColor start, SKColor end)
        {
            var paint = BasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, 300),
                new[] { start, SKColors.BlueViolet, end },
                new[] { 0f, 0.5f, 1f },
                SKShaderTileMode.Clamp);
            paint.BlendMode = SKBlendMode.Screen;
            return paint;
        }
    }

    public sealed class ShinyStyleCommand : BaseStyleCommand
    {
        public override string Name => "Shiny";
        public override StyleDefinition CreateStyle() =>
            new(SKColors.DeepSkyBlue, SKColors.Blue, CreateSpecularHighlightPaint);

        private static SKPaint CreateSpecularHighlightPaint(SKColor baseColor, SKColor highlightColor)
        {
            var paint = BasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(1, 1),
                new[] { baseColor, highlightColor, baseColor },
                new[] { 0f, 0.3f, 1f },
                SKShaderTileMode.Clamp);
            paint.ColorFilter = SKColorFilter.CreateBlendMode(SKColors.White.WithAlpha(50), SKBlendMode.Overlay);
            paint.StrokeCap = SKStrokeCap.Round;
            return paint;
        }
    }

    public sealed class DarkGradientStyleCommand : BaseStyleCommand
    {
        public override string Name => "DarkGradient";
        public override StyleDefinition CreateStyle() =>
            new(new SKColor(0x33, 0x33, 0x33), new SKColor(0x66, 0x66, 0x66),
                (start, end) => CreateLinearGradient(start, end, new SKPoint(0, 0), new SKPoint(0, 1)));
    }

    public sealed class LightPatternStyleCommand : BaseStyleCommand
    {
        public override string Name => "LightPattern";
        public override StyleDefinition CreateStyle() =>
            new(SKColors.White, SKColors.LightGray,
                (start, end) =>
                {
                    using var bmp = new SKBitmap(20, 20);
                    using var canvas = new SKCanvas(bmp);
                    using var p = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.LightGray };
                    canvas.Clear(SKColors.White);
                    canvas.DrawCircle(10, 10, 5, p);
                    var paint = BasePaint();
                    paint.Shader = SKShader.CreateBitmap(bmp, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
                    return paint;
                });
    }

    public sealed class FrostedGlassStyleCommand : BaseStyleCommand
    {
        public override string Name => "FrostedGlass";
        public override StyleDefinition CreateStyle() =>
            new(SKColors.White, SKColors.LightGray, CreateFrostedGlassPaint);

        private static SKPaint CreateFrostedGlassPaint(SKColor start, SKColor end)
        {
            var paint = BasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, 1),
                new[] { start, end }, null, SKShaderTileMode.Clamp);
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5.0f);
            paint.Color = ColorUtilities.ApplyOpacity(paint.Color, 0.5f);
            return paint;
        }
    }

    public sealed class MetallicGradientStyleCommand : BaseStyleCommand
    {
        public override string Name => "MetallicGradient";
        public override StyleDefinition CreateStyle() =>
            new(new SKColor(0x80, 0x80, 0x80), new SKColor(0xE0, 0xE0, 0xE0),
                CreateMetallicGradientPaint);

        private static SKPaint CreateMetallicGradientPaint(SKColor start, SKColor end)
        {
            var paint = BasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(200, 200),
                new[] { start, SKColors.White.WithAlpha(128), end, SKColors.Black.WithAlpha(64), start },
                new[] { 0f, 0.3f, 0.6f, 0.8f, 1f },
                SKShaderTileMode.Clamp);
            paint.BlendMode = SKBlendMode.Overlay;
            var highlight = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, 100),
                new[] { SKColors.White.WithAlpha(50), SKColors.Transparent },
                null, SKShaderTileMode.Clamp);
            paint.Shader = SKShader.CreateCompose(highlight, paint.Shader, SKBlendMode.SoftLight);
            return paint;
        }
    }

    public sealed class RainbowStyleCommand : BaseStyleCommand
    {
        public override string Name => "Rainbow";
        public override StyleDefinition CreateStyle() =>
            new(SKColors.Red, SKColors.Violet,
                (_, __) =>
                {
                    var colors = new[]
                    {
                        SKColors.Red,
                        SKColors.Orange,
                        SKColors.Yellow,
                        SKColors.Green,
                        SKColors.Blue,
                        SKColors.Indigo,
                        SKColors.Violet
                    };
                    var paint = BasePaint();
                    paint.Shader = SKShader.CreateSweepGradient(new SKPoint(0.5f, 0.5f), colors);
                    return paint;
                });
    }

    public sealed class ShadowedStyleCommand : BaseStyleCommand
    {
        private static readonly SKPoint GStart = new(0, 0), GEnd = new(0, 1);
        private static readonly SKMaskFilter BlurFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Outer, 5.0f);
        public override string Name => "Shadowed";
        public override StyleDefinition CreateStyle() =>
            new(SKColors.Gray, SKColors.DarkGray,
                (start, end) =>
                {
                    var paint = BasePaint();
                    paint.Shader = SKShader.CreateLinearGradient(GStart, GEnd, new[] { start, end }, null, SKShaderTileMode.Clamp);
                    paint.MaskFilter = BlurFilter;
                    return paint;
                });
    }

    public sealed class TextureStyleCommand : BaseStyleCommand
    {
        public override string Name => "Texture";
        public override StyleDefinition CreateStyle() =>
            new(SKColors.LightBlue, SKColors.LightGreen,
                (start, end) =>
                {
                    using var bmp = new SKBitmap(40, 40);
                    using var canvas = new SKCanvas(bmp);
                    var grad = new SKPaint
                    {
                        Shader = SKShader.CreateLinearGradient(
                            new SKPoint(0, 0), new SKPoint(40, 40),
                            new[] { SKColors.LightBlue, SKColors.LightGreen },
                            null, SKShaderTileMode.Clamp)
                    };
                    canvas.DrawRect(0, 0, 40, 40, grad);
                    using var linePaint = new SKPaint { Color = SKColors.DarkBlue, StrokeWidth = 2 };
                    for (int y = 5; y < 40; y += 10)
                        canvas.DrawLine(0, y, 40, y, linePaint);
                    var paint = BasePaint();
                    paint.Shader = SKShader.CreateBitmap(bmp, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
                    return paint;
                });
    }

    public sealed class GlowStyleCommand : BaseStyleCommand
    {
        public override string Name => "Glow";
        public override StyleDefinition CreateStyle() =>
            new(SKColors.Yellow, SKColors.Gold,
                (start, end) =>
                {
                    var paint = BasePaint();
                    paint.Shader = SKShader.CreateRadialGradient(new SKPoint(0.5f, 0.5f), 0.5f, new[] { start, end }, null, SKShaderTileMode.Clamp);
                    paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.0f);
                    return paint;
                });
    }

    public sealed class MosaicStyleCommand : BaseStyleCommand
    {
        public override string Name => "Mosaic";
        public override StyleDefinition CreateStyle() =>
            new(SKColors.Cyan, SKColors.Azure,
                (start, end) =>
                {
                    using var bmp = new SKBitmap(10, 10);
                    using var canvas = new SKCanvas(bmp);
                    canvas.Clear(SKColors.White);
                    using var pStart = new SKPaint { Color = start, Style = SKPaintStyle.Fill };
                    using var pEnd = new SKPaint { Color = end, Style = SKPaintStyle.Fill };
                    canvas.DrawRect(new SKRect(0, 0, 5, 5), pStart);
                    canvas.DrawRect(new SKRect(5, 5, 10, 10), pEnd);
                    var paint = BasePaint();
                    paint.Shader = SKShader.CreateBitmap(bmp, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
                    return paint;
                });
    }

    public sealed class SparkleStyleCommand : BaseStyleCommand
    {
        public override string Name => "Sparkle";
        public override StyleDefinition CreateStyle() =>
            new(SKColors.Gold, SKColors.Purple, CreateSparklePaint);

        private static SKPaint CreateSparklePaint(SKColor start, SKColor end)
        {
            var paint = BasePaint();
            paint.Shader = SKShader.CreateRadialGradient(new SKPoint(0.5f, 0.5f), 0.5f,
                new[] { start, end },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp);
            paint.BlendMode = SKBlendMode.Lighten;
            return paint;
        }
    }

    public sealed class SoftGradientStyleCommand : BaseStyleCommand
    {
        public override string Name => "SoftGradient";
        public override StyleDefinition CreateStyle() =>
            new(SKColors.Teal, SKColors.OrangeRed, CreateSoftGradientPaint);

        private static SKPaint CreateSoftGradientPaint(SKColor start, SKColor end)
        {
            var paint = BasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, 1),
                new[] { start, SKColors.LightSeaGreen, SKColors.Goldenrod, end },
                new[] { 0f, 0.4f, 0.7f, 1f },
                SKShaderTileMode.Clamp);
            paint.ColorFilter = SKColorFilter.CreateBlendMode(SKColors.White.WithAlpha(50), SKBlendMode.SoftLight);
            return paint;
        }
    }

    public sealed class DarkNeonStyleCommand : BaseStyleCommand
    {
        public override string Name => "DarkNeon";
        public override StyleDefinition CreateStyle() =>
            new(SKColors.DeepSkyBlue, SKColors.DodgerBlue, CreateDarkNeonPaint);

        private static SKPaint CreateDarkNeonPaint(SKColor start, SKColor end)
        {
            var paint = BasePaint();
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1, 1), new[] { start, end }, null, SKShaderTileMode.Clamp);
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Outer, 3.0f);
            return paint;
        }
    }

    public sealed class VaporwaveStyleCommand : BaseStyleCommand
    {
        private static readonly SKColor NeonPink = new(0xFF, 0x00, 0xFF);
        private static readonly SKColor NeonBlue = new(0x00, 0xFF, 0xFF);
        public override string Name => "Vaporwave";
        public override StyleDefinition CreateStyle() =>
            new(NeonPink, NeonBlue, CreateVaporwavePaint);

        private static SKPaint CreateVaporwavePaint(SKColor start, SKColor end)
        {
            var paint = BasePaint();
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1, 1), new[] { start, end }, null, SKShaderTileMode.Mirror);
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.0f);
            paint.Color = ColorUtilities.ApplyOpacity(paint.Color, 0.9f);
            return paint;
        }
    }

    public sealed class EmeraldGlowStyleCommand : BaseStyleCommand
    {
        private static readonly SKColor EmeraldStart = new(0x50, 0xE3, 0x50);
        private static readonly SKColor EmeraldEnd = new(0x00, 0xC9, 0x00);
        public override string Name => "EmeraldGlow";
        public override StyleDefinition CreateStyle() =>
            new(EmeraldStart, EmeraldEnd, CreateEmeraldGlowPaint);

        private static SKPaint CreateEmeraldGlowPaint(SKColor start, SKColor end)
        {
            var paint = BasePaint();
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1, 0), new[] { start, end }, null, SKShaderTileMode.Mirror);
            paint.Color = ColorUtilities.ApplyOpacity(paint.Color, 0.8f);
            return paint;
        }
    }
}
