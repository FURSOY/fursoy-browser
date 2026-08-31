#nullable enable
using System.Globalization;
using System.Resources;

namespace Fursoy.Browser.App.Resources;

/// <summary>
/// Strongly-typed erişim için Strings.resx üzerine yazılmış sarmalayıcı.
/// Yeni bir dize eklerken hem Strings.resx'e hem buraya (ve gerekiyorsa
/// diğer kültür .resx dosyalarına) eklemeyi unutma.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager ResourceManager =
        new("Fursoy.Browser.App.Resources.Strings", typeof(Strings).Assembly);

    public static CultureInfo? Culture { get; set; }

    public static string NewTab => GetString("NewTab");

    public static string Bookmarks => GetString("Bookmarks");

    public static string BookmarksEmpty => GetString("BookmarksEmpty");

    public static string AddBookmarkTooltip => GetString("AddBookmarkTooltip");

    public static string BookmarksTooltip => GetString("BookmarksTooltip");

    public static string AppTitle => GetString("AppTitle");

    private static string GetString(string name) =>
        ResourceManager.GetString(name, Culture) ?? name;
}
