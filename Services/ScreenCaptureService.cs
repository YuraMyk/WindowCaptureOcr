using System.Drawing;
using System.Runtime.InteropServices;

namespace WindowCaptureOcr.Services;

/// <summary>
/// Captures any rectangle of the physical screen via BitBlt.
/// Call <see cref="ExcludeFromCapture"/> once on the overlay window handle so our own
/// window is invisible to BitBlt and never ends up in captured frames.
/// </summary>
public class ScreenCaptureService
{
    // ─── P/Invoke ────────────────────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest,
        int nWidth, int nHeight, IntPtr hdcSrc, int xSrc, int ySrc, uint dwRop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// WDA_EXCLUDEFROMCAPTURE (0x11) — makes the window invisible to BitBlt/PrintScreen
    /// while remaining fully visible to the user on screen.
    /// Requires Windows 10 2004 (build 19041) or later.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    private const uint WDA_NONE               = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const uint SRCCOPY                = 0x00CC0020;

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Marks <paramref name="hWnd"/> so that it will NOT appear in any screen capture
    /// performed by this process (or any process using BitBlt on the desktop DC).
    /// Call this once in <c>OnSourceInitialized</c>.
    /// </summary>
    /// <returns><c>true</c> if successful; <c>false</c> if the OS version is too old.</returns>
    public static bool ExcludeFromCapture(IntPtr hWnd)
        => SetWindowDisplayAffinity(hWnd, WDA_EXCLUDEFROMCAPTURE);

    /// <summary>
    /// Captures a rectangle of the physical screen (device pixels) and returns a
    /// <see cref="Bitmap"/>. The caller is responsible for disposing the bitmap.
    /// </summary>
    public Bitmap CaptureRect(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Capture rectangle must have positive dimensions.");

        IntPtr hdcScreen = GetDC(IntPtr.Zero);   // full desktop DC
        IntPtr hdcMem    = CreateCompatibleDC(hdcScreen);
        IntPtr hBmp      = CreateCompatibleBitmap(hdcScreen, width, height);
        IntPtr hOld      = SelectObject(hdcMem, hBmp);

        BitBlt(hdcMem, 0, 0, width, height, hdcScreen, x, y, SRCCOPY);

        SelectObject(hdcMem, hOld);
        DeleteDC(hdcMem);
        ReleaseDC(IntPtr.Zero, hdcScreen);

        Bitmap bmp = System.Drawing.Image.FromHbitmap(hBmp);
        DeleteObject(hBmp);
        return bmp;
    }
}
