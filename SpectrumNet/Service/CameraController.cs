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
        private const float MouseSensitivity = 0.5f, MoveSpeed = 20f;
        private readonly HashSet<Key> _pressedKeys = new();
        private Point _lastMousePosition;
        private readonly Renderer _renderer;
        private ICameraControllable? _currentCamera;
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
                    _currentCamera.CameraRotationOffset = new Vector2(0.1f, 0.1f);
                    _renderer.RequestRender();
                    Task.Delay(100).ContinueWith(_ =>
                    {
                        if (_currentCamera != null)
                        {
                            _currentCamera.CameraRotationOffset = new Vector2(0.2f, 0.2f);
                            _renderer.RequestRender();
                        }
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка инициализации камеры: {ex}");
            }
        }

        public void UpdateActiveCamera(RenderStyle style)
        {
            // Сохраняем предыдущие настройки (если есть)
            var prevPos = _currentCamera?.CameraPositionOffset ?? Vector3.Zero;
            var prevRot = _currentCamera?.CameraRotationOffset ?? Vector2.Zero;
            var prevTilt = _currentCamera?.CameraTiltAngle ?? 10f;
            var prevHeight = _currentCamera?.CameraHeightFactor ?? 0.8f;

            var renderer = SpectrumRendererFactory.GetCachedRenderer(style);
            if (renderer is ICameraControllable cam)
            {
                _currentCamera = cam;
                if (prevPos != Vector3.Zero || prevRot != Vector2.Zero)
                {
                    cam.CameraPositionOffset = prevPos;
                    cam.CameraRotationOffset = prevRot;
                    cam.CameraTiltAngle = prevTilt;
                    cam.CameraHeightFactor = prevHeight;
                }
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
                UpdateCameraPosition();
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
                var delta = currentPos - _lastMousePosition;
                var rotationDelta = new Vector2((float)delta.X * MouseSensitivity, (float)delta.Y * MouseSensitivity);
                Vector2 currentRotation = _currentCamera.CameraRotationOffset;
                var newRotation = currentRotation + rotationDelta;
                _currentCamera.CameraRotationOffset = newRotation;
                SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Mouse drag: {currentRotation} -> {newRotation}");
                _renderer.RequestRender();
            }
            _lastMousePosition = currentPos;
        }

        private void UpdateCameraPosition()
        {
            if (_currentCamera == null) return;
            Vector3 offset = Vector3.Zero;
            if (_pressedKeys.Contains(Key.W)) offset.Z -= MoveSpeed;
            if (_pressedKeys.Contains(Key.S)) offset.Z += MoveSpeed;
            if (_pressedKeys.Contains(Key.A)) offset.X -= MoveSpeed;
            if (_pressedKeys.Contains(Key.D)) offset.X += MoveSpeed;
            var newPos = _currentCamera.CameraPositionOffset + offset;
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Camera position: {_currentCamera.CameraPositionOffset} -> {newPos}");
            _currentCamera.CameraPositionOffset = newPos;
            _renderer.RequestRender();
        }

        public void HandleMouseWheel(MouseWheelEventArgs e)
        {
            if (_currentCamera == null) return;
            const float zoomSpeed = 20f;
            float zoomDelta = e.Delta > 0 ? -zoomSpeed : zoomSpeed;
            var newPos = _currentCamera.CameraPositionOffset + new Vector3(0, 0, zoomDelta);
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Zoom: {_currentCamera.CameraPositionOffset.Z} -> {newPos.Z}");
            _currentCamera.CameraPositionOffset = newPos;
            _renderer.RequestRender();
        }

        public void ResetCamera()
        {
            if (_currentCamera == null) return;
            _currentCamera.CameraPositionOffset = Vector3.Zero;
            _currentCamera.CameraRotationOffset = Vector2.Zero;
            _currentCamera.CameraTiltAngle = 10f;
            _currentCamera.CameraHeightFactor = 0.8f;
            _pressedKeys.Clear();
            SmartLogger.Log(LogLevel.Information, LogPrefix, "Camera reset to default position");
            _renderer.RequestRender();
        }

        public void ActivateCamera()
        {
            if (_currentCamera == null)
            {
                UpdateActiveCamera(_renderer.Controller.SelectedDrawingType);
                if (_currentCamera == null) return;
            }
            var originalRotation = _currentCamera.CameraRotationOffset;
            _currentCamera.CameraRotationOffset = originalRotation + new Vector2(0.5f, 0.5f);
            _renderer.RequestRender();
            Task.Delay(100).ContinueWith(_ =>
            {
                if (_currentCamera != null)
                {
                    _currentCamera.CameraRotationOffset = originalRotation;
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
                var curCamera = _cameraController?.CurrentCamera;
                var currentPos = curCamera?.CameraPositionOffset ?? Vector3.Zero;
                var currentRot = curCamera?.CameraRotationOffset ?? Vector2.Zero;
                var currentTilt = curCamera?.CameraTiltAngle ?? 10f;
                var currentHeight = curCamera?.CameraHeightFactor ?? 0.8f;

                _cameraController?.UpdateActiveCamera(style);
                if (_cameraController?.CurrentCamera != null &&
                    (currentPos != Vector3.Zero || currentRot != Vector2.Zero))
                {
                    var cam = _cameraController.CurrentCamera;
                    cam.CameraPositionOffset = currentPos;
                    cam.CameraRotationOffset = currentRot;
                    cam.CameraTiltAngle = currentTilt;
                    cam.CameraHeightFactor = currentHeight;
                }
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