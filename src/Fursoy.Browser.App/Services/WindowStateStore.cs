using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace Fursoy.Browser.App.Services;

public class WindowLayout
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>
/// Persists the window's size/position/maximized state across launches, so
/// the app reopens the way the user left it instead of always at the
/// hardcoded default size.
/// </summary>
public static class WindowStateStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FursoyBrowser", "window.json");

    public static void Apply(Window window)
    {
        var layout = Load();
        var maximized = layout?.Maximized == true;
        if (layout != null && IsOnScreen(layout))
        {
            window.Left = layout.Left;
            window.Top = layout.Top;
            window.Width = layout.Width;
            window.Height = layout.Height;
            if (layout.Maximized) window.WindowState = WindowState.Maximized;
        }

        // Terminalden Ctrl+C ile kapatıldığında Closing çalışmayabilir.
        // Boyut/konum değişikliğini kısa gecikmeyle kaydetmek bunu da kapsar.
        var saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        saveTimer.Tick += (_, _) =>
        {
            saveTimer.Stop();
            Save(window, maximized);
        };

        void ScheduleSave()
        {
            if (window.WindowState == WindowState.Minimized) return;
            saveTimer.Stop();
            saveTimer.Start();
        }

        window.LocationChanged += (_, _) => ScheduleSave();
        window.SizeChanged += (_, _) => ScheduleSave();
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Maximized) maximized = true;
            else if (window.WindowState == WindowState.Normal) maximized = false;
            ScheduleSave();
        };
        window.Closing += (_, _) =>
        {
            saveTimer.Stop();
            Save(window, maximized);
        };
    }

    private static bool IsOnScreen(WindowLayout layout)
    {
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return layout.Width >= 500 && layout.Height >= 88
               && virtualScreen.IntersectsWith(new Rect(layout.Left, layout.Top, layout.Width, layout.Height));
    }

    private static WindowLayout? Load()
    {
        if (!File.Exists(FilePath)) return null;

        try
        {
            return JsonSerializer.Deserialize<WindowLayout>(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private static void Save(Window window, bool maximized)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        var layout = new WindowLayout
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = maximized,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(layout));
    }
}
