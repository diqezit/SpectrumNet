namespace SpectrumNet;

public class DefaultAudioDeviceService : IAudioDeviceService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    public MMDevice? GetDefaultAudioDevice() => _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    public void RegisterEndpointNotificationCallback(IMMNotificationClient client) => _enumerator.RegisterEndpointNotificationCallback(client);
    public void UnregisterEndpointNotificationCallback(IMMNotificationClient client) => _enumerator.UnregisterEndpointNotificationCallback(client);
    public void Dispose() => _enumerator.Dispose();
}

public interface IAudioDeviceService : IDisposable
{
    MMDevice? GetDefaultAudioDevice();
    void RegisterEndpointNotificationCallback(IMMNotificationClient client);
    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

public interface IAudioVisualizationController : INotifyPropertyChanged
{
    SpectrumAnalyzer Analyzer { get; set; }
    int BarCount { get; set; }
    double BarSpacing { get; set; }
    bool CanStartCapture { get; }
    Dispatcher Dispatcher { get; }
    GainParameters GainParameters { get; }
    bool IsOverlayActive { get; }
    bool IsRecording { get; set; }
    bool IsTransitioning { get; }
    RenderQuality RenderQuality { get; set; }
    void OnPropertyChanged(params string[] propertyNames);
    Renderer? Renderer { get; set; }
    SpectrumScale ScaleType { get; set; }
    RenderStyle SelectedDrawingType { get; set; }
    string SelectedStyle { get; set; }
    GLWpfControl? SpectrumCanvas { get; }
    SpectrumBrushes SpectrumStyles { get; }
    string StatusText { get; set; }
    FftWindowType WindowType { get; set; }
    bool ShowPerformanceInfo { get; set; }
}

public interface ICameraControllable
{
    Vector3 CameraPosition { get; set; }
    Vector3 CameraForward { get; set; }
    Vector3 CameraUp { get; set; }
    Vector2 CameraRotationOffset { get; set; }
    Vector3 CameraPositionOffset { get; set; }
}

public interface IFftProcessor
{
    event EventHandler<FftEventArgs>? FftCalculated;
    ValueTask AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate);
    ValueTask DisposeAsync();
    FftWindowType WindowType { get; set; }
    void ResetFftState();
}

public interface IGainParametersProvider
{
    float AmplificationFactor { get; }
    float MaxDbValue { get; }
    float MinDbValue { get; }
}

public interface IOpenGLService
{
    bool MakeCurrent();
    bool IsValid();
    bool IsContextValid();
    void Finish();
    string GetString(StringName name);
    void Viewport(int x, int y, int width, int height);
    void ClearColor(float red, float green, float blue, float alpha);
    void Clear(ClearBufferMask mask);
    int GetCurrentContextId();
    int CheckErrors();
    int CreateShader(ShaderType type, string source);
    int CreateProgram(int vertexShaderId, int fragmentShaderId);
    void UseProgram(int programId);
    void GenBuffers(int count, int[] buffers);
    void BindBuffer(BufferTarget target, int bufferId);
    void BufferData<T>(BufferTarget target, T[] data, BufferUsageHint usage) where T : struct;
    void VertexAttribPointer(int index, int size, VertexAttribPointerType type, bool normalized, int stride, int offset);
    void EnableVertexAttribArray(int index);
    void DisableVertexAttribArray(int index);
    void DrawArrays(PrimitiveType mode, int first, int count);
    void Uniform1f(int location, float value);
    void Uniform1i(int location, int value);
    void Uniform3f(int location, Vector3 value);
    void UniformMatrix4fv(int location, bool transpose, Matrix4 value);
    int GetUniformLocation(int programId, string name);
    int GetAttribLocation(int programId, string name);
    void DeleteProgram(int programId);
    void DeleteShader(int shaderId);
    void DeleteBuffers(int count, int[] buffers);
}

public interface IRenderer
{
    IAudioVisualizationController Controller { get; }
    string CurrentStyleName { get; }
    RenderQuality CurrentQuality { get; }
    void RequestRender();
    void UpdateRenderDimensions(int width, int height);
    bool CalculateRenderParameters(out float barWidth, out float barSpacing, out int barCount);
    event EventHandler<PerformanceMetrics>? PerformanceUpdate;
    event EventHandler? RenderRequested;
}

public interface ISceneRenderer
{
    SceneGeometry? SceneGeometry { get; }
}

public interface ISpectralDataProvider
{
    event EventHandler<SpectralDataEventArgs>? SpectralDataReady;
    SpectralData? GetCurrentSpectrum();
    Task AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken cancellationToken = default);
}

public interface ISpectrumAnalyzer
{
    SpectralData? GetCurrentSpectrum();
    void UpdateSettings(FftWindowType windowType, SpectrumScale scaleType);
}

public interface ISpectrumConverter
{
    float[] ConvertToSpectrum(Complex[] fftResult, int sampleRate, SpectrumScale scale);
}

public interface ISpectrumRenderer : IDisposable
{
    void Initialize();
    void Render(float[]? spectrum, Viewport viewport, float barWidth, float barSpacing, int barCount, ShaderProgram? shader, Action<Viewport> drawPerformanceInfo);
    void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium);
    RenderQuality Quality { get; set; }
}

public class OpenGLService : IOpenGLService
{
    private int _contextId = 0;
    private bool _isInitialized = false;
    public OpenGLService()
    {
        SmartLogger.Safe(() => {
            _isInitialized = true;
            _contextId = GetCurrentContextIdInternal();
            SmartLogger.Log(LogLevel.Debug, "OpenGLService", $"OpenGL service initialized, context ID: {_contextId}");
        }, "OpenGLService", "Error initializing OpenGL service");
    }
    public bool MakeCurrent() => IsContextValid();
    public bool IsValid() => _isInitialized;
    public bool IsContextValid() => GetCurrentContextIdInternal() > 0 && GetCurrentContextIdInternal() == _contextId;
    public void Finish() => GL.Finish();
    public string GetString(StringName name) => GL.GetString(name);
    public void Viewport(int x, int y, int width, int height) => GL.Viewport(x, y, width, height);
    public void ClearColor(float red, float green, float blue, float alpha) => GL.ClearColor(red, green, blue, alpha);
    public void Clear(ClearBufferMask mask) => GL.Clear(mask);
    public int GetCurrentContextId() => GetCurrentContextIdInternal();
    private int GetCurrentContextIdInternal() => 1;
    public int CheckErrors()
    {
        OpenTK.Graphics.OpenGL4.ErrorCode error = GL.GetError();
        if (error != OpenTK.Graphics.OpenGL4.ErrorCode.NoError)
        {
            SmartLogger.Log(LogLevel.Warning, "OpenGLService", $"OpenGL error: {error}");
            return (int)error;
        }
        return 0;
    }
    public int CreateShader(ShaderType type, string source)
    {
        int shaderId = GL.CreateShader(type);
        GL.ShaderSource(shaderId, source);
        GL.CompileShader(shaderId);
        GL.GetShader(shaderId, ShaderParameter.CompileStatus, out int status);
        if (status != 1)
        {
            string log = GL.GetShaderInfoLog(shaderId);
            SmartLogger.Log(LogLevel.Error, "OpenGLService", $"Shader compilation error: {log}");
            GL.DeleteShader(shaderId);
            return 0;
        }
        return shaderId;
    }
    public int CreateProgram(int vertexShaderId, int fragmentShaderId)
    {
        int programId = GL.CreateProgram();
        GL.AttachShader(programId, vertexShaderId);
        GL.AttachShader(programId, fragmentShaderId);
        GL.LinkProgram(programId);
        GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out int status);
        if (status != 1)
        {
            string log = GL.GetProgramInfoLog(programId);
            SmartLogger.Log(LogLevel.Error, "OpenGLService", $"Program linking error: {log}");
            GL.DeleteProgram(programId);
            return 0;
        }
        return programId;
    }
    public void UseProgram(int programId) => GL.UseProgram(programId);
    public void GenBuffers(int count, int[] buffers) => GL.GenBuffers(count, buffers);
    public void BindBuffer(BufferTarget target, int bufferId) => GL.BindBuffer(target, bufferId);
    public void BufferData<T>(BufferTarget target, T[] data, BufferUsageHint usage) where T : struct => GL.BufferData(target, data.Length * Marshal.SizeOf<T>(), data, usage);
    public void VertexAttribPointer(int index, int size, VertexAttribPointerType type, bool normalized, int stride, int offset) => GL.VertexAttribPointer(index, size, type, normalized, stride, offset);
    public void EnableVertexAttribArray(int index) => GL.EnableVertexAttribArray(index);
    public void DisableVertexAttribArray(int index) => GL.DisableVertexAttribArray(index);
    public void DrawArrays(PrimitiveType mode, int first, int count) => GL.DrawArrays(mode, first, count);
    public void Uniform1f(int location, float value) => GL.Uniform1(location, value);
    public void Uniform1i(int location, int value) => GL.Uniform1(location, value);
    public void Uniform3f(int location, Vector3 value) => GL.Uniform3(location, value.X, value.Y, value.Z);
    public void UniformMatrix4fv(int location, bool transpose, Matrix4 value) => GL.UniformMatrix4(location, transpose, ref value);
    public int GetUniformLocation(int programId, string name) => GL.GetUniformLocation(programId, name);
    public int GetAttribLocation(int programId, string name) => GL.GetAttribLocation(programId, name);
    public void DeleteProgram(int programId) => GL.DeleteProgram(programId);
    public void DeleteShader(int shaderId) => GL.DeleteShader(shaderId);
    public void DeleteBuffers(int count, int[] buffers) => GL.DeleteBuffers(count, buffers);
}

public readonly struct Viewport
{
    public readonly int X, Y, Width, Height;
    public Viewport(int x, int y, int width, int height) => (X, Y, Width, Height) = (x, y, width, height);
}