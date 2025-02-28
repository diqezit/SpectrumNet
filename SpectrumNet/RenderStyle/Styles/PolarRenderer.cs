#nullable enable

namespace SpectrumNet
{
    public sealed class PolarRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static PolarRenderer? _instance;
        private bool _isInitialized;
        private bool _isOverlayActive;
        private volatile bool _disposed;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);

        private float[]? _processedSpectrum;
        private float[]? _previousSpectrum;
        private SKPath? _outerPath;
        private SKPath? _innerPath;
        private SKPaint? _fillPaint;
        private SKPaint? _strokePaint;
        private SKPaint? _centerPaint;
        private float _rotation;
        private float _time;
        #endregion

        #region Constants
        private const int PointCountDefault = 120;
        private const int PointCountOverlay = 80;
        private const float SmoothingFactor = 0.15f;
        private const float MinRadius = 30f;
        private const float RadiusMultiplier = 200f;
        private const float InnerRadiusRatio = 0.5f;
        private const float RotationSpeed = 0.3f;
        private const float TimeStep = 0.016f;
        private const float StrokeWidth = 1.5f;
        private const float CenterCircleSize = 6f;
        private const byte FillAlpha = 90;
        #endregion

        #region Constructor and Initialization
        private PolarRenderer() { }

        public static PolarRenderer GetInstance() => _instance ??= new PolarRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _outerPath = new SKPath();
            _innerPath = new SKPath();

            _fillPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            _strokePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = StrokeWidth,
                StrokeJoin = SKStrokeJoin.Round
            };

            _centerPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            _isInitialized = true;
            Log.Debug("PolarRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            _isOverlayActive = isOverlayActive;
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
                    ProcessSpectrum(spectrum!);
                    UpdatePolarPaths(info);
                }

                RenderPolarGraph(canvas!, info, paint!);
                drawPerformanceInfo?.Invoke(canvas!, info);
            }
            catch (Exception ex)
            {
                Log.Error($"Error in PolarRenderer.Render: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                    _spectrumSemaphore.Release();
            }
        }

        private void RenderPolarGraph(SKCanvas canvas, SKImageInfo info, SKPaint basePaint)
        {
            if (_outerPath == null || _innerPath == null ||
                _fillPaint == null || _strokePaint == null || _centerPaint == null)
                return;

            // Настраиваем цвета на основе базовой кисти
            _fillPaint.Color = basePaint.Color.WithAlpha(FillAlpha);
            _strokePaint.Color = basePaint.Color;
            _centerPaint.Color = basePaint.Color;

            float centerX = info.Width / 2f;
            float centerY = info.Height / 2f;

            // Рисуем заполненную область между внешним и внутренним путями
            canvas.Save();
            canvas.Translate(centerX, centerY);
            canvas.RotateDegrees(_rotation);

            // Рисуем внешний и внутренний контуры
            canvas.DrawPath(_outerPath, _fillPaint);
            canvas.DrawPath(_outerPath, _strokePaint);
            canvas.DrawPath(_innerPath, _strokePaint);

            // Рисуем центральную точку
            canvas.DrawCircle(0, 0, CenterCircleSize, _centerPaint);

            canvas.Restore();
        }
        #endregion

        #region Path Generation
        private void UpdatePolarPaths(SKImageInfo info)
        {
            if (_processedSpectrum == null || _outerPath == null || _innerPath == null)
                return;

            _time += TimeStep;
            _rotation += RotationSpeed * _time * 0.01f;

            int pointCount = _isOverlayActive ? PointCountOverlay : PointCountDefault;
            float angleStep = 360f / pointCount;

            float centerX = 0;
            float centerY = 0;

            // Сбрасываем пути
            _outerPath.Reset();
            _innerPath.Reset();

            // Создаем внешний путь
            for (int i = 0; i <= pointCount; i++)
            {
                float angle = i * angleStep;
                float spectrumIndex = (i % pointCount) * _processedSpectrum.Length / pointCount;
                int baseIndex = (int)spectrumIndex;
                float fraction = spectrumIndex - baseIndex;

                // Получаем значение спектра для текущего угла
                float spectrumValue;
                if (baseIndex >= _processedSpectrum.Length - 1)
                {
                    spectrumValue = _processedSpectrum[_processedSpectrum.Length - 1];
                }
                else
                {
                    spectrumValue = _processedSpectrum[baseIndex] * (1 - fraction) +
                                  _processedSpectrum[baseIndex + 1] * fraction;
                }

                // Модулируем значение спектра для создания дополнительных узоров
                float modulation = 1 + 0.3f * (float)Math.Sin(angle * 5 * Math.PI / 180 + _time * 2);
                spectrumValue *= modulation;

                // Вычисляем радиус
                float radius = MinRadius + spectrumValue * RadiusMultiplier;

                // Преобразуем полярные координаты в декартовы
                float x = radius * (float)Math.Cos(angle * Math.PI / 180);
                float y = radius * (float)Math.Sin(angle * Math.PI / 180);

                // Добавляем точку к пути
                if (i == 0)
                {
                    _outerPath.MoveTo(x, y);
                }
                else
                {
                    _outerPath.LineTo(x, y);
                }
            }

            // Создаем внутренний путь
            for (int i = 0; i <= pointCount; i++)
            {
                float angle = i * angleStep;
                float spectrumIndex = (i % pointCount) * _processedSpectrum.Length / pointCount;
                int baseIndex = (int)spectrumIndex;
                float fraction = spectrumIndex - baseIndex;

                // Получаем значение спектра для внутреннего пути
                float spectrumValue;
                if (baseIndex >= _processedSpectrum.Length - 1)
                {
                    spectrumValue = _processedSpectrum[_processedSpectrum.Length - 1];
                }
                else
                {
                    spectrumValue = _processedSpectrum[baseIndex] * (1 - fraction) +
                                  _processedSpectrum[baseIndex + 1] * fraction;
                }

                // Используем меньшее значение для внутреннего пути
                spectrumValue *= InnerRadiusRatio;

                // Модулируем значение в противофазе с внешним путем
                float modulation = 1 + 0.3f * (float)Math.Sin(angle * 5 * Math.PI / 180 + _time * 2 + Math.PI);
                spectrumValue *= modulation;

                // Вычисляем радиус
                float radius = MinRadius + spectrumValue * RadiusMultiplier;

                // Преобразуем полярные координаты в декартовы
                float x = radius * (float)Math.Cos(angle * Math.PI / 180);
                float y = radius * (float)Math.Sin(angle * Math.PI / 180);

                // Добавляем точку к пути
                if (i == 0)
                {
                    _innerPath.MoveTo(x, y);
                }
                else
                {
                    _innerPath.LineTo(x, y);
                }
            }

            // Замыкаем пути
            _outerPath.Close();
            _innerPath.Close();
        }
        #endregion

        #region Spectrum Processing
        private void ProcessSpectrum(float[] spectrum)
        {
            int pointCount = _isOverlayActive ? PointCountOverlay : PointCountDefault;

            // Инициализируем массивы, если нужно
            if (_processedSpectrum == null || _processedSpectrum.Length < pointCount)
            {
                _processedSpectrum = new float[pointCount];
            }

            if (_previousSpectrum == null || _previousSpectrum.Length < pointCount)
            {
                _previousSpectrum = new float[pointCount];
                Array.Copy(_processedSpectrum, _previousSpectrum, pointCount);
            }

            // Масштабируем спектр к нужному количеству точек
            float[] tempSpectrum = new float[pointCount];

            for (int i = 0; i < pointCount; i++)
            {
                float spectrumIndex = i * spectrum.Length / (2f * pointCount);
                int baseIndex = (int)spectrumIndex;
                float fraction = spectrumIndex - baseIndex;

                if (baseIndex >= spectrum.Length / 2 - 1)
                {
                    tempSpectrum[i] = spectrum[spectrum.Length / 2 - 1];
                }
                else
                {
                    tempSpectrum[i] = spectrum[baseIndex] * (1 - fraction) +
                                    spectrum[baseIndex + 1] * fraction;
                }

                // Усиливаем для лучшей видимости
                tempSpectrum[i] *= 2.0f;
                tempSpectrum[i] = Math.Min(tempSpectrum[i], 1.0f);
            }

            // Применяем сглаживание
            for (int i = 0; i < pointCount; i++)
            {
                _processedSpectrum[i] = _previousSpectrum[i] +
                                      (tempSpectrum[i] - _previousSpectrum[i]) * SmoothingFactor;
                _previousSpectrum[i] = _processedSpectrum[i];
            }
        }
        #endregion

        #region Helper Methods
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
                Log.Error("PolarRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null ||
                spectrum == null || spectrum.Length < 2 ||
                paint == null ||
                drawPerformanceInfo == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                Log.Error("Invalid render parameters for PolarRenderer");
                return false;
            }

            return true;
        }
        #endregion

        #region Disposal
        public void Dispose()
        {
            if (_disposed) return;

            _spectrumSemaphore.Dispose();
            _outerPath?.Dispose();
            _innerPath?.Dispose();
            _fillPaint?.Dispose();
            _strokePaint?.Dispose();
            _centerPaint?.Dispose();

            _outerPath = null;
            _innerPath = null;
            _fillPaint = null;
            _strokePaint = null;
            _centerPaint = null;
            _processedSpectrum = null;
            _previousSpectrum = null;

            _disposed = true;
            _isInitialized = false;
            Log.Debug("PolarRenderer disposed");
        }
        #endregion
    }
}