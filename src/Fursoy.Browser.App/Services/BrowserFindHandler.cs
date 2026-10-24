using CefSharp;
using CefSharp.Handler;
using CefSharp.Structs;

namespace Fursoy.Browser.App.Services;

/// <summary>CEF find bildirimlerini pencerenin UI dispatcher'ına iletir.</summary>
public sealed class BrowserFindHandler(Action<int, int, int, bool> report) : FindHandler
{
    protected override void OnFindResult(IWebBrowser chromiumWebBrowser, IBrowser browser,
        int identifier, int count, Rect selectionRect, int activeMatchOrdinal, bool finalUpdate)
    {
        report(identifier, count, activeMatchOrdinal, finalUpdate);
    }
}
