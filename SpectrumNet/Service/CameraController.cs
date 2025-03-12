#nullable enable

namespace SpectrumNet
{
    public class CameraController
    {
        private const string LogPrefix = "CameraController";

        private static class CameraSettings
        {
            public static class Sensitivity { public const float Mouse = 0.25f, Movement = 250f, Zoom = 0.1f; }
            public static class Limits
            {
                public const float MaxPitch = 89f, MinPitch = -89f;
                public const float MinDistance = 50f, MaxDistance = 2000f;
            }
            public static class Defaults
            {
                public const float DefaultYaw = -90f, DefaultPitch = -30f, DefaultZ = 600f;
                public const float DistanceFactor = 1.2f, HeightFactor = 0.33f, FieldOfView = 80f;
            }
        }

        private readonly float _sceneWidth, _sceneHeight, _sceneDepth;
        private readonly HashSet<Key> _pressedKeys = new();
        private readonly IRenderer _renderer;
        private readonly Stopwatch _stopwatch = new();
        private readonly IAudioVisualizationController _controller;
        private readonly Vector3 _upVector = new(0, 1, 0);

        private ICameraControllable? _currentCamera;
        private Point _lastMousePosition;
        private Vector3 _cameraPosition = new(0, 0, CameraSettings.Defaults.DefaultZ);
        private Vector3 _forward, _right, _sceneCenter;
        private float _yaw = CameraSettings.Defaults.DefaultYaw, _pitch = CameraSettings.Defaults.DefaultPitch;

        public ICameraControllable? CurrentCamera => _currentCamera;

        public CameraController(IRenderer renderer, float sceneWidth, float sceneHeight, float sceneDepth)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _controller = renderer.Controller;
            (_sceneWidth, _sceneHeight, _sceneDepth) = (sceneWidth, sceneHeight, sceneDepth);
            SmartLogger.Safe(InitializeCamera, LogPrefix, "Camera initialization error");
        }

        private void InitializeCamera()
        {
            UpdateSceneCenter();
            _cameraPosition = new Vector3(
                _sceneCenter.X,
                _sceneCenter.Y * CameraSettings.Defaults.HeightFactor,
                CameraSettings.Defaults.DefaultZ
            );

            UpdateActiveCamera(_controller.SelectedDrawingType);
            UpdateCameraOrientation();
            SyncCameraState();

            SmartLogger.Log(LogLevel.Information, LogPrefix,
                $"Initialized for {_controller.SelectedDrawingType}, camera active: {_currentCamera != null}");
        }

        public void UpdateActiveCamera(RenderStyle style)
        {
            var camRenderer = SpectrumRendererFactory.GetCachedRenderer(style);
            if (camRenderer is not ICameraControllable cam)
            {
                _currentCamera = null;
                SmartLogger.Log(LogLevel.Information, LogPrefix, $"Renderer {style} does not support camera control");
                return;
            }

            var oldCamera = _currentCamera;
            _currentCamera = cam;

            if (oldCamera != null)
            {
                _cameraPosition = oldCamera.CameraPosition;
                _forward = oldCamera.CameraForward;
            }

            SyncCameraState();
        }

        public void HandleKeyDown(KeyEventArgs e)
        {
            if (_currentCamera == null || !IsMovementKey(e.Key)) return;
            _pressedKeys.Add(e.Key);
            e.Handled = true;
        }

        public void HandleKeyUp(KeyEventArgs e)
        {
            if (!IsMovementKey(e.Key)) return;
            _pressedKeys.Remove(e.Key);
            e.Handled = true;
        }

        private static bool IsMovementKey(Key key) => key is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E;

        public void HandleMouseMove(MouseEventArgs e)
        {
            if (_currentCamera == null) return;

            Point currentPos = e.GetPosition(null);

            if (e.LeftButton == MouseButtonState.Pressed)
                HandleRotation(currentPos);
            else if (e.RightButton == MouseButtonState.Pressed)
                HandlePanning(currentPos);

            _lastMousePosition = currentPos;
        }

        private void HandleRotation(Point currentPos)
        {
            float deltaX = (float)(currentPos.X - _lastMousePosition.X) * CameraSettings.Sensitivity.Mouse;
            float deltaY = (float)(currentPos.Y - _lastMousePosition.Y) * CameraSettings.Sensitivity.Mouse;

            _yaw += deltaX;
            _pitch = Math.Clamp(_pitch - deltaY, CameraSettings.Limits.MinPitch, CameraSettings.Limits.MaxPitch);

            UpdateCameraOrientation();
            SyncCameraState();
        }

        private void HandlePanning(Point currentPos)
        {
            const float panningFactor = 0.5f;
            float sensitivity = CameraSettings.Sensitivity.Mouse * panningFactor;
            float deltaX = (float)(currentPos.X - _lastMousePosition.X) * sensitivity;
            float deltaY = (float)(currentPos.Y - _lastMousePosition.Y) * sensitivity;

            Vector3 newPosition = _cameraPosition - _right * deltaX + _upVector * deltaY;
            ApplyMovementLimits(ref newPosition);
            _cameraPosition = newPosition;
            SyncCameraState();
        }

        private void UpdateCameraOrientation()
        {
            float yawRad = MathHelper.DegreesToRadians(_yaw);
            float pitchRad = MathHelper.DegreesToRadians(_pitch);

            _forward = Vector3.Normalize(new Vector3(
                (float)(Math.Cos(yawRad) * Math.Cos(pitchRad)),
                (float)Math.Sin(pitchRad),
                (float)(Math.Sin(yawRad) * Math.Cos(pitchRad))
            ));

            _right = Vector3.Normalize(Vector3.Cross(_forward, _upVector));

            if (_currentCamera != null)
            {
                _currentCamera.CameraForward = _forward;
                _currentCamera.CameraUp = _upVector;
            }
        }

        public void Update() =>
            SmartLogger.Safe(() => {
                if (!_stopwatch.IsRunning)
                {
                    _stopwatch.Start();
                    return;
                }

                float deltaTime = (float)_stopwatch.Elapsed.TotalSeconds;
                _stopwatch.Restart();

                UpdateCameraPosition(deltaTime);
                UpdateSceneGeometry();
            }, LogPrefix, "Camera update error");

        private void UpdateCameraPosition(float deltaTime)
        {
            if (_currentCamera == null) return;

            Vector3 moveDirection = CalculateMoveDirection();
            if (moveDirection == Vector3.Zero) return;

            moveDirection = Vector3.Normalize(moveDirection);
            Vector3 newPosition = _cameraPosition + moveDirection * CameraSettings.Sensitivity.Movement * deltaTime;

            ApplyMovementLimits(ref newPosition);
            _cameraPosition = newPosition;
            SyncCameraState();
        }

        private void UpdateSceneGeometry()
        {
            if (_currentCamera is not ISceneRenderer sceneRenderer || sceneRenderer.SceneGeometry == null) return;

            float aspectRatio = (float)_controller!.SpectrumCanvas!.ActualWidth /
                               (float)_controller.SpectrumCanvas.ActualHeight;

            sceneRenderer.SceneGeometry.UpdateSceneSize(
                _cameraPosition,
                CameraSettings.Defaults.FieldOfView,
                aspectRatio
            );
        }

        private Vector3 CalculateMoveDirection()
        {
            Vector3 direction = Vector3.Zero;

            if (_pressedKeys.Contains(Key.W)) direction += _forward;
            if (_pressedKeys.Contains(Key.S)) direction -= _forward;
            if (_pressedKeys.Contains(Key.A)) direction -= _right;
            if (_pressedKeys.Contains(Key.D)) direction += _right;
            if (_pressedKeys.Contains(Key.Q)) direction -= _upVector;
            if (_pressedKeys.Contains(Key.E)) direction += _upVector;

            return direction;
        }

        private void ApplyMovementLimits(ref Vector3 pos)
        {
            float sceneMinX = -_sceneWidth / 2, sceneMaxX = _sceneWidth / 2;
            float sceneMinY = 0f, sceneMaxY = _sceneHeight;
            float sceneMinZ = -_sceneDepth / 2, sceneMaxZ = _sceneDepth / 2;

            pos.X = Math.Clamp(pos.X, sceneMinX, sceneMaxX);
            pos.Y = Math.Clamp(pos.Y, sceneMinY, sceneMaxY);
            pos.Z = Math.Clamp(pos.Z, sceneMinZ, sceneMaxZ);
        }

        public void HandleMouseWheel(MouseWheelEventArgs e)
        {
            if (_currentCamera == null) return;

            float zoomFactor = 1.0f + (e.Delta > 0
                ? -CameraSettings.Sensitivity.Zoom
                : CameraSettings.Sensitivity.Zoom);

            Vector3 directionToCamera = _cameraPosition - _sceneCenter;
            float currentDistance = directionToCamera.Length;

            if (currentDistance <= 0) return;

            float newDistance = Math.Clamp(
                currentDistance * zoomFactor,
                CameraSettings.Limits.MinDistance,
                CameraSettings.Limits.MaxDistance
            );

            Vector3 newPosition = _sceneCenter + Vector3.Normalize(directionToCamera) * newDistance;
            ApplyMovementLimits(ref newPosition);
            _cameraPosition = newPosition;
            SyncCameraState();
        }

        public void ResetCamera()
        {
            if (_currentCamera == null) return;

            UpdateSceneCenter();
            _cameraPosition = new Vector3(
                _sceneCenter.X,
                _sceneCenter.Y * CameraSettings.Defaults.HeightFactor,
                CameraSettings.Defaults.DefaultZ
            );

            _yaw = CameraSettings.Defaults.DefaultYaw;
            _pitch = CameraSettings.Defaults.DefaultPitch;

            UpdateCameraOrientation();
            _pressedKeys.Clear();
            SyncCameraState();
        }

        private void UpdateSceneCenter() => _sceneCenter = new Vector3(0, _sceneHeight / 2, 0);

        public void AdjustCameraToSpectrum() =>
            SmartLogger.Safe(() => {
                if (_currentCamera == null) return;

                UpdateSceneCenter();

                float viewportWidth = (float)_controller!.SpectrumCanvas!.ActualWidth;
                float fovRadians = MathHelper.DegreesToRadians(CameraSettings.Defaults.FieldOfView);
                float distanceForWidth = viewportWidth / (2 * MathF.Tan(fovRadians / 2));

                _cameraPosition = new Vector3(
                    _sceneCenter.X,
                    _sceneCenter.Y,
                    distanceForWidth * CameraSettings.Defaults.DistanceFactor
                );

                _yaw = CameraSettings.Defaults.DefaultYaw;
                _pitch = CameraSettings.Defaults.DefaultPitch;

                UpdateCameraOrientation();
                SyncCameraState();
            }, LogPrefix, "Error adjusting camera to spectrum");

        public void ActivateCamera() =>
            SmartLogger.Safe(() => {
                if (_currentCamera == null)
                {
                    UpdateActiveCamera(_controller.SelectedDrawingType);
                    if (_currentCamera == null) return;
                }

                AnimateCameraPosition();
            }, LogPrefix, "Camera activation error");

        private void AnimateCameraPosition()
        {
            Vector3 originalPosition = _currentCamera!.CameraPosition;

            _currentCamera.CameraPosition += new Vector3(0, 0, 50);
            _renderer.RequestRender();

            Task.Delay(100).ContinueWith(_ => {
                if (_currentCamera != null)
                {
                    _currentCamera.CameraPosition = originalPosition;
                    _renderer.RequestRender();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void SyncCameraState() =>
            SmartLogger.Safe(() => {
                if (_currentCamera == null) return;

                _currentCamera.CameraPosition = _cameraPosition;
                _currentCamera.CameraForward = _forward;
                _currentCamera.CameraUp = _upVector;

                _renderer.RequestRender();
            }, LogPrefix, "Camera state synchronization error");
    }

    public sealed class VisualizationManager : IDisposable
    {
        private const string LogPrefix = "VisualizationManager";
        private const float SceneWidth = 1500f, SceneHeight = 500f, SceneDepth = 1500f;

        private readonly IAudioVisualizationController _controller;
        private readonly GLWpfControl? _renderElement;
        private readonly IOpenGLService _glService;
        private IRenderer? _renderer;
        private CameraController? _cameraController;
        private SpectrumAnalyzer? _analyzer;
        private SpectrumBrushes? _spectrumStyles;
        private bool _disposed, _isInitialized, _needsRender;

        public IOpenGLService GLService => _glService;
        public IRenderer? Renderer => _renderer;
        public CameraController? CameraController => _cameraController;
        public bool NeedsRender { get => _needsRender; set => _needsRender = value; }

        public VisualizationManager(IAudioVisualizationController controller,
                                   GLWpfControl? renderElement,
                                   IOpenGLService glService)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _renderElement = renderElement ?? throw new ArgumentNullException(nameof(renderElement));
            _glService = glService ?? throw new ArgumentNullException(nameof(glService));
            InitializeOpenGL();
        }

        private void InitializeOpenGL() =>
            SmartLogger.Safe(() => {
                if (!_glService.IsValid())
                    throw new InvalidOperationException("OpenGL service is invalid");

                if (!_glService.MakeCurrent() || !_glService.IsContextValid())
                    throw new InvalidOperationException("OpenGL context is invalid");

                var openGlVersion = _glService.GetString(StringName.Version);
                var vendor = _glService.GetString(StringName.Vendor);
                var renderer = _glService.GetString(StringName.Renderer);

                SmartLogger.Log(LogLevel.Information, LogPrefix,
                    $"OpenGL: {openGlVersion}, Vendor: {vendor}, Renderer: {renderer}");
            }, LogPrefix, "Error during OpenGL initialization");

        public void Initialize(SpectrumAnalyzer analyzer, SpectrumBrushes spectrumStyles)
        {
            if (_isInitialized) return;

            SmartLogger.Safe(() => {
                ValidateOpenGLContext("during initialization");
                InitializeComponents(analyzer, spectrumStyles);
                InitializeRenderers();
                InitializeCamera();
                _isInitialized = true;
                _needsRender = true;
            }, LogPrefix, "Visualization initialization error");

            if (!_isInitialized)
                throw new InvalidOperationException($"{LogPrefix} Failed to initialize visualization");

            SmartLogger.Log(LogLevel.Information, LogPrefix, "Visualization initialized");
        }

        private void ValidateOpenGLContext(string context)
        {
            if (!_glService.MakeCurrent() || !_glService.IsContextValid())
                throw new InvalidOperationException($"OpenGL context is invalid {context}");
        }

        private void InitializeComponents(SpectrumAnalyzer analyzer, SpectrumBrushes spectrumStyles)
        {
            _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            _spectrumStyles = spectrumStyles ?? throw new ArgumentNullException(nameof(spectrumStyles));

            SmartLogger.Safe(() => {
                ValidateOpenGLContext("during component initialization");
                var rendererImpl = new Renderer(_spectrumStyles, _controller, _analyzer, _renderElement!, _glService);
                _renderer = rendererImpl;
                _controller.Renderer = rendererImpl;
                rendererImpl.RenderRequested += OnRenderRequested;
                _needsRender = true;
            }, LogPrefix, "Error initializing visualization components");
        }

        private void OnRenderRequested(object? sender, EventArgs e) => _needsRender = true;

        private void InitializeRenderers() =>
            SmartLogger.Safe(() => {
                ValidateOpenGLContext("during renderer initialization");
                Enum.GetValues(typeof(RenderStyle))
                    .Cast<RenderStyle>()
                    .ForEach(style => SpectrumRendererFactory.CreateRenderer(
                        style, _controller.IsOverlayActive, _controller.RenderQuality));
            }, LogPrefix, "Error initializing renderers");

        private void InitializeCamera()
        {
            if (_renderer == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Renderer not initialized during camera setup");
                return;
            }

            SmartLogger.Safe(() => {
                _cameraController = new CameraController(_renderer, SceneWidth, SceneHeight, SceneDepth);
                _cameraController.UpdateActiveCamera(_controller.SelectedDrawingType);
                _cameraController.AdjustCameraToSpectrum();
                _needsRender = true;
                _controller.Dispatcher.InvokeAsync(ActivateCamera,
                    System.Windows.Threading.DispatcherPriority.Background);
            }, LogPrefix, "Camera initialization error");
        }

        public void ActivateCamera()
        {
            if (_cameraController == null || _disposed) return;

            SmartLogger.Safe(() => {
                ValidateOpenGLContext("during camera activation");
                _cameraController.UpdateActiveCamera(_controller.SelectedDrawingType);

                if (_cameraController.CurrentCamera is ICameraControllable cam)
                {
                    ActivateCameraAnimation(cam);
                    SmartLogger.Log(LogLevel.Information, LogPrefix,
                        $"Camera activated for {_controller.SelectedDrawingType}");
                }
            }, LogPrefix, "Camera activation error");
        }

        private void ActivateCameraAnimation(ICameraControllable camera)
        {
            var originalOffset = camera.CameraRotationOffset;
            camera.CameraRotationOffset += new Vector2(0.5f, 0.5f);
            _needsRender = true;

            _controller.Dispatcher.InvokeAsync(() => {
                if (_disposed || camera == null) return;

                if (_glService.MakeCurrent() && _glService.IsContextValid())
                {
                    camera.CameraRotationOffset = originalOffset;
                    _needsRender = true;
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        public void HandleRenderStyleChanged(RenderStyle style) =>
            SmartLogger.Safe(() => {
                if (!_glService.MakeCurrent() || !_glService.IsContextValid()) return;

                _cameraController?.UpdateActiveCamera(style);
                _cameraController?.AdjustCameraToSpectrum();
                _needsRender = true;
                _renderElement?.InvalidateVisual();
            }, LogPrefix, "Error during render style change");

        public void UpdateRenderDimensions(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Invalid dimensions: {width}x{height}");
                return;
            }

            SmartLogger.Safe(() => {
                if (!_glService.MakeCurrent() || !_glService.IsContextValid()) return;

                _renderer?.UpdateRenderDimensions(width, height);
                _cameraController?.AdjustCameraToSpectrum();
                _needsRender = true;
            }, LogPrefix, "Error updating dimensions");
        }

        public void RequestRender() =>
            SmartLogger.Safe(() => {
                if (_disposed) return;
                if (!_glService.MakeCurrent() || !_glService.IsContextValid()) return;

                _cameraController?.Update();
                _renderer?.RequestRender();
                _needsRender = true;

                _controller.Dispatcher.InvokeAsync(
                    () => { if (!_disposed) _renderElement?.InvalidateVisual(); },
                    System.Windows.Threading.DispatcherPriority.Render);
            }, LogPrefix, "Render request error");

        public void Dispose()
        {
            if (_disposed) return;

            SmartLogger.Safe(() => {
                if (_glService.MakeCurrent())
                {
                    if (_renderer is Renderer concreteRenderer)
                        concreteRenderer.RenderRequested -= OnRenderRequested;

                    SmartLogger.SafeDispose(_renderer as IDisposable, "Renderer");
                }

                _renderer = null;
                _cameraController = null;
                _analyzer = null;
                _spectrumStyles = null;
                _disposed = true;

                SmartLogger.Log(LogLevel.Information, LogPrefix, "VisualizationManager disposed");
            }, LogPrefix, "Error disposing VisualizationManager");

            GC.SuppressFinalize(this);
        }
    }
}