#nullable enable

namespace SpectrumNet
{
    public sealed class ConstellationRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static ConstellationRenderer? _instance;
        private bool _isInitialized;
        private bool _isOverlayActive;
        private volatile bool _disposed;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);

        private float[]? _processedSpectrum;
        private List<Star>? _stars;
        private List<Constellation>? _constellations;
        private SKPaint? _starPaint;
        private SKPaint? _linePaint;
        private SKPaint? _glowPaint;
        private Random _random = new();
        private float _time;
        private float _rotationAngle;
        #endregion

        #region Constants
        private const int DefaultStarCount = 150;
        private const int OverlayStarCount = 100;
        private const int ConstellationCount = 5;
        private const float SmoothingFactor = 0.1f;
        private const float MinStarSize = 1f;
        private const float MaxStarSize = 5f;
        private const byte ConstellationLineAlpha = 80; 
        private const float BassThreshold = 0.4f;
        private const byte StarGlowAlpha = 120; 
        private const float TimeStep = 0.016f;
        private const float RotationSpeed = 0.01f;
        private const float TwinkleSpeed = 3f;
        #endregion

        #region Constructor and Initialization
        private ConstellationRenderer() { }

        public static ConstellationRenderer GetInstance() => _instance ??= new ConstellationRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _starPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            _linePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.0f,
                StrokeCap = SKStrokeCap.Round
            };

            _glowPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
            };

            int starCount = _isOverlayActive ? OverlayStarCount : DefaultStarCount;
            InitializeStars(starCount);
            CreateConstellations();

            _isInitialized = true;
            Log.Debug("ConstellationRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive == isOverlayActive) return;

            _isOverlayActive = isOverlayActive;
            int starCount = _isOverlayActive ? OverlayStarCount : DefaultStarCount;
            InitializeStars(starCount);
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
                    UpdateStars(info);
                }

                RenderStarField(canvas!, info, paint!);
                drawPerformanceInfo?.Invoke(canvas!, info);
            }
            catch (Exception ex)
            {
                Log.Error($"Error in ConstellationRenderer.Render: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                    _spectrumSemaphore.Release();
            }
        }

        private void RenderStarField(SKCanvas canvas, SKImageInfo info, SKPaint basePaint)
        {
            if (_stars == null || _constellations == null ||
                _starPaint == null || _linePaint == null || _glowPaint == null ||
                _processedSpectrum == null)
                return;

            // Очищаем холст черным цветом
            canvas.Clear(SKColors.Black);

            // Настраиваем цвета на основе базовой кисти
            SKColor baseColor = basePaint.Color;

            // Настраиваем центр вращения
            canvas.Save();
            canvas.Translate(info.Width / 2, info.Height / 2);
            canvas.RotateRadians(_rotationAngle);
            canvas.Translate(-info.Width / 2, -info.Height / 2);

            // Первым слоем рисуем созвездия
            bool showConstellations = _processedSpectrum[0] > BassThreshold;

            if (showConstellations)
            {
                _linePaint.Color = baseColor.WithAlpha(ConstellationLineAlpha); // Теперь ConstellationLineAlpha - byte

                foreach (var constellation in _constellations)
                {
                    if (!constellation.IsActive) continue;

                    // Рисуем линии созвездия
                    for (int i = 0; i < constellation.LineIndices.Count; i += 2)
                    {
                        int idx1 = constellation.LineIndices[i];
                        int idx2 = constellation.LineIndices[i + 1];

                        if (idx1 < _stars.Count && idx2 < _stars.Count)
                        {
                            var star1 = _stars[idx1];
                            var star2 = _stars[idx2];

                            if (star1.IsActive && star2.IsActive)
                            {
                                // Рисуем линию с пульсацией в зависимости от спектра
                                float pulseFactor = 0.5f + _processedSpectrum[1] * 0.5f;
                                _linePaint.StrokeWidth = 1.0f * pulseFactor;

                                canvas.DrawLine(star1.X, star1.Y, star2.X, star2.Y, _linePaint);
                            }
                        }
                    }
                }
            }

            // Вторым слоем рисуем звезды
            foreach (var star in _stars)
            {
                if (!star.IsActive) continue;

                // Вычисляем яркость звезды на основе её мерцания и соответствующей частоты
                float brightness = star.Brightness;
                int freqBand = Math.Min(star.FrequencyBand, _processedSpectrum.Length - 1);
                float spectrumFactor = _processedSpectrum[freqBand];

                // Сияние для ярких звезд
                if (brightness > 0.7f || spectrumFactor > 0.7f)
                {
                    float glowSize = star.Size * (1 + spectrumFactor);
                    // Явное преобразование результата умножения float в byte
                    byte glowAlpha = (byte)Math.Clamp((int)(StarGlowAlpha * brightness), 0, 255);
                    _glowPaint.Color = star.Color.WithAlpha(glowAlpha);
                    canvas.DrawCircle(star.X, star.Y, glowSize * 2, _glowPaint);
                }

                // Рисуем саму звезду
                // Явное преобразование результата умножения float в byte
                byte starAlpha = (byte)Math.Clamp((int)(255 * brightness), 0, 255);
                _starPaint.Color = star.Color.WithAlpha(starAlpha);
                canvas.DrawCircle(star.X, star.Y, star.Size, _starPaint);
            }

            canvas.Restore();
        }
        #endregion

        #region Star and Constellation Management
        private void InitializeStars(int count)
        {
            _stars = new List<Star>(count);

            // Создаем звезды разных размеров и цветов
            for (int i = 0; i < count; i++)
            {
                float size = MinStarSize + (float)_random.NextDouble() * (MaxStarSize - MinStarSize);

                // Звезды с разными цветовыми оттенками
                byte r = (byte)(180 + _random.Next(0, 75));
                byte g = (byte)(180 + _random.Next(0, 75));
                byte b = (byte)(180 + _random.Next(0, 75));

                // Небольшая вероятность для цветных звезд
                if (_random.NextDouble() < 0.2)
                {
                    // Красные, синие или желтые звезды
                    switch (_random.Next(3))
                    {
                        case 0: // Красные
                            r = (byte)(200 + _random.Next(0, 55));
                            g = (byte)(100 + _random.Next(0, 55));
                            b = (byte)(100 + _random.Next(0, 30));
                            break;
                        case 1: // Синие
                            r = (byte)(100 + _random.Next(0, 30));
                            g = (byte)(150 + _random.Next(0, 50));
                            b = (byte)(220 + _random.Next(0, 35));
                            break;
                        case 2: // Желтые
                            r = (byte)(220 + _random.Next(0, 35));
                            g = (byte)(220 + _random.Next(0, 35));
                            b = (byte)(100 + _random.Next(0, 50));
                            break;
                    }
                }

                _stars.Add(new Star
                {
                    X = _random.Next(100, 900),
                    Y = _random.Next(100, 700),
                    Size = size,
                    Brightness = 0.5f + (float)_random.NextDouble() * 0.5f,
                    TwinkleFactor = (float)_random.NextDouble() * 6.28f, // Случайная фаза
                    TwinkleSpeed = 0.5f + (float)_random.NextDouble() * 2f, // Случайная скорость
                    Color = new SKColor(r, g, b),
                    IsActive = true,
                    FrequencyBand = _random.Next(0, 3) // Случайный диапазон частот (0-2)
                });
            }
        }

        private void CreateConstellations()
        {
            if (_stars == null || _stars.Count < 10) return;

            _constellations = new List<Constellation>();

            // Создаем несколько созвездий, выбирая случайные звезды
            for (int c = 0; c < ConstellationCount; c++)
            {
                // Выбираем начальную звезду
                int startIdx = _random.Next(_stars.Count);
                List<int> constellationStars = new() { startIdx };

                // Определяем размер созвездия (3-8 звезд)
                int constellationSize = 3 + _random.Next(5);

                // Находим ближайшие звезды для созвездия
                for (int i = 1; i < constellationSize; i++)
                {
                    int lastStarIdx = constellationStars[^1];
                    float lastX = _stars[lastStarIdx].X;
                    float lastY = _stars[lastStarIdx].Y;

                    // Найти ближайшую звезду, которая еще не в созвездии
                    float minDist = float.MaxValue;
                    int nearestIdx = -1;

                    for (int j = 0; j < _stars.Count; j++)
                    {
                        if (constellationStars.Contains(j)) continue;

                        float dx = _stars[j].X - lastX;
                        float dy = _stars[j].Y - lastY;
                        float dist = dx * dx + dy * dy;

                        // Ограничиваем расстояние между звездами
                        if (dist < minDist && dist < 40000) // ~200 пикселей
                        {
                            minDist = dist;
                            nearestIdx = j;
                        }
                    }

                    if (nearestIdx != -1)
                    {
                        constellationStars.Add(nearestIdx);
                    }
                    else
                    {
                        // Не нашли подходящую звезду, заканчиваем созвездие
                        break;
                    }
                }

                // Создаем линии между звездами
                List<int> lines = new();
                for (int i = 0; i < constellationStars.Count - 1; i++)
                {
                    lines.Add(constellationStars[i]);
                    lines.Add(constellationStars[i + 1]);
                }

                _constellations.Add(new Constellation
                {
                    StarIndices = constellationStars,
                    LineIndices = lines,
                    IsActive = _random.NextDouble() > 0.3 // 70% шанс активности
                });
            }
        }

        private void UpdateStars(SKImageInfo info)
        {
            if (_stars == null || _processedSpectrum == null) return;

            _time += TimeStep;

            // Обновляем угол вращения
            float rotationMultiplier = 1.0f;
            if (_processedSpectrum.Length > 0)
            {
                rotationMultiplier = 1.0f + _processedSpectrum[0] * 2.0f;
            }
            _rotationAngle += RotationSpeed * rotationMultiplier * TimeStep;

            // Обновляем звезды
            for (int i = 0; i < _stars.Count; i++)
            {
                var star = _stars[i];

                // Обновляем мерцание
                float twinkling = (float)Math.Sin(_time * TwinkleSpeed * star.TwinkleSpeed + star.TwinkleFactor);

                // Применяем влияние спектра на яркость
                int freqBand = Math.Min(star.FrequencyBand, _processedSpectrum.Length - 1);
                float spectrumFactor = _processedSpectrum[freqBand];

                // Вычисляем новую яркость
                float targetBrightness = 0.5f + (twinkling * 0.25f) + (spectrumFactor * 0.25f);
                star.Brightness = Math.Clamp(targetBrightness, 0.3f, 1.0f);

                _stars[i] = star;
            }

            // Обновляем видимость созвездий
            if (_constellations != null && _processedSpectrum.Length > 0)
            {
                bool bassActive = _processedSpectrum[0] > BassThreshold;

                if (bassActive)
                {
                    // Активируем случайные созвездия при сильном басе
                    for (int i = 0; i < _constellations.Count; i++)
                    {
                        var constellation = _constellations[i];

                        if (!constellation.IsActive && _random.NextDouble() < 0.05)
                        {
                            constellation.IsActive = true;
                            _constellations[i] = constellation;
                        }
                    }
                }
                else
                {
                    // Деактивируем случайные созвездия при слабом басе
                    for (int i = 0; i < _constellations.Count; i++)
                    {
                        var constellation = _constellations[i];

                        if (constellation.IsActive && _random.NextDouble() < 0.01)
                        {
                            constellation.IsActive = false;
                            _constellations[i] = constellation;
                        }
                    }
                }
            }
        }
        #endregion

        #region Spectrum Processing
        private void ProcessSpectrum(float[] spectrum)
        {
            // Обрабатываем спектр, разделяя на 3 диапазона
            int lows = spectrum.Length / 8;
            int mids = spectrum.Length / 3;

            // Средние значения по диапазонам
            float lowAvg = CalculateAverage(spectrum, 0, lows);
            float midAvg = CalculateAverage(spectrum, lows, mids);
            float highAvg = CalculateAverage(spectrum, mids, spectrum.Length / 2);

            // Инициализируем массив, если нужно
            if (_processedSpectrum == null || _processedSpectrum.Length < 3)
            {
                _processedSpectrum = new float[3];
                _processedSpectrum[0] = lowAvg;
                _processedSpectrum[1] = midAvg;
                _processedSpectrum[2] = highAvg;
            }
            else
            {
                // Применяем сглаживание
                _processedSpectrum[0] += (lowAvg - _processedSpectrum[0]) * SmoothingFactor;
                _processedSpectrum[1] += (midAvg - _processedSpectrum[1]) * SmoothingFactor;
                _processedSpectrum[2] += (highAvg - _processedSpectrum[2]) * SmoothingFactor;
            }

            // Ограничиваем максимальные значения
            for (int i = 0; i < _processedSpectrum.Length; i++)
            {
                _processedSpectrum[i] = Math.Min(_processedSpectrum[i] * 3, 1.0f); // Усиливаем для лучшей видимости
            }
        }

        private float CalculateAverage(float[] data, int start, int end)
        {
            float sum = 0;
            for (int i = start; i < end; i++)
            {
                sum += data[i];
            }
            return sum / (end - start);
        }
        #endregion

        #region Structures
        private struct Star
        {
            public float X;
            public float Y;
            public float Size;
            public float Brightness;
            public float TwinkleFactor;
            public float TwinkleSpeed;
            public SKColor Color;
            public bool IsActive;
            public int FrequencyBand;
        }

        private struct Constellation
        {
            public List<int> StarIndices;
            public List<int> LineIndices;
            public bool IsActive;
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
                Log.Error("ConstellationRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null ||
                spectrum == null || spectrum.Length < 2 ||
                paint == null ||
                drawPerformanceInfo == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                Log.Error("Invalid render parameters for ConstellationRenderer");
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
            _starPaint?.Dispose();
            _linePaint?.Dispose();
            _glowPaint?.Dispose();

            _starPaint = null;
            _linePaint = null;
            _glowPaint = null;
            _stars = null;
            _constellations = null;
            _processedSpectrum = null;

            _disposed = true;
            _isInitialized = false;
            Log.Debug("ConstellationRenderer disposed");
        }
        #endregion
    }
}