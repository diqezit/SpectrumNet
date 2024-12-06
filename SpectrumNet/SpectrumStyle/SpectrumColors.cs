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
        Pattern,
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
        EmeraldGlow
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
        new PatternStyleCommand(),
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
                (start, end) => CreateLinearGradient(start, end, new(0, 0), new(0, 1))
            );
    }

    public sealed class SolidStyleCommand : BaseStyleCommand
    {
        public override string Name => "Solid";

        public override StyleDefinition CreateStyle() =>
            new(
                new SKColor(0xFF, 0x63, 0x47),
                new SKColor(0xFF, 0x63, 0x47),
                (color, _) => new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true }
            );
    }

    public sealed class RadialStyleCommand : BaseStyleCommand
    {
        public override string Name => "Radial";

        public override StyleDefinition CreateStyle() =>
            new(
                new SKColor(0x8A, 0x2B, 0xE2),
                new SKColor(0xFF, 0x14, 0x93),
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

    public sealed class RadiantGlassStyleCommand : BaseStyleCommand
    {
        private static readonly SKColor InnerColor = new SKColor(0xE0, 0xF7, 0xFA);
        private static readonly SKColor OuterColor = new SKColor(0xB3, 0xE5, 0xFC);

        public override string Name => "RadiantGlass";

        public override StyleDefinition CreateStyle()
        {
            return new StyleDefinition(
                InnerColor,
                OuterColor,
                CreateRadiantGlassPaint
            );
        }

        private static SKPaint CreateRadiantGlassPaint(SKColor startColor, SKColor endColor)
        {
            // Базовая настройка кисти
            var paint = CreateBasePaint();

            // Настройка радиального градиента
            paint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(0.5f, 0.5f), // Центр градиента
                1.0f,                    // Радиус градиента
                new[]
                {
                ColorUtilities.ApplyOpacity(startColor, 0.8f),
                ColorUtilities.ApplyOpacity(endColor, 0.5f)
                },
                new float[] { 0.0f, 1.0f },
                SKShaderTileMode.Clamp
            );

            // Применение эффекта "глубины" через модификацию цвета
            paint.Color = ColorUtilities.MixColors(startColor, endColor, 0.2f);

            // Применение дополнительного эффекта размытия для мягкости
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Inner, 10.0f);

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
        private static readonly SKColor PatternPrimary = SKColors.DarkSlateGray;
        private static readonly SKColor PatternSecondary = SKColors.LightSteelBlue;

        public override string Name => "MinimalistPattern";

        public override StyleDefinition CreateStyle() =>
            new(
                PatternPrimary,
                PatternSecondary,
                CreatePatternPaint
            );

        private static SKPaint CreatePatternPaint(SKColor primary, SKColor secondary)
        {
            const int gridSize = 20; // Размер клетки
            using var bitmap = new SKBitmap(gridSize, gridSize);
            using var canvas = new SKCanvas(bitmap);

            // Очистка фона
            canvas.Clear(SKColors.White);

            using var primaryPaint = new SKPaint { Color = primary, Style = SKPaintStyle.Fill };
            using var secondaryPaint = new SKPaint { Color = secondary, Style = SKPaintStyle.Fill };

            // Нарисуем простой геометрический узор
            canvas.DrawRect(new SKRect(0, 0, gridSize / 2, gridSize / 2), primaryPaint); // Верхний левый
            canvas.DrawRect(new SKRect(gridSize / 2, gridSize / 2, gridSize, gridSize), primaryPaint); // Нижний правый
            canvas.DrawCircle(gridSize / 2, gridSize / 2, gridSize / 4, secondaryPaint); // Круг в центре

            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            return paint;
        }
    }

    // New Styles
    public sealed class EnhancedGradientStyleCommand : BaseStyleCommand
    {
        public override string Name => "EnhancedGradient";

        public override StyleDefinition CreateStyle()
        {
            var startColor = SKColors.MidnightBlue;  // Темно-синий
            var endColor = SKColors.Firebrick;      // Кирпично-красный

            // Генерация улучшенного градиентного фона
            var paintCreator = CreateEnhancedGradient(startColor, endColor);

            return new StyleDefinition(startColor, endColor, paintCreator);
        }

        private static Func<SKColor, SKColor, SKPaint> CreateEnhancedGradient(SKColor start, SKColor end)
        {
            return (startColor, endColor) =>
            {
                var paint = CreateBasePaint();

                // Создание сложного градиента
                paint.Shader = CreateComplexGradientShader(startColor, endColor);

                // Легкий эффект свечения
                paint.BlendMode = SKBlendMode.Screen;
                return paint;
            };
        }

        private static SKShader CreateComplexGradientShader(SKColor start, SKColor end)
        {
            return SKShader.CreateLinearGradient(
                new SKPoint(0, 0),             // Начало градиента
                new SKPoint(0, 300),           // Вертикальный градиент
                new[]
                {
                start,                      // Начальный цвет
                SKColors.BlueViolet,        // Промежуточный холодный тон
                SKColors.Coral,             // Теплый промежуточный тон
                SKColors.OrangeRed.WithAlpha(200), // Слегка прозрачный яркий цвет
                SKColors.Black.WithAlpha(80),      // Теневой эффект
                end                         // Конечный цвет
                },
                new float[] { 0f, 0.2f, 0.5f, 0.75f, 0.9f, 1f }, // Позиции цветов
                SKShaderTileMode.Clamp
            );
        }
    }

    public sealed class ShinyStyleCommand : BaseStyleCommand
    {
        public override string Name => "Shiny";

        public override StyleDefinition CreateStyle()
        {
            var startColor = SKColors.Silver;
            var endColor = SKColors.Gray;
            Func<SKColor, SKColor, SKPaint> shaderFunction = (start, end) =>
            {
                var paint = CreateBasePaint();
                paint.Shader = CreateGradientShader(start, end);
                paint.BlendMode = SKBlendMode.Screen;
                return paint;
            };

            return new StyleDefinition(startColor, endColor, shaderFunction);
        }

        private SKShader CreateGradientShader(SKColor start, SKColor end)
        {
            return SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(1, 1),
                new[] { start, end },
                null,
                SKShaderTileMode.Clamp
            );
        }
    }

    public sealed class DarkGradientStyleCommand : BaseStyleCommand
    {
        public override string Name => "DarkGradient";

        public override StyleDefinition CreateStyle() =>
            new(
                new SKColor(0x33, 0x33, 0x33),
                new SKColor(0x66, 0x66, 0x66),
                (start, end) => CreateLinearGradient(start, end, new SKPoint(0, 0), new SKPoint(0, 1))
            );
    }

    public sealed class LightPatternStyleCommand : BaseStyleCommand
    {
        public override string Name => "LightPattern";

        public override StyleDefinition CreateStyle() =>
            new(
                SKColors.White,
                SKColors.LightGray,
                CreateLightPatternPaint
            );

        private static SKPaint CreateLightPatternPaint(SKColor start, SKColor end)
        {
            using var bitmap = new SKBitmap(20, 20);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Style = SKPaintStyle.Fill };

            canvas.Clear(SKColors.White);
            paint.Color = SKColors.LightGray;
            canvas.DrawCircle(10, 10, 5, paint);

            var patternPaint = CreateBasePaint();
            patternPaint.Shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            return patternPaint;
        }
    }

    public sealed class FrostedGlassStyleCommand : BaseStyleCommand
    {
        public override string Name => "FrostedGlass";

        public override StyleDefinition CreateStyle()
        {
            return new StyleDefinition(
                SKColors.White,
                SKColors.LightGray,
                CreateFrostedGlassPaint
            );
        }

        private static SKPaint CreateFrostedGlassPaint(SKColor startColor, SKColor endColor)
        {
            // Создаем базовую кисть с базовыми настройками
            var paint = CreateBasePaint();

            // Настройка градиентного шейдера
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, 1),
                new[] { startColor, endColor },
                null,
                SKShaderTileMode.Clamp
            );

            // Применяем эффект размытия
            const float blurRadius = 5.0f;
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);

            // Применяем прозрачность к цвету
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
                new SKColor(0x80, 0x80, 0x80), // Темный оттенок металла
                new SKColor(0xE0, 0xE0, 0xE0), // Светлый оттенок металла
                (start, end) => CreateMetallicGradientPaint(start, end)
            );

        private static SKPaint CreateMetallicGradientPaint(SKColor start, SKColor end)
        {
            var paint = CreateBasePaint();

            // Создаем сложный линейный градиент с переходами
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(200, 200), // Длинная диагональная линия для плавных переходов
                new[]
                {
                start,                           // Начальный цвет
                SKColors.White.WithAlpha(128),  // Легкий блик
                end,                             // Конечный цвет
                SKColors.Black.WithAlpha(64),   // Теневой эффект
                start                            // Повтор начального цвета
                },
                new float[] { 0f, 0.3f, 0.6f, 0.8f, 1f }, // Позиции цветов в градиенте
                SKShaderTileMode.Clamp
            );

            // Устанавливаем более мягкий режим смешивания для металлического эффекта
            paint.BlendMode = SKBlendMode.Overlay;

            // Добавляем немного глянца через заливку и размытие
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
            new(
                SKColors.Red,
                SKColors.Violet,
                (start, end) => CreateRainbowPaint()
            );

        private static SKPaint CreateRainbowPaint()
        {
            var colors = new SKColor[]
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
                colors
            );
            return paint;
        }
    }

    public sealed class ShadowedStyleCommand : BaseStyleCommand
    {
        public override string Name => "Shadowed";

        public override StyleDefinition CreateStyle() =>
            new(
                SKColors.Gray,
                SKColors.DarkGray,
                CreateShadowedPaint
            );

        private static SKPaint CreateShadowedPaint(SKColor start, SKColor end)
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
        }
    }

    public sealed class TextureStyleCommand : BaseStyleCommand
    {
        public override string Name => "Texture";

        public override StyleDefinition CreateStyle() =>
            new(
                SKColors.LightBlue,
                SKColors.LightGreen,
                CreateTexturePaint
            );

        private static SKPaint CreateTexturePaint(SKColor start, SKColor end)
        {
            using var bitmap = new SKBitmap(40, 40);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Style = SKPaintStyle.Fill };

            // Задаем фон текстуры градиентом
            var gradientShader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(40, 40),
                new[] { SKColors.LightBlue, SKColors.LightGreen },
                null,
                SKShaderTileMode.Clamp);

            paint.Shader = gradientShader;
            canvas.DrawRect(0, 0, 40, 40, paint);

            // Добавляем горизонтальные линии на текстуру
            paint.Shader = null;
            paint.Color = SKColors.DarkBlue;
            paint.StrokeWidth = 2;

            for (int y = 5; y < 40; y += 10)
            {
                canvas.DrawLine(0, y, 40, y, paint);
            }

            // Создаем и возвращаем paint с текстурой
            var texturePaint = CreateBasePaint();
            texturePaint.Shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            return texturePaint;
        }
    }

    public sealed class GlowStyleCommand : BaseStyleCommand
    {
        public override string Name => "Glow";

        public override StyleDefinition CreateStyle() =>
            new(
                SKColors.Yellow,
                SKColors.Gold,
                CreateGlowPaint
            );

        private static SKPaint CreateGlowPaint(SKColor start, SKColor end)
        {
            var paint = CreateBasePaint();
            paint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(0.5f, 0.5f),
                0.5f,
                new[] { start, end },
                new float[] { 0, 1 },
                SKShaderTileMode.Clamp
            );
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5.0f);
            return paint;
        }
    }

    public sealed class MosaicStyleCommand : BaseStyleCommand
    {
        public override string Name => "Mosaic";

        public override StyleDefinition CreateStyle() =>
            new(
                SKColors.Cyan,
                SKColors.Azure,
                CreateMosaicPaint
            );

        private static SKPaint CreateMosaicPaint(SKColor start, SKColor end)
        {
            using var bitmap = new SKBitmap(10, 10);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Style = SKPaintStyle.Fill };

            canvas.Clear(SKColors.White);
            paint.Color = start;
            canvas.DrawRect(new SKRect(0, 0, 5, 5), paint);
            paint.Color = end;
            canvas.DrawRect(new SKRect(5, 5, 10, 10), paint);

            var mosaicPaint = CreateBasePaint();
            mosaicPaint.Shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            return mosaicPaint;
        }
    }

    public sealed class SparkleStyleCommand : BaseStyleCommand
    {
        public override string Name => "Sparkle";

        public override StyleDefinition CreateStyle()
        {
            var startColor = SKColors.Gold; 
            var endColor = SKColors.Purple; 

            // Создание Paint с использованием нового метода
            var paintCreator = CreateSparklePaint(startColor, endColor);

            return new StyleDefinition(startColor, endColor, paintCreator);
        }

        private static Func<SKColor, SKColor, SKPaint> CreateSparklePaint(SKColor start, SKColor end)
        {
            return (startColor, endColor) =>
            {
                var paint = CreateBasePaint();
                paint.Shader = CreateRadialGradientShader(startColor, endColor);
                paint.BlendMode = SKBlendMode.Lighten;
                return paint;
            };
        }

        private static SKShader CreateRadialGradientShader(SKColor start, SKColor end)
        {
            return SKShader.CreateRadialGradient(
                new SKPoint(0.5f, 0.5f),
                0.5f,
                new[] { start, end },
                new float[] { 0, 1 },
                SKShaderTileMode.Clamp
            );
        }
    }

    public sealed class SoftGradientStyleCommand : BaseStyleCommand
    {
        public override string Name => "SoftGradient";

        public override StyleDefinition CreateStyle()
        {
            var startColor = SKColors.Teal;
            var endColor = SKColors.OrangeRed;

            // Создание фабрики для градиентной заливки
            var paintCreator = CreateSoftGradientPaint(startColor, endColor);

            return new StyleDefinition(startColor, endColor, paintCreator);
        }

        private static Func<SKColor, SKColor, SKPaint> CreateSoftGradientPaint(SKColor start, SKColor end)
        {
            return (startColor, endColor) =>
            {
                var paint = CreateBasePaint();
                paint.Shader = CreateSmoothGradientShader(startColor, endColor);
                paint.ColorFilter = SKColorFilter.CreateBlendMode(
                    SKColors.White.WithAlpha(50), // Добавление лёгкой прозрачности
                    SKBlendMode.SoftLight
                );
                return paint;
            };
        }

        private static SKShader CreateSmoothGradientShader(SKColor start, SKColor end)
        {
            // Градиент с промежуточными цветами для плавности
            return SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, 1),
                new[]
                {
                start,
                SKColors.LightSeaGreen, // Промежуточный цвет
                SKColors.Goldenrod,     // Промежуточный цвет
                end
                },
                new[] { 0f, 0.4f, 0.7f, 1f }, // Позиции цветов
                SKShaderTileMode.Clamp
            );
        }
    }

    public sealed class DarkNeonStyleCommand : BaseStyleCommand
    {
        public override string Name => "DarkNeon";

        public override StyleDefinition CreateStyle() =>
            new(
                SKColors.DeepSkyBlue,
                SKColors.DodgerBlue,
                CreateDarkNeonPaint
            );

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
        private static readonly SKColor NeonPink = new SKColor(0xFF, 0x00, 0xFF);
        private static readonly SKColor NeonBlue = new SKColor(0x00, 0xFF, 0xFF);

        public override string Name => "Vaporwave";

        public override StyleDefinition CreateStyle()
        {
            return new StyleDefinition(
                NeonPink,
                NeonBlue,
                CreateVaporwavePaint
            );
        }

        private static SKPaint CreateVaporwavePaint(SKColor startColor, SKColor endColor)
        {
            var paint = CreateBasePaint();

            // Линейный градиент для эффекта неона
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(1, 1),
                new[] { startColor, endColor },
                null,
                SKShaderTileMode.Mirror
            );

            // Эффект размытия для создания мягкости
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.0f);

            // Прозрачность и насыщенность цвета для более яркого эффекта
            paint.Color = ColorUtilities.ApplyOpacity(paint.Color, 0.9f);

            return paint;
        }
    }

    public sealed class EmeraldGlowStyleCommand : BaseStyleCommand
    {
        private static readonly SKColor EmeraldStart = new SKColor(0x50, 0xE3, 0x50);
        private static readonly SKColor EmeraldEnd = new SKColor(0x00, 0xC9, 0x00);

        public override string Name => "EmeraldGlow";

        public override StyleDefinition CreateStyle()
        {
            return new StyleDefinition(
                EmeraldStart,
                EmeraldEnd,
                CreateEmeraldGlowPaint
            );
        }

        private static SKPaint CreateEmeraldGlowPaint(SKColor startColor, SKColor endColor)
        {
            var paint = CreateBasePaint();

            // Создаем линейный градиент с эффектом свечения
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(1, 0),
                new[] { startColor, endColor },
                null,
                SKShaderTileMode.Mirror
            );

            // Применяем прозрачность для усиления свечения
            paint.Color = ColorUtilities.ApplyOpacity(paint.Color, 0.8f);

            return paint;
        }
    }

}