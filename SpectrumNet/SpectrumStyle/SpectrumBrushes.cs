#nullable enable

namespace SpectrumNet
{
    public sealed class SpectrumBrushes : IDisposable
    {
        private readonly ConcurrentDictionary<string, StyleDefinition> _styles;
        private readonly ConcurrentDictionary<string, SKPaint> _paintCache;
        private volatile bool _disposed;
        private const int DefaultCapacity = 12;

        public IReadOnlyDictionary<string, StyleDefinition> Styles => _styles;

        public SpectrumBrushes()
        {
            _styles = new ConcurrentDictionary<string, StyleDefinition>(Environment.ProcessorCount, DefaultCapacity, StringComparer.OrdinalIgnoreCase);
            _paintCache = new ConcurrentDictionary<string, SKPaint>(Environment.ProcessorCount, DefaultCapacity, StringComparer.OrdinalIgnoreCase);
            RegisterDefaultStyles();
        }

        private void RegisterDefaultStyles()
        {
            var styleNames = StyleFactory.GetAllStyleNames();
            if (styleNames.Count() > 10)
                Parallel.ForEach(styleNames, styleName =>
                {
                    var command = StyleFactory.CreateStyleCommand(styleName);
                    RegisterStyle(command.Name, command.CreateStyle());
                });
            else
                foreach (var styleName in styleNames)
                {
                    var command = StyleFactory.CreateStyleCommand(styleName);
                    RegisterStyle(command.Name, command.CreateStyle());
                }
        }

        public void RegisterStyle(string styleName, StyleDefinition definition, bool overwriteIfExists = false)
        {
            ValidateDisposed();
            if (string.IsNullOrWhiteSpace(styleName))
                throw new ArgumentException("Style name cannot be empty.", nameof(styleName));

            if (!overwriteIfExists && _styles.ContainsKey(styleName))
                return;

            if (_styles.TryAdd(styleName, definition) || overwriteIfExists)
                _paintCache.TryRemove(styleName, out _);
        }

        public (SKColor startColor, SKColor endColor, SKPaint paint) GetColorsAndBrush(string styleName)
        {
            ValidateDisposed();
            if (string.IsNullOrWhiteSpace(styleName))
                throw new ArgumentException("Style name cannot be empty.", nameof(styleName));

            if (!_styles.TryGetValue(styleName, out var styleDefinition))
                throw new InvalidOperationException($"Style '{styleName}' not found.");

            var (startColor, endColor) = styleDefinition.GetColors();
            var paint = _paintCache.GetOrAdd(styleName, _ => styleDefinition.CreatePaint());
            return (startColor, endColor, paint);
        }

        public bool RemoveStyle(string styleName)
        {
            ValidateDisposed();
            if (string.IsNullOrWhiteSpace(styleName))
                throw new ArgumentException("Style name cannot be empty.", nameof(styleName));

            bool removed = _styles.TryRemove(styleName, out _);
            if (removed && _paintCache.TryRemove(styleName, out var paint))
            {
                paint?.Dispose();
            }
            return removed;
        }

        public void ClearStyles()
        {
            ValidateDisposed();
            foreach (var paint in _paintCache.Values)
                paint?.Dispose();
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
            if (_disposed)
                return;

            _disposed = true;
            foreach (var paint in _paintCache.Values)
                paint?.Dispose();
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
        private readonly object _cacheLock = new();

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
            if (_cachedPaint is not null)
                return _cachedPaint;

            lock (_cacheLock)
            {
                _cachedPaint ??= _factory(_startColor, _endColor);
                return _cachedPaint ?? throw new InvalidOperationException("Factory failed to create paint.");
            }
        }
    }

    public static class StyleFactory
    {
        private static readonly Dictionary<string, IStyleCommand> StyleCommands = new Dictionary<string, IStyleCommand>();

        static StyleFactory()
        {
            var commandTypes = Assembly.GetCallingAssembly().GetTypes()
                .Where(type => typeof(IStyleCommand).IsAssignableFrom(type) && !type.IsAbstract);

            foreach (var type in commandTypes)
            {
                var command = (IStyleCommand)Activator.CreateInstance(type);
                StyleCommands[command.Name] = command;
            }
        }

        public static IStyleCommand CreateStyleCommand(string styleName)
        {
            if (StyleCommands.TryGetValue(styleName, out var command))
                return command;
            else
                throw new ArgumentException($"Unknown style name: {styleName}", nameof(styleName));
        }

        public static IEnumerable<string> GetAllStyleNames() => StyleCommands.Keys;
    }

    public interface IStyleCommand
    {
        string Name { get; }
        StyleDefinition CreateStyle();
    }
}