#nullable enable

using System;
using System.Collections.Concurrent;

namespace SpectrumNet
{
    public class ObjectPool<T> : IDisposable where T : class
    {
        private readonly ConcurrentBag<T> _objects;
        private readonly Func<T> _objectGenerator;
        private readonly Action<T> _objectReset;

        public ObjectPool(Func<T> objectGenerator, Action<T> objectReset, int initialCount = 0)
        {
            _objects = new ConcurrentBag<T>();
            _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
            _objectReset = objectReset;

            // Предварительное заполнение пула
            for (int i = 0; i < initialCount; i++)
            {
                _objects.Add(_objectGenerator());
            }
        }

        public T Get()
        {
            if (_objects.TryTake(out T? item))
            {
                _objectReset?.Invoke(item);
                return item;
            }

            return _objectGenerator();
        }

        public void Return(T item)
        {
            _objects.Add(item);
        }

        public void Dispose()
        {
            if (typeof(IDisposable).IsAssignableFrom(typeof(T)))
            {
                foreach (var obj in _objects)
                {
                    (obj as IDisposable)?.Dispose();
                }

                _objects.Clear();
            }
        }
    }
}