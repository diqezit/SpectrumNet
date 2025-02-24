#nullable enable

namespace SpectrumNet
{
    public sealed class SpectrumBrushes : IDisposable
    {
        private readonly ConcurrentDictionary<string, StyleDefinition> _styles = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SKPaint> _paintCache = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public IReadOnlyDictionary<string, StyleDefinition> Styles => _styles;

        public SpectrumBrushes()
        {
            RegisterDefaultStyles();
        }

        private void RegisterDefaultStyles()
        {
            var styleNames = StyleFactory.GetAllStyleNames();

            Parallel.ForEach(styleNames, styleName =>
            {
                var command = StyleFactory.CreateStyleCommand(styleName);
                _styles.TryAdd(command.Name, command.CreateStyle());
            });
        }

        public void RegisterStyle(string styleName, StyleDefinition definition, bool overwriteIfExists = false)
        {
            EnsureNotDisposed();

            if (string.IsNullOrWhiteSpace(styleName))
                throw new ArgumentException("Название стиля не может быть пустым.", nameof(styleName));

            _styles.AddOrUpdate(
                styleName,
                _ => definition,
                (_, existing) =>
                {
                    if (!overwriteIfExists)
                        return existing;
                    RemovePaint(styleName);
                    return definition;
                });
        }

        public (SKColor startColor, SKColor endColor, SKPaint paint) GetColorsAndBrush(string styleName)
        {
            EnsureNotDisposed();

            if (!_styles.TryGetValue(styleName, out var styleDefinition))
                throw new InvalidOperationException($"Стиль '{styleName}' не найден.");

            var (startColor, endColor) = styleDefinition.GetColors();
            var paint = _paintCache.GetOrAdd(styleName, _ => styleDefinition.CreatePaint());
            return (startColor, endColor, paint);
        }

        public bool RemoveStyle(string styleName)
        {
            EnsureNotDisposed();

            var styleRemoved = _styles.TryRemove(styleName, out _);
            var paintRemoved = RemovePaint(styleName);
            return styleRemoved || paintRemoved;
        }

        public void ClearStyles()
        {
            EnsureNotDisposed();

            foreach (var paint in _paintCache.Values)
            {
                paint?.Dispose();
            }
            _styles.Clear();
            _paintCache.Clear();
        }

        private bool RemovePaint(string styleName)
        {
            if (_paintCache.TryRemove(styleName, out var paint))
            {
                paint?.Dispose();
                return true;
            }
            return false;
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SpectrumBrushes));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var paint in _paintCache.Values)
                {
                    paint?.Dispose();
                }
                _styles.Clear();
                _paintCache.Clear();
            }
            _disposed = true;
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
                ?? throw new InvalidOperationException("Фабрика не смогла создать кисть.");
        }
    }

    public static class StyleFactory
    {
        private static readonly SortedDictionary<string, IStyleCommand> StyleCommands = new(StringComparer.OrdinalIgnoreCase);

        static StyleFactory()
        {
            var commands = typeof(StyleFactory).Assembly.GetTypes()
                .Where(type => typeof(IStyleCommand).IsAssignableFrom(type) && !type.IsAbstract)
                .Select(type => Activator.CreateInstance(type) as IStyleCommand)
                .Where(command => command?.Name is not null);

            foreach (var command in commands)
            {
                StyleCommands[command!.Name] = command;
            }
        }

        public static IStyleCommand CreateStyleCommand(string styleName) =>
            StyleCommands.TryGetValue(styleName, out var command)
                ? command
                : throw new ArgumentException($"Неизвестное имя стиля: {styleName}", nameof(styleName));

        public static IEnumerable<string> GetAllStyleNames() => StyleCommands.Keys;
    }

    public interface IStyleCommand
    {
        string Name { get; }
        StyleDefinition CreateStyle();
    }
}