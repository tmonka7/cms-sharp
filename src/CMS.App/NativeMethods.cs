using System.Runtime.InteropServices;

namespace CMS.App;

/// <summary>
/// The handful of Win32 calls the borderless windows need. Dragging is handed
/// to the window manager so Aero Snap and multi-monitor behaviour keep working.
/// </summary>
internal static class NativeMethods
{
    private const int HtCaption = 0x2;
    private const int WmNcLButtonDown = 0xA1;

    /// <summary>Starts a window drag as if the caption bar had been grabbed.</summary>
    public static void DragWindow(IntPtr handle)
    {
        ReleaseCapture();
        SendMessage(handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
