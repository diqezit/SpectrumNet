using SkiaSharp;
using System;

#nullable enable

namespace SpectrumNet
{
    public enum StyleType
    {
        Gradient,
        Solid,
        Radial,
        Neon,
        Glass,
        Pattern,
        Metallic,
        NeonOutline,
        BlueRedGradient
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
        private static readonly IStyleCommand[] StyleCommands = new IStyleCommand[12];

        static StyleFactory()
        {
            StyleCommands[(int)StyleType.Gradient] = new GradientStyleCommand();
            StyleCommands[(int)StyleType.Solid] = new SolidStyleCommand();
            StyleCommands[(int)StyleType.Radial] = new RadialStyleCommand();
            StyleCommands[(int)StyleType.Neon] = new NeonStyleCommand();
            StyleCommands[(int)StyleType.Glass] = new GlassStyleCommand();
            StyleCommands[(int)StyleType.Pattern] = new PatternStyleCommand();
            StyleCommands[(int)StyleType.Metallic] = new MetallicStyleCommand();
            StyleCommands[(int)StyleType.NeonOutline] = new NeonOutlineStyleCommand();
            StyleCommands[(int)StyleType.BlueRedGradient] = new BlueRedGradientStyleCommand();
        }

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
                (start, end) => CreateLinearGradient(start, end, new(0, 0), new(0, 1))
            );
    }

    public sealed class SolidStyleCommand : BaseStyleCommand
    {
        public override string Name => "Solid";

        public override StyleDefinition CreateStyle() =>
            new(
                new SKColor(0xFF, 0x63, 0x47), // Tomato color
                new SKColor(0xFF, 0x63, 0x47), // Tomato color
                (color, _) => new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true }
            );
    }

    public sealed class RadialStyleCommand : BaseStyleCommand
    {
        public override string Name => "Radial";

        public override StyleDefinition CreateStyle() =>
            new(
                new SKColor(0x8A, 0x2B, 0xE2), // BlueViolet color
                new SKColor(0xFF, 0x14, 0x93), // DeepPink color
                (start, end) => CreateRadialGradient(start, end, new(0.5f, 0.5f), 0.5f)
            );
    }

    public sealed class NeonStyleCommand : BaseStyleCommand
    {
        private static readonly SKColor DefaultNeonStart = new(0x00, 0xFF, 0x00);
        private static readonly SKColor DefaultNeonEnd = new(0x66, 0xFF, 0x66);

        public override string Name => "Neon";

        public override StyleDefinition CreateStyle() =>
            new(
                DefaultNeonStart,
                DefaultNeonEnd,
                (start, end) => CreateLinearGradient(
                    start,
                    ColorUtilities.MixColors(end, ColorConstants.Primary, 0.5f),
                    new(0, 0),
                    new(0, 1)
                )
            );
    }

    public sealed class GlassStyleCommand : BaseStyleCommand
    {
        private static readonly SKColor GlassStart = new(0x88, 0xC0, 0xFF);
        private static readonly SKColor GlassEnd = new(0xFF, 0x88, 0xC0);

        public override string Name => "Glass";

        public override StyleDefinition CreateStyle() =>
            new(
                GlassStart,
                GlassEnd,
                CreateGlassPaint
            );

        private static SKPaint CreateGlassPaint(SKColor start, SKColor end)
        {
            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(1, 1),
                new[] {
                    ColorUtilities.ApplyOpacity(ColorUtilities.MixColors(start, ColorConstants.Primary, 0.3f), 0.9f),
                    ColorUtilities.ApplyOpacity(ColorUtilities.MixColors(end, ColorConstants.Secondary, 0.3f), 0.7f)
                },
                new float[] { 0, 1 },
                SKShaderTileMode.Clamp
            );
            return paint;
        }
    }

    public sealed class MetallicStyleCommand : BaseStyleCommand
    {
        private static readonly SKColor MetallicStart = new(0xC0, 0xC0, 0xC0);
        private static readonly SKColor MetallicEnd = new(0xA0, 0xA0, 0xA0);

        public override string Name => "Metallic";

        public override StyleDefinition CreateStyle() =>
            new(
                MetallicStart,
                MetallicEnd,
                CreateMetallicPaint
            );

        private static SKPaint CreateMetallicPaint(SKColor start, SKColor end)
        {
            var paint = CreateLinearGradient(
                start, end,
                new SKPoint(0, 0),
                new SKPoint(1, 1)
            );
            paint.Style = SKPaintStyle.Fill;
            paint.StrokeCap = SKStrokeCap.Round;
            paint.StrokeJoin = SKStrokeJoin.Round;
            return paint;
        }
    }

    public sealed class NeonOutlineStyleCommand : BaseStyleCommand
    {
        private static readonly SKColor OutlineStart = new(0x00, 0xFF, 0x00);
        private static readonly SKColor OutlineEnd = new(0xFF, 0x00, 0xFF);

        public override string Name => "NeonOutline";

        public override StyleDefinition CreateStyle() =>
            new(
                OutlineStart,
                OutlineEnd,
                CreateNeonOutlinePaint
            );

        private static SKPaint CreateNeonOutlinePaint(SKColor start, SKColor end)
        {
            var paint = CreateLinearGradient(
                start, end,
                new SKPoint(0, 0),
                new SKPoint(0, 1)
            );
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 4;
            paint.StrokeCap = SKStrokeCap.Round;
            paint.StrokeJoin = SKStrokeJoin.Round;
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Solid, 4.0f);
            return paint;
        }
    }

    public sealed class PatternStyleCommand : BaseStyleCommand
    {
        private static readonly SKColor PatternStart = SKColors.White;
        private static readonly SKColor PatternEnd = SKColors.Black;

        public override string Name => "Pattern";

        public override StyleDefinition CreateStyle() =>
            new(
                PatternStart,
                PatternEnd,
                CreatePatternPaint
            );

        private static SKPaint CreatePatternPaint(SKColor start, SKColor end)
        {
            using var bitmap = new SKBitmap(10, 10);
            using var canvas = new SKCanvas(bitmap);
            using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill };

            canvas.Clear(SKColors.White);

            fillPaint.Color = start;
            canvas.DrawRect(new SKRect(0, 0, 5, 5), fillPaint);

            fillPaint.Color = end;
            canvas.DrawRect(new SKRect(5, 5, 10, 10), fillPaint);

            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            return paint;
        }
    }

    public sealed class BlueRedGradientStyleCommand : BaseStyleCommand
    {
        public override string Name => "BlueRedGradient";

        public override StyleDefinition CreateStyle() =>
            new(
                SKColors.Blue,
                SKColors.Red,
                CreateTriColorGradient
            );

        private static SKPaint CreateTriColorGradient(SKColor start, SKColor end)
        {
            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(1, 0),
                new[] { start, SKColors.Purple, end },
                null,
                SKShaderTileMode.Clamp
            );
            return paint;
        }
    }

    // Style command implementations...
    // Add implementations for all style commands 
    // following the same pattern as in the original code
}