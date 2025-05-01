#nullable enable

namespace SpectrumNet.Controllers.RenderCore.Overlay;

internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(nint hwnd, int index, int newStyle);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowCompositionAttribute(nint hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    public enum WINDOWCOMPOSITIONATTRIB { WCA_ACCENT_POLICY = 19 }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public WINDOWCOMPOSITIONATTRIB Attrib;
        public nint pvData;
        public int cbData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ACCENT_POLICY
    {
        public int nAccentState;
        public int nFlags;
        public int nColor;
        public int nAnimationId;
    }
}