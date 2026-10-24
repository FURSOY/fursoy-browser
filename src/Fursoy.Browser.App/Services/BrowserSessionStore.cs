using System.IO;
using System.Text.Json;

namespace Fursoy.Browser.App.Services;

public sealed class BrowserSession
{
    public List<string> Tabs { get; set; } = [];
    public List<string> Titles { get; set; } = [];
    public List<string?> Favicons { get; set; } = [];
    public List<int> PinnedIndices { get; set; } = [];
    public int ActiveIndex { get; set; }
}

public static class BrowserSessionStore
{
    private const int MaxRestoredTabs = 50;
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FursoyBrowser", "session.json");

    public static BrowserSession? Load()
    {
        if (!File.Exists(FilePath)) return null;

        try
        {
            var session = JsonSerializer.Deserialize<BrowserSession>(File.ReadAllText(FilePath));
            if (session == null) return null;
            return Normalize(session);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    public static BrowserSession Normalize(BrowserSession session)
    {
        session.Tabs = (session.Tabs ?? []).Take(MaxRestoredTabs).ToList();
        session.Titles = (session.Titles ?? []).Take(MaxRestoredTabs).ToList();
        session.Favicons = (session.Favicons ?? []).Take(MaxRestoredTabs).ToList();
        session.PinnedIndices = (session.PinnedIndices ?? [])
            .Where(index => index >= 0 && index < session.Tabs.Count)
            .Distinct()
            .ToList();
        session.ActiveIndex = Math.Clamp(session.ActiveIndex, 0, Math.Max(0, session.Tabs.Count - 1));
        return session;
    }

    public static void Save(
        IEnumerable<string> addresses,
        IEnumerable<string> titles,
        IEnumerable<string?> favicons,
        IEnumerable<int> pinnedIndices,
        int activeIndex)
    {
        var session = new BrowserSession
        {
            Tabs = addresses.Take(MaxRestoredTabs).ToList(),
            Titles = titles.Take(MaxRestoredTabs).ToList(),
            Favicons = favicons.Take(MaxRestoredTabs).ToList(),
            PinnedIndices = pinnedIndices.Where(index => index is >= 0 and < MaxRestoredTabs).ToList(),
            ActiveIndex = Math.Max(0, activeIndex),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(session));
    }
}
