namespace Fursoy.Browser.App.Models;

public class HistoryEntry
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime VisitedAt { get; set; }
}
