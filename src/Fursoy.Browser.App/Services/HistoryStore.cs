using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Fursoy.Browser.App.Models;

namespace Fursoy.Browser.App.Services;

public class HistoryStore
{
    private const int MaxEntries = 500;

    private readonly string _filePath;

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    public HistoryStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FursoyBrowser");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "history.json");
        Load();
    }

    public void Add(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;

        Entries.Insert(0, new HistoryEntry
        {
            Url = url,
            Title = string.IsNullOrWhiteSpace(title) ? url : title,
            VisitedAt = DateTime.Now,
        });

        while (Entries.Count > MaxEntries) Entries.RemoveAt(Entries.Count - 1);
        Save();
    }

    public void Remove(HistoryEntry entry)
    {
        Entries.Remove(entry);
        Save();
    }

    public void Clear()
    {
        Entries.Clear();
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var items = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
            if (items == null) return;
            foreach (var item in items) Entries.Add(item);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Bozuk/okunamayan dosya durumunda sıfırdan başla; bir sonraki
            // Save() üzerine yazacağı için burada dosyayı silmiyoruz.
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(Entries.ToList(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
