namespace Fursoy.Browser.App.Services;

public enum OmniboxSuggestionSource
{
    History,
    Bookmark,
    OpenTab,
    VerbatimSearch,
    VerbatimUrl,
}

public sealed record OmniboxSuggestionCandidate(
    string Title,
    string Url,
    OmniboxSuggestionSource Source,
    DateTime? LastUsedUtc = null,
    Guid? OpenTabId = null,
    int SourceOrder = 0);

public sealed record OmniboxSuggestion(
    string Title,
    string Url,
    OmniboxSuggestionSource Source,
    Guid? OpenTabId,
    bool IsSearchResult = false,
    string? CachedFaviconUrl = null,
    string? ActionLabel = null)
{
    // WPF Run.Text'in varsayılan bağlama modu kaynağa yazmayı deneyebilir.
    // XAML açıkça OneWay kullanır; set edilebilir sunum özelliği de şablonun
    // yeniden düzenlenmesi halinde salt-okunur property crash'ini önler.
    public string SecondaryText { get; set; } = ActionLabel ?? Url;
}

public static class OmniboxSuggestionService
{
    public const int DefaultMaximumResults = 8;

    public static IReadOnlyList<OmniboxSuggestion> Find(
        string query,
        IEnumerable<OmniboxSuggestionCandidate> candidates,
        DateTime nowUtc,
        int maximumResults = DefaultMaximumResults)
    {
        query = query.Trim();
        if (query.Length == 0 || maximumResults <= 0) return [];

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Url))
            .GroupBy(candidate => NormalizeUrl(candidate.Url), StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateRankedResult(group, query, nowUtc))
            .Where(result => result != null)
            .Select(result => result!)
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.LastUsedUtc)
            .ThenBy(result => result.SourceOrder)
            .ThenBy(result => result.Suggestion.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(maximumResults)
            .Select(result => result.Suggestion)
            .ToList();
    }

    public static IReadOnlyList<OmniboxSuggestion> FindWithVerbatim(
        string query,
        IEnumerable<OmniboxSuggestionCandidate> candidates,
        DateTime nowUtc,
        string searchEngine,
        string searchActionLabel,
        string goToAddressLabel,
        int maximumResults = DefaultMaximumResults)
    {
        query = query.Trim();
        if (query.Length == 0 || maximumResults <= 0) return [];

        var resolution = NavigationResolver.ResolveWithKind(query, searchEngine);
        var source = resolution.Kind == NavigationInputKind.Url
            ? OmniboxSuggestionSource.VerbatimUrl
            : OmniboxSuggestionSource.VerbatimSearch;
        var actionLabel = resolution.Kind == NavigationInputKind.Url
            ? goToAddressLabel
            : searchActionLabel;
        var verbatim = new OmniboxSuggestion(
            query,
            resolution.DestinationUrl,
            source,
            null,
            IsSearchResult: resolution.Kind == NavigationInputKind.Search,
            ActionLabel: actionLabel);

        if (maximumResults == 1) return [verbatim];

        return new[] { verbatim }
            .Concat(Find(query, candidates, nowUtc, maximumResults - 1))
            .Take(maximumResults)
            .ToList();
    }

    public static IReadOnlyList<OmniboxSuggestion> FindOnFocus(
        IEnumerable<OmniboxSuggestionCandidate> candidates,
        int maximumResults = DefaultMaximumResults)
    {
        if (maximumResults <= 0) return [];

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Url))
            .GroupBy(candidate => NormalizeUrl(candidate.Url), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(candidate => SourcePriority(candidate.Source))
                .ThenByDescending(candidate => candidate.LastUsedUtc)
                .ThenBy(candidate => candidate.SourceOrder)
                .First())
            .OrderByDescending(candidate => candidate.LastUsedUtc ?? DateTime.MinValue)
            .ThenByDescending(candidate => SourcePriority(candidate.Source))
            .ThenBy(candidate => candidate.SourceOrder)
            .Take(maximumResults)
            .Select(candidate => new OmniboxSuggestion(
                candidate.Title,
                candidate.Url,
                candidate.Source,
                candidate.OpenTabId))
            .ToList();
    }

    private static RankedSuggestion? CreateRankedResult(
        IEnumerable<OmniboxSuggestionCandidate> group,
        string query,
        DateTime nowUtc)
    {
        var matchingCandidates = group
            .Select(candidate => (Candidate: candidate, MatchScore: GetMatchScore(candidate, query)))
            .Where(item => item.MatchScore >= 0)
            .ToList();
        if (matchingCandidates.Count == 0) return null;

        var representative = matchingCandidates
            .OrderByDescending(item => SourcePriority(item.Candidate.Source))
            .ThenByDescending(item => item.Candidate.LastUsedUtc)
            .ThenBy(item => item.Candidate.SourceOrder)
            .First()
            .Candidate;
        var bestMatch = matchingCandidates
            .OrderByDescending(item => item.MatchScore)
            .ThenByDescending(item => item.Candidate.LastUsedUtc)
            .First();
        var lastUsed = matchingCandidates
            .Select(item => item.Candidate.LastUsedUtc)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        var score = bestMatch.MatchScore + SourcePriority(representative.Source) + RecencyScore(lastUsed, nowUtc);

        // Bir yer imi için özel başlık eşleşmiş olsa bile aynı URL açık bir
        // sekmedeyse eylem açık sekmeye geçer; eşleşmenin nedeni olan başlık korunur.
        return new RankedSuggestion(
            new OmniboxSuggestion(
                bestMatch.Candidate.Title,
                representative.Url,
                representative.Source,
                representative.OpenTabId),
            score,
            lastUsed,
            bestMatch.Candidate.SourceOrder);
    }

    private static int GetMatchScore(OmniboxSuggestionCandidate candidate, string query)
    {
        var titleScore = FieldScore(candidate.Title, query, title: true);
        var urlScore = FieldScore(candidate.Url, query, title: false);
        var best = Math.Max(titleScore, urlScore);
        if (best >= 0) return best;

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length <= 1) return -1;
        return tokens.All(token =>
                   candidate.Title.Contains(token, StringComparison.CurrentCultureIgnoreCase) ||
                   candidate.Url.Contains(token, StringComparison.OrdinalIgnoreCase))
            ? 420 + tokens.Length * 12
            : -1;
    }

    private static int FieldScore(string value, string query, bool title)
    {
        if (string.IsNullOrWhiteSpace(value)) return -1;
        var comparison = title
            ? StringComparison.CurrentCultureIgnoreCase
            : StringComparison.OrdinalIgnoreCase;
        if (value.Equals(query, comparison)) return title ? 1000 : 980;
        if (value.StartsWith(query, comparison)) return title ? 860 : 830;

        var index = value.IndexOf(query, comparison);
        if (index < 0) return -1;
        var isWordBoundary = index == 0 || !char.IsLetterOrDigit(value[index - 1]);
        return (title ? 650 : 620) + (isWordBoundary ? 80 : 0) - Math.Min(index, 80);
    }

    private static int RecencyScore(DateTime lastUsedUtc, DateTime nowUtc)
    {
        if (lastUsedUtc == DateTime.MinValue) return 0;
        var age = nowUtc - lastUsedUtc.ToUniversalTime();
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age <= TimeSpan.FromHours(1)) return 90;
        if (age <= TimeSpan.FromDays(1)) return 72;
        if (age <= TimeSpan.FromDays(7)) return 52;
        if (age <= TimeSpan.FromDays(30)) return 32;
        if (age <= TimeSpan.FromDays(365)) return 12;
        return 0;
    }

    private static int SourcePriority(OmniboxSuggestionSource source) => source switch
    {
        OmniboxSuggestionSource.OpenTab => 40,
        OmniboxSuggestionSource.Bookmark => 20,
        _ => 0,
    };

    private static string NormalizeUrl(string url)
    {
        url = url.Trim();
        return url.Length > 1 ? url.TrimEnd('/') : url;
    }

    private sealed record RankedSuggestion(
        OmniboxSuggestion Suggestion,
        int Score,
        DateTime LastUsedUtc,
        int SourceOrder);
}

public static class AddressBarInteractionPolicy
{
    public static bool ShouldSelectAllAfterMouseFocus(
        bool hadKeyboardFocus,
        bool exceededDragThreshold) =>
        !hadKeyboardFocus && !exceededDragThreshold;

    public static bool ShouldSelectAllForShortcut() => true;

    public static bool ShouldSelectAllAfterTabReturn(
        bool exitedByTab,
        bool mouseFocusPending) =>
        exitedByTab && !mouseFocusPending;
}

public static class OmniboxSuggestionPresentation
{
    public static OmniboxSuggestion Prepare(OmniboxSuggestion suggestion)
    {
        var isSearchResult = suggestion.IsSearchResult ||
                             suggestion.Source == OmniboxSuggestionSource.History &&
                             IsSearchResultUrl(suggestion.Url);
        var cachedFavicon = suggestion.Source == OmniboxSuggestionSource.History && !isSearchResult
            ? FaviconCache.TryGetCachedForPage(suggestion.Url)
            : null;

        return suggestion with
        {
            IsSearchResult = isSearchResult,
            CachedFaviconUrl = cachedFavicon,
        };
    }

    public static bool IsSearchResultUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        var host = uri.Host.TrimStart('.');
        var isGoogle = (host.Equals("google.com", StringComparison.OrdinalIgnoreCase) ||
                        host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase)) &&
                       uri.AbsolutePath.Equals("/search", StringComparison.OrdinalIgnoreCase);
        var isBing = (host.Equals("bing.com", StringComparison.OrdinalIgnoreCase) ||
                      host.EndsWith(".bing.com", StringComparison.OrdinalIgnoreCase)) &&
                     uri.AbsolutePath.Equals("/search", StringComparison.OrdinalIgnoreCase);
        var isDuckDuckGo = host.Equals("duckduckgo.com", StringComparison.OrdinalIgnoreCase) ||
                           host.EndsWith(".duckduckgo.com", StringComparison.OrdinalIgnoreCase);

        return (isGoogle || isBing || isDuckDuckGo) && HasQueryParameter(uri.Query, "q");
    }

    private static bool HasQueryParameter(string query, string name) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)[0])
            .Any(key => Uri.UnescapeDataString(key).Equals(name, StringComparison.OrdinalIgnoreCase));
}
