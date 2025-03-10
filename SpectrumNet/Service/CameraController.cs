#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Контроллер управления камерой для 3D-рендереров.
    /// </summary>
    public class CameraController
    {
        private const string LogPrefix = "CameraController";

        #region Вспомогательные настройки

        private record CameraSensitivity
        {
            public const float Mouse = 0.25f;
            public const float Movement = 250f;
            public const float Zoom = 0.1f;
        }

        private record CameraLimits
        {
            public const float MaxPitch = 89f;
            public const float MinPitch = -89f;
            public const float MinHeight = -50f;
            public const float MaxHeight = 1000f;
            public const float MinDistance = 50f;
            public const float MaxDistance = 2000f;
            public const float MaxHorizontalDistance = 500f;
        }

        private record CameraDefaults
        {
            public const float DefaultYaw = -90f;
            public const float DefaultPitch = -30f;
            public const float DefaultZ = 600f;
            public const float DistanceFactor = 1.2f;
            public const float HeightFactor = 0.33f;
            public const float FieldOfView = 80f;
        }

        #endregion

        private readonly float _sceneWidth;
        private readonly float _sceneHeight;
        private readonly float _sceneDepth;

        private readonly HashSet<Key> _pressedKeys = new();
        private readonly IRenderer _renderer;
        private readonly Stopwatch _stopwatch = new();
        private readonly IAudioVisualizationController _controller;

        private ICameraControllable? _currentCamera;
        private Point _lastMousePosition;
        private Vector3 _cameraPosition = new(0, 0, CameraDefaults.DefaultZ);
        private Vector3 _forward;
        private Vector3 _right;
        private Vector3 _up = new(0, 1, 0);
        private float _yaw = CameraDefaults.DefaultYaw;
        private float _pitch = CameraDefaults.DefaultPitch;
        private Vector3 _sceneCenter;

        public ICameraControllable? CurrentCamera => _currentCamera;

        public CameraController(IRenderer renderer, float sceneWidth, float sceneHeight, float sceneDepth)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _controller = renderer.Controller;
            _sceneWidth = sceneWidth;
            _sceneHeight = sceneHeight;
            _sceneDepth = sceneDepth;

            SmartLogger.Safe(() => InitializeCamera(), LogPrefix, "Ошибка инициализации камеры");
        }

        private void InitializeCamera()
        {
            UpdateSceneCenter();
            _cameraPosition = new Vector3(
                _sceneCenter.X,
                _sceneCenter.Y * CameraDefaults.HeightFactor,
                CameraDefaults.DefaultZ
            );

            UpdateActiveCamera(_controller.SelectedDrawingType);
            UpdateCameraOrientation();
            SyncCameraState();

            SmartLogger.Log(LogLevel.Information, LogPrefix,
                $"Инициализирован для {_controller.SelectedDrawingType}, камера активна: {_currentCamera != null}");
        }

        public void UpdateActiveCamera(RenderStyle style)
        {
            var camRenderer = SpectrumRendererFactory.GetCachedRenderer(style);
            if (camRenderer is ICameraControllable cam)
            {
                var oldCamera = _currentCamera;
                _currentCamera = cam;
                if (oldCamera != null)
                {
                    _cameraPosition = oldCamera.CameraPosition;
                    _forward = oldCamera.CameraForward;
                    _up = oldCamera.CameraUp;
                }
                SyncCameraState();
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
            if (e.Key is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E)
            {
                _pressedKeys.Add(e.Key);
                e.Handled = true;
            }
        }

        public void HandleKeyUp(KeyEventArgs e)
        {
            if (e.Key is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E)
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
                HandleRotation(currentPos);
            }
            else if (e.RightButton == MouseButtonState.Pressed)
            {
                HandlePanning(currentPos);
            }
            _lastMousePosition = currentPos;
        }

        private void HandleRotation(Point currentPos)
        {
            float deltaX = (float)(currentPos.X - _lastMousePosition.X) * CameraSensitivity.Mouse;
            float deltaY = (float)(currentPos.Y - _lastMousePosition.Y) * CameraSensitivity.Mouse;

            _yaw += deltaX;
            _pitch = Math.Clamp(_pitch - deltaY, CameraLimits.MinPitch, CameraLimits.MaxPitch);

            UpdateCameraOrientation();
            SyncCameraState();
        }

        private void HandlePanning(Point currentPos)
        {
            float deltaX = (float)(currentPos.X - _lastMousePosition.X) * CameraSensitivity.Mouse * 0.5f;
            float deltaY = (float)(currentPos.Y - _lastMousePosition.Y) * CameraSensitivity.Mouse * 0.5f;

            Vector3 newPosition = _cameraPosition - _right * deltaX + _up * deltaY;
            ApplyMovementLimits(ref newPosition);
            _cameraPosition = newPosition;
            SyncCameraState();
        }

        private void UpdateCameraOrientation()
        {
            _forward = new Vector3(
                (float)(Math.Cos(MathHelper.DegreesToRadians(_yaw)) * Math.Cos(MathHelper.DegreesToRadians(_pitch))),
                (float)Math.Sin(MathHelper.DegreesToRadians(_pitch)),
                (float)(Math.Sin(MathHelper.DegreesToRadians(_yaw)) * Math.Cos(MathHelper.DegreesToRadians(_pitch)))
            );
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

            Vector3 moveDirection = CalculateMoveDirection();
            if (moveDirection != Vector3.Zero)
            {
                moveDirection = Vector3.Normalize(moveDirection);
                Vector3 newPosition = _cameraPosition + moveDirection * CameraSensitivity.Movement * deltaTime;
                ApplyMovementLimits(ref newPosition);
                _cameraPosition = newPosition;
                SyncCameraState();
            }
        }

        private Vector3 CalculateMoveDirection()
        {
            Vector3 direction = Vector3.Zero;
            if (_pressedKeys.Contains(Key.W)) direction += _forward;
            if (_pressedKeys.Contains(Key.S)) direction -= _forward;
            if (_pressedKeys.Contains(Key.A)) direction -= _right;
            if (_pressedKeys.Contains(Key.D)) direction += _right;
            if (_pressedKeys.Contains(Key.Q)) direction -= new Vector3(0, 1, 0);
            if (_pressedKeys.Contains(Key.E)) direction += new Vector3(0, 1, 0);
            return direction;
        }

        private void ApplyMovementLimits(ref Vector3 pos)
        {
            float sceneMinX = -_sceneWidth / 2;
            float sceneMaxX = _sceneWidth / 2;
            float sceneMinY = 0f;
            float sceneMaxY = _sceneHeight;
            float sceneMinZ = -_sceneDepth / 2;
            float sceneMaxZ = _sceneDepth / 2;

            pos.X = Math.Clamp(pos.X, sceneMinX, sceneMaxX);
            pos.Y = Math.Clamp(pos.Y, sceneMinY, sceneMaxY);
            pos.Z = Math.Clamp(pos.Z, sceneMinZ, sceneMaxZ);
        }

        public void Update()
        {
            SmartLogger.Safe(() => {
                if (_stopwatch.IsRunning)
                {
                    float deltaTime = (float)_stopwatch.Elapsed.TotalSeconds;
                    _stopwatch.Restart();
                    UpdateCameraPosition(deltaTime);

                    if (_currentCamera is ISceneRenderer sceneRenderer && sceneRenderer.SceneGeometry != null)
                    {
                        float aspectRatio = (float)_controller.SpectrumCanvas.ActualWidth / (float)_controller.SpectrumCanvas.ActualHeight;
                        sceneRenderer.SceneGeometry.UpdateSceneSize(_cameraPosition, CameraDefaults.FieldOfView, aspectRatio);
                    }
                }
                else
                {
                    _stopwatch.Start();
                }
            }, LogPrefix, "Ошибка обновления камеры");
        }

        public void HandleMouseWheel(MouseWheelEventArgs e)
        {
            if (_currentCamera == null) return;
            float zoomFactor = 1.0f + (e.Delta > 0 ? -CameraSensitivity.Zoom : CameraSensitivity.Zoom);

            Vector3 directionToCamera = _cameraPosition - _sceneCenter;
            float currentDistance = directionToCamera.Length;
            float newDistance = Math.Clamp(currentDistance * zoomFactor, CameraLimits.MinDistance, CameraLimits.MaxDistance);

            if (currentDistance > 0)
            {
                Vector3 newPosition = _sceneCenter + Vector3.Normalize(directionToCamera) * newDistance;
                ApplyMovementLimits(ref newPosition);
                _cameraPosition = newPosition;
                SyncCameraState();
            }
        }

        public void ResetCamera()
        {
            if (_currentCamera == null) return;
            UpdateSceneCenter();
            _cameraPosition = new Vector3(_sceneCenter.X, _sceneCenter.Y * CameraDefaults.HeightFactor, CameraDefaults.DefaultZ);
            _yaw = CameraDefaults.DefaultYaw;
            _pitch = CameraDefaults.DefaultPitch;
            UpdateCameraOrientation();
            _pressedKeys.Clear();
            SyncCameraState();
        }

        private void UpdateSceneCenter()
        {
            _sceneCenter = new Vector3(0, _sceneHeight / 2, 0);
        }

        public void AdjustCameraToSpectrum()
        {
            if (_currentCamera == null) return;

            SmartLogger.Safe(() => {
                UpdateSceneCenter();
                float viewportWidth = (float)_controller.SpectrumCanvas.ActualWidth;
                float fovRadians = MathHelper.DegreesToRadians(CameraDefaults.FieldOfView);
                float distanceForWidth = viewportWidth / (2 * MathF.Tan(fovRadians / 2));

                _cameraPosition = new Vector3(_sceneCenter.X, _sceneCenter.Y, distanceForWidth * CameraDefaults.DistanceFactor);
                _yaw = CameraDefaults.DefaultYaw;
                _pitch = CameraDefaults.DefaultPitch;
                UpdateCameraOrientation();
                SyncCameraState();
            }, LogPrefix, "Ошибка настройки камеры для спектра");
        }

        public void ActivateCamera()
        {
            if (_currentCamera == null)
            {
                UpdateActiveCamera(_controller.SelectedDrawingType);
                if (_currentCamera == null) return;
            }

            SmartLogger.Safe(() => {
                Vector3 originalPosition = _currentCamera.CameraPosition;
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
            }, LogPrefix, "Ошибка активации камеры");
        }

        private void SyncCameraState()
        {
            SmartLogger.Safe(() => {
                if (_currentCamera != null)
                {
                    _currentCamera.CameraPosition = _cameraPosition;
                    _currentCamera.CameraForward = _forward;
                    _currentCamera.CameraUp = _up;
                }
                _renderer.RequestRender();
            }, LogPrefix, "Ошибка синхронизации состояния камеры");
        }
    }

    /// <summary>
    /// Менеджер визуализации спектра (рендеринг и управление камерой).
    /// </summary>
    public sealed class VisualizationManager : IDisposable
    {
        private const string LogPrefix = "VisualizationManager";

        private readonly IAudioVisualizationController _controller;
        private readonly GLWpfControl _renderElement;
        private readonly IOpenGLService _glService;

        private IRenderer? _renderer;
        private CameraController? _cameraController;
        private SpectrumAnalyzer? _analyzer;
        private SpectrumBrushes? _spectrumStyles;
        private bool _disposed, _isInitialized;
        private bool _needsRender;

        public IRenderer? Renderer => _renderer;
        public CameraController? CameraController => _cameraController;

        /// <summary>
        /// Указывает, требуется ли перерисовка в следующем кадре
        /// </summary>
        public bool NeedsRender
        {
            get => _needsRender;
            set => _needsRender = value;
        }

        public VisualizationManager(IAudioVisualizationController controller, GLWpfControl renderElement, IOpenGLService glService)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _renderElement = renderElement ?? throw new ArgumentNullException(nameof(renderElement));
            _glService = glService ?? throw new ArgumentNullException(nameof(glService));
        }

        public void Initialize(SpectrumAnalyzer analyzer, SpectrumBrushes spectrumStyles)
        {
            if (_isInitialized) return;

            bool initSuccess = false;

            SmartLogger.Safe(() => {
                InitializeComponents(analyzer, spectrumStyles);
                InitializeRenderers();
                InitializeCamera();
                _isInitialized = true;
                _needsRender = true; 
                initSuccess = true;
            }, LogPrefix, "Ошибка инициализации визуализации");

            if (initSuccess)
            {
                SmartLogger.Log(LogLevel.Information, LogPrefix, "Визуализация инициализирована");
            }
            else
            {
                throw new InvalidOperationException($"{LogPrefix}Не удалось инициализировать визуализацию");
            }
        }

        private void InitializeComponents(SpectrumAnalyzer analyzer, SpectrumBrushes spectrumStyles)
        {
            _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            _spectrumStyles = spectrumStyles ?? throw new ArgumentNullException(nameof(spectrumStyles));

            var rendererImpl = new Renderer(_spectrumStyles, _controller, _analyzer, _renderElement, _glService);
            _renderer = rendererImpl;
            _controller.Renderer = rendererImpl;

            // Подписываемся на событие запроса рендеринга от рендерера
            if (rendererImpl is Renderer concreteRenderer)
            {
                concreteRenderer.RenderRequested += OnRenderRequested;
            }

            _needsRender = true;
        }

        private void OnRenderRequested(object? sender, EventArgs e)
        {
            _needsRender = true;
        }

        private void InitializeRenderers()
        {
            foreach (RenderStyle style in Enum.GetValues(typeof(RenderStyle)))
            {
                SmartLogger.Safe(() => {
                    SpectrumRendererFactory.CreateRenderer(style, _controller.IsOverlayActive, _controller.RenderQuality);
                }, LogPrefix, $"Ошибка инициализации рендерера {style}");
            }
        }

        private void InitializeCamera()
        {
            float sceneWidth = 1500f;
            float sceneHeight = 500f;
            float sceneDepth = 1500f;

            if (_renderer == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Не удалось инициализировать камеру: рендерер не инициализирован");
                return;
            }

            _cameraController = new CameraController(_renderer, sceneWidth, sceneHeight, sceneDepth);
            _cameraController.UpdateActiveCamera(_controller.SelectedDrawingType);
            _cameraController.AdjustCameraToSpectrum();
            _needsRender = true;

            // Используем Dispatcher вместо Task.Delay.ContinueWith для UI-потока
            _controller.Dispatcher.InvokeAsync(() =>
            {
                if (!_disposed)
                {
                    ActivateCamera();
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        public void ActivateCamera()
        {
            if (_cameraController == null || _disposed) return;

            SmartLogger.Safe(() => {
                _cameraController.UpdateActiveCamera(_controller.SelectedDrawingType);
                if (_cameraController.CurrentCamera != null)
                {
                    var cam = _cameraController.CurrentCamera;
                    var orig = cam.CameraRotationOffset;
                    cam.CameraRotationOffset = orig + new Vector2(0.5f, 0.5f);
                    _needsRender = true;

                    _controller.Dispatcher.InvokeAsync(() =>
                    {
                        if (!_disposed && cam != null)
                        {
                            cam.CameraRotationOffset = orig;
                            _needsRender = true;
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);

                    SmartLogger.Log(LogLevel.Information, LogPrefix, $"Камера активирована для {_controller.SelectedDrawingType}");
                }
            }, LogPrefix, "Ошибка активации камеры");
        }

        public void HandleRenderStyleChanged(RenderStyle style)
        {
            SmartLogger.Safe(() => {
                _cameraController?.UpdateActiveCamera(style);
                _cameraController?.AdjustCameraToSpectrum();
                _needsRender = true;
                _renderElement?.InvalidateVisual();
            }, LogPrefix, "Ошибка при изменении стиля рендеринга");
        }

        public void UpdateRenderDimensions(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Недопустимые размеры: {width}x{height}");
                return;
            }

            SmartLogger.Safe(() => {
                _renderer?.UpdateRenderDimensions(width, height);
                _cameraController?.AdjustCameraToSpectrum();
                _needsRender = true;
            }, LogPrefix, "Ошибка обновления размеров");
        }

        public void RequestRender()
        {
            if (_disposed) return;

            SmartLogger.Safe(() => {
                _cameraController?.Update();
                _renderer?.RequestRender();
                _needsRender = true;

                // Используем Dispatcher.InvokeAsync для отрисовки в UI-потоке
                _controller.Dispatcher.InvokeAsync(() =>
                {
                    if (!_disposed)
                    {
                        _renderElement?.InvalidateVisual();
                    }
                }, System.Windows.Threading.DispatcherPriority.Render);
            }, LogPrefix, "Ошибка запроса рендеринга");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Отписываемся от событий
                if (_renderer is Renderer concreteRenderer)
                {
                    concreteRenderer.RenderRequested -= OnRenderRequested;
                }

                SmartLogger.SafeDispose(_renderer as IDisposable, "Renderer");

                _renderer = null;
                _cameraController = null;
                _analyzer = null;
                _spectrumStyles = null;
                _disposed = true;
                GC.SuppressFinalize(this);

                SmartLogger.Log(LogLevel.Information, LogPrefix, "VisualizationManager утилизирован");
            }
        }
    }
}