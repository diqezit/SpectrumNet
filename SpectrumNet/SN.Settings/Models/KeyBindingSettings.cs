#nullable enable

using SpectrumNet;

namespace SpectrumNet.SN.Settings.Models;

public class KeyBindingSettings : ObservableObject
{
    private Key _nextRenderer = Key.X;
    private Key _previousRenderer = Key.Z;
    private Key _qualityLow = Key.Q;
    private Key _qualityMedium = Key.W;
    private Key _qualityHigh = Key.E;
    private Key _toggleOverlay = Key.O;
    private Key _toggleControlPanel = Key.P;
    private Key _increaseBarCount = Key.Right;
    private Key _decreaseBarCount = Key.Left;
    private Key _increaseBarSpacing = Key.Up;
    private Key _decreaseBarSpacing = Key.Down;
    private Key _toggleRecording = Key.Space;
    private Key _closePopup = Key.Escape;

    [JsonConverter(typeof(StringEnumConverter))]
    public Key NextRenderer
    {
        get => _nextRenderer;
        set => SetProperty(ref _nextRenderer, value);
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public Key PreviousRenderer
    {
        get => _previousRenderer;
        set => SetProperty(ref _previousRenderer, value);
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public Key QualityLow
    {
        get => _qualityLow;
        set => SetProperty(ref _qualityLow, value);
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public Key QualityMedium
    {
        get => _qualityMedium;
        set => SetProperty(ref _qualityMedium, value);
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public Key QualityHigh
    {
        get => _qualityHigh;
        set => SetProperty(ref _qualityHigh, value);
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public Key ToggleOverlay
    {
        get => _toggleOverlay;
        set => SetProperty(ref _toggleOverlay, value);
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public Key ToggleControlPanel
    {
        get => _toggleControlPanel;
        set => SetProperty(ref _toggleControlPanel, value);
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public Key IncreaseBarCount
    {
        get => _increaseBarCount;
        set => SetProperty(ref _increaseBarCount, value);
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public Key DecreaseBarCount
    {
        get => _decreaseBarCount;
        set => SetProperty(ref _decreaseBarCount, value);
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public Key IncreaseBarSpacing
    {
        get => _increaseBarSpacing;
        set => SetProperty(ref _increaseBarSpacing, value);
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public Key DecreaseBarSpacing
    {
        get => _decreaseBarSpacing;
        set => SetProperty(ref _decreaseBarSpacing, value);
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public Key ToggleRecording
    {
        get => _toggleRecording;
        set => SetProperty(ref _toggleRecording, value);
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public Key ClosePopup
    {
        get => _closePopup;
        set => SetProperty(ref _closePopup, value);
    }
}