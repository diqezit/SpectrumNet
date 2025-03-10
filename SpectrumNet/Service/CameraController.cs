using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;

#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Контроллер управления камерой для 3D-рендереров
    /// </summary>
    public class CameraController
    {
        private const string LogPrefix = "[CameraController] ";
        private const float MouseSensitivity = 0.25f; // Чувствительность мышки
        private const float MoveSpeed = 250f; // Скорость камеры

        private readonly HashSet<Key> _pressedKeys = new();
        private readonly Renderer _renderer;
        private readonly Stopwatch _stopwatch = new();

        private ICameraControllable? _currentCamera;
        private Point _lastMousePosition;
        private Vector3 _cameraPosition = new(0, 0, 400);
        private Vector3 _forward;
        private Vector3 _right;
        private Vector3 _up = new(0, 1, 0);
        private float _yaw = 0f;
        private float _pitch = 0f;

        public ICameraControllable? CurrentCamera => _currentCamera;

        public CameraController(Renderer renderer)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            try
            {
                UpdateActiveCamera(_renderer.Controller.SelectedDrawingType);
                SmartLogger.Log(LogLevel.Information, LogPrefix,
                    $"Инициализирован для {_renderer.Controller.SelectedDrawingType}, камера активна: {_currentCamera != null}");
                if (_currentCamera != null)
                {
                    _currentCamera.CameraPosition = new Vector3(0, 0, 400);
                    _renderer.RequestRender();
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка инициализации камеры: {ex}");
            }
        }

        public void UpdateActiveCamera(RenderStyle style)
        {
            var renderer = SpectrumRendererFactory.GetCachedRenderer(style);
            if (renderer is ICameraControllable cam)
            {
                _currentCamera = cam;
                _currentCamera.CameraPosition = _cameraPosition;
                _currentCamera.CameraForward = _forward;
                _currentCamera.CameraUp = _up;
                _renderer.RequestRender();
            }
            else
            {
                _currentCamera = null;
                SmartLogger.Log(LogLevel.Information, LogPrefix, $"Рендерер {style} не поддерживает управление камерой");
            }
        }

        public void HandleKeyDown(KeyEventArgs e)
        {
            if (_currentCamera == null) return;
            if (e.Key is Key.W or Key.A or Key.S or Key.D)
            {
                _pressedKeys.Add(e.Key);
                e.Handled = true;
            }
        }

        public void HandleKeyUp(KeyEventArgs e)
        {
            if (e.Key is Key.W or Key.A or Key.S or Key.D)
            {
                _pressedKeys.Remove(e.Key);
                e.Handled = true;
            }
        }

        public void HandleMouseMove(MouseEventArgs e)
        {
            if (_currentCamera == null) return;
            Point currentPos = e.GetPosition(null);
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var deltaX = (float)(currentPos.X - _lastMousePosition.X) * MouseSensitivity;
                var deltaY = (float)(currentPos.Y - _lastMousePosition.Y) * MouseSensitivity;

                _yaw += deltaX;
                _pitch -= deltaY;
                _pitch = Math.Clamp(_pitch, -89f, 89f);

                UpdateCameraOrientation();
                _renderer.RequestRender();
            }
            _lastMousePosition = currentPos;
        }

        private void UpdateCameraOrientation()
        {
            _forward.X = (float)(Math.Cos(MathHelper.DegreesToRadians(_yaw)) * Math.Cos(MathHelper.DegreesToRadians(_pitch)));
            _forward.Y = (float)Math.Sin(MathHelper.DegreesToRadians(_pitch));
            _forward.Z = (float)(Math.Sin(MathHelper.DegreesToRadians(_yaw)) * Math.Cos(MathHelper.DegreesToRadians(_pitch)));
            _forward = Vector3.Normalize(_forward);
            _right = Vector3.Normalize(Vector3.Cross(_forward, _up));

            if (_currentCamera != null)
            {
                _currentCamera.CameraForward = _forward;
                _currentCamera.CameraUp = _up;
            }
        }

        private void UpdateCameraPosition(float deltaTime)
        {
            if (_currentCamera == null) return;

            Vector3 moveDirection = Vector3.Zero;
            if (_pressedKeys.Contains(Key.W)) moveDirection += _forward;
            if (_pressedKeys.Contains(Key.S)) moveDirection -= _forward;
            if (_pressedKeys.Contains(Key.A)) moveDirection -= _right;
            if (_pressedKeys.Contains(Key.D)) moveDirection += _right;

            if (moveDirection != Vector3.Zero)
            {
                moveDirection = Vector3.Normalize(moveDirection);
                _cameraPosition += moveDirection * MoveSpeed * deltaTime;
                _currentCamera.CameraPosition = _cameraPosition;
                _currentCamera.CameraForward = _forward;
                _currentCamera.CameraUp = _up;
                _renderer.RequestRender();
            }
        }

        public void Update()
        {
            if (_stopwatch.IsRunning)
            {
                float deltaTime = (float)_stopwatch.Elapsed.TotalSeconds;
                _stopwatch.Restart();
                UpdateCameraPosition(deltaTime);
            }
            else
            {
                _stopwatch.Start();
            }
        }

        public void HandleMouseWheel(MouseWheelEventArgs e)
        {
            if (_currentCamera == null) return;
            const float zoomSpeed = 50f;
            float zoomDelta = e.Delta > 0 ? -zoomSpeed : zoomSpeed;
            _cameraPosition += _forward * zoomDelta * 0.1f;
            _renderer.RequestRender();
        }

        public void ResetCamera()
        {
            if (_currentCamera == null) return;
            _cameraPosition = new Vector3(0, 0, 400);
            _yaw = 0f;
            _pitch = 0f;
            UpdateCameraOrientation();
            _pressedKeys.Clear();
            _renderer.RequestRender();
        }

        public void ActivateCamera()
        {
            if (_currentCamera == null)
            {
                UpdateActiveCamera(_renderer.Controller.SelectedDrawingType);
                if (_currentCamera == null) return;
            }

            var originalPosition = _currentCamera.CameraPosition;
            _currentCamera.CameraPosition += new Vector3(0, 0, 50);
            _renderer.RequestRender();

            Task.Delay(100).ContinueWith(_ =>
            {
                if (_currentCamera != null)
                {
                    _currentCamera.CameraPosition = originalPosition;
                    _renderer.RequestRender();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    /// <summary>
    /// Менеджер визуализации спектра (рендеринг и управление камерой)
    /// </summary>
    public sealed class VisualizationManager : IDisposable
    {
        private const string LogPrefix = "[VisualizationManager] ";

        private readonly IAudioVisualizationController _controller;
        private readonly GLWpfControl _renderElement;
        private readonly IOpenGLService _glService;

        private Renderer? _renderer;
        private CameraController? _cameraController;
        private SpectrumAnalyzer? _analyzer;
        private SpectrumBrushes? _spectrumStyles;
        private bool _disposed, _isInitialized;

        public Renderer? Renderer => _renderer;
        public CameraController? CameraController => _cameraController;

        public VisualizationManager(IAudioVisualizationController controller, GLWpfControl renderElement, IOpenGLService glService)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _renderElement = renderElement ?? throw new ArgumentNullException(nameof(renderElement));
            _glService = glService ?? throw new ArgumentNullException(nameof(glService));
        }

        public void Initialize(SpectrumAnalyzer analyzer, SpectrumBrushes spectrumStyles)
        {
            if (_isInitialized) return;
            try
            {
                _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
                _spectrumStyles = spectrumStyles ?? throw new ArgumentNullException(nameof(spectrumStyles));
                _renderer = new Renderer(_spectrumStyles, _controller, _analyzer, _renderElement, _glService);
                _renderer.RequestRender();

                foreach (RenderStyle style in Enum.GetValues(typeof(RenderStyle)))
                    SpectrumRendererFactory.CreateRenderer(style, _controller.IsOverlayActive, _controller.RenderQuality);

                _cameraController = new CameraController(_renderer);
                _cameraController.UpdateActiveCamera(_controller.SelectedDrawingType);
                _renderer.RequestRender();

                Task.Delay(300).ContinueWith(_ => ActivateCamera(),
                    TaskScheduler.FromCurrentSynchronizationContext());

                _isInitialized = true;
                SmartLogger.Log(LogLevel.Information, LogPrefix, "Визуализация инициализирована");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка инициализации визуализации: {ex}");
                throw;
            }
        }

        public void ActivateCamera()
        {
            if (_cameraController == null || _disposed) return;
            try
            {
                _cameraController.UpdateActiveCamera(_controller.SelectedDrawingType);
                if (_cameraController.CurrentCamera != null)
                {
                    var cam = _cameraController.CurrentCamera;
                    var orig = cam.CameraRotationOffset;
                    cam.CameraRotationOffset = orig + new Vector2(0.5f, 0.5f);
                    _renderer?.RequestRender();

                    Task.Delay(100).ContinueWith(_ =>
                    {
                        if (!_disposed)
                        {
                            cam.CameraRotationOffset = orig;
                            _renderer?.RequestRender();
                        }
                    }, TaskScheduler.FromCurrentSynchronizationContext());

                    SmartLogger.Log(LogLevel.Information, LogPrefix, $"Камера активирована для {_controller.SelectedDrawingType}");
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка активации камеры: {ex}");
            }
        }

        public void HandleRenderStyleChanged(RenderStyle style)
        {
            try
            {
                _cameraController?.UpdateActiveCamera(style);
                _renderer?.RequestRender();
                _renderElement?.InvalidateVisual();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка при изменении стиля рендеринга: {ex}");
            }
        }

        public void UpdateRenderDimensions(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Недопустимые размеры: {width}x{height}");
                return;
            }
            try { _renderer?.UpdateRenderDimensions(width, height); }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка обновления размеров: {ex}");
            }
        }

        public void RequestRender()
        {
            if (_disposed) return;
            _cameraController?.Update();
            _renderer?.RequestRender();
            _renderElement?.InvalidateVisual();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _renderer?.Dispose();
                _renderer = null;
                _cameraController = null;
                _analyzer = null;
                _spectrumStyles = null;
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}