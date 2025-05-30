﻿#nullable enable

namespace SpectrumNet.SN.Controllers.Interfaces;

public interface IMainController :
    IUIController,
    IAudioController,
    IViewController,
    IInputController,
    INotifyPropertyChanged,
    IDisposable,
    IAsyncDisposable
{
    Dispatcher Dispatcher { get; }
    IUIController UIController { get; }
    IAudioController AudioController { get; }
    IViewController ViewController { get; }
    IInputController InputController { get; }
    IOverlayManager OverlayManager { get; }

    bool LimitFpsTo60 { get; set; }

    void DisposeResources();
}