using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Fursoy.Browser.App.Models;

namespace Fursoy.Browser.App.Services;

public class BookmarkStore
{
    private readonly string _filePath;

    public ObservableCollection<Bookmark> Bookmarks { get; } = new();

    public BookmarkStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FursoyBrowser");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "bookmarks.json");
        Load();
    }

    public bool Contains(string url) => Bookmarks.Any(b => b.Url == url);

    public void Toggle(string url, string title)
    {
        var existing = Bookmarks.FirstOrDefault(b => b.Url == url);
        if (existing != null)
        {
            Bookmarks.Remove(existing);
        }
        else
        {
            Bookmarks.Insert(0, new Bookmark { Url = url, Title = string.IsNullOrWhiteSpace(title) ? url : title });
        }

        Save();
    }

    public void Remove(Bookmark bookmark)
    {
        Bookmarks.Remove(bookmark);
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var items = JsonSerializer.Deserialize<List<Bookmark>>(json);
            if (items == null) return;
            foreach (var item in items) Bookmarks.Add(item);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Bozuk/okunamayan dosya durumunda sıfırdan başla; kalıcı veri kaybı riski
            // yaratmamak için burada dosyayı silmiyoruz, bir sonraki Save() üzerine yazacak.
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(Bookmarks.ToList(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
