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

    public static string History => GetString("History");

    public static string HistoryEmpty => GetString("HistoryEmpty");

    public static string HistoryTooltip => GetString("HistoryTooltip");

    public static string ClearHistory => GetString("ClearHistory");

    public static string Downloads => GetString("Downloads");

    public static string DownloadsEmpty => GetString("DownloadsEmpty");

    public static string DownloadsTooltip => GetString("DownloadsTooltip");

    public static string DownloadComplete => GetString("DownloadComplete");

    public static string DownloadCancelled => GetString("DownloadCancelled");

    public static string NewTabSearchPlaceholder => GetString("NewTabSearchPlaceholder");

    public static string AddressBarPlaceholder => GetString("AddressBarPlaceholder");

    public static string TabSearchTooltip => GetString("TabSearchTooltip");

    public static string MenuTooltip => GetString("MenuTooltip");

    public static string ProfileTooltip => GetString("ProfileTooltip");

    public static string NewTabTooltip => GetString("NewTabTooltip");

    public static string AppTitle => GetString("AppTitle");

    private static string GetString(string name) =>
        ResourceManager.GetString(name, Culture) ?? name;
}
