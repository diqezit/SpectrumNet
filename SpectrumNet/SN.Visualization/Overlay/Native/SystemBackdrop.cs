#nullable enable

namespace SpectrumNet.SN.Visualization.Overlay.Native;

internal sealed class SystemBackdrop
{
    public static void SetTransparentBackground(Window window)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == nint.Zero) return;

        var accent = new NativeMethods.ACCENT_POLICY { nAccentState = 2, nColor = 0x00000000 };
        var accentStructSize = Marshal.SizeOf(accent);
        var accentPtr = Marshal.AllocHGlobal(accentStructSize);

        try
        {
            Marshal.StructureToPtr(accent, accentPtr, false);
            var nativeData = new NativeMethods.WINDOWCOMPOSITIONATTRIBDATA
            {
                Attrib = NativeMethods.WINDOWCOMPOSITIONATTRIB.WCA_ACCENT_POLICY,
                pvData = accentPtr,
                cbData = accentStructSize
            };
            _ = NativeMethods.SetWindowCompositionAttribute(hwnd, ref nativeData);
        }
        finally { Marshal.FreeHGlobal(accentPtr); }
    }

    public static void DisableWindowAnimations(nint hwnd)
    {
        if (hwnd == nint.Zero || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        int policy = NativeMethods.DWMNCRP_DISABLED;
        _ = NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_NCRENDERING_POLICY,
            ref policy,
            sizeof(int));
    }
}