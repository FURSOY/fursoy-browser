using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Fursoy.Browser.App.Services;

public readonly record struct WindowPixelRect(int Left, int Top, int Right, int Bottom);

public readonly record struct MaximizedWindowBounds(int X, int Y, int Width, int Height);

/// <summary>
/// Custom WindowChrome windows normally maximize a few pixels beyond the
/// monitor bounds (to hide their invisible resize border), which
/// clips the top-left corner of the content. Handling WM_GETMINMAXINFO and
/// pinning the maximized rect to the monitor's own work area fixes this at
/// the source, so no compensating margin is needed anywhere else.
/// </summary>
public static class WindowMaximizeFix
{
    public static readonly DependencyProperty IsFullScreenProperty = DependencyProperty.RegisterAttached(
        "IsFullScreen",
        typeof(bool),
        typeof(WindowMaximizeFix),
        new PropertyMetadata(false));

    public static void SetIsFullScreen(DependencyObject target, bool value) =>
        target.SetValue(IsFullScreenProperty, value);

    public static bool GetIsFullScreen(DependencyObject target) =>
        (bool)target.GetValue(IsFullScreenProperty);

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwndSource = (HwndSource)PresentationSource.FromVisual(window)!;
            ApplyDwmFrame(hwndSource.Handle);
            hwndSource.AddHook(WndProc);
        };
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_ERASEBKGND = 0x0014;
        const int WM_GETMINMAXINFO = 0x0024;
        const int WM_NCHITTEST = 0x0084;

        if (msg == WM_ERASEBKGND)
        {
            // Pencere yeniden boyutlandırılırken DWM ve User32'nin istemci alanını
            // varsayılan yarı saydam fırçayla silerek üst başlık/sekme çubuğunda
            // titreme (flicker) oluşturmasını engeller.
            handled = true;
            return new IntPtr(1);
        }

        if (msg == WM_NCHITTEST)
        {
            var hit = HitTestResizeBorder(hwnd);
            if (hit != 0)
            {
                handled = true;
                return new IntPtr(hit);
            }
            return IntPtr.Zero;
        }

        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var window = HwndSource.FromHwnd(hwnd)?.RootVisual as Window;
        var dpiScale = Math.Max(1d, GetDpiForWindow(hwnd) / 96d);
        if (window != null)
        {
            var minimumHeight = window is Fursoy.Browser.App.MainWindow mainWindow
                ? Math.Max(window.MinHeight, mainWindow.MinimumChromeHeight)
                : window.MinHeight;
            mmi.ptMinTrackSize.X = (int)Math.Ceiling(window.MinWidth * dpiScale);
            mmi.ptMinTrackSize.Y = (int)Math.Ceiling(minimumHeight * dpiScale);
        }
        var maximizeBounds = CalculateMaximizedBounds(
            new WindowPixelRect(
                info.rcMonitor.Left,
                info.rcMonitor.Top,
                info.rcMonitor.Right,
                info.rcMonitor.Bottom),
            new WindowPixelRect(
                info.rcWork.Left,
                info.rcWork.Top,
                info.rcWork.Right,
                info.rcWork.Bottom),
            window != null && GetIsFullScreen(window));
        mmi.ptMaxPosition.X = maximizeBounds.X;
        mmi.ptMaxPosition.Y = maximizeBounds.Y;
        mmi.ptMaxSize.X = maximizeBounds.Width;
        mmi.ptMaxSize.Y = maximizeBounds.Height;
        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;

        return IntPtr.Zero;
    }

    /// <summary>
    /// GetMonitorInfo koordinatları fiziksel pikseldir. WM_GETMINMAXINFO da
    /// fiziksel piksel beklediği için burada DIP/DPI dönüşümü yapılmaz.
    /// Konum monitörün başlangıcına göre, boyut ise gerçek work area'dan
    /// hesaplanır; böylece ikincil monitör ve her taskbar kenarı desteklenir.
    /// </summary>
    public static MaximizedWindowBounds CalculateMaximizedBounds(
        WindowPixelRect monitor,
        WindowPixelRect workArea,
        bool fullScreen)
    {
        var bounds = fullScreen ? monitor : workArea;
        return new MaximizedWindowBounds(
            bounds.Left - monitor.Left,
            bounds.Top - monitor.Top,
            Math.Max(0, bounds.Right - bounds.Left),
            Math.Max(0, bounds.Bottom - bounds.Top));
    }

    private static int HitTestResizeBorder(IntPtr hwnd)
    {
        var window = HwndSource.FromHwnd(hwnd)?.RootVisual as Window;
        if (window == null || window.WindowState != WindowState.Normal ||
            window.ResizeMode is not (ResizeMode.CanResize or ResizeMode.CanResizeWithGrip) ||
            !GetCursorPos(out var cursor) || !GetWindowRect(hwnd, out var bounds))
            return 0;

        var dpi = GetDpiForWindow(hwnd);
        var border = Math.Max(5, (int)Math.Ceiling(5 * (dpi == 0 ? 1d : dpi / 96d)));
        var left = cursor.X < bounds.Left + border;
        var right = cursor.X >= bounds.Right - border;
        var top = cursor.Y < bounds.Top + border;
        var bottom = cursor.Y >= bounds.Bottom - border;

        if (top && left) return 13;
        if (top && right) return 14;
        if (bottom && left) return 16;
        if (bottom && right) return 17;
        if (left) return 10;
        if (right) return 11;
        if (top) return 12;
        if (bottom) return 15;
        return 0;
    }

    private static void ApplyDwmFrame(IntPtr hwnd)
    {
        const int DwmWindowCornerPreference = 33;
        const int DwmBorderColor = 34;
        const int RoundCorners = 2;
        var cornerPreference = RoundCorners;
        _ = DwmSetWindowAttribute(
            hwnd,
            DwmWindowCornerPreference,
            ref cornerPreference,
            Marshal.SizeOf<int>());

        var borderColor = unchecked((int)ToColorRef(AppTheme.GetArgb("WindowBorderColor")));
        _ = DwmSetWindowAttribute(
            hwnd,
            DwmBorderColor,
            ref borderColor,
            Marshal.SizeOf<int>());
    }

    private static uint ToColorRef(uint argb) =>
        (argb & 0x000000FF) << 16
        | (argb & 0x0000FF00)
        | (argb & 0x00FF0000) >> 16;

    private const int MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point ptReserved;
        public Point ptMaxSize;
        public Point ptMaxPosition;
        public Point ptMinTrackSize;
        public Point ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public int dwFlags;
    }
}
