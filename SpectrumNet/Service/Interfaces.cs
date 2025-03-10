#nullable enable

namespace SpectrumNet;

/// <summary>
/// Interface defining the contract for an audio spectrum visualization controller.
/// This interface provides properties and methods for managing spectrum rendering,
/// analysis settings, and audio capture state.
/// </summary>
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

    GLWpfControl SpectrumCanvas { get; }

    SpectrumBrushes SpectrumStyles { get; }

    string StatusText { get; set; }

    FftWindowType WindowType { get; set; }

    bool ShowPerformanceInfo { get; set; }
}

public interface IAudioDeviceService : IDisposable
{
    MMDevice? GetDefaultAudioDevice();
    void RegisterEndpointNotificationCallback(IMMNotificationClient client);
    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

/// <summary>
/// Интерфейс для объектов с управляемой камерой
/// </summary>
public interface ICameraControllable
{
    Vector3 CameraPosition { get; set; }
    Vector3 CameraForward { get; set; }
    Vector3 CameraUp { get; set; }
    Vector2 CameraRotationOffset { get; set; }
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
    void Finish();
    string GetString(StringName name);
    void Viewport(int x, int y, int width, int height);
    void ClearColor(float red, float green, float blue, float alpha);
    void Clear(ClearBufferMask mask);
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

    void Render(float[]? spectrum, Viewport viewport, float barWidth,
                float barSpacing, int barCount, ShaderProgram? shader,
                Action<Viewport> drawPerformanceInfo);

    void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium);

    RenderQuality Quality { get; set; }
}

public class DefaultAudioDeviceService : IAudioDeviceService
{
    private readonly MMDeviceEnumerator _enumerator = new MMDeviceEnumerator();

    public MMDevice? GetDefaultAudioDevice() =>
        _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

    public void RegisterEndpointNotificationCallback(IMMNotificationClient client) =>
        _enumerator.RegisterEndpointNotificationCallback(client);

    public void UnregisterEndpointNotificationCallback(IMMNotificationClient client) =>
        _enumerator.UnregisterEndpointNotificationCallback(client);

    public void Dispose() => _enumerator.Dispose();
}

public class OpenGLService : IOpenGLService
{
    public void Finish() => GL.Finish();

    public string GetString(StringName name) => GL.GetString(name);

    public void Viewport(int x, int y, int width, int height) => GL.Viewport(x, y, width, height);

    public void ClearColor(float red, float green, float blue, float alpha) =>
        GL.ClearColor(red, green, blue, alpha);

    public void Clear(ClearBufferMask mask) => GL.Clear(mask);
}

public readonly struct Viewport
{
    public readonly int X, Y, Width, Height;

    public Viewport(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}