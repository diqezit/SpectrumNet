
# SpectrumNet - Real-Time Audio Spectrum Visualizer

[![MIT License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com)

**SpectrumNet** is a high-performance audio visualizer for Windows that transforms any system audio into a captivating, real-time spectral display. Built with C#, WPF, and .NET 8, it combines advanced signal processing with a powerful rendering engine to create immersive visual experiences.

![Intro](https://github.com/user-attachments/assets/82777947-28cb-4d22-a9e3-166294801efb)

> ### 🚀 Now Available in C++!
>
> For users seeking maximum performance and a lighter footprint, a native C++ version of this project is now available! **[SpectrumCpp](https://github.com/diqezit/SpectrumCpp)** is built from the ground up with Win32 and Direct2D for raw speed and efficiency.
>
> **Check out SpectrumCpp if you:**
> - Need the absolute best performance, especially in overlay mode.
> - Prefer a minimal, dependency-free executable.
> - Are interested in native Windows development with C++.

## ✨ Key Features

### 🎧 Advanced Audio Processing
- **Real-time System Audio Capture:** Captures desktop audio directly using WASAPI loopback—no extra configuration needed.
- **Multiple FFT Window Functions:** Choose between Hann, Hamming, and Blackman windows to fine-tune the spectral analysis.
- **Flexible Frequency Scaling:** Visualize sound across Linear, Logarithmic, Mel, and Bark frequency scales.

### 🎨 Powerful Visualization Engine
- **20+ Unique Render Styles:** A vast collection of visualizers, including:
  - **Bars:** Vertical, Circular, LED Meter
  - **Waveforms:** Gradient Line, Heartbeat, Waterfall
  - **Particles:** Fire, Raindrop, Text Effects
  - **Advanced:** Voronoi Diagrams, Spectrum Fractals
- **Dynamic Color Palettes:** Customize your visualizer with beautiful gradient effects and pre-built themes.
- **Adjustable Quality Presets:** Instantly switch between Low, Medium, and High quality settings, with options for manual override to balance performance and visuals.

### ⚙️ Customization and Control
- **Flexible Display Modes:** Use in a standard window or as an **Always-on-Top** overlay for seamless integration with your desktop.
- **Configurable Hotkeys:** Control the application without interrupting your workflow (`Start/Stop Capture`, `Toggle Overlay`, `Open Panel`).
- **Real-time Adjustments:** Fine-tune spectrum sensitivity, range, and other parameters on the fly with an interactive control panel.

## 📸 Visual Showcase

| Main View | Overlay Mode |
| :---: | :---: |
| ![Visualisation](https://github.com/user-attachments/assets/a480ce47-28a2-4462-a717-29fef8fcf029) | ![Overlay Mode](https://github.com/user-attachments/assets/bc2052b7-0294-4698-825d-6b2a27fc27d5) |
| **Menu & Performance Settings** | **Visual Settings Panel** |
| ![Menu](https://github.com/user-attachments/assets/9e3159f3-09da-472f-a357-7beb663b69df) | ![Visual Settings](https://github.com/user-attachments/assets/b7e5397d-7de5-479f-b2ca-412f57cefa80) |

![Demo Visualization](https://github.com/user-attachments/assets/52eac8ad-b97c-4395-a998-2fb35c1ca5aa)

## 🚀 Getting Started (For Users)

1.  **Download the latest version** from the [**Releases**](https://github.com/diqezit/SpectrumNet/releases) page.
2.  Unzip the archive and run `SpectrumNet.exe`.
3.  Click **"Start Capture"** to begin visualizing your system's audio.
4.  Use the controls and hotkeys to customize your experience:
    - `Space`: Start or stop the visualization.
    - `Ctrl + O`: Toggle the always-on-top overlay mode.
    - `Ctrl + P`: Show or hide the main control panel.

## 🛠️ Building from Source (For Developers)

### Prerequisites
- [Visual Studio 2022](https://visualstudio.microsoft.com/)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Steps
1.  **Clone the repository:**
    ```bash
    git clone https://github.com/diqezit/SpectrumNet.git
    ```
2.  **Open the solution file** (`.sln`) in Visual Studio.
3.  **Restore NuGet packages** (this should happen automatically).
4.  **Build and run** the project in `Debug` or `Release` mode.

## 💻 Technology Stack
- **Framework:** .NET 8.0
- **UI:** WPF (Windows Presentation Foundation)
- **Rendering:** SkiaSharp for high-performance 2D graphics.
- **Audio Capture:** WASAPI Loopback (via a .NET wrapper like CSCore or NAudio).

## ⚠️ Known Issues

### Transparency Rendering Issues on Intel GPUs
Some users with older or integrated Intel GPUs may experience rendering artifacts in overlay mode.

**Symptoms**:
- Partial or complete loss of window transparency.
- Flickering or visual glitches when interacting with the interface.
- Poor performance in overlay mode.

**Affected Hardware**:
- Primarily observed on Intel HD/UHD Graphics (pre-2021 models).
- Laptops with hybrid graphics systems (NVIDIA Optimus).
- Systems with outdated graphics drivers.

**Workaround**:
1.  **Force Dedicated GPU**: In your NVIDIA or AMD control panel, set `SpectrumNet.exe` to always use the high-performance dedicated GPU.
2.  **(For Developers) Force Software Rendering**: As a last resort, you can disable hardware acceleration by replacing `SKGLElement` with `SKElement` in the relevant XAML files. This will use CPU-based rendering, which is slower but more compatible.
    ```xaml
    <!-- In the visualizer's XAML, replace the hardware-accelerated element: -->
    <skia:SKElement /> 
    
    <!-- Instead of: -->
    <!-- <skia:SKGLElement /> -->
    ```

## 🤝 Contributing

Contributions are welcome! If you have an idea for a new feature, a bug fix, or a new visualizer style, feel free to fork the repository, make your changes, and submit a pull request.

1.  Fork the Project
2.  Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3.  Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4.  Push to the Branch (`git push origin feature/AmazingFeature`)
5.  Open a Pull Request

## 📄 License

This project is licensed under the **MIT License**. See the [LICENSE](LICENSE) file for details.
