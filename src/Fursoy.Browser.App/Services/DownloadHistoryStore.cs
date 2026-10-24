using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fursoy.Browser.App.Models;

namespace Fursoy.Browser.App.Services;

public sealed class DownloadHistoryRecord
{
    public Guid HistoryId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long ReceivedBytes { get; set; }
    public long TotalBytes { get; set; }
    public DownloadState State { get; set; }
}

public static class DownloadHistoryStore
{
    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FursoyBrowser", "downloads.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<DownloadHistoryRecord> Load(string? filePath = null)
    {
        filePath ??= DefaultFilePath;
        if (!File.Exists(filePath)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<DownloadHistoryRecord>>(File.ReadAllText(filePath), Options) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<DownloadEntry> entries, string? filePath = null)
    {
        filePath ??= DefaultFilePath;
        var records = entries
            .Where(entry => !entry.IsActive)
            .Take(500)
            .Select(entry => new DownloadHistoryRecord
            {
                HistoryId = entry.HistoryId,
                FileName = entry.FileName,
                SourceUrl = entry.SourceUrl,
                FullPath = entry.FullPath,
                StartedAt = entry.StartedAt,
                FinishedAt = entry.FinishedAt,
                ReceivedBytes = entry.ReceivedBytes,
                TotalBytes = entry.TotalBytes,
                State = entry.State,
            })
            .ToList();

        var temporaryPath = filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(records, Options));
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
        }
    }
}

public static class DownloadPathPolicy
{
    public const string PartialExtension = ".fsdownload";

    public static string GetDefaultDownloadsFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public static string SanitizeFileName(string? suggestedFileName)
    {
        var name = Path.GetFileName(suggestedFileName);
        if (string.IsNullOrWhiteSpace(name)) name = "download";
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return name;
    }

    public static string GetUniquePath(
        string directory,
        string suggestedFileName,
        IReadOnlySet<string>? reservedPaths = null)
    {
        Directory.CreateDirectory(directory);
        var safeName = SanitizeFileName(suggestedFileName);
        var candidate = Path.Combine(directory, safeName);
        if (IsAvailable(candidate, reservedPaths)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        for (var suffix = 1; suffix < int.MaxValue; suffix++)
        {
            candidate = Path.Combine(directory, $"{stem} ({suffix}){extension}");
            if (IsAvailable(candidate, reservedPaths)) return candidate;
        }

        throw new IOException("A unique download path could not be generated.");
    }

    public static string GetTemporaryPath(string finalPath) => finalPath + PartialExtension;

    public static string GetAvailableTemporaryPath(string finalPath)
    {
        var candidate = GetTemporaryPath(finalPath);
        if (!File.Exists(candidate)) return candidate;

        for (var suffix = 1; suffix < int.MaxValue; suffix++)
        {
            candidate = $"{finalPath}.{suffix}{PartialExtension}";
            if (!File.Exists(candidate)) return candidate;
        }

        throw new IOException("A unique temporary download path could not be generated.");
    }

    public static bool TryFinalize(string temporaryPath, string finalPath, bool overwrite)
    {
        if (!File.Exists(temporaryPath)) return File.Exists(finalPath);
        try
        {
            File.Move(temporaryPath, finalPath, overwrite);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void DeletePartial(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kilitli/kurtarılabilir bir yarım dosyayı zorla silmek yerine yerinde bırak.
        }
    }

    private static bool IsAvailable(string finalPath, IReadOnlySet<string>? reservedPaths) =>
        !File.Exists(finalPath) &&
        !File.Exists(GetTemporaryPath(finalPath)) &&
        reservedPaths?.Contains(finalPath) != true;
}
