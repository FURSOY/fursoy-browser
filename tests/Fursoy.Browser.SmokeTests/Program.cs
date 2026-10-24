using Fursoy.Browser.App.Services;
using Fursoy.Browser.App.Models;
using Fursoy.Browser.App.Controls;
using CefSharp;

var failures = new List<string>();
var checkCount = 0;

// Konum popup'ın saydam gölge host'una değil, görünen 320 DIP yüzeye göre hesaplanır.
foreach (var targetWidth in new[] { 24d, 32d, 40d })
{
    var origin = ToolbarPopupAnchor.CalculateOrigin(targetWidth, 28, 352, 16, 12, 4);
    Check($"toolbar popup right edge at {targetWidth} DIP",
        origin.X + 352 - 16, targetWidth);
    Check($"toolbar popup gap at {targetWidth} DIP", origin.Y + 12 - 28, 4d);
    foreach (var dpiScale in new[] { 1d, 1.25d, 1.5d })
        Check($"toolbar popup right edge at {targetWidth} DIP / {dpiScale}x",
            (origin.X + 352 - 16) * dpiScale, targetWidth * dpiScale);

    foreach (var windowWidth in new[] { 500d, 960d, 1920d })
    {
        var targetLeft = windowWidth - 120;
        Check($"toolbar popup alignment in {windowWidth} DIP window / {targetWidth} DIP target",
            targetLeft + origin.X + 352 - 16, targetLeft + targetWidth);
    }
}

Check("absolute HTTPS", NavigationResolver.Resolve("https://example.com"), "https://example.com/");
Check("host adds HTTPS", NavigationResolver.Resolve("example.com/path"), "https://example.com/path");
Check("localhost adds HTTPS", NavigationResolver.Resolve("localhost:8080"), "https://localhost:8080/");
Check("Google search", NavigationResolver.Resolve("iki kelime"), "https://www.google.com/search?q=iki%20kelime");
Check("Bing search", NavigationResolver.Resolve("iki kelime", "Bing"), "https://www.bing.com/search?q=iki%20kelime");
Check("DuckDuckGo search", NavigationResolver.Resolve("iki kelime", "DuckDuckGo"), "https://duckduckgo.com/?q=iki%20kelime");
Check("single number is a search", NavigationResolver.Resolve("1"), "https://www.google.com/search?q=1");
Check("short dotted number is a search", NavigationResolver.Resolve("1.2"), "https://www.google.com/search?q=1.2");
Check("strict IPv4 remains an address", NavigationResolver.Resolve("127.0.0.1"), "https://127.0.0.1/");
Check("new tab Bing action", NewTabPage.GetSearchConfiguration("Bing").Action, "https://www.bing.com/search");
Check("new tab DuckDuckGo action", NewTabPage.GetSearchConfiguration("DuckDuckGo").Action, "https://duckduckgo.com/");
Check("internal downloads URL remains an address", NavigationResolver.Resolve("fursoy://downloads"), "fursoy://downloads/");

var session = BrowserSessionStore.Normalize(new BrowserSession
{
    Tabs = Enumerable.Range(0, 60).Select(index => $"https://example.com/{index}").ToList(),
    ActiveIndex = 99,
    PinnedIndices = [-1, 0, 0, 49, 50],
});
Check("session tab cap", session.Tabs.Count, 50);
Check("session active clamp", session.ActiveIndex, 49);
Check("session pinned cleanup", string.Join(',', session.PinnedIndices), "0,49");

var now = DateTime.UtcNow;
var idle = TimeSpan.FromMinutes(10);
Check("idle background suspends", BrowserTabPolicy.ShouldSuspend(false, false, false, false, true, now - idle, now, idle), true);
Check("active tab stays alive", BrowserTabPolicy.ShouldSuspend(true, false, false, false, true, now - idle, now, idle), false);
Check("pinned tab stays alive", BrowserTabPolicy.ShouldSuspend(false, true, false, false, true, now - idle, now, idle), false);
Check("audible tab stays alive", BrowserTabPolicy.ShouldSuspend(false, false, true, false, true, now - idle, now, idle), false);
Check("loading tab stays alive", BrowserTabPolicy.ShouldSuspend(false, false, false, true, true, now - idle, now, idle), false);
Check("recent tab stays alive", BrowserTabPolicy.ShouldSuspend(false, false, false, false, true, now, now, idle), false);
Check("pinned drop stays in pinned area", BrowserTabPolicy.ConstrainInsertionIndex(8, 8, 3, true), 3);
Check("regular drop stays after pinned area", BrowserTabPolicy.ConstrainInsertionIndex(0, 8, 3, false), 3);
Check("drop index clamps at end", BrowserTabPolicy.ConstrainInsertionIndex(99, 8, 3, false), 8);
Check("empty strip accepts first tab", BrowserTabPolicy.ConstrainInsertionIndex(4, 0, 0, false), 0);
var openerId = Guid.NewGuid();
Check("first opener child is inserted immediately to the right",
    BrowserTabPolicy.CalculateOpenerInsertionIndex([null, null, null, null], 1, openerId, 0), 2);
Check("later opener children preserve creation order",
    BrowserTabPolicy.CalculateOpenerInsertionIndex([null, null, openerId, openerId, null], 1, openerId, 0), 4);
Check("unpinned opener child respects pinned boundary",
    BrowserTabPolicy.CalculateOpenerInsertionIndex([null, null, null], 0, openerId, 2), 2);
Check("dragged center chooses gap", BrowserTabPolicy.CalculateInsertionIndex(250, new[] { 100d, 200d, 300d }), 2);
Check("right drag closes first gap", BrowserTabPolicy.GetInternalGapOffset(2, 1, 3, 120), -120d);
Check("right drag leaves far tab", BrowserTabPolicy.GetInternalGapOffset(4, 1, 3, 120), 0d);
Check("left drag opens target gap", BrowserTabPolicy.GetInternalGapOffset(1, 3, 1, 120), 120d);
Check("stationary drag has no gap", BrowserTabPolicy.GetInternalGapOffset(2, 2, 2, 120), 0d);
Check("half-slot crossing changes target", BrowserTabPolicy.CalculateNearestSlotIndex(161, new[] { 100d, 220d, 340d }), 1);
Check("before half-slot keeps target", BrowserTabPolicy.CalculateNearestSlotIndex(159, new[] { 100d, 220d, 340d }), 0);
Check("exact right half opens next slot", BrowserTabPolicy.CalculateDragSlotIndex(160, new[] { 100d, 220d, 340d }, 0), 1);
Check("exact left half opens previous slot", BrowserTabPolicy.CalculateDragSlotIndex(160, new[] { 100d, 220d, 340d }, 1), 0);
Check("drag can pass old plus position", BrowserTabPolicy.CalculateMaximumRightDragOffset(220, 120, 900, 32), 528d);
Check("right clamp reserves plus button", BrowserTabPolicy.CalculateMaximumRightDragOffset(700, 120, 900, 32), 48d);
Check("plus follows tab after it is passed", BrowserTabPolicy.CalculateTrailingButtonOffset(690, 120, 780, 28, 900, true), 34d);
Check("plus stays when target is not last", BrowserTabPolicy.CalculateTrailingButtonOffset(690, 120, 780, 28, 900, false), 0d);
Check("browser Ctrl+Tab shortcut", BrowserKeyboardHandler.ResolveShortcut(0x09, true, false, false), BrowserShortcut.NextTab);
Check("browser Ctrl+Shift+Tab shortcut", BrowserKeyboardHandler.ResolveShortcut(0x09, true, true, false), BrowserShortcut.PreviousTab);
Check("browser Ctrl+L shortcut", BrowserKeyboardHandler.ResolveShortcut(0x4C, true, false, false), BrowserShortcut.FocusAddressBar);
Check("browser Ctrl+9 shortcut", BrowserKeyboardHandler.ResolveShortcut(0x39, true, false, false), BrowserShortcut.SelectLastTab);
Check("browser F11 shortcut", BrowserKeyboardHandler.ResolveShortcut(0x7A, false, false, false), BrowserShortcut.ToggleFullScreen);
Check("browser Ctrl+J opens downloads", BrowserKeyboardHandler.ResolveShortcut(0x4A, true, false, false), BrowserShortcut.Downloads);
Check("browser Ctrl+F opens find", BrowserKeyboardHandler.ResolveShortcut(0x46, true, false, false), BrowserShortcut.FindInPage);
Check("browser Escape can close find", BrowserKeyboardHandler.ResolveShortcut(0x1B, false, false, false), BrowserShortcut.CloseFindInPage);
Check("plain F stays in page", BrowserKeyboardHandler.ResolveShortcut(0x46, false, false, false), null);
var findShortcutCalls = 0;
var closedFindKeyboard = new BrowserKeyboardHandler(_ => findShortcutCalls++, () => false);
var isKeyboardShortcut = false;
Check("page Escape passes through while find is closed",
    closedFindKeyboard.OnPreKeyEvent(null!, null!, KeyType.RawKeyDown, 0x1B, 0,
        CefEventFlags.None, false, ref isKeyboardShortcut), false);
Check("closed find Escape dispatches no shortcut", findShortcutCalls, 0);
var openFindKeyboard = new BrowserKeyboardHandler(_ => findShortcutCalls++, () => true);
Check("page Escape closes an open find panel",
    openFindKeyboard.OnPreKeyEvent(null!, null!, KeyType.RawKeyDown, 0x1B, 0,
        CefEventFlags.None, false, ref isKeyboardShortcut), true);
Check("open find Escape dispatches shortcut", findShortcutCalls, 1);
var findOrigin = ToolbarPopupAnchor.CalculateContentOrigin(420, 382, 16, 12, 6);
Check("find panel follows address bar right edge", findOrigin.X + 382 - 16, 420d);
Check("find panel top inset", findOrigin.Y + 12, 6d);
Check("Find toolbar button defaults to visible", new AppSettingsData().ShowFindButtonInToolbar, true);
var hiddenFindButtonSettings = System.Text.Json.JsonSerializer.Deserialize<AppSettingsData>(
    System.Text.Json.JsonSerializer.Serialize(new AppSettingsData { ShowFindButtonInToolbar = false }));
Check("Find toolbar visibility persists in settings JSON",
    hiddenFindButtonSettings?.ShowFindButtonInToolbar, false);

var findState = new FindInPageState { IsOpen = true, Query = "İstanbul" };
Check("Unicode find text is preserved", findState.Query, "İstanbul");
Check("find starts with zero matches", findState.Counter, "0/0");
Check("first CEF find result applies", findState.ApplyResult(1, 7, 3), true);
Check("CEF counter is ordinal/total", findState.Counter, "3/7");
Check("older CEF result cannot overwrite current find", findState.ApplyResult(0, 2, 1), false);
Check("CEF zero matches shows zero counter", findState.ApplyResult(2, 0, 0), true);
Check("find zero counter", findState.Counter, "0/0");
foreach (var unicodeQuery in new[] { "test", "Fursoy", "İstanbul", "ş", "ğ" })
{
    findState.Query = unicodeQuery;
    Check($"find query preserves {unicodeQuery}", findState.Query, unicodeQuery);
}
findState.ResetResults();
Check("find results reset on navigation", findState.Counter, "0/0");

var suggestionNow = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
var suggestionCandidates = new[]
{
    new OmniboxSuggestionCandidate("Alpha Documentation", "https://docs.example.com/alpha", OmniboxSuggestionSource.History, suggestionNow.AddDays(-2)),
    new OmniboxSuggestionCandidate("Project home", "https://project.example.com/", OmniboxSuggestionSource.Bookmark),
};
Check("omnibox matches page title",
    OmniboxSuggestionService.Find("Alpha Doc", suggestionCandidates, suggestionNow)[0].Url,
    "https://docs.example.com/alpha");
Check("omnibox matches URL",
    OmniboxSuggestionService.Find("project.example", suggestionCandidates, suggestionNow)[0].Title,
    "Project home");

var relevanceCandidates = new[]
{
    new OmniboxSuggestionCandidate("Portal", "https://old.example.com/", OmniboxSuggestionSource.History, suggestionNow.AddDays(-200)),
    new OmniboxSuggestionCandidate("My old portal notes", "https://recent.example.com/", OmniboxSuggestionSource.History, suggestionNow),
};
Check("omnibox relevance outranks recency",
    OmniboxSuggestionService.Find("Portal", relevanceCandidates, suggestionNow)[0].Url,
    "https://old.example.com/");

var recencyCandidates = new[]
{
    new OmniboxSuggestionCandidate("One result", "https://one.example.com/", OmniboxSuggestionSource.History, suggestionNow.AddDays(-10)),
    new OmniboxSuggestionCandidate("Two result", "https://two.example.com/", OmniboxSuggestionSource.History, suggestionNow.AddMinutes(-5)),
};
Check("omnibox recent result wins equal match",
    OmniboxSuggestionService.Find("result", recencyCandidates, suggestionNow)[0].Url,
    "https://two.example.com/");

var openTabId = Guid.NewGuid();
var duplicateCandidates = new[]
{
    new OmniboxSuggestionCandidate("Example", "https://example.com/", OmniboxSuggestionSource.History, suggestionNow.AddHours(-1)),
    new OmniboxSuggestionCandidate("Example bookmark", "https://example.com", OmniboxSuggestionSource.Bookmark),
    new OmniboxSuggestionCandidate("Example open", "https://example.com/", OmniboxSuggestionSource.OpenTab, suggestionNow, openTabId),
};
var duplicateResults = OmniboxSuggestionService.Find("example.com", duplicateCandidates, suggestionNow);
Check("omnibox merges duplicate URLs", duplicateResults.Count, 1);
Check("omnibox open tab wins duplicate URL", duplicateResults[0].Source, OmniboxSuggestionSource.OpenTab);
Check("omnibox preserves open tab target", duplicateResults[0].OpenTabId, openTabId);

var cappedCandidates = Enumerable.Range(0, 20)
    .Select(index => new OmniboxSuggestionCandidate($"Result {index}", $"https://result{index}.example/", OmniboxSuggestionSource.History));
Check("omnibox result cap", OmniboxSuggestionService.Find("result", cappedCandidates, suggestionNow).Count,
    OmniboxSuggestionService.DefaultMaximumResults);
Check("omnibox focus result cap",
    OmniboxSuggestionService.FindOnFocus(cappedCandidates).Count,
    OmniboxSuggestionService.DefaultMaximumResults);
Check("first mouse focus selects all",
    AddressBarInteractionPolicy.ShouldSelectAllAfterMouseFocus(false, false), true);
Check("focused mouse click preserves caret",
    AddressBarInteractionPolicy.ShouldSelectAllAfterMouseFocus(true, false), false);
Check("mouse drag preserves manual selection",
    AddressBarInteractionPolicy.ShouldSelectAllAfterMouseFocus(false, true), false);
Check("omnibox recognizes Google search result",
    OmniboxSuggestionPresentation.IsSearchResultUrl("https://www.google.com/search?q=browser"), true);
Check("omnibox recognizes Bing search result",
    OmniboxSuggestionPresentation.IsSearchResultUrl("https://www.bing.com/search?q=browser"), true);
Check("omnibox does not treat a regular site as search",
    OmniboxSuggestionPresentation.IsSearchResultUrl("https://support.google.com/chrome"), false);

var inlineCandidates = new[]
{
    new OmniboxSuggestionCandidate(
        "YouTube",
        "https://www.youtube.com/",
        OmniboxSuggestionSource.History,
        suggestionNow),
    new OmniboxSuggestionCandidate(
        "GitHub",
        "https://github.com/features",
        OmniboxSuggestionSource.Bookmark),
};
var youtubeCompletion = InlineAutocompleteService.Find("you", inlineCandidates);
Check("inline URL prefix activates", youtubeCompletion.IsInlineCompletionActive, true);
Check("inline preserves real user text", youtubeCompletion.UserText, "you");
Check("inline computes selected suffix", youtubeCompletion.InlineSuffix, "tube.com");
Check("inline computes visible fill text", youtubeCompletion.FillText, "youtube.com");
Check("inline keeps real destination URL", youtubeCompletion.DestinationUrl, "https://www.youtube.com/");
Check("inline records candidate source", youtubeCompletion.CandidateSource, OmniboxSuggestionSource.History);

var httpsCompletion = InlineAutocompleteService.Find("https://you", inlineCandidates);
Check("inline normalizes https and www", httpsCompletion.FillText, "https://youtube.com");
Check("inline https destination remains canonical", httpsCompletion.DestinationUrl, "https://www.youtube.com/");
var wwwCompletion = InlineAutocompleteService.Find("www.you", inlineCandidates);
Check("inline preserves explicit www", wwwCompletion.FillText, "www.youtube.com");
var httpCompletion = InlineAutocompleteService.Find(
    "http://exa",
    [new OmniboxSuggestionCandidate("Example", "http://www.example.com/", OmniboxSuggestionSource.History)]);
Check("inline normalizes http and www", httpCompletion.FillText, "http://example.com");

var titleOnlyCompletion = InlineAutocompleteService.Find(
    "you",
    [new OmniboxSuggestionCandidate("YouTube videos", "https://example.com/", OmniboxSuggestionSource.History)]);
Check("inline ignores title-only matches", titleOnlyCompletion.IsInlineCompletionActive, false);
Check("inline ignores normal search queries",
    InlineAutocompleteService.Find("youtube video", inlineCandidates).IsInlineCompletionActive, false);
Check("inline ignores middle URL matches",
    InlineAutocompleteService.Find("tube", inlineCandidates).IsInlineCompletionActive, false);
Check("inline ignores open tabs",
    InlineAutocompleteService.Find(
        "you",
        [new OmniboxSuggestionCandidate("YouTube", "https://www.youtube.com/", OmniboxSuggestionSource.OpenTab)])
        .IsInlineCompletionActive,
    false);

var singleCharacterCandidates = inlineCandidates
    .Concat([
        new OmniboxSuggestionCandidate("Empty", "", OmniboxSuggestionSource.History),
        new OmniboxSuggestionCandidate("Null", null!, OmniboxSuggestionSource.Bookmark),
    ])
    .ToList();
foreach (var singleCharacter in new[] { "a", "y", "g", "w", "1" })
{
    var singleInline = InlineAutocompleteService.Find(singleCharacter, singleCharacterCandidates);
    Check($"single '{singleCharacter}' never creates inline range",
        singleInline.IsInlineCompletionActive,
        false);
    var singleResults = OmniboxSuggestionService.FindWithVerbatim(
        singleCharacter,
        singleCharacterCandidates,
        suggestionNow,
        "Google",
        "Google ile ara",
        "Adrese git");
    Check($"single '{singleCharacter}' always creates a safe action", singleResults.Count > 0, true);
    Check($"single '{singleCharacter}' respects result cap",
        singleResults.Count <= OmniboxSuggestionService.DefaultMaximumResults,
        true);
    foreach (var result in singleResults) _ = OmniboxSuggestionPresentation.Prepare(result);
}

var yoCompletion = InlineAutocompleteService.Find("yo", inlineCandidates);
var youCompletion = InlineAutocompleteService.Find("you", inlineCandidates);
var yAfterBackspace = InlineAutocompleteInteractionPolicy.BackspaceText(yoCompletion, yoCompletion.FillText);
Check("yo creates a valid completion", yoCompletion.FillText, "youtube.com");
Check("you creates a valid completion", youCompletion.FillText, "youtube.com");
Check("yo Backspace returns safely to one character", yAfterBackspace, "y");
Check("one character after Backspace has no inline completion",
    InlineAutocompleteService.Find(yAfterBackspace, inlineCandidates).IsInlineCompletionActive,
    false);

var rapidText = "";
foreach (var character in "youtube")
{
    rapidText += character;
    _ = InlineAutocompleteService.Find(rapidText, inlineCandidates);
    Check($"rapid typing '{rapidText}' keeps suggestions capped",
        OmniboxSuggestionService.FindWithVerbatim(
            rapidText,
            singleCharacterCandidates,
            suggestionNow,
            "Google",
            "Google ile ara",
            "Adrese git").Count <= OmniboxSuggestionService.DefaultMaximumResults,
        true);
}
while (rapidText.Length > 0)
{
    rapidText = rapidText[..^1];
    _ = InlineAutocompleteService.Find(rapidText, inlineCandidates);
    _ = OmniboxSuggestionService.FindWithVerbatim(
        rapidText,
        singleCharacterCandidates,
        suggestionNow,
        "Google",
        "Google ile ara",
        "Adrese git");
}
Check("rapid delete reaches a stable empty state", rapidText, "");
Check("suggestion secondary text remains WPF-bindable",
    typeof(OmniboxSuggestion).GetProperty(nameof(OmniboxSuggestion.SecondaryText))?.CanWrite,
    true);

var continuedCompletion = InlineAutocompleteService.Find("yout", inlineCandidates);
Check("typing continues with a new completion", continuedCompletion.InlineSuffix, "ube.com");
var backspacedText = InlineAutocompleteInteractionPolicy.BackspaceText(youtubeCompletion, youtubeCompletion.FillText);
Check("backspace edits user text instead of suffix", backspacedText, "yo");
Check("backspace recomputes completion",
    InlineAutocompleteService.Find(backspacedText, inlineCandidates).FillText,
    "youtube.com");
Check("delete cancels and preserves user text",
    InlineAutocompleteInteractionPolicy.Cancel(youtubeCompletion).FillText,
    "you");
Check("right arrow accepts completion",
    InlineAutocompleteInteractionPolicy.Accept(youtubeCompletion).FillText,
    "youtube.com");
Check("left arrow moves inside user text",
    InlineAutocompleteInteractionPolicy.LeftArrowCaret(youtubeCompletion),
    2);
Check("left arrow cancellation preserves user text",
    InlineAutocompleteInteractionPolicy.Cancel(youtubeCompletion).UserText,
    "you");
Check("escape cancellation preserves user text",
    InlineAutocompleteInteractionPolicy.Cancel(youtubeCompletion).FillText,
    "you");
Check("mouse caret movement cancels to user text",
    InlineAutocompleteInteractionPolicy.Cancel(youtubeCompletion).FillText,
    "you");
Check("typing allows inline completion",
    InlineAutocompleteInteractionPolicy.AllowsCompletion(OmniboxTextChangeOrigin.UserTyping), true);
Check("paste suppresses inline completion",
    InlineAutocompleteInteractionPolicy.AllowsCompletion(OmniboxTextChangeOrigin.Paste), false);
Check("programmatic URL update suppresses inline completion",
    InlineAutocompleteInteractionPolicy.AllowsCompletion(OmniboxTextChangeOrigin.Programmatic), false);
Check("IME composition suppresses inline completion",
    InlineAutocompleteInteractionPolicy.AllowsCompletion(OmniboxTextChangeOrigin.ImeComposition), false);
Check("Ctrl+L keeps select-all policy", AddressBarInteractionPolicy.ShouldSelectAllForShortcut(), true);

var inlineTab = OmniboxTabNavigationPolicy.Resolve(
    popupOpen: true,
    resultCount: 3,
    selectedIndex: -1,
    reverse: false);
Check("Tab with inline popup stays in address bar", inlineTab.Handled, true);
Check("Tab with inline popup does not exit focus", inlineTab.ExitAddressBar, false);
Check("Tab treats the default match as row zero", inlineTab.SelectedIndex, 1);
var stateAfterInlineTab = InlineAutocompleteInteractionPolicy.Cancel(youtubeCompletion);
Check("Tab clears active inline suffix state", stateAfterInlineTab.IsInlineCompletionActive, false);
Check("Tab restores real user text before suggestion traversal", stateAfterInlineTab.FillText, "you");
Check("Tab caret collapses at restored user-text end", stateAfterInlineTab.UserText.Length, 3);

var inlineShiftTab = OmniboxTabNavigationPolicy.Resolve(
    popupOpen: true,
    resultCount: 3,
    selectedIndex: -1,
    reverse: true);
Check("Shift+Tab at popup beginning allows focus traversal", inlineShiftTab.Handled, false);
Check("Shift+Tab exits address bar at popup beginning", inlineShiftTab.ExitAddressBar, true);
Check("Shift+Tab closes popup at popup beginning", inlineShiftTab.ClosePopup, true);

var popupWithoutInlineTab = OmniboxTabNavigationPolicy.Resolve(
    popupOpen: true,
    resultCount: 3,
    selectedIndex: 0,
    reverse: false);
Check("Tab traverses popup even without inline completion", popupWithoutInlineTab.Handled, true);
Check("popup traversal advances one result", popupWithoutInlineTab.SelectedIndex, 1);

var closedPopupTab = OmniboxTabNavigationPolicy.Resolve(
    popupOpen: false,
    resultCount: 0,
    selectedIndex: -1,
    reverse: false);
Check("Tab with closed popup uses normal focus traversal", closedPopupTab.Handled, false);
Check("Tab with closed popup exits address bar", closedPopupTab.ExitAddressBar, true);
Check("Tab with closed popup does not close it again", closedPopupTab.ClosePopup, false);
Check("mouse focus does not reuse Tab-return selection policy",
    AddressBarInteractionPolicy.ShouldSelectAllAfterTabReturn(true, true),
    false);
Check("Shift+Tab return selects the address text",
    AddressBarInteractionPolicy.ShouldSelectAllAfterTabReturn(true, false),
    true);

var afterTabTyping = InlineAutocompleteService.Find(
    stateAfterInlineTab.UserText + "t",
    inlineCandidates);
Check("typing after Tab starts from restored user text", afterTabTyping.UserText, "yout");
Check("typing after Tab can create a fresh completion", afterTabTyping.IsInlineCompletionActive, true);

var tabThenShiftTab = OmniboxTabNavigationPolicy.Resolve(
    popupOpen: true,
    resultCount: 3,
    selectedIndex: inlineTab.SelectedIndex,
    reverse: true);
Check("Tab then Shift+Tab stays in omnibox result traversal", tabThenShiftTab.Handled, true);
Check("Tab then Shift+Tab returns to default result", tabThenShiftTab.SelectedIndex, 0);
var tabAtEnd = OmniboxTabNavigationPolicy.Resolve(
    popupOpen: true,
    resultCount: 3,
    selectedIndex: 2,
    reverse: false);
Check("Tab at final result exits suggestion list", tabAtEnd.ExitAddressBar, true);
Check("Tab at final result closes popup", tabAtEnd.ClosePopup, true);

var verbatimSearchResults = OmniboxSuggestionService.FindWithVerbatim(
    "minecraft sunucu",
    suggestionCandidates,
    suggestionNow,
    "Bing",
    "Bing ile ara",
    "Adrese git");
Check("verbatim search is first", verbatimSearchResults[0].Source, OmniboxSuggestionSource.VerbatimSearch);
Check("verbatim search uses selected engine", verbatimSearchResults[0].Url,
    "https://www.bing.com/search?q=minecraft%20sunucu");
Check("verbatim search exposes action label", verbatimSearchResults[0].SecondaryText, "Bing ile ara");
Check("verbatim search keeps search icon semantics after presentation",
    OmniboxSuggestionPresentation.Prepare(verbatimSearchResults[0]).IsSearchResult,
    true);
var verbatimUrlResults = OmniboxSuggestionService.FindWithVerbatim(
    "github.com",
    suggestionCandidates,
    suggestionNow,
    "Google",
    "Google ile ara",
    "Adrese git");
Check("verbatim URL is first", verbatimUrlResults[0].Source, OmniboxSuggestionSource.VerbatimUrl);
Check("verbatim URL reuses navigation resolver", verbatimUrlResults[0].Url, "https://github.com/");
Check("verbatim URL exposes action label", verbatimUrlResults[0].SecondaryText, "Adrese git");
Check("navigation resolver reports URL intent",
    NavigationResolver.ResolveWithKind("github.com").Kind,
    NavigationInputKind.Url);
Check("navigation resolver reports search intent",
    NavigationResolver.ResolveWithKind("iki kelime").Kind,
    NavigationInputKind.Search);

var verbatimCapped = OmniboxSuggestionService.FindWithVerbatim(
    "result",
    cappedCandidates,
    suggestionNow,
    "Google",
    "Google ile ara",
    "Adrese git");
Check("verbatim is included in maximum result cap", verbatimCapped.Count,
    OmniboxSuggestionService.DefaultMaximumResults);

var selectedOpenTab = new OmniboxSuggestion(
    "Open YouTube",
    "https://www.youtube.com/",
    OmniboxSuggestionSource.OpenTab,
    openTabId);
var selectedDecision = OmniboxEnterResolver.Resolve(
    selectedOpenTab,
    youtubeCompletion,
    "you",
    "Google");
Check("selected popup result wins over inline", selectedDecision.Kind,
    OmniboxEnterActionKind.SelectedSuggestion);
Check("selected open tab target is preserved", selectedDecision.Suggestion?.OpenTabId, openTabId);
var inlineDecision = OmniboxEnterResolver.Resolve(null, youtubeCompletion, "you", "Google");
Check("inline completion wins over raw input", inlineDecision.Kind, OmniboxEnterActionKind.InlineCompletion);
Check("inline Enter uses DestinationUrl", inlineDecision.DestinationUrl, "https://www.youtube.com/");
var rawDecision = OmniboxEnterResolver.Resolve(
    null,
    InlineAutocompleteState.Inactive("iki kelime"),
    "iki kelime",
    "DuckDuckGo");
Check("raw input is the final Enter fallback", rawDecision.Kind, OmniboxEnterActionKind.RawInput);
Check("raw Enter uses selected engine", rawDecision.DestinationUrl,
    "https://duckduckgo.com/?q=iki%20kelime");

var primaryMaximized = WindowMaximizeFix.CalculateMaximizedBounds(
    new WindowPixelRect(0, 0, 1920, 1080),
    new WindowPixelRect(0, 0, 1920, 1040),
    fullScreen: false);
Check("maximize fills primary work-area width", primaryMaximized.Width, 1920);
Check("maximize fills primary work-area height", primaryMaximized.Height, 1040);
Check("maximize has no artificial bottom gap", primaryMaximized.Y + primaryMaximized.Height, 1040);
var leftMonitorMaximized = WindowMaximizeFix.CalculateMaximizedBounds(
    new WindowPixelRect(-2560, 0, 0, 1440),
    new WindowPixelRect(-2520, 0, 0, 1400),
    fullScreen: false);
Check("secondary monitor uses monitor-relative work X", leftMonitorMaximized.X, 40);
Check("secondary monitor uses exact work width", leftMonitorMaximized.Width, 2520);
Check("secondary monitor uses exact work height", leftMonitorMaximized.Height, 1400);
var fullScreenBounds = WindowMaximizeFix.CalculateMaximizedBounds(
    new WindowPixelRect(0, 0, 2560, 1440),
    new WindowPixelRect(0, 0, 2560, 1400),
    fullScreen: true);
Check("full screen still uses full monitor height", fullScreenBounds.Height, 1440);

var historyEntries = Enumerable.Range(0, 20)
    .Select(index => new Fursoy.Browser.App.Models.HistoryEntry
    {
        Title = $"History {index}",
        Url = $"https://history{index}.example/",
        VisitedAt = suggestionNow.AddMinutes(-index),
    })
    .ToList();
var recentHistory = HistoryStore.TakeRecent(historyEntries, 8);
Check("recent history popup cap", recentHistory.Count, 8);
Check("recent history preserves newest first", recentHistory[^1].Title, "History 7");
Check("recent history rejects non-positive cap", HistoryStore.TakeRecent(historyEntries, 0).Count, 0);

var downloadTestRoot = Path.Combine(Path.GetTempPath(), $"fursoy-download-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(downloadTestRoot);
try
{
    var originalPath = Path.Combine(downloadTestRoot, "archive.zip");
    File.WriteAllText(originalPath, "existing");
    Check("duplicate download gets numbered path",
        Path.GetFileName(DownloadPathPolicy.GetUniquePath(downloadTestRoot, "archive.zip")),
        "archive (1).zip");
    var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Path.Combine(downloadTestRoot, "parallel.bin"),
    };
    Check("parallel same-name download respects reserved target",
        Path.GetFileName(DownloadPathPolicy.GetUniquePath(downloadTestRoot, "parallel.bin", reservedPaths)),
        "parallel (1).bin");
    Check("download name strips parent traversal",
        DownloadPathPolicy.SanitizeFileName("..\\folder\\payload.exe"),
        "payload.exe");
    var orphanPartialPath = Path.Combine(downloadTestRoot, "orphan.bin.fsdownload");
    File.WriteAllText(orphanPartialPath, "partial");
    Check("stale partial is never overwritten by a new download",
        Path.GetFileName(DownloadPathPolicy.GetUniquePath(downloadTestRoot, "orphan.bin")),
        "orphan (1).bin");

    var completeDownload = new DownloadEntry
    {
        HistoryId = Guid.NewGuid(),
        FileName = "archive.zip",
        FullPath = originalPath,
        SourceUrl = "https://example.com/archive.zip",
        StartedAt = DateTimeOffset.Now.AddMinutes(-1),
        FinishedAt = DateTimeOffset.Now,
        ReceivedBytes = 8,
        TotalBytes = 8,
        State = DownloadState.Complete,
    };
    var activeDownload = new DownloadEntry
    {
        FileName = "active.bin",
        FullPath = Path.Combine(downloadTestRoot, "active.bin"),
        SourceUrl = "https://example.com/active.bin",
        State = DownloadState.InProgress,
    };
    var historyPath = Path.Combine(downloadTestRoot, "history.json");
    DownloadHistoryStore.Save([completeDownload, activeDownload], historyPath);
    var loadedDownloads = DownloadHistoryStore.Load(historyPath);
    Check("download history stores terminal entries only", loadedDownloads.Count, 1);
    Check("download history preserves source URL", loadedDownloads[0].SourceUrl, completeDownload.SourceUrl);
    Check("download history preserves enum state", loadedDownloads[0].State, DownloadState.Complete);

    File.Delete(originalPath);
    completeDownload.RefreshFileAvailability();
    Check("missing completed file becomes missing", completeDownload.State, DownloadState.Missing);
    Check("missing file cannot be opened", completeDownload.CanOpen, false);

    var downloadsJson = DownloadsPage.Serialize([completeDownload, activeDownload]);
    Check("downloads page state includes filename search source", downloadsJson.Contains("active.bin", StringComparison.Ordinal), true);
    Check("downloads page state includes action identity", downloadsJson.Contains(activeDownload.HistoryId.ToString("D"), StringComparison.Ordinal), true);
    Check("downloads page state includes active flag", downloadsJson.Contains("\"Active\":true", StringComparison.Ordinal), true);

    var handlerSettings = new AppSettingsData
    {
        DownloadDirectory = downloadTestRoot,
        AskWhereToSaveDownloads = false,
    };
    var handlerHistoryPath = Path.Combine(downloadTestRoot, "handler-history.json");
    var handler = new AppDownloadHandler(handlerSettings, handlerHistoryPath);
    var mainWindowXamlPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "Fursoy.Browser.App", "MainWindow.xaml"));
    var mainWindowXaml = File.ReadAllText(mainWindowXamlPath);
    var toolbarXaml = System.Xml.Linq.XDocument.Parse(mainWindowXaml);
    var presentationNs = System.Xml.Linq.XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
    var xamlNs = System.Xml.Linq.XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
    var addressContainer = toolbarXaml.Descendants(presentationNs + "Border")
        .First(element => (string?)element.Attribute(xamlNs + "Name") == "AddressBarContainer");
    var addressActions = addressContainer.Descendants(presentationNs + "StackPanel")
        .FirstOrDefault(element => (string?)element.Attribute("Grid.Column") == "2");
    Check("address actions use a content-sized toolbar group",
        addressActions != null && !mainWindowXaml.Contains("FindButtonColumn", StringComparison.Ordinal), true);
    Check("Find precedes the rightmost bookmark action",
        addressActions?.Elements().Select(element => (string?)element.Attribute(xamlNs + "Name"))
            .Where(name => name != null).SequenceEqual(["FindPageButton", "StarButton"]), true);
    Check("New Tab bookmark action collapses without reserving a column",
        addressActions?.Descendants(presentationNs + "DataTrigger")
            .Any(element => (string?)element.Attribute("Value") == "True" &&
                            element.ToString().Contains("ElementName=AddressBar", StringComparison.Ordinal) &&
                            element.ToString().Contains("Collapsed", StringComparison.Ordinal)), true);

    var iconsXamlPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "Fursoy.Browser.App", "Themes", "Icons.xaml"));
    var iconsXaml = File.ReadAllText(iconsXamlPath);
    var controlsXamlPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "Fursoy.Browser.App", "Themes", "Controls.xaml"));
    var controlsXaml = File.ReadAllText(controlsXamlPath);

    Check("FindPageButton has matched 28x28 size and 2px margin",
        mainWindowXaml.Contains("x:Name=\"FindPageButton\" Width=\"28\" Height=\"28\" Margin=\"2,0,2,0\"", StringComparison.Ordinal), true);
    Check("StarButton uses matching 28x28 omnibox action size",
        controlsXaml.Contains("x:Key=\"StarToggleButtonStyle\"", StringComparison.Ordinal) &&
        controlsXaml.Contains("<Setter Property=\"Width\" Value=\"28\" />", StringComparison.Ordinal) &&
        controlsXaml.Contains("<Setter Property=\"Height\" Value=\"28\" />", StringComparison.Ordinal), true);
    Check("FindPageButton uses vector page and magnifier icon",
        mainWindowXaml.Contains("Data=\"{StaticResource IconFindPage}\"", StringComparison.Ordinal), true);
    Check("FindPanelPopup uses vector chevrons and close icon instead of plain text",
        mainWindowXaml.Contains("Data=\"{StaticResource IconFindChevronUp}\"", StringComparison.Ordinal) &&
        mainWindowXaml.Contains("Data=\"{StaticResource IconFindChevronDown}\"", StringComparison.Ordinal) &&
        mainWindowXaml.Contains("Data=\"{StaticResource IconFindClose}\"", StringComparison.Ordinal), true);
    Check("Icons.xaml defines IconFindPage with outline, fold and magnifier",
        iconsXaml.Contains("x:Key=\"IconFindPage\"", StringComparison.Ordinal) &&
        iconsXaml.Contains("x:Key=\"IconFindChevronUp\"", StringComparison.Ordinal) &&
        iconsXaml.Contains("x:Key=\"IconFindChevronDown\"", StringComparison.Ordinal) &&
        iconsXaml.Contains("x:Key=\"IconFindClose\"", StringComparison.Ordinal), true);
    Check("normal web navigation resolves to url",
        NavigationResolver.ResolveWithKind("https://example.com").Kind == NavigationInputKind.Url, true);
    Check("fursoy:// navigation resolves to internal url",
        NavigationResolver.Resolve("fursoy://downloads").StartsWith("fursoy://downloads", StringComparison.OrdinalIgnoreCase), true);
    Check("download progress read-only bindings are explicitly one-way",
        mainWindowXaml.Contains(
            "Value=\"{Binding ProgressPercent, Mode=OneWay}\" IsIndeterminate=\"{Binding IsIndeterminate, Mode=OneWay}\"",
            StringComparison.Ordinal),
        true);

    var firstTarget = new FakeBeforeDownloadCallback();
    var secondTarget = new FakeBeforeDownloadCallback();
    handler.OnBeforeDownload(null!, null!, new DownloadItem { Id = 101, SuggestedFileName = "same.bin" }, firstTarget);
    handler.OnBeforeDownload(null!, null!, new DownloadItem { Id = 102, SuggestedFileName = "same.bin" }, secondTarget);
    Check("central handler uses partial extension for first target", Path.GetFileName(firstTarget.TargetPath), "same.bin.fsdownload");
    Check("central handler reserves second final name before partial extension", Path.GetFileName(secondTarget.TargetPath), "same (1).bin.fsdownload");

    var rapidTargets = Enumerable.Range(0, 4).Select(_ => new FakeBeforeDownloadCallback()).ToArray();
    Parallel.For(0, rapidTargets.Length, index =>
        handler.OnBeforeDownload(null!, null!, new DownloadItem
        {
            Id = 103 + index,
            SuggestedFileName = "rapid.bin",
        }, rapidTargets[index]));
    var rapidTargetPaths = rapidTargets.Select(item => item.TargetPath).ToArray();
    Check("rapid concurrent download starts all receive targets",
        rapidTargetPaths.All(path => !string.IsNullOrWhiteSpace(path)), true);
    Check("rapid same-file downloads reserve distinct targets",
        rapidTargetPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count(), rapidTargets.Length);
    Check("rapid download callbacks are each completed exactly once",
        rapidTargets.All(item => item.ContinueCount == 1 && item.IsDisposed), true);

    var lifecycleTarget = new FakeBeforeDownloadCallback();
    handler.OnBeforeDownload(null!, null!, new DownloadItem { Id = 150, SuggestedFileName = "lifecycle.bin" }, lifecycleTarget);
    File.WriteAllText(lifecycleTarget.TargetPath, "complete payload");
    handler.OnDownloadUpdated(null!, null!, new DownloadItem
    {
        Id = 150,
        SuggestedFileName = "lifecycle.bin",
        Url = "https://example.com/lifecycle.bin",
        FullPath = lifecycleTarget.TargetPath,
        IsComplete = true,
        ReceivedBytes = 16,
        TotalBytes = 16,
        StartTime = DateTime.Now,
    }, new FakeDownloadItemCallback());
    var lifecycleFinalPath = Path.Combine(downloadTestRoot, "lifecycle.bin");
    Check("completed partial is renamed to final path", File.Exists(lifecycleFinalPath), true);
    Check("completed partial extension is removed", File.Exists(lifecycleTarget.TargetPath), false);
    Check("completed model exposes final path",
        handler.Downloads.Single(item => item.RuntimeId == 150).FullPath, lifecycleFinalPath);

    var cancelledTarget = new FakeBeforeDownloadCallback();
    var cancelledCallback = new FakeDownloadItemCallback();
    handler.OnBeforeDownload(null!, null!, new DownloadItem { Id = 151, SuggestedFileName = "cancel-me.bin" }, cancelledTarget);
    File.WriteAllText(cancelledTarget.TargetPath, "partial payload");
    handler.OnDownloadUpdated(null!, null!, new DownloadItem
    {
        Id = 151,
        SuggestedFileName = "cancel-me.bin",
        Url = "https://example.com/cancel-me.bin",
        FullPath = cancelledTarget.TargetPath,
        IsInProgress = true,
        ReceivedBytes = 7,
        TotalBytes = 20,
        StartTime = DateTime.Now,
    }, cancelledCallback);
    var cancelEntry = handler.Downloads.Single(item => item.RuntimeId == 151);
    Check("cancel request reaches partial download callback", handler.Cancel(cancelEntry.HistoryId), true);
    handler.OnDownloadUpdated(null!, null!, new DownloadItem
    {
        Id = 151,
        SuggestedFileName = "cancel-me.bin",
        Url = "https://example.com/cancel-me.bin",
        FullPath = cancelledTarget.TargetPath,
        IsCancelled = true,
        ReceivedBytes = 7,
        TotalBytes = 20,
        StartTime = DateTime.Now,
    }, cancelledCallback);
    Check("cancelled partial file is cleaned up", File.Exists(cancelledTarget.TargetPath), false);
    Check("cancelled partial never creates a final file",
        File.Exists(Path.Combine(downloadTestRoot, "cancel-me.bin")), false);
    handler.OnDownloadUpdated(null!, null!, new DownloadItem
    {
        Id = 151,
        SuggestedFileName = "cancel-me.bin",
        Url = "https://example.com/cancel-me.bin",
        FullPath = cancelledTarget.TargetPath,
        IsCancelled = true,
        ReceivedBytes = 7,
        TotalBytes = 20,
        StartTime = DateTime.Now,
    }, cancelledCallback);
    Check("duplicate terminal update preserves user-cancelled state", cancelEntry.State, DownloadState.Cancelled);

    var failedTarget = new FakeBeforeDownloadCallback();
    handler.OnBeforeDownload(null!, null!, new DownloadItem { Id = 152, SuggestedFileName = "failed.bin" }, failedTarget);
    File.WriteAllText(failedTarget.TargetPath, "failed partial");
    handler.OnDownloadUpdated(null!, null!, new DownloadItem
    {
        Id = 152,
        SuggestedFileName = "failed.bin",
        Url = "https://example.com/failed.bin",
        FullPath = failedTarget.TargetPath,
        IsCancelled = true,
        ReceivedBytes = 5,
        TotalBytes = 20,
        StartTime = DateTime.Now,
    }, new FakeDownloadItemCallback());
    var failedEntry = handler.Downloads.Single(item => item.RuntimeId == 152);
    Check("non-user interruption becomes failed", failedEntry.State, DownloadState.Failed);
    Check("failed partial file is cleaned up", File.Exists(failedTarget.TargetPath), false);
    Check("failed download remains retryable by source URL",
        handler.GetRetryUrl(failedEntry.HistoryId), "https://example.com/failed.bin");

    var pausedTarget = new FakeBeforeDownloadCallback();
    var pausedCallback = new FakeDownloadItemCallback();
    handler.OnBeforeDownload(null!, null!, new DownloadItem { Id = 153, SuggestedFileName = "pause.bin" }, pausedTarget);
    File.WriteAllText(pausedTarget.TargetPath, "paused partial");
    handler.OnDownloadUpdated(null!, null!, new DownloadItem
    {
        Id = 153,
        SuggestedFileName = "pause.bin",
        Url = "https://example.com/pause.bin",
        FullPath = pausedTarget.TargetPath,
        IsInProgress = true,
        ReceivedBytes = 6,
        TotalBytes = 20,
        StartTime = DateTime.Now,
    }, pausedCallback);
    var pausedEntry = handler.Downloads.Single(item => item.RuntimeId == 153);
    Check("partial download can pause", handler.Pause(pausedEntry.HistoryId), true);
    Check("pause keeps partial file in place", File.Exists(pausedTarget.TargetPath), true);
    Check("partial download can resume", handler.Resume(pausedEntry.HistoryId), true);
    Check("resume keeps using partial file", File.Exists(pausedTarget.TargetPath), true);

    var startAnimationSignalCount = 0;
    handler.DownloadStarted += (_, _) => startAnimationSignalCount++;
    handler.OnDownloadUpdated(null!, null!, new DownloadItem
    {
        Id = 160,
        SuggestedFileName = "animated.bin",
        Url = "https://example.com/animated.bin",
        FullPath = Path.Combine(downloadTestRoot, "animated.bin.fsdownload"),
        IsInProgress = true,
        ReceivedBytes = 1,
        TotalBytes = 10,
        StartTime = DateTime.Now,
    }, new FakeDownloadItemCallback());
    handler.OnDownloadUpdated(null!, null!, new DownloadItem
    {
        Id = 160,
        SuggestedFileName = "animated.bin",
        Url = "https://example.com/animated.bin",
        FullPath = Path.Combine(downloadTestRoot, "animated.bin.fsdownload"),
        IsInProgress = true,
        ReceivedBytes = 2,
        TotalBytes = 10,
        StartTime = DateTime.Now,
    }, new FakeDownloadItemCallback());
    Check("download started signal fires once rather than for every progress update", startAnimationSignalCount, 1);
    handler.OnDownloadUpdated(null!, null!, new DownloadItem
    {
        Id = 161,
        SuggestedFileName = "another.bin",
        Url = "https://example.com/another.bin",
        FullPath = Path.Combine(downloadTestRoot, "another.bin.fsdownload"),
        IsInProgress = true,
        ReceivedBytes = 1,
        TotalBytes = 10,
        StartTime = DateTime.Now,
    }, new FakeDownloadItemCallback());
    Check("another new download produces another start signal", startAnimationSignalCount, 2);

    var concurrentUpdateIds = Enumerable.Range(401, 4).ToArray();
    using var concurrentUpdatesFlushed = new ManualResetEventSlim();
    handler.DownloadsChanged += (_, _) =>
    {
        if (concurrentUpdateIds.All(id => handler.Downloads.Any(item => item.RuntimeId == id)))
            concurrentUpdatesFlushed.Set();
    };
    Parallel.ForEach(concurrentUpdateIds, id =>
        handler.OnDownloadUpdated(null!, null!, new DownloadItem
        {
            Id = id,
            SuggestedFileName = "simultaneous.bin",
            Url = $"https://example.com/download/{id}",
            FullPath = rapidTargetPaths[id - concurrentUpdateIds[0]],
            IsInProgress = true,
            ReceivedBytes = id - concurrentUpdateIds[0],
            TotalBytes = 100,
            CurrentSpeed = 10,
            StartTime = DateTime.Now,
        }, new FakeDownloadItemCallback()));
    Check("simultaneous CEF download updates are all retained",
        concurrentUpdatesFlushed.Wait(2000), true);
    Check("simultaneous CEF download updates create distinct models",
        handler.Downloads.Count(item => concurrentUpdateIds.Contains(item.RuntimeId)), concurrentUpdateIds.Length);

    var runtimeCallbacks = Enumerable.Range(0, 3).Select(_ => new FakeDownloadItemCallback()).ToArray();
    for (var index = 0; index < runtimeCallbacks.Length; index++)
    {
        handler.OnDownloadUpdated(null!, null!, new DownloadItem
        {
            Id = 201 + index,
            SuggestedFileName = $"parallel-{index}.bin",
            Url = $"https://example.com/parallel-{index}.bin",
            FullPath = Path.Combine(downloadTestRoot, $"parallel-{index}.bin"),
            IsInProgress = true,
            ReceivedBytes = index * 10,
            TotalBytes = 100,
            CurrentSpeed = 20,
            StartTime = DateTime.Now,
        }, runtimeCallbacks[index]);
    }
    Check("three CEF updates coalesce into central collection",
        SpinWait.SpinUntil(() => Enumerable.Range(201, 3)
            .All(id => handler.Downloads.Any(item => item.RuntimeId == id)), 2000), true);
    var controlledDownload = handler.Downloads.Single(item => item.RuntimeId == 201);
    Check("pause action reaches CEF callback", handler.Pause(controlledDownload.HistoryId), true);
    Check("pause callback invoked exactly once", runtimeCallbacks[0].PauseCount, 1);
    Check("paused state reflected centrally", controlledDownload.State, DownloadState.Paused);
    Check("resume action reaches CEF callback", handler.Resume(controlledDownload.HistoryId), true);
    Check("resume callback invoked exactly once", runtimeCallbacks[0].ResumeCount, 1);
    Check("cancel action reaches CEF callback", handler.Cancel(controlledDownload.HistoryId), true);
    Check("cancel callback invoked exactly once", runtimeCallbacks[0].CancelCount, 1);
    Check("cancelled state is persisted model state", controlledDownload.State, DownloadState.Cancelled);

    var completedPath = Path.Combine(downloadTestRoot, "completed.txt");
    File.WriteAllText(completedPath, "done");
    handler.OnDownloadUpdated(null!, null!, new DownloadItem
    {
        Id = 301,
        SuggestedFileName = "completed.txt",
        Url = "https://example.com/completed.txt",
        FullPath = completedPath,
        IsComplete = true,
        ReceivedBytes = 4,
        TotalBytes = 4,
        StartTime = DateTime.Now.AddSeconds(-1),
    }, new FakeDownloadItemCallback());
    Check("completed CEF update reaches central history",
        SpinWait.SpinUntil(() => handler.Downloads.Any(item => item.RuntimeId == 301), 2000), true);
    var completedRuntime = handler.Downloads.Single(item => item.RuntimeId == 301);
    Check("completed download can be removed from history", handler.Remove(completedRuntime.HistoryId), true);
    Check("history removal never deletes physical file", File.Exists(completedPath), true);

    handler.OnDownloadUpdated(null!, null!, new DownloadItem
    {
        Id = 302,
        SuggestedFileName = "interrupted.bin",
        Url = "https://example.com/interrupted.bin",
        FullPath = Path.Combine(downloadTestRoot, "interrupted.bin"),
        IsCancelled = true,
        StartTime = DateTime.Now,
    }, new FakeDownloadItemCallback());
    Check("non-user CEF interruption is classified as failed",
        SpinWait.SpinUntil(() => handler.Downloads.Any(item => item.RuntimeId == 302 && item.State == DownloadState.Failed), 2000),
        true);
}
finally
{
    if (Directory.Exists(downloadTestRoot)) Directory.Delete(downloadTestRoot, recursive: true);
}

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine($"{checkCount}/{checkCount} browser stability checks passed.");
return 0;

void Check<T>(string name, T actual, T expected)
{
    checkCount++;
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        failures.Add($"FAIL {name}: expected '{expected}', got '{actual}'");
    }
}

sealed class FakeBeforeDownloadCallback : IBeforeDownloadCallback
{
    public string TargetPath { get; private set; } = string.Empty;
    public int ContinueCount { get; private set; }
    public bool IsDisposed { get; private set; }
    public void Continue(string downloadPath, bool showDialog)
    {
        TargetPath = downloadPath;
        ContinueCount++;
    }
    public void Dispose() => IsDisposed = true;
}

sealed class FakeDownloadItemCallback : IDownloadItemCallback
{
    public int PauseCount { get; private set; }
    public int ResumeCount { get; private set; }
    public int CancelCount { get; private set; }
    public bool IsDisposed { get; private set; }
    public void Pause() => PauseCount++;
    public void Resume() => ResumeCount++;
    public void Cancel() => CancelCount++;
    public void Dispose() => IsDisposed = true;
}
