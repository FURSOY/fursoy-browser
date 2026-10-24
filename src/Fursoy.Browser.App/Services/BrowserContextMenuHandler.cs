using CefSharp;
using CefSharp.Handler;
using Fursoy.Browser.App.Resources;

namespace Fursoy.Browser.App.Services;

public sealed class BrowserContextMenuHandler(
    Func<string> getSearchEngine,
    Func<bool> isInternalPage,
    Action<string> openNewTab,
    Action<int, int, IReadOnlyList<BrowserContextMenuEntry>, IRunContextMenuCallback> showMenu) : ContextMenuHandler
{
    private static readonly CefMenuCommand SearchSelectionCommand =
        (CefMenuCommand)((int)CefMenuCommand.CustomFirst + 1);

    protected override void OnBeforeContextMenu(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IContextMenuParams parameters,
        IMenuModel model)
    {
        if (model.GetIndexOf(CefMenuCommand.Reload) < 0 && !parameters.IsEditable)
            model.InsertItemAt(Math.Min(2, model.Count), CefMenuCommand.Reload, Strings.ContextReload);

        // Yeni sekme sayfasında yazdırma/kaynak/inceleme seçenekleri kullanıcıya
        // anlamlı bir işlem sunmuyor; Chrome benzeri sade başlangıç menüsü bırak.
        if (isInternalPage())
        {
            model.Remove(CefMenuCommand.Print);
            model.Remove(CefMenuCommand.ViewSource);
            RemoveCommandByName(model, "InspectElement");
        }

        for (var index = 0; index < model.Count; index++)
        {
            var command = model.GetCommandIdAt(index);
            var localized = GetLocalizedLabel(command.ToString());
            if (localized != null) model.SetLabelAt(index, localized);
        }

        if (string.IsNullOrWhiteSpace(parameters.SelectionText)) return;
        if (model.Count > 0) model.AddSeparator();
        var selection = Compact(parameters.SelectionText, 36);
        var engine = NewTabPage.GetSearchConfiguration(getSearchEngine()).EngineName;
        model.AddItem(
            SearchSelectionCommand,
            string.Format(Strings.ContextSearchSelection, engine, selection));
    }

    private static void RemoveCommandByName(IMenuModel model, string commandName)
    {
        for (var index = model.Count - 1; index >= 0; index--)
        {
            if (model.GetCommandIdAt(index).ToString().Equals(commandName, StringComparison.Ordinal))
                model.RemoveAt(index);
        }
    }

    protected override bool OnContextMenuCommand(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IContextMenuParams parameters,
        CefMenuCommand commandId,
        CefEventFlags eventFlags)
    {
        if (commandId != SearchSelectionCommand || string.IsNullOrWhiteSpace(parameters.SelectionText))
            return false;

        openNewTab(NavigationResolver.Resolve(parameters.SelectionText, getSearchEngine()));
        return true;
    }

    protected override bool RunContextMenu(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IContextMenuParams parameters,
        IMenuModel model,
        IRunContextMenuCallback callback)
    {
        var entries = CopyEntries(model);
        if (entries.Count == 0) return false;

        showMenu(parameters.XCoord, parameters.YCoord, entries, callback);
        return true;
    }

    private static IReadOnlyList<BrowserContextMenuEntry> CopyEntries(IMenuModel model)
    {
        var entries = new List<BrowserContextMenuEntry>(model.Count);
        for (var index = 0; index < model.Count; index++)
        {
            var kind = model.GetTypeAt(index).ToString();
            if (kind.Equals("Separator", StringComparison.OrdinalIgnoreCase) &&
                (entries.Count == 0 || entries[^1].Kind.Equals("Separator", StringComparison.OrdinalIgnoreCase)))
                continue;

            var submenu = model.GetSubMenuAt(index);
            entries.Add(new BrowserContextMenuEntry(
                model.GetCommandIdAt(index),
                model.GetLabelAt(index),
                kind,
                model.IsEnabledAt(index),
                model.IsCheckedAt(index),
                GetShortcut(model.GetCommandIdAt(index)),
                submenu == null ? [] : CopyEntries(submenu)));
        }

        if (entries.Count > 0 && entries[^1].Kind.Equals("Separator", StringComparison.OrdinalIgnoreCase))
            entries.RemoveAt(entries.Count - 1);
        return entries;
    }

    private static string GetShortcut(CefMenuCommand command) => command.ToString() switch
    {
        "Back" => "Alt+←",
        "Forward" => "Alt+→",
        "Reload" => "Ctrl+R",
        "ReloadNoCache" => "Ctrl+Shift+R",
        "Undo" => "Ctrl+Z",
        "Redo" => "Ctrl+Y",
        "Cut" => "Ctrl+X",
        "Copy" => "Ctrl+C",
        "Paste" => "Ctrl+V",
        "SelectAll" => "Ctrl+A",
        "Print" => "Ctrl+P",
        "ViewSource" => "Ctrl+U",
        "InspectElement" => "Ctrl+Shift+I",
        _ => string.Empty,
    };

    private static string Compact(string value, int maximumLength)
    {
        var compact = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= maximumLength ? compact : compact[..(maximumLength - 1)] + "…";
    }

    private static string? GetLocalizedLabel(string command) => command switch
    {
        "Back" => Strings.ContextBack,
        "Forward" => Strings.ContextForward,
        "Reload" => Strings.ContextReload,
        "ReloadNoCache" => Strings.ContextReloadNoCache,
        "StopLoad" => Strings.ContextStop,
        "Undo" => Strings.ContextUndo,
        "Redo" => Strings.ContextRedo,
        "Cut" => Strings.ContextCut,
        "Copy" => Strings.ContextCopy,
        "Paste" => Strings.ContextPaste,
        "Delete" => Strings.ContextDelete,
        "SelectAll" => Strings.ContextSelectAll,
        "Print" => Strings.ContextPrint,
        "ViewSource" => Strings.ContextViewSource,
        "InspectElement" => Strings.ContextInspect,
        "OpenLinkInNewTab" => Strings.ContextOpenLinkNewTab,
        "OpenLinkInNewWindow" => Strings.ContextOpenLinkNewWindow,
        "CopyLinkUrl" => Strings.ContextCopyLink,
        "SaveLinkAs" => Strings.ContextSaveLink,
        "OpenImageInNewTab" => Strings.ContextOpenImageNewTab,
        "SaveImageAs" => Strings.ContextSaveImage,
        "CopyImage" => Strings.ContextCopyImage,
        "CopyImageUrl" => Strings.ContextCopyImageAddress,
        _ => null,
    };
}

public sealed record BrowserContextMenuEntry(
    CefMenuCommand CommandId,
    string Label,
    string Kind,
    bool IsEnabled,
    bool IsChecked,
    string Shortcut,
    IReadOnlyList<BrowserContextMenuEntry> Children);
