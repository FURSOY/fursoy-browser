using CefSharp;

namespace Fursoy.Browser.App.Services;

public enum BrowserShortcut
{
    Reload, HardReload, NextTab, PreviousTab, NewTab, CloseTab, ReopenClosedTab,
    FocusAddressBar, History, Downloads, Back, Forward, ToggleFullScreen,
    FindInPage, CloseFindInPage,
    SelectLastTab, SelectTab1, SelectTab2, SelectTab3, SelectTab4, SelectTab5,
    SelectTab6, SelectTab7, SelectTab8,
}

/// <summary>
/// Chromium'un native HWND'si odaktayken WPF PreviewKeyDown'a ulaşmayan
/// tarayıcı kısayollarını ana pencereye yönlendirir.
/// </summary>
public sealed class BrowserKeyboardHandler(
    Action<BrowserShortcut> execute, Func<bool>? isFindInPageOpen = null) : IKeyboardHandler
{
    private const int VkTab = 0x09;
    private const int VkEscape = 0x1B;
    private const int VkF5 = 0x74;
    private const int VkF11 = 0x7A;
    private const int VkLeft = 0x25;
    private const int VkRight = 0x27;

    public bool OnPreKeyEvent(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        KeyType type,
        int windowsKeyCode,
        int nativeKeyCode,
        CefEventFlags modifiers,
        bool isSystemKey,
        ref bool isKeyboardShortcut)
    {
        if (type != KeyType.RawKeyDown) return false;
        var control = modifiers.HasFlag(CefEventFlags.ControlDown);
        var shift = modifiers.HasFlag(CefEventFlags.ShiftDown);
        var alt = modifiers.HasFlag(CefEventFlags.AltDown);
        var shortcut = ResolveShortcut(windowsKeyCode, control, shift, alt);

        if (shortcut == null) return false;
        if (shortcut == BrowserShortcut.CloseFindInPage && isFindInPageOpen?.Invoke() != true)
            return false;
        execute(shortcut.Value);
        isKeyboardShortcut = true;
        return true;
    }

    public static BrowserShortcut? ResolveShortcut(int windowsKeyCode, bool control, bool shift, bool alt) =>
        (windowsKeyCode, control, shift, alt) switch
        {
            (VkF5, _, false, _) => BrowserShortcut.Reload,
            (0x52, true, false, _) => BrowserShortcut.Reload,
            (0x52, true, true, _) => BrowserShortcut.HardReload,
            (VkTab, true, false, _) => BrowserShortcut.NextTab,
            (VkTab, true, true, _) => BrowserShortcut.PreviousTab,
            (0x54, true, false, _) => BrowserShortcut.NewTab,
            (0x57, true, false, _) => BrowserShortcut.CloseTab,
            (0x54, true, true, _) => BrowserShortcut.ReopenClosedTab,
            (0x4C, true, false, _) => BrowserShortcut.FocusAddressBar,
            (0x46, true, false, false) => BrowserShortcut.FindInPage,
            (VkEscape, false, false, false) => BrowserShortcut.CloseFindInPage,
            (0x48, true, false, _) => BrowserShortcut.History,
            (0x4A, true, false, _) => BrowserShortcut.Downloads,
            (VkLeft, false, false, true) => BrowserShortcut.Back,
            (VkRight, false, false, true) => BrowserShortcut.Forward,
            (VkF11, false, false, false) => BrowserShortcut.ToggleFullScreen,
            (0x31, true, false, _) => BrowserShortcut.SelectTab1,
            (0x32, true, false, _) => BrowserShortcut.SelectTab2,
            (0x33, true, false, _) => BrowserShortcut.SelectTab3,
            (0x34, true, false, _) => BrowserShortcut.SelectTab4,
            (0x35, true, false, _) => BrowserShortcut.SelectTab5,
            (0x36, true, false, _) => BrowserShortcut.SelectTab6,
            (0x37, true, false, _) => BrowserShortcut.SelectTab7,
            (0x38, true, false, _) => BrowserShortcut.SelectTab8,
            (0x39, true, false, _) => BrowserShortcut.SelectLastTab,
            _ => null,
        };

    public bool OnKeyEvent(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        KeyType type,
        int windowsKeyCode,
        int nativeKeyCode,
        CefEventFlags modifiers,
        bool isSystemKey) => false;
}
