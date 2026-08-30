using System.Windows;
using CefSharp;
using CefSharp.Wpf;

namespace Fursoy.Browser.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var settings = new CefSettings
        {
            CachePath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FursoyBrowser", "Cache"),
        };

        Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cef.Shutdown();
        base.OnExit(e);
    }
}
