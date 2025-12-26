# SpectrumNet — Real-Time Audio Spectrum Visualizer

[![MIT License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com)

**SpectrumNet** is a Windows audio visualizer that renders real-time spectrum effects from system audio. Built with C#, WPF, and .NET 8, it uses a SkiaSharp-based renderer pipeline with shared object pooling and centralized spectrum processing.

![Intro](https://github.com/user-attachments/assets/82777947-28cb-4d22-a9e3-166294801efb)

## Native C++ version

For maximum performance and a smaller footprint, there is a native C++ version: **[SpectrumCpp](https://github.com/diqezit/SpectrumCpp)** (Win32 + Direct2D).

## Features

### Audio
- Real-time system audio capture (WASAPI loopback).
- FFT-based spectrum analysis (windowing via **FftSharp.Windows**).
- Configurable sensitivity/range and smoothing.

### Visualization
- 25+ renderers (bars, waves, particles, grids, etc.).
- Quality presets (Low/Medium/High) with adaptive advanced effects.
- Shared object pooling for **SKPaint/SKPath** across renderers to reduce allocations.
- Integrated performance metrics and FPS limiting.

### UI / Controls
- Window mode and overlay mode (always-on-top).
- Hotkeys for common actions (Space, Ctrl+O, Ctrl+P).
- Themes/styles with persistence and instant save on theme change.
- Control panel improvements (Grid overlay instead of Popup) and StereoMode selector.

## Screenshots

| Control Panel | Animations | Themes |
| :---: | :---: | :---: |
| ![panel1](https://github.com/user-attachments/assets/bc27de70-c278-4add-9483-bcaa9583c6d7) | ![anim1](https://github.com/user-attachments/assets/2feb61b0-cadf-47be-8a50-47ecf20e1c75) | ![theme1](https://github.com/user-attachments/assets/bab7b8a7-266a-4bb1-b82f-3e229b0485e6) |

## Getting started

1. Download the latest build from the [Releases](https://github.com/diqezit/SpectrumNet/releases) page.
2. Unzip and run `SpectrumNet.exe`.
3. Click **Start Capture** to begin.
4. Hotkeys:
   - `Space`: Start/Stop visualization.
   - `O`: Toggle overlay mode.
   - `P`: Toggle control panel.

## Building from source

### Prerequisites
- Visual Studio 2022
- .NET 8.0 SDK

### Steps
```bash
git clone https://github.com/diqezit/SpectrumNet.git
