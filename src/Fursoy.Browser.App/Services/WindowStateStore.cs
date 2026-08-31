using System.IO;
using System.Text.Json;
using System.Windows;

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
        if (layout != null && IsOnScreen(layout))
        {
            window.Left = layout.Left;
            window.Top = layout.Top;
            window.Width = layout.Width;
            window.Height = layout.Height;
            if (layout.Maximized) window.WindowState = WindowState.Maximized;
        }

        window.Closing += (_, _) => Save(window);
    }

    private static bool IsOnScreen(WindowLayout layout)
    {
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return layout.Width >= 400 && layout.Height >= 300
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

    private static void Save(Window window)
    {
        var bounds = window.WindowState == WindowState.Maximized ? window.RestoreBounds : new Rect(window.Left, window.Top, window.Width, window.Height);
        var layout = new WindowLayout
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = window.WindowState == WindowState.Maximized,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(layout));
    }
}
