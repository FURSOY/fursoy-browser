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

    public static string AllHistory => GetString("AllHistory");

    public static string ShowAllHistory => GetString("ShowAllHistory");

    public static string OpenTab => GetString("OpenTab");

    public static string HistoryEmpty => GetString("HistoryEmpty");

    public static string HistoryTooltip => GetString("HistoryTooltip");

    public static string ClearHistory => GetString("ClearHistory");

    public static string Downloads => GetString("Downloads");

    public static string DownloadsEmpty => GetString("DownloadsEmpty");

    public static string DownloadsTooltip => GetString("DownloadsTooltip");

    public static string FindInPage => GetString("FindInPage");
    public static string FindPrevious => GetString("FindPrevious");
    public static string FindNext => GetString("FindNext");
    public static string FindClose => GetString("FindClose");
    public static string ShowFindButtonInToolbar => GetString("ShowFindButtonInToolbar");

    public static string DownloadComplete => GetString("DownloadComplete");

    public static string DownloadCancelled => GetString("DownloadCancelled");
    public static string DownloadPaused => GetString("DownloadPaused");
    public static string DownloadFailed => GetString("DownloadFailed");
    public static string DownloadFileMissing => GetString("DownloadFileMissing");
    public static string DownloadSizeUnknown => GetString("DownloadSizeUnknown");
    public static string DownloadSecondsRemainingFormat => GetString("DownloadSecondsRemainingFormat");
    public static string DownloadMinutesRemainingFormat => GetString("DownloadMinutesRemainingFormat");
    public static string DownloadPause => GetString("DownloadPause");
    public static string DownloadResume => GetString("DownloadResume");
    public static string DownloadCancel => GetString("DownloadCancel");
    public static string DownloadOpen => GetString("DownloadOpen");
    public static string DownloadShowInFolder => GetString("DownloadShowInFolder");
    public static string DownloadRemoveFromHistory => GetString("DownloadRemoveFromHistory");
    public static string DownloadRetry => GetString("DownloadRetry");
    public static string ShowAllDownloads => GetString("ShowAllDownloads");
    public static string ClearDownloadHistory => GetString("ClearDownloadHistory");
    public static string ActiveDownloads => GetString("ActiveDownloads");
    public static string RecentDownloads => GetString("RecentDownloads");
    public static string DownloadSearchPlaceholder => GetString("DownloadSearchPlaceholder");
    public static string DownloadLocation => GetString("DownloadLocation");
    public static string ChooseDownloadLocation => GetString("ChooseDownloadLocation");
    public static string AskWhereToSaveDownloads => GetString("AskWhereToSaveDownloads");
    public static string DownloadSaveDialogTitle => GetString("DownloadSaveDialogTitle");
    public static string DownloadExecutableWarningTitle => GetString("DownloadExecutableWarningTitle");
    public static string DownloadExecutableWarning => GetString("DownloadExecutableWarning");

    public static string NewTabSearchPlaceholder => GetString("NewTabSearchPlaceholder");
    public static string NewTabSearchPlaceholderFormat => GetString("NewTabSearchPlaceholderFormat");
    public static string AddressBarPlaceholderFormat => GetString("AddressBarPlaceholderFormat");
    public static string ReloadTooltip => GetString("ReloadTooltip");
    public static string StopLoadingTooltip => GetString("StopLoadingTooltip");
    public static string ContextBack => GetString("ContextBack");
    public static string ContextForward => GetString("ContextForward");
    public static string ContextReload => GetString("ContextReload");
    public static string ContextReloadNoCache => GetString("ContextReloadNoCache");
    public static string ContextStop => GetString("ContextStop");
    public static string ContextUndo => GetString("ContextUndo");
    public static string ContextRedo => GetString("ContextRedo");
    public static string ContextCut => GetString("ContextCut");
    public static string ContextCopy => GetString("ContextCopy");
    public static string ContextPaste => GetString("ContextPaste");
    public static string ContextDelete => GetString("ContextDelete");
    public static string ContextSelectAll => GetString("ContextSelectAll");
    public static string ContextPrint => GetString("ContextPrint");
    public static string ContextViewSource => GetString("ContextViewSource");
    public static string ContextInspect => GetString("ContextInspect");
    public static string ContextOpenLinkNewTab => GetString("ContextOpenLinkNewTab");
    public static string ContextOpenLinkNewWindow => GetString("ContextOpenLinkNewWindow");
    public static string ContextCopyLink => GetString("ContextCopyLink");
    public static string ContextSaveLink => GetString("ContextSaveLink");
    public static string ContextOpenImageNewTab => GetString("ContextOpenImageNewTab");
    public static string ContextSaveImage => GetString("ContextSaveImage");
    public static string ContextCopyImage => GetString("ContextCopyImage");
    public static string ContextCopyImageAddress => GetString("ContextCopyImageAddress");
    public static string ContextSearchSelection => GetString("ContextSearchSelection");

    public static string AddressBarPlaceholder => GetString("AddressBarPlaceholder");

    public static string TabSearchTooltip => GetString("TabSearchTooltip");

    public static string MenuTooltip => GetString("MenuTooltip");

    public static string MoreTools => GetString("MoreTools");

    public static string ProfileTooltip => GetString("ProfileTooltip");

    public static string NewTabTooltip => GetString("NewTabTooltip");

    public static string ExtensionsTooltip => GetString("ExtensionsTooltip");

    public static string TranslateTooltip => GetString("TranslateTooltip");

    public static string PinTab => GetString("PinTab");

    public static string UnpinTab => GetString("UnpinTab");

    public static string MuteTab => GetString("MuteTab");

    public static string UnmuteTab => GetString("UnmuteTab");

    public static string NewTabToRight => GetString("NewTabToRight");

    public static string ReloadTab => GetString("ReloadTab");

    public static string DuplicateTab => GetString("DuplicateTab");

    public static string MoveTabToNewWindow => GetString("MoveTabToNewWindow");

    public static string CloseTab => GetString("CloseTab");

    public static string CloseOtherTabs => GetString("CloseOtherTabs");

    public static string CloseTabsToRight => GetString("CloseTabsToRight");

    public static string ReopenClosedTab => GetString("ReopenClosedTab");

    public static string Settings => GetString("Settings");

    public static string DarkTheme => GetString("DarkTheme");

    public static string LightTheme => GetString("LightTheme");

    public static string Language => GetString("Language");

    public static string Theme => GetString("Theme");

    public static string SearchEngine => GetString("SearchEngine");

    public static string RestoreSession => GetString("RestoreSession");

    public static string Turkish => GetString("Turkish");

    public static string English => GetString("English");

    public static string SecureConnection => GetString("SecureConnection");

    public static string NotSecure => GetString("NotSecure");

    public static string CertificateError => GetString("CertificateError");

    public static string SitePermissions => GetString("SitePermissions");

    public static string PermissionRequest => GetString("PermissionRequest");

    public static string Allow => GetString("Allow");

    public static string Block => GetString("Block");

    public static string Camera => GetString("Camera");

    public static string Microphone => GetString("Microphone");

    public static string Location => GetString("Location");

    public static string Notifications => GetString("Notifications");

    public static string Clipboard => GetString("Clipboard");

    public static string SearchWithEngine => GetString("SearchWithEngine");

    public static string GoToAddress => GetString("GoToAddress");

    public static string AppTitle => GetString("AppTitle");

    private static string GetString(string name) =>
        ResourceManager.GetString(name, Culture) ?? name;
}
