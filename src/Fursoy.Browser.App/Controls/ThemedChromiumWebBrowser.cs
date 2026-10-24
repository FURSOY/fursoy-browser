using System.Runtime.InteropServices;
using System.Windows;
using CefSharp;
using CefSharp.Wpf.HwndHost;
using Fursoy.Browser.App.Services;

namespace Fursoy.Browser.App.Controls;

/// <summary>
/// Native HwndHost yüzeyi Chromium ilk karesini çizene kadar Windows'un
/// varsayılan beyaz fırçasıyla silinmesin diye tema rengiyle doldurulur.
/// </summary>
public sealed class ThemedChromiumWebBrowser : ChromiumWebBrowser
{
    private const int WmEraseBackground = 0x0014;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpHideWindow = 0x0080;
    private const uint SwpNoSendChanging = 0x0400;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private static readonly IntPtr HwndTop = new IntPtr(0);
    private static readonly IntPtr HwndBottom = new IntPtr(1);
    private bool _isPresentationActive;
    private IntPtr _hostHandle;

    /// <summary>
    /// Sekmeler arası geçişte pasif yüzeyin koordinatlarını bozmadan (0,0 dummy katman
    /// kalıntılarını önlemek için) Z-order ve görünürlük durumunu yönetir.
    /// </summary>
    public void SetPresentationActive(bool active)
    {
        if (_isPresentationActive == active && (active || _hostHandle == IntPtr.Zero)) return;
        _isPresentationActive = active;
        if (active)
        {
            try
            {
                this.GetBrowserHost()?.WasHidden(false);
            }
            catch
            {
            }

            if (_hostHandle != IntPtr.Zero)
            {
                SetWindowPos(
                    _hostHandle,
                    HwndTop,
                    0,
                    0,
                    0,
                    0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
                ShowWindow(_hostHandle, SwShowNoActivate);
            }
            Visibility = Visibility.Visible;
        }
        else if (_hostHandle != IntPtr.Zero)
        {
            CancelInteractiveOverlays();
            try
            {
                this.GetBrowserHost()?.WasHidden(true);
            }
            catch
            {
            }

            SetWindowPos(
                _hostHandle,
                HwndBottom,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpHideWindow);
            ShowWindow(_hostHandle, SwHide);
            Visibility = Visibility.Hidden;
        }
        InvalidateArrange();
    }

    /// <summary>
    /// Sekme değiştirilirken veya gizlenirken orta tuş kaydırma (autoscroll),
    /// sürükle-bırak veya IME bağlamlarını iptal eder.
    /// </summary>
    public void CancelInteractiveOverlays()
    {
        try
        {
            var host = this.GetBrowserHost();
            if (host != null)
            {
                host.SendCaptureLostEvent();
                host.ImeCancelComposition();
            }
        }
        catch
        {
        }
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        if (_hostHandle != IntPtr.Zero)
            ShowWindow(_hostHandle, _isPresentationActive ? SwShowNoActivate : SwHide);
    }

    protected override IntPtr WndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        _hostHandle = hwnd;
        if (msg == WmEraseBackground && wParam != IntPtr.Zero)
        {
            var argb = AppTheme.GetArgb("BackgroundColor");
            var colorRef = (argb & 0x000000FF) << 16
                           | (argb & 0x0000FF00)
                           | (argb & 0x00FF0000) >> 16;
            var brush = CreateSolidBrush(colorRef);
            try
            {
                if (GetClientRect(hwnd, out var rect)) FillRect(wParam, ref rect, brush);
            }
            finally
            {
                DeleteObject(brush);
            }

            handled = true;
            return new IntPtr(1);
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hdc, ref NativeRect rect, IntPtr brush);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint colorRef);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
