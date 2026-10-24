using CefSharp;
using CefSharp.Handler;

namespace Fursoy.Browser.App.Services;

/// <summary>Native Chromium HWND odağını WPF Find TextBox odağıyla eşitler.</summary>
public sealed class BrowserFocusHandler(Action onGotFocus) : FocusHandler
{
    protected override void OnGotFocus(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
        onGotFocus();
    }
}
