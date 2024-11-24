﻿#nullable enable

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
            var styleTypes = Enum.GetValues<StyleType>();
            if (styleTypes.Length > 10)
                Parallel.ForEach(styleTypes, styleType => RegisterStyle(StyleFactory.CreateStyleCommand(styleType).Name, StyleFactory.CreateStyleCommand(styleType).CreateStyle()));
            else
                foreach (var styleType in styleTypes)
                    RegisterStyle(StyleFactory.CreateStyleCommand(styleType).Name, StyleFactory.CreateStyleCommand(styleType).CreateStyle());
        }

        public void RegisterStyle(string styleName, StyleDefinition definition, bool overwriteIfExists = false)
        {
            ValidateDisposed();
            if (string.IsNullOrWhiteSpace(styleName))
                throw new ArgumentException("Style name cannot be empty.", nameof(styleName));

            if (!overwriteIfExists && _styles.ContainsKey(styleName))
                return;

            if (_styles.TryAdd(styleName, definition) || overwriteIfExists)
                _paintCache.TryRemove(styleName, out var oldPaint);
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
                paint.Dispose();
            return removed;
        }

        public void ClearStyles()
        {
            ValidateDisposed();
            foreach (var paint in _paintCache.Values)
                paint.Dispose();
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
                paint.Dispose();
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

        public (SKColor, SKColor) GetColors() => (_startColor, _endColor);

        public SKPaint CreatePaint()
        {
            if (_cachedPaint != null)
                return _cachedPaint;

            lock (_cacheLock)
                _cachedPaint ??= _factory(_startColor, _endColor);
            return _cachedPaint;
        }
    }
}