


![image](https://github.com/user-attachments/assets/fb82e09f-a124-4ec8-8ed2-0cb7205f2b1a)
![image](https://github.com/user-attachments/assets/6139d754-ba9c-46cd-a0f2-89a5b4719b74)
![image](https://github.com/user-attachments/assets/4ae0c79c-d9d7-4902-8278-8c96453340ca)
![image](https://github.com/user-attachments/assets/bccb1143-f099-4346-a38b-d65d8938f77b)
![image](https://github.com/user-attachments/assets/e83c1cbd-8a4d-4b25-8ac0-7f66e8db49fb)

# SpectrumNet

SpectrumNet is a real-time audio visualization application that captures audio from the system's loopback device and displays it as a spectrum analyzer. The application is built using C# and WPF, and it leverages the NAudio library for audio capture and processing.

## Features

- **Real-time Audio Capture**: Captures audio from the system's loopback device.
- **Spectrum Analysis**: Processes the captured audio to generate a frequency spectrum.
- **Customizable Visualization**: Allows customization of the spectrum visualization, including bar width, spacing, and style.
- **Overlay Mode**: Supports an overlay mode that can be displayed on top of other applications.
- **Theme Support**: Supports both light and dark themes.
- **Error Handling**: Comprehensive error handling and logging to ensure robustness.

## Installation

To run SpectrumNet, you need to have the following prerequisites installed:

- .NET SDK (version 5.0 or later)
- Visual Studio (optional, for development)

### Steps to Run

1. Clone the repository:
   ```bash
   git clone https://github.com/diqezit/SpectrumNet.git
   ```

2. Navigate to the project directory:
   ```bash
   cd SpectrumNet
   ```

3. Build and run the application:
   ```bash
   dotnet run
   ```

## Usage

### Main Window

- **Start/Stop Capture**: Use the buttons to start and stop audio capture.
- **Visualization Settings**: Adjust the bar width, spacing, and count using the sliders.
- **Style Selection**: Choose from different visualization styles.
- **Overlay Mode**: Toggle the overlay mode to display the spectrum on top of other applications.

### Overlay Mode

- **Open Overlay**: Click the "Overlay" button to open the overlay window.
- **Close Overlay**: Close the overlay window by clicking the close button or by toggling the overlay button again.

### Theme

- **Toggle Theme**: Use the theme toggle button to switch between light and dark themes.

## Configuration

The application uses constants defined in the `MwConstants` class for various settings such as render interval, FFT size, and default styles. These constants can be modified to adjust the behavior of the application.

## Error Handling

The application includes robust error handling and logging. Errors are logged using the `Log` class, which is assumed to be a custom logging mechanism. The logs can be found in the application's log files.

## Contributing

Contributions are welcome! Please feel free to submit a pull request or open an issue for any bugs or feature requests.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [NAudio](https://github.com/naudio/NAudio) - For providing the audio capture and processing capabilities.
- [SkiaSharp](https://github.com/mono/SkiaSharp) - For the rendering engine used in the application.

---

For any questions or support, please contact me :) 



