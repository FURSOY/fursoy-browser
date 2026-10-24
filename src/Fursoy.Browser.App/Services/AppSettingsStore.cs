using System.IO;
using System.Text.Json;

namespace Fursoy.Browser.App.Services;

public sealed class AppSettingsData
{
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "tr-TR";
    public string SearchEngine { get; set; } = "Google";
    public bool RestoreSession { get; set; } = true;
    public string DownloadDirectory { get; set; } = DownloadPathPolicy.GetDefaultDownloadsFolder();
    public bool AskWhereToSaveDownloads { get; set; }
    public bool ShowFindButtonInToolbar { get; set; } = true;
}

public static class AppSettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FursoyBrowser", "settings.json");

    public static AppSettingsData Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettingsData>(File.ReadAllText(FilePath)) ?? new AppSettingsData()
                : new AppSettingsData();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new AppSettingsData();
        }
    }

    public static void Save(AppSettingsData settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
    }
}
