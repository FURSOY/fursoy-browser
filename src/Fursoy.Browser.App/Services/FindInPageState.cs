namespace Fursoy.Browser.App.Services;

/// <summary>Bir sekmenin Chromium find oturumunun pencere tarafındaki görünür durumu.</summary>
public sealed class FindInPageState
{
    private volatile bool _isOpen;

    public bool IsOpen
    {
        get => _isOpen;
        set => _isOpen = value;
    }
    public string Query { get; set; } = string.Empty;
    public int MatchCount { get; private set; }
    public int ActiveOrdinal { get; private set; }
    public int LastIdentifier { get; private set; } = -1;

    public string Counter => $"{ActiveOrdinal}/{MatchCount}";

    public void ResetResults()
    {
        MatchCount = 0;
        ActiveOrdinal = 0;
    }

    public bool ApplyResult(int identifier, int count, int ordinal)
    {
        if (identifier < LastIdentifier) return false;
        LastIdentifier = identifier;
        MatchCount = Math.Max(0, count);
        ActiveOrdinal = MatchCount == 0 ? 0 : Math.Clamp(ordinal, 0, MatchCount);
        return true;
    }
}
