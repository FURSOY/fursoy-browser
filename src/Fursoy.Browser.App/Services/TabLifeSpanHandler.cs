using CefSharp;
using CefSharp.Handler;

namespace Fursoy.Browser.App.Services;

/// <summary>
/// Web sayfalarının yeni native pencere açmasını engeller ve hedefi
/// uygulamanın kendi sekme sistemine yönlendirir.
/// </summary>
public sealed class TabLifeSpanHandler(Action<string> openInNewTab) : LifeSpanHandler
{
    protected override bool OnBeforePopup(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        string targetUrl,
        string targetFrameName,
        WindowOpenDisposition targetDisposition,
        bool userGesture,
        IPopupFeatures popupFeatures,
        IWindowInfo windowInfo,
        IBrowserSettings browserSettings,
        ref bool noJavascriptAccess,
        out IWebBrowser? newBrowser)
    {
        newBrowser = null;
        if (!string.IsNullOrWhiteSpace(targetUrl)) openInNewTab(targetUrl);
        return true;
    }
}
