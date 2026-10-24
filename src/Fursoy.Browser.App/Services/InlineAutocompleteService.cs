namespace Fursoy.Browser.App.Services;

public sealed record InlineAutocompleteState(
    string UserText,
    string InlineSuffix,
    string FillText,
    string DestinationUrl,
    OmniboxSuggestionSource? CandidateSource,
    bool IsInlineCompletionActive)
{
    public static InlineAutocompleteState Inactive(string userText = "") =>
        new(userText, "", userText, "", null, false);
}

public static class InlineAutocompleteService
{
    public static InlineAutocompleteState Find(
        string userText,
        IEnumerable<OmniboxSuggestionCandidate> candidates)
    {
        if (!IsEligibleInput(userText)) return InlineAutocompleteState.Inactive(userText);

        var match = candidates
            .Where(candidate => candidate.Source is OmniboxSuggestionSource.History or OmniboxSuggestionSource.Bookmark)
            .Select(candidate => CreateMatch(userText, candidate))
            .Where(candidate => candidate != null)
            .Select(candidate => candidate!)
            .GroupBy(candidate => candidate.DestinationUrl, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(candidate => SourcePriority(candidate.Source))
                .ThenByDescending(candidate => candidate.LastUsedUtc ?? DateTime.MinValue)
                .ThenBy(candidate => candidate.SourceOrder)
                .First())
            .OrderBy(candidate => candidate.FillText.Length)
            .ThenByDescending(candidate => SourcePriority(candidate.Source))
            .ThenByDescending(candidate => candidate.LastUsedUtc ?? DateTime.MinValue)
            .ThenBy(candidate => candidate.SourceOrder)
            .FirstOrDefault();

        if (match == null) return InlineAutocompleteState.Inactive(userText);

        var fillText = userText + match.FillText[userText.Length..];
        return new InlineAutocompleteState(
            userText,
            fillText[userText.Length..],
            fillText,
            match.DestinationUrl,
            match.Source,
            true);
    }

    private static bool IsEligibleInput(string userText) =>
        userText.Length >= 2 &&
        userText.Equals(userText.Trim(), StringComparison.Ordinal) &&
        !userText.Any(char.IsWhiteSpace);

    private static CandidateMatch? CreateMatch(string userText, OmniboxSuggestionCandidate candidate)
    {
        if (!Uri.TryCreate(candidate.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        var fillText = CreateFillText(userText, uri);
        if (fillText.Length <= userText.Length ||
            !fillText.StartsWith(userText, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new CandidateMatch(
            fillText,
            uri.AbsoluteUri,
            candidate.Source,
            candidate.LastUsedUtc,
            candidate.SourceOrder);
    }

    private static string CreateFillText(string userText, Uri uri)
    {
        var host = uri.IdnHost;
        if (!uri.IsDefaultPort) host += $":{uri.Port}";

        var path = uri.PathAndQuery + uri.Fragment;
        if (path == "/") path = "";

        if (userText.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            userText.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            var schemeSeparator = userText.IndexOf("://", StringComparison.Ordinal) + 3;
            var typedHostStartsWithWww = userText[schemeSeparator..]
                .StartsWith("www.", StringComparison.OrdinalIgnoreCase);
            if (!typedHostStartsWithWww && host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                host = host[4..];
            return $"{uri.Scheme}://{host}{path}";
        }

        if (userText.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            return host + path;

        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];

        return host + path;
    }

    private static int SourcePriority(OmniboxSuggestionSource source) => source switch
    {
        OmniboxSuggestionSource.Bookmark => 10,
        OmniboxSuggestionSource.History => 5,
        _ => 0,
    };

    private sealed record CandidateMatch(
        string FillText,
        string DestinationUrl,
        OmniboxSuggestionSource Source,
        DateTime? LastUsedUtc,
        int SourceOrder);
}

public enum OmniboxTextChangeOrigin
{
    UserTyping,
    Backspace,
    Paste,
    Programmatic,
    ImeComposition,
}

public static class InlineAutocompleteInteractionPolicy
{
    public static bool AllowsCompletion(OmniboxTextChangeOrigin origin) =>
        origin is OmniboxTextChangeOrigin.UserTyping or OmniboxTextChangeOrigin.Backspace;

    public static string BackspaceText(InlineAutocompleteState state, string displayedText)
    {
        var userText = state.IsInlineCompletionActive ? state.UserText : displayedText;
        return userText.Length == 0 ? "" : userText[..^1];
    }

    public static InlineAutocompleteState Cancel(InlineAutocompleteState state) =>
        InlineAutocompleteState.Inactive(state.IsInlineCompletionActive ? state.UserText : state.FillText);

    public static InlineAutocompleteState Accept(InlineAutocompleteState state) =>
        InlineAutocompleteState.Inactive(state.FillText);

    public static int LeftArrowCaret(InlineAutocompleteState state) =>
        Math.Max(0, state.UserText.Length - 1);
}

public sealed record OmniboxTabNavigationDecision(
    bool Handled,
    bool ExitAddressBar,
    bool ClosePopup,
    int SelectedIndex);

public static class OmniboxTabNavigationPolicy
{
    public static OmniboxTabNavigationDecision Resolve(
        bool popupOpen,
        int resultCount,
        int selectedIndex,
        bool reverse)
    {
        if (!popupOpen || resultCount <= 0)
            return new OmniboxTabNavigationDecision(false, true, false, -1);

        // Chrome'un popup modelinde ilk/default satır başlangıçta seçilidir.
        // WPF ListBox'ımız bunu görsel olarak -1 tutuyor; Tab hesabında aynı
        // başlangıç durumunu satır 0 olarak yorumlarız.
        var currentIndex = selectedIndex < 0 ? 0 : Math.Min(selectedIndex, resultCount - 1);
        var nextIndex = reverse ? currentIndex - 1 : currentIndex + 1;
        if (nextIndex < 0 || nextIndex >= resultCount)
            return new OmniboxTabNavigationDecision(false, true, true, -1);

        return new OmniboxTabNavigationDecision(true, false, false, nextIndex);
    }
}

public enum OmniboxEnterActionKind
{
    None,
    SelectedSuggestion,
    InlineCompletion,
    RawInput,
}

public sealed record OmniboxEnterDecision(
    OmniboxEnterActionKind Kind,
    string? DestinationUrl,
    OmniboxSuggestion? Suggestion = null);

public static class OmniboxEnterResolver
{
    public static OmniboxEnterDecision Resolve(
        OmniboxSuggestion? selectedSuggestion,
        InlineAutocompleteState inlineState,
        string userText,
        string searchEngine)
    {
        if (selectedSuggestion != null)
            return new OmniboxEnterDecision(
                OmniboxEnterActionKind.SelectedSuggestion,
                selectedSuggestion.Url,
                selectedSuggestion);

        if (inlineState.IsInlineCompletionActive &&
            !string.IsNullOrWhiteSpace(inlineState.DestinationUrl))
        {
            return new OmniboxEnterDecision(
                OmniboxEnterActionKind.InlineCompletion,
                inlineState.DestinationUrl);
        }

        userText = userText.Trim();
        return userText.Length == 0
            ? new OmniboxEnterDecision(OmniboxEnterActionKind.None, null)
            : new OmniboxEnterDecision(
                OmniboxEnterActionKind.RawInput,
                NavigationResolver.Resolve(userText, searchEngine));
    }
}
