using CefSharp;
using CefSharp.Handler;

namespace Fursoy.Browser.App.Services;

public sealed class BrowserRequestHandler(Action<string> certificateError) : RequestHandler
{
    protected override bool OnCertificateError(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        CefErrorCode errorCode,
        string requestUrl,
        ISslInfo sslInfo,
        IRequestCallback callback)
    {
        certificateError(requestUrl);
        callback.Continue(false);
        return true;
    }
}
