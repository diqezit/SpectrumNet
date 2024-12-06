#nullable enable

namespace SpectrumNet
{
    public sealed class SpectrumBrushes : IDisposable
    {
        private readonly ConcurrentDictionary<string, StyleDefinition> _styles = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SKPaint> _paintCache = new(StringComparer.OrdinalIgnoreCase);
        private volatile bool _disposed;

        public IReadOnlyDictionary<string, StyleDefinition> Styles => _styles;

        public SpectrumBrushes()
        {
            RegisterDefaultStyles();
        }

        private void RegisterDefaultStyles()
        {
            var styleNames = StyleFactory.GetAllStyleNames();

            // Используем Parallel.ForEach для параллельной загрузки стилей
            Parallel.ForEach(styleNames, styleName =>
            {
                var command = StyleFactory.CreateStyleCommand(styleName);
                _styles.TryAdd(command.Name, command.CreateStyle());
            });
        }

        public void RegisterStyle(string styleName, StyleDefinition definition, bool overwriteIfExists = false)
        {
            ValidateDisposed();

            if (string.IsNullOrWhiteSpace(styleName))
                throw new ArgumentException("Style name cannot be empty.", nameof(styleName));

            if (overwriteIfExists)
            {
                _styles[styleName] = definition; // Перезаписываем без лишних проверок
                _paintCache.TryRemove(styleName, out _); // Удаляем старый кэш
            }
            else
            {
                if (_styles.TryAdd(styleName, definition))
                    _paintCache.TryRemove(styleName, out _);
            }
        }

        public (SKColor startColor, SKColor endColor, SKPaint paint) GetColorsAndBrush(string styleName)
        {
            ValidateDisposed();

            if (!_styles.TryGetValue(styleName, out var styleDefinition))
                throw new InvalidOperationException($"Style '{styleName}' not found.");

            var (startColor, endColor) = styleDefinition.GetColors();

            // Потокобезопасное получение или создание кисти
            var paint = _paintCache.GetOrAdd(styleName, _ => styleDefinition.CreatePaint());
            return (startColor, endColor, paint);
        }

        public bool RemoveStyle(string styleName)
        {
            ValidateDisposed();

            // Удаляем стиль и кисть, если они существуют
            if (_styles.TryRemove(styleName, out _) && _paintCache.TryRemove(styleName, out var paint))
            {
                paint?.Dispose();
                return true;
            }

            return false;
        }

        public void ClearStyles()
        {
            ValidateDisposed();

            // Освобождаем ресурсы кистей
            Parallel.ForEach(_paintCache.Values, paint => paint?.Dispose());

            _styles.Clear();
            _paintCache.Clear();
        }

        private void ValidateDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SpectrumBrushes));
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            // Освобождаем ресурсы кистей перед выходом
            Parallel.ForEach(_paintCache.Values, paint => paint?.Dispose());

            _styles.Clear();
            _paintCache.Clear();
            GC.SuppressFinalize(this);
        }
    }

    public sealed class StyleDefinition
    {
        private readonly SKColor _startColor;
        private readonly SKColor _endColor;
        private readonly Func<SKColor, SKColor, SKPaint> _factory;
        private SKPaint? _cachedPaint;

        public SKColor StartColor => _startColor;
        public SKColor EndColor => _endColor;

        public StyleDefinition(SKColor startColor, SKColor endColor, Func<SKColor, SKColor, SKPaint> factory)
        {
            _startColor = startColor;
            _endColor = endColor;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public (SKColor startColor, SKColor endColor) GetColors() => (_startColor, _endColor);

        public SKPaint CreatePaint()
        {
            return _cachedPaint ??= _factory(_startColor, _endColor)
                ?? throw new InvalidOperationException("Factory failed to create paint.");
        }
    }

    public static class StyleFactory
    {
        private static readonly SortedDictionary<string, IStyleCommand> StyleCommands = new(StringComparer.OrdinalIgnoreCase);

        static StyleFactory()
        {
            // Тут буду использовать LINQ для загрузки всех команд
            foreach (var command in Assembly.GetCallingAssembly().GetTypes()
                         .Where(type => typeof(IStyleCommand).IsAssignableFrom(type) && !type.IsAbstract)
                         .Select(type => Activator.CreateInstance(type) as IStyleCommand)
                         .Where(command => command?.Name is not null))
            {
                StyleCommands[command!.Name] = command;
            }
        }

        public static IStyleCommand CreateStyleCommand(string styleName) =>
            StyleCommands.TryGetValue(styleName, out var command)
                ? command
                : throw new ArgumentException($"Unknown style name: {styleName}", nameof(styleName));

        public static IEnumerable<string> GetAllStyleNames() => StyleCommands.Keys;
    }

    public interface IStyleCommand
    {
        string Name { get; }
        StyleDefinition CreateStyle();
    }
}