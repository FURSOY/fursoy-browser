using System.Net;

namespace Fursoy.Browser.App.Services;

public enum NavigationInputKind
{
    Url,
    Search,
}

public sealed record NavigationResolution(string DestinationUrl, NavigationInputKind Kind);

public static class NavigationResolver
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        Uri.UriSchemeFtp,
        Uri.UriSchemeFile,
        "about",
        "fursoy",
    };

    public static string Resolve(string input, string searchEngine = "Google")
        => ResolveWithKind(input, searchEngine).DestinationUrl;

    public static NavigationResolution ResolveWithKind(string input, string searchEngine = "Google")
    {
        input = input.Trim();

        if (Uri.TryCreate(input, UriKind.Absolute, out var absolute) &&
            AllowedSchemes.Contains(absolute.Scheme))
        {
            return new NavigationResolution(absolute.AbsoluteUri, NavigationInputKind.Url);
        }

        if (!input.Any(char.IsWhiteSpace) && LooksLikeHost(input) &&
            Uri.TryCreate($"https://{input}", UriKind.Absolute, out var webAddress))
        {
            return new NavigationResolution(webAddress.AbsoluteUri, NavigationInputKind.Url);
        }

        var query = Uri.EscapeDataString(input);
        var destination = searchEngine.ToUpperInvariant() switch
        {
            "BING" => $"https://www.bing.com/search?q={query}",
            "DUCKDUCKGO" => $"https://duckduckgo.com/?q={query}",
            _ => $"https://www.google.com/search?q={query}",
        };
        return new NavigationResolution(destination, NavigationInputKind.Search);
    }

    private static bool LooksLikeHost(string input)
    {
        var host = input.Split('/', '?', '#')[0];
        var hostWithoutPort = host.StartsWith('[')
            ? host.Trim('[', ']').Split(']')[0]
            : host.Split(':')[0];

        return hostWithoutPort.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || IsStrictIpAddress(hostWithoutPort)
               || (hostWithoutPort.Contains('.') &&
                   hostWithoutPort.Any(char.IsLetter) &&
                   !hostWithoutPort.StartsWith('.') &&
                   !hostWithoutPort.EndsWith('.'));
    }

    private static bool IsStrictIpAddress(string value)
    {
        if (value.Contains(':'))
            return IPAddress.TryParse(value, out var ipv6) &&
                   ipv6.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;

        var parts = value.Split('.');
        return parts.Length == 4 && parts.All(part =>
            byte.TryParse(part, out _) && (part == "0" || !part.StartsWith('0')));
    }
}
