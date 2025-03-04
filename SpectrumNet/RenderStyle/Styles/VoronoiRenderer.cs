#nullable enable

namespace SpectrumNet
{
    public sealed class VoronoiRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static VoronoiRenderer? _instance;
        private bool _isInitialized;
        private bool _isOverlayActive;
        private volatile bool _disposed;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);

        private float[]? _processedSpectrum;
        private List<VoronoiPoint>? _voronoiPoints;
        private SKPaint? _cellPaint;
        private SKPaint? _borderPaint;
        private Random _random = new();
        private float _timeAccumulator;
        #endregion

        #region Constants
        private const int DefaultPointCount = 25;
        private const int OverlayPointCount = 15;
        private const float SmoothingFactor = 0.2f;
        private const float MinPointSize = 3f;
        private const float MaxPointSize = 15f;
        private const float MinMoveSpeed = 0.3f;
        private const float MaxMoveSpeed = 2.0f;
        private const float BorderWidth = 1.0f;
        private const byte BorderAlpha = 180; 
        private const float TimeStep = 0.016f; // ~60fps
        #endregion

        #region Constructor and Initialization
        private VoronoiRenderer() { }

        public static VoronoiRenderer GetInstance() => _instance ??= new VoronoiRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _cellPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            _borderPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = BorderWidth,
                Color = SKColors.White.WithAlpha(BorderAlpha)
            };

            int pointCount = _isOverlayActive ? OverlayPointCount : DefaultPointCount;
            InitializePoints(pointCount);

            _isInitialized = true;
            Log.Debug("VoronoiRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive == isOverlayActive) return;

            _isOverlayActive = isOverlayActive;
            int pointCount = _isOverlayActive ? OverlayPointCount : DefaultPointCount;
            InitializePoints(pointCount);
        }
        #endregion

        #region Rendering
        public void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!ValidateRenderParams(canvas, spectrum, info, paint, drawPerformanceInfo))
                return;

            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    int freqBands = Math.Min(spectrum!.Length, DefaultPointCount);
                    ProcessSpectrum(spectrum, freqBands);
                    UpdateVoronoiPoints(info.Width, info.Height);
                }

                RenderVoronoiDiagram(canvas!, info, paint!);
                drawPerformanceInfo?.Invoke(canvas!, info);
            }
            catch (Exception ex)
            {
                Log.Error($"Error in VoronoiRenderer.Render: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                    _spectrumSemaphore.Release();
            }
        }

        private void RenderVoronoiDiagram(SKCanvas canvas, SKImageInfo info, SKPaint basePaint)
        {
            if (_voronoiPoints == null || _voronoiPoints.Count == 0 || _cellPaint == null || _borderPaint == null)
                return;

            // Используем цвет из базовой кисти для границ
            _borderPaint.Color = basePaint.Color.WithAlpha(BorderAlpha);

            // Разделяем экран на сетку для оптимизации
            int gridSize = 20;
            int gridCols = (int)Math.Ceiling((float)info.Width / gridSize);
            int gridRows = (int)Math.Ceiling((float)info.Height / gridSize);

            // Для каждой ячейки сетки находим ближайшую точку Вороного
            for (int row = 0; row < gridRows; row++)
            {
                for (int col = 0; col < gridCols; col++)
                {
                    float cellX = col * gridSize;
                    float cellY = row * gridSize;

                    // Найти ближайшую точку Вороного
                    int nearestIndex = FindNearestPointIndex(cellX, cellY);
                    if (nearestIndex >= 0 && nearestIndex < _voronoiPoints.Count)
                    {
                        var point = _voronoiPoints[nearestIndex];

                        // Используем цвет из базовой кисти, варьирующийся по частоте
                        byte r = (byte)(basePaint.Color.Red + (point.FrequencyIndex * 3) % 50);
                        byte g = (byte)(basePaint.Color.Green + (point.FrequencyIndex * 7) % 50);
                        byte b = (byte)(basePaint.Color.Blue + (point.FrequencyIndex * 11) % 50);

                        // Прозрачность зависит от размера точки - исправлено
                        byte a = (byte)Math.Clamp(55 + (int)(point.Size * 10), 0, 255);

                        _cellPaint.Color = new SKColor(r, g, b, a);

                        // Рисуем ячейку
                        canvas.DrawRect(
                            cellX,
                            cellY,
                            Math.Min(gridSize, info.Width - cellX),
                            Math.Min(gridSize, info.Height - cellY),
                            _cellPaint
                        );
                    }
                }
            }

            // Рисуем границы между регионами (упрощенно)
            for (int i = 0; i < _voronoiPoints.Count; i++)
            {
                var p1 = _voronoiPoints[i];
                for (int j = i + 1; j < _voronoiPoints.Count; j++)
                {
                    var p2 = _voronoiPoints[j];

                    // Находим середину между точками
                    float midX = (p1.X + p2.X) / 2;
                    float midY = (p1.Y + p2.Y) / 2;

                    // Рисуем границу если точки достаточно близко
                    float distance = (float)Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
                    if (distance < Math.Max(info.Width, info.Height) / 3)
                    {
                        canvas.DrawLine(p1.X, p1.Y, midX, midY, _borderPaint);
                    }
                }
            }

            // Рисуем сами точки Вороного
            foreach (var point in _voronoiPoints)
            {
                _cellPaint.Color = basePaint.Color.WithAlpha(200);
                canvas.DrawCircle(point.X, point.Y, point.Size, _cellPaint);
            }
        }
        #endregion

        #region Spectrum Processing
        private void ProcessSpectrum(float[] spectrum, int freqBands)
        {
            if (_voronoiPoints == null || _voronoiPoints.Count == 0) return;

            if (_processedSpectrum == null || _processedSpectrum.Length < freqBands)
                _processedSpectrum = new float[freqBands];

            float spectrumStep = spectrum.Length / (float)freqBands;

            for (int i = 0; i < freqBands; i++)
            {
                int startBin = (int)(i * spectrumStep);
                int endBin = (int)((i + 1) * spectrumStep);
                endBin = Math.Min(endBin, spectrum.Length);

                float sum = 0;
                for (int j = startBin; j < endBin; j++)
                {
                    sum += spectrum[j];
                }

                // Нормализуем и сглаживаем
                float avg = sum / (endBin - startBin);
                _processedSpectrum[i] = avg * 3f; // Усиливаем для лучшей видимости
                _processedSpectrum[i] = Math.Clamp(_processedSpectrum[i], 0, 1);
            }

            // Обновляем параметры каждой точки на основе спектра
            for (int i = 0; i < _voronoiPoints.Count; i++)
            {
                var point = _voronoiPoints[i];
                int freqIndex = point.FrequencyIndex;

                if (freqIndex < _processedSpectrum.Length)
                {
                    float intensity = _processedSpectrum[freqIndex];
                    float targetSize = MinPointSize + (MaxPointSize - MinPointSize) * intensity;

                    // Плавно изменяем размер
                    point.Size += (targetSize - point.Size) * SmoothingFactor;

                    // Увеличиваем скорость при сильном сигнале
                    point.VelocityX *= 1 + intensity * 0.3f;
                    point.VelocityY *= 1 + intensity * 0.3f;

                    _voronoiPoints[i] = point;
                }
            }
        }
        #endregion

        #region Helper Methods
        private void InitializePoints(int count)
        {
            _voronoiPoints = new List<VoronoiPoint>(count);

            for (int i = 0; i < count; i++)
            {
                _voronoiPoints.Add(new VoronoiPoint
                {
                    X = _random.Next(100, 700),
                    Y = _random.Next(100, 500),
                    VelocityX = MinMoveSpeed + (float)_random.NextDouble() * (MaxMoveSpeed - MinMoveSpeed),
                    VelocityY = MinMoveSpeed + (float)_random.NextDouble() * (MaxMoveSpeed - MinMoveSpeed),
                    Size = MinPointSize,
                    FrequencyIndex = i % DefaultPointCount
                });
            }
        }

        private void UpdateVoronoiPoints(float width, float height)
        {
            if (_voronoiPoints == null) return;

            _timeAccumulator += TimeStep;

            for (int i = 0; i < _voronoiPoints.Count; i++)
            {
                var point = _voronoiPoints[i];

                // Обновляем позицию с течением времени
                point.X += point.VelocityX * (float)Math.Sin(_timeAccumulator + i);
                point.Y += point.VelocityY * (float)Math.Cos(_timeAccumulator * 0.7f + i);

                // Отражаем от границ
                if (point.X < 0)
                {
                    point.X = 0;
                    point.VelocityX = -point.VelocityX;
                }
                else if (point.X > width)
                {
                    point.X = width;
                    point.VelocityX = -point.VelocityX;
                }

                if (point.Y < 0)
                {
                    point.Y = 0;
                    point.VelocityY = -point.VelocityY;
                }
                else if (point.Y > height)
                {
                    point.Y = height;
                    point.VelocityY = -point.VelocityY;
                }

                _voronoiPoints[i] = point;
            }
        }

        private int FindNearestPointIndex(float x, float y)
        {
            if (_voronoiPoints == null || _voronoiPoints.Count == 0)
                return -1;

            int nearest = 0;
            float minDistance = float.MaxValue;

            for (int i = 0; i < _voronoiPoints.Count; i++)
            {
                var point = _voronoiPoints[i];
                float distance = (float)(Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2));

                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = i;
                }
            }

            return nearest;
        }

        private bool ValidateRenderParams(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (_disposed)
                return false;

            if (!_isInitialized)
            {
                Log.Error("VoronoiRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null ||
                spectrum == null || spectrum.Length < 2 ||
                paint == null ||
                drawPerformanceInfo == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                Log.Error("Invalid render parameters for VoronoiRenderer");
                return false;
            }

            return true;
        }
        #endregion

        #region Structures
        private struct VoronoiPoint
        {
            public float X;
            public float Y;
            public float VelocityX;
            public float VelocityY;
            public float Size;
            public int FrequencyIndex;
        }
        #endregion

        #region Disposal
        public void Dispose()
        {
            if (_disposed) return;

            _spectrumSemaphore.Dispose();
            _cellPaint?.Dispose();
            _borderPaint?.Dispose();

            _cellPaint = null;
            _borderPaint = null;
            _voronoiPoints = null;
            _processedSpectrum = null;

            _disposed = true;
            _isInitialized = false;
            Log.Debug("VoronoiRenderer disposed");
        }
        #endregion
    }
}