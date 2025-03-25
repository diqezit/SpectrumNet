# SpectrumNet - Real-Time Audio Spectrum Visualizer

[![MIT License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com)

SpectrumNet transforms real-time audio input into dynamic visual spectrums using C#/WPF. Combines advanced signal processing with modern rendering for immersive audio visualization.


![Menu](https://github.com/user-attachments/assets/9e3159f3-09da-472f-a357-7beb663b69df)
![Visualisation](https://github.com/user-attachments/assets/a480ce47-28a2-4462-a717-29fef8fcf029)
![Intro](https://github.com/user-attachments/assets/82777947-28cb-4d22-a9e3-166294801efb)


## ✨ Key Features

### 🎧 Audio Processing
- Real-time system audio capture via WASAPI loopback
- Multi-window FFT analysis (Hann/Hamming/Blackman)
- Flexible spectrum scaling: Linear/Logarithmic/Mel/Bark

### 🎨 Visualization Engine
- **20+ Render Styles**:
  - Bars: Vertical, Circular, LED Meter
  - Waveforms: Gradient, Heartbeat, Waterfall
  - Particles: Fire, Raindrop, Text Effects
  - Advanced: Voronoi diagrams, Spectrum fractals
- Dynamic color palettes with gradient effects
- Quality presets (Low/Medium/High) with manual override

### ⚙️ Customization & Control
- Display modes: Windowed/Always-on-Top overlay
- Configurable hotkeys (Capture start/stop, Mode toggle)
- Real-time adjustment of spectrum sensitivity/range

## 🚀 Quick Start

1. Launch SpectrumNet.exe
2. Click **Start Capture** to begin audio analysis
3. Use hotkeys:
   - `Ctrl+O`: Toggle overlay mode
   - `Space`: Start/stop visualization
   - `Ctrl+P`: Open control panel
4. Adjust settings via interactive preview

## ⚠️ Known Issues

### Transparency Rendering Issues (Intel GPUs)
**Symptoms**:
- Partial loss of window transparency
- Visual artifacts in overlay mode
- Interface flickering

**Common Cases**:
- Intel HD Graphics (pre-2021 models)
- Hybrid graphics systems
- Drivers older than 2021

**Workaround**:
1. Force dedicated GPU usage via driver settings
2. Disable OpenGL acceleration:
   ```xaml
   <!-- Replace in XAML -->
   <skia:SKElement /> <!-- Instead of SKGLElement -->

   
![Demo Visualization](https://github.com/user-attachments/assets/52eac8ad-b97c-4395-a998-2fb35c1ca5aa)
![Overlay Mode](https://github.com/user-attachments/assets/bc2052b7-0294-4698-825d-6b2a27fc27d5)
![Visual Settings](https://github.com/user-attachments/assets/b7e5397d-7de5-479f-b2ca-412f57cefa80)
![Performance Settings](https://github.com/user-attachments/assets/260d3634-b1e9-4765-97a3-927aa06404a7)

