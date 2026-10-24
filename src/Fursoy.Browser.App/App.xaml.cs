using System.Windows;
using System.Globalization;
using System.IO;
using CefSharp;
using CefSharp.Wpf;
using CefSharp.Enums;
using Fursoy.Browser.App.Resources;
using Fursoy.Browser.App.Services;

namespace Fursoy.Browser.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, eventArgs) => WriteCrashLog(eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception) WriteCrashLog(exception);
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) => WriteCrashLog(eventArgs.Exception);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var appSettings = AppSettingsStore.Load();
        var culture = CultureInfo.GetCultureInfo(appSettings.Language);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Strings.Culture = culture;
        ThemeManager.Apply(appSettings.Theme);

        var browserDataPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FursoyBrowser");

        var settings = new CefSettings
        {
            RootCachePath = browserDataPath,
            CachePath = System.IO.Path.Combine(browserDataPath, "Cache"),
            PersistSessionCookies = true,
            BackgroundColor = AppTheme.GetArgb("BackgroundColor"),
            LogSeverity = LogSeverity.Warning,
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                        "(KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36",
        };
        settings.CefCommandLineArgs["enable-features"] = "OverlayScrollbar,FluentScrollbar";
        settings.RegisterScheme(new CefCustomScheme(
            "fursoy",
            SchemeOptions.Standard | SchemeOptions.Local | SchemeOptions.Secure | SchemeOptions.DisplayIsolated));

        Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cef.Shutdown();
        base.OnExit(e);
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FursoyBrowser", "Logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "crash.log"),
                $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Hata raporlaması ana hatayı gölgelememeli.
        }
    }
}
