using System.Windows;
using System.Windows.Media;

namespace Fursoy.Browser.App.Services;

/// <summary>
/// WPF ve Chromium tarafını aynı tema kaynaklarına bağlar. Tema sözlüğü
/// değiştirildiğinde yeni sekme sayfası ve tarayıcı zemini de uyumlu kalır.
/// </summary>
public static class AppTheme
{
    public static uint GetArgb(string resourceKey)
    {
        var color = GetColor(resourceKey);
        return ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
    }

    public static string GetCssColor(string resourceKey)
    {
        var color = GetColor(resourceKey);
        return color.A == byte.MaxValue
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : FormattableString.Invariant($"rgba({color.R}, {color.G}, {color.B}, {color.A / 255d:0.###})");
    }

    public static string GetFontFamily(string resourceKey) =>
        Application.Current.TryFindResource(resourceKey) is FontFamily fontFamily
            ? fontFamily.ToString()
            : throw new InvalidOperationException($"Theme resource '{resourceKey}' is missing or is not a FontFamily.");

    private static Color GetColor(string resourceKey) =>
        Application.Current.TryFindResource(resourceKey) is Color color
            ? color
            : throw new InvalidOperationException($"Theme resource '{resourceKey}' is missing or is not a Color.");
}
