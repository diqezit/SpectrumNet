

# SpectrumNet - Real-Time Audio Spectrum Visualizer

SpectrumNet is a powerful audio visualization application that transforms real-time audio input into captivating visual spectrums. Built with C# and WPF, it leverages advanced signal processing and modern rendering techniques to provide an immersive experience.

## Key Features

- 🎤 **Real-Time Audio Capture**: Utilizes WASAPI loopback to capture system audio.
- 📊 **FFT Processing**: Supports multiple window functions (Hann, Hamming, Blackman, etc.) for accurate frequency analysis.
- 🎨 **Dynamic Visual Styles**: 20+ rendering styles including bars, waveforms, particles, and Voronoi diagrams.
- ⚙️ **Customizable Visualization**:
  - Adjustable spectrum scale (Linear/Logarithmic/Mel/Bark)
  - Custom color palettes and gradient effects
  - Quality presets (Low/Medium/High)
- 🖥 **Flexible Display Modes**: Switch between windowed mode and always-on-top overlay.
- ⌨ **Hotkey Support**: Quick control over recording, quality settings, and overlay.

## System Requirements

- Windows 10/11 required
- .NET 6.0 Runtime
- DirectX 9 compatible GPU recommended

## ⚠️ Known Issues

### Transparency Issues on Integrated GPUs (Intel HD) 
**When using OpenGL hardware acceleration**, you might encounter:
- Partial loss of window transparency
- Rendering artifacts in overlay mode
- Interface flickering

**Most commonly occurs on:**  
✔️ Older Intel HD Graphics  
✔️ Outdated drivers (< 2021)  
✔️ Systems with hybrid graphics

**Workaround:**  
Force dedicated GPU selection in your graphics driver settings.

- https://github.com/mono/SkiaSharp/issues/2837


## Quick Start

1. **Launch** the application
2. Click **Start Capture** to begin audio analysis
3. Use **Control+O** to toggle overlay mode
4. Press **Space** to start/stop visualization
5. Adjust settings via **Control Panel** (Control+P)

## Supported Render Styles

| Bars           | Waveforms      | Particle Effects |
|----------------|----------------|-------------------|
| Vertical Bars  | Gradient Wave  | Fire Visualization|
| Circular Bars  | Heartbeat      | Raindrop Effects  |
| LED Meter      | Waterfall      | Text Particles    |


![Intro](https://github.com/user-attachments/assets/52eac8ad-b97c-4395-a998-2fb35c1ca5aa)

![Working](https://github.com/user-attachments/assets/bc2052b7-0294-4698-825d-6b2a27fc27d5)

![Settings](https://github.com/user-attachments/assets/b7e5397d-7de5-479f-b2ca-412f57cefa80)

![Settings](https://github.com/user-attachments/assets/260d3634-b1e9-4765-97a3-927aa06404a7)

## License

MIT License - Free for personal and educational use. See [LICENSE](LICENSE) for details.
