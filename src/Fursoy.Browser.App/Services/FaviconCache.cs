using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fursoy.Browser.App.Services;

public static class FaviconCache
{
    private const int MaxIconBytes = 512 * 1024;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FursoyBrowser", "Favicons");
    private static readonly string IndexFilePath = Path.Combine(CacheDirectory, "index.json");
    private static readonly object IndexLock = new();
    private static Dictionary<string, string>? _pageIconIndex;

    public static async Task<string> GetOrCreateAsync(string faviconUrl, string? pageUrl = null)
    {
        if (!Uri.TryCreate(faviconUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return faviconUrl;
        }

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(faviconUrl)));
        var filePath = Path.Combine(CacheDirectory, $"{key}.ico");
        if (File.Exists(filePath))
        {
            RegisterPageIcon(pageUrl, filePath);
            return new Uri(filePath).AbsoluteUri;
        }

        try
        {
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxIconBytes) return faviconUrl;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length is 0 or > MaxIconBytes) return faviconUrl;

            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllBytesAsync(filePath, bytes);
            RegisterPageIcon(pageUrl, filePath);
            return new Uri(filePath).AbsoluteUri;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return faviconUrl;
        }
    }

    /// <summary>
    /// Yalnızca diskte bulunan favicon'u döndürür; hiçbir ağ isteği oluşturmaz.
    /// </summary>
    public static string? TryGetCachedForPage(string pageUrl)
    {
        var origin = GetOrigin(pageUrl);
        if (origin == null) return null;

        lock (IndexLock)
        {
            EnsureIndexLoaded();
            if (!_pageIconIndex!.TryGetValue(origin, out var filePath) || !File.Exists(filePath))
                return null;

            return new Uri(filePath).AbsoluteUri;
        }
    }

    internal static void RememberCachedForPage(string pageUrl, string? cachedFaviconUrl)
    {
        if (!Uri.TryCreate(cachedFaviconUrl, UriKind.Absolute, out var iconUri) ||
            !iconUri.IsFile || !File.Exists(iconUri.LocalPath))
        {
            return;
        }

        RegisterPageIcon(pageUrl, iconUri.LocalPath);
    }

    private static void RegisterPageIcon(string? pageUrl, string filePath)
    {
        var origin = GetOrigin(pageUrl);
        if (origin == null) return;

        lock (IndexLock)
        {
            EnsureIndexLoaded();
            if (_pageIconIndex!.TryGetValue(origin, out var existing) &&
                string.Equals(existing, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _pageIconIndex[origin] = filePath;
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                File.WriteAllText(IndexFilePath, JsonSerializer.Serialize(_pageIconIndex));
            }
            catch (IOException)
            {
                // Favicon görüntülenmesi indeks yazılamadığı için engellenmez.
            }
        }
    }

    private static void EnsureIndexLoaded()
    {
        if (_pageIconIndex != null) return;

        try
        {
            _pageIconIndex = File.Exists(IndexFilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(IndexFilePath))
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _pageIconIndex = null;
        }

        _pageIconIndex ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetOrigin(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/').ToLowerInvariant();
    }
}
