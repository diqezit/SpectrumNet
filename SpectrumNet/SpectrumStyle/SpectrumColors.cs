#nullable enable

namespace SpectrumNet
{
    public enum StyleType
    {
        Gradient,
        Solid,
        Radial,
        Neon,
        RadiantGlass,
        Minimalist,
        Metallic,
        NeonOutline,
        EnhancedGradient,

        // New styles
        Shiny,
        DarkGradient,
        LightPattern,
        FrostedGlass,
        MetallicGradient,
        Rainbow,
        Shadowed,
        Texture,
        Glow,
        Mosaic,
        Sparkle,
        SoftGradient,
        DarkNeon,
        Vaporwave,
        EmeraldGlow,
    }

    public interface IStyleCommand
    {
        string Name { get; }
        StyleDefinition CreateStyle();
    }

    public static class ColorUtilities
    {
        private const float AlphaMultiplier = 255f;

        public static SKColor MixColors(SKColor color1, SKColor color2, float amount)
        {
            float inverse = 1 - amount;
            return new SKColor(
                (byte)(color1.Red * inverse + color2.Red * amount),
                (byte)(color1.Green * inverse + color2.Green * amount),
                (byte)(color1.Blue * inverse + color2.Blue * amount),
                (byte)(color1.Alpha * inverse + color2.Alpha * amount)
            );
        }

        public static SKColor ApplyOpacity(SKColor color, float opacity) =>
            new(color.Red, color.Green, color.Blue, (byte)(AlphaMultiplier * opacity));
    }

    public static class ColorConstants
    {
        public static readonly SKColor Primary = new(0x21, 0x96, 0xF3, 0xFF);
        public static readonly SKColor PrimaryDark = new(0x19, 0x76, 0xD2, 0xFF);
        public static readonly SKColor Secondary = new(0xFF, 0x40, 0x81, 0xFF);
        public static readonly SKColor Background = new(0x1E, 0x1E, 0x1E, 0xFF);
        public static readonly SKColor Surface = new(0x25, 0x25, 0x25, 0xFF);
    }

    public static class StyleFactory
    {
        private static readonly IStyleCommand[] StyleCommands = new IStyleCommand[]
        {
        new GradientStyleCommand(),
        new SolidStyleCommand(),
        new RadialStyleCommand(),
        new NeonStyleCommand(),
        new RadiantGlassStyleCommand(),
        new MinimalistPatternStyleCommand(),
        new MetallicStyleCommand(),
        new NeonOutlineStyleCommand(),
        new EnhancedGradientStyleCommand(),
        new ShinyStyleCommand(),
        new DarkGradientStyleCommand(),
        new LightPatternStyleCommand(),
        new FrostedGlassStyleCommand(),
        new MetallicGradientStyleCommand(),
        new RainbowStyleCommand(),
        new ShadowedStyleCommand(),
        new TextureStyleCommand(),
        new GlowStyleCommand(),
        new MosaicStyleCommand(),
        new SparkleStyleCommand(),
        new SoftGradientStyleCommand(),
        new DarkNeonStyleCommand(),
        new VaporwaveStyleCommand(),
        new EmeraldGlowStyleCommand(),
        };

        public static IStyleCommand CreateStyleCommand(StyleType styleType)
        {
            if (!Enum.IsDefined(typeof(StyleType), styleType))
                throw new ArgumentException($"Unknown style type: {styleType}", nameof(styleType));
            return StyleCommands[(int)styleType];
        }
    }

    public abstract class BaseStyleCommand : IStyleCommand
    {
        public abstract string Name { get; }

        protected static SKPaint CreateBasePaint() => new()
        {
            IsAntialias = true
        };

        public abstract StyleDefinition CreateStyle();

        protected static SKPaint CreateLinearGradient(
            SKColor start,
            SKColor end,
            SKPoint startPoint,
            SKPoint endPoint,
            SKShaderTileMode tileMode = SKShaderTileMode.Clamp)
        {
            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                startPoint,
                endPoint,
                new[] { start, end },
                null,
                tileMode
            );
            return paint;
        }

        protected static SKPaint CreateRadialGradient(
            SKColor start,
            SKColor end,
            SKPoint center,
            float radius)
        {
            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateRadialGradient(
                center,
                radius,
                new[] { start, end },
                new float[] { 0, 1 },
                SKShaderTileMode.Clamp
            );
            return paint;
        }
    }

    public sealed class GradientStyleCommand : BaseStyleCommand
    {
        public override string Name => "Gradient";

        public override StyleDefinition CreateStyle() =>
            new(
                ColorConstants.Primary,
                ColorConstants.PrimaryDark,
                (start, end) => CreateLinearGradient(
                    start,
                    end,
                    new SKPoint(0, 0),
                    new SKPoint(0, 1)
                )
            );
    }

    public sealed class SolidStyleCommand : BaseStyleCommand
    {
        public override string Name => "Solid";

        public override StyleDefinition CreateStyle() =>
            new(
                new SKColor(0xFF, 0x63, 0x47),
                new SKColor(0xFF, 0x63, 0x47),
                (color, _) => new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                }
            );
    }

    public sealed class RadialStyleCommand : BaseStyleCommand
    {
        public override string Name => "Radial";

        public override StyleDefinition CreateStyle() =>
            new(
                new SKColor(0x8A, 0x2B, 0xE2),
                new SKColor(0xFF, 0x14, 0x93),
                (start, end) => CreateRadialGradient(
                    start,
                    end,
                    new SKPoint(0.5f, 0.5f),
                    0.5f
                )
            );
    }

    public sealed class NeonStyleCommand : BaseStyleCommand
    {
        public override string Name => "Neon";

        public override StyleDefinition CreateStyle() =>
            new(
                new SKColor(0x00, 0xFF, 0x00),
                new SKColor(0x66, 0xFF, 0x66),
                (start, end) => CreateLinearGradient(
                    start,
                    ColorUtilities.MixColors(end, ColorConstants.Primary, 0.5f),
                    new SKPoint(0, 0),
                    new SKPoint(0, 1)
                )
            );
    }

    public sealed class RadiantGlassStyleCommand : BaseStyleCommand
    {
        public override string Name => "RadiantGlass";

        public override StyleDefinition CreateStyle() =>
            new(
                new SKColor(0xE0, 0xF7, 0xFA),
                new SKColor(0xB3, 0xE5, 0xFC),
                (startColor, endColor) =>
                {
                    var paint = CreateBasePaint();
                    paint.Shader = SKShader.CreateRadialGradient(
                        new SKPoint(0.5f, 0.5f),
                        1.0f,
                        new[]
                        {
                        ColorUtilities.ApplyOpacity(startColor, 0.7f),
                        ColorUtilities.ApplyOpacity(endColor, 0.4f)
                        },
                        null,
                        SKShaderTileMode.Clamp
                    );
                    return paint;
                });
    }

    public sealed class MetallicStyleCommand : BaseStyleCommand
    {
        public override string Name => "Metallic";

        public override StyleDefinition CreateStyle() =>
            new(
                new SKColor(0xC0, 0xC0, 0xC0),
                new SKColor(0xA0, 0xA0, 0xA0),
                (start, end) =>
                {
                    var paint = CreateLinearGradient(
                    start, end,
                    new SKPoint(0, 0),
                    new SKPoint(1, 1)
                );
                    // Add metallic styling properties
                    paint.Style = SKPaintStyle.Fill;
                    paint.StrokeCap = SKStrokeCap.Round;
                    paint.StrokeJoin = SKStrokeJoin.Round;
                    return paint;
                });
    }

    public sealed class NeonOutlineStyleCommand : BaseStyleCommand
    {
        public override string Name => "NeonOutline";

        public override StyleDefinition CreateStyle() =>
            new(SKColors.Green, SKColors.Magenta, (start, end) =>
            {
                var paint = CreateLinearGradient(
                    start, end,
                    new SKPoint(0, 0),
                    new SKPoint(0, 1)
                );
                // Configure for outline effect
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 4;
                paint.StrokeCap = SKStrokeCap.Round;
                paint.StrokeJoin = SKStrokeJoin.Round;
                // Add neon-like blur effect
                paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Solid, 4.0f);
                return paint;
            });
    }

    public sealed class MinimalistPatternStyleCommand : BaseStyleCommand
    {
        public override string Name => "MinimalistPattern";

        public override StyleDefinition CreateStyle() =>
            new(SKColors.DarkSlateGray, SKColors.LightSteelBlue, (primary, secondary) =>
            {
                const int gridSize = 20; // Grid cell size
                using var bitmap = new SKBitmap(gridSize, gridSize);
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(SKColors.White);

                // Create paints for primary and secondary colors
                using var primaryPaint = new SKPaint { Color = primary, Style = SKPaintStyle.Fill };
                using var secondaryPaint = new SKPaint { Color = secondary, Style = SKPaintStyle.Fill };

                // Draw a simple geometric pattern
                canvas.DrawRect(new SKRect(0, 0, gridSize / 2, gridSize / 2), primaryPaint); // Top left
                canvas.DrawRect(new SKRect(gridSize / 2, gridSize / 2, gridSize, gridSize), primaryPaint); // Bottom right
                canvas.DrawCircle(gridSize / 2, gridSize / 2, gridSize / 4, secondaryPaint); // Center circle

                var paint = CreateBasePaint();
                paint.Shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
                return paint;
            });
    }

    // New Styles
    public sealed class EnhancedGradientStyleCommand : BaseStyleCommand
    {
        public override string Name => "EnhancedGradient";

        public override StyleDefinition CreateStyle()
        {
            var startColor = SKColors.MidnightBlue;
            var endColor = SKColors.Firebrick;

            return new StyleDefinition(startColor, endColor, CreateEnhancedGradient);
        }

        private static Func<SKColor, SKColor, SKPaint> CreateEnhancedGradient = (startColor, endColor) =>
        {
            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, 300),
                new[] { startColor, SKColors.BlueViolet, endColor },
                new float[] { 0f, 0.5f, 1f },
                SKShaderTileMode.Clamp
            );
            paint.BlendMode = SKBlendMode.Screen;
            return paint;
        };
    }

    public sealed class ShinyStyleCommand : BaseStyleCommand
    {
        public override string Name => "Shiny";

        public override StyleDefinition CreateStyle() =>
            new(SKColors.DeepSkyBlue, SKColors.Blue, CreateSpecularHighlightPaint);

        private static SKPaint CreateSpecularHighlightPaint(SKColor baseColor, SKColor highlightColor)
        {
            var paint = CreateBasePaint();

            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),   
                new SKPoint(1, 1),   
                new[]
                {
                baseColor,       
                highlightColor,   
                baseColor         
                },
                new float[] { 0f, 0.3f, 1f },
                SKShaderTileMode.Clamp
            );

            paint.ColorFilter = SKColorFilter.CreateBlendMode(
                SKColors.White.WithAlpha(50),  
                SKBlendMode.Overlay
            );

            paint.StrokeCap = SKStrokeCap.Round;

            return paint;
        }
    }

    public sealed class DarkGradientStyleCommand : BaseStyleCommand
    {
        public override string Name => "DarkGradient";

        public override StyleDefinition CreateStyle() =>
            new(
                new SKColor(0x33, 0x33, 0x33),
                new SKColor(0x66, 0x66, 0x66),
                CreateDarkGradientPaint
            );

        private static SKPaint CreateDarkGradientPaint(SKColor start, SKColor end) =>
            CreateLinearGradient(start, end, new SKPoint(0, 0), new SKPoint(0, 1));
    }

    public sealed class LightPatternStyleCommand : BaseStyleCommand
    {
        public override string Name => "LightPattern";

        public override StyleDefinition CreateStyle() =>
            new(SKColors.White, SKColors.LightGray, CreateLightPatternPaint);

        private static SKPaint CreateLightPatternPaint(SKColor start, SKColor end)
        {
            using var bitmap = new SKBitmap(20, 20);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.LightGray };

            canvas.Clear(SKColors.White);
            canvas.DrawCircle(10, 10, 5, paint);

            var patternPaint = CreateBasePaint();
            patternPaint.Shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            return patternPaint;
        }
    }

    public sealed class FrostedGlassStyleCommand : BaseStyleCommand
    {
        public override string Name => "FrostedGlass";

        public override StyleDefinition CreateStyle() =>
            new(SKColors.White, SKColors.LightGray, CreateFrostedGlassPaint);

        private static SKPaint CreateFrostedGlassPaint(SKColor start, SKColor end)
        {
            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, 1),
                new[] { start, end },
                null,
                SKShaderTileMode.Clamp
            );

            const float blurRadius = 5.0f;
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);

            const float opacity = 0.5f;
            paint.Color = ColorUtilities.ApplyOpacity(paint.Color, opacity);

            return paint;
        }
    }

    public sealed class MetallicGradientStyleCommand : BaseStyleCommand
    {
        public override string Name => "MetallicGradient";

        public override StyleDefinition CreateStyle() =>
            new(
                new SKColor(0x80, 0x80, 0x80),
                new SKColor(0xE0, 0xE0, 0xE0),
                CreateMetallicGradientPaint
            );

        private static SKPaint CreateMetallicGradientPaint(SKColor start, SKColor end)
        {
            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(200, 200),
                new[]
                {
                start,
                SKColors.White.WithAlpha(128),
                end,
                SKColors.Black.WithAlpha(64),
                start
                },
                new float[] { 0f, 0.3f, 0.6f, 0.8f, 1f },
                SKShaderTileMode.Clamp
            );

            paint.BlendMode = SKBlendMode.Overlay;

            var highlightShader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, 100),
                new[] { SKColors.White.WithAlpha(50), SKColors.Transparent },
                null,
                SKShaderTileMode.Clamp
            );

            paint.Shader = SKShader.CreateCompose(highlightShader, paint.Shader, SKBlendMode.SoftLight);

            return paint;
        }
    }

    public sealed class RainbowStyleCommand : BaseStyleCommand
    {
        public override string Name => "Rainbow";

        public override StyleDefinition CreateStyle() =>
            new(SKColors.Red, SKColors.Violet, (_, __) =>
            {
                var rainbowColors = new SKColor[]
                {
                SKColors.Red,
                SKColors.Orange,
                SKColors.Yellow,
                SKColors.Green,
                SKColors.Blue,
                SKColors.Indigo,
                SKColors.Violet
                };

                var paint = CreateBasePaint();
                paint.Shader = SKShader.CreateSweepGradient(
                    new SKPoint(0.5f, 0.5f),
                    rainbowColors
                );
                return paint;
            });
    }

    public sealed class ShadowedStyleCommand : BaseStyleCommand
    {
        public override string Name => "Shadowed";

        public override StyleDefinition CreateStyle() =>
            new(SKColors.Gray, SKColors.DarkGray, (start, end) =>
            {
                var paint = CreateBasePaint();
                paint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(0, 1),
                    new[] { start, end },
                    null,
                    SKShaderTileMode.Clamp
                );
                paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Outer, 5.0f);
                return paint;
            });
    }

    public sealed class TextureStyleCommand : BaseStyleCommand
    {
        public override string Name => "Texture";

        public override StyleDefinition CreateStyle() =>
            new(SKColors.LightBlue, SKColors.LightGreen, (start, end) =>
            {
                using var bitmap = new SKBitmap(40, 40);
                using var canvas = new SKCanvas(bitmap);

                // Background gradient
                var gradientPaint = new SKPaint
                {
                    Shader = SKShader.CreateLinearGradient(
                        new SKPoint(0, 0),
                        new SKPoint(40, 40),
                        new[] { SKColors.LightBlue, SKColors.LightGreen },
                        null,
                        SKShaderTileMode.Clamp
                    )
                };
                canvas.DrawRect(0, 0, 40, 40, gradientPaint);

                // Horizontal lines
                using var linePaint = new SKPaint
                {
                    Color = SKColors.DarkBlue,
                    StrokeWidth = 2
                };
                for (int y = 5; y < 40; y += 10)
                {
                    canvas.DrawLine(0, y, 40, y, linePaint);
                }

                var texturePaint = CreateBasePaint();
                texturePaint.Shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
                return texturePaint;
            });
    }

    public sealed class GlowStyleCommand : BaseStyleCommand
    {
        public override string Name => "Glow";

        public override StyleDefinition CreateStyle() =>
            new(SKColors.Yellow, SKColors.Gold, (start, end) =>
            {
                var paint = CreateBasePaint();
                paint.Shader = SKShader.CreateRadialGradient(
                    new SKPoint(0.5f, 0.5f),
                    0.5f,
                    new[] { start, end },
                    null,
                    SKShaderTileMode.Clamp
                );
                paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.0f); // Reduced blur radius
                return paint;
            });
    }

    public sealed class MosaicStyleCommand : BaseStyleCommand
    {
        public override string Name => "Mosaic";

        public override StyleDefinition CreateStyle() =>
            new(SKColors.Cyan, SKColors.Azure, (start, end) =>
            {
                using var bitmap = new SKBitmap(10, 10);
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(SKColors.White);

                using var startPaint = new SKPaint { Color = start, Style = SKPaintStyle.Fill };
                using var endPaint = new SKPaint { Color = end, Style = SKPaintStyle.Fill };

                canvas.DrawRect(new SKRect(0, 0, 5, 5), startPaint);
                canvas.DrawRect(new SKRect(5, 5, 10, 10), endPaint);

                var mosaicPaint = CreateBasePaint();
                mosaicPaint.Shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
                return mosaicPaint;
            });
    }

    public sealed class SparkleStyleCommand : BaseStyleCommand
    {
        public override string Name => "Sparkle";

        public override StyleDefinition CreateStyle() =>
            new(SKColors.Gold, SKColors.Purple, CreateSparklePaint);

        private static SKPaint CreateSparklePaint(SKColor start, SKColor end)
        {
            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(0.5f, 0.5f),
                0.5f,
                new[] { start, end },
                new float[] { 0, 1 },
                SKShaderTileMode.Clamp
            );
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
            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, 1),
                new[]
                {
                start,
                SKColors.LightSeaGreen,
                SKColors.Goldenrod,
                end
                },
                new[] { 0f, 0.4f, 0.7f, 1f },
                SKShaderTileMode.Clamp
            );
            paint.ColorFilter = SKColorFilter.CreateBlendMode(
                SKColors.White.WithAlpha(50),
                SKBlendMode.SoftLight
            );
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
            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(1, 1),
                new[] { start, end },
                null,
                SKShaderTileMode.Clamp
            );
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
            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(1, 1),
                new[] { start, end },
                null,
                SKShaderTileMode.Mirror
            );
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
            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(1, 0),
                new[] { start, end },
                null,
                SKShaderTileMode.Mirror
            );
            paint.Color = ColorUtilities.ApplyOpacity(paint.Color, 0.8f);
            return paint;
        }
    }

}