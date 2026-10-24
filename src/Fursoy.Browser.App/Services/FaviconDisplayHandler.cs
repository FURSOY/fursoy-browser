using CefSharp;
using CefSharp.Handler;

namespace Fursoy.Browser.App.Services;

public sealed class FaviconDisplayHandler(Action<string?> faviconChanged) : DisplayHandler
{
    protected override void OnFaviconUrlChange(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IList<string> iconUrls)
    {
        var iconUrl = iconUrls.FirstOrDefault(url =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            !uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));

        faviconChanged(iconUrl);
    }
}
