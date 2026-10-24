using CefSharp;
using CefSharp.Handler;

namespace Fursoy.Browser.App.Services;

public sealed class BrowserPermissionHandler(
    Action<string, string, Action, Action> showPrompt) : PermissionHandler
{
    protected override bool OnRequestMediaAccessPermission(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        string requestingOrigin,
        MediaAccessPermissionType requestedPermissions,
        IMediaAccessCallback callback)
    {
        showPrompt(
            requestingOrigin,
            requestedPermissions.ToString(),
            () =>
            {
                callback.Continue(requestedPermissions);
                callback.Dispose();
            },
            () =>
            {
                callback.Cancel();
                callback.Dispose();
            });
        return true;
    }

    protected override bool OnShowPermissionPrompt(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        ulong promptId,
        string requestingOrigin,
        PermissionRequestType requestedPermissions,
        IPermissionPromptCallback callback)
    {
        showPrompt(
            requestingOrigin,
            requestedPermissions.ToString(),
            () =>
            {
                callback.Continue(PermissionRequestResult.Accept);
                callback.Dispose();
            },
            () =>
            {
                callback.Continue(PermissionRequestResult.Deny);
                callback.Dispose();
            });
        return true;
    }
}
