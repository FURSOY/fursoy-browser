using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Text;
using Fursoy.Browser.App.Models;
using Fursoy.Browser.App.Resources;

namespace Fursoy.Browser.App.Services;

public static class NewTabPage
{
    public static string BuildHtml(ObservableCollection<Bookmark> bookmarks, string searchEngine)
    {
        var shortcuts = new StringBuilder();
        foreach (var bookmark in bookmarks.Take(8))
        {
            var title = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(bookmark.Title) ? bookmark.Url : bookmark.Title);
            var url = WebUtility.HtmlEncode(bookmark.Url);
            shortcuts.Append($"<a class=\"shortcut\" href=\"{url}\">{title}</a>");
        }

        var appTitle = WebUtility.HtmlEncode(Strings.AppTitle);
        var (normalizedSearchEngine, searchAction) = GetSearchConfiguration(searchEngine);
        var placeholder = WebUtility.HtmlEncode(string.Format(
            CultureInfo.CurrentUICulture,
            Strings.NewTabSearchPlaceholderFormat,
            normalizedSearchEngine));
        var newTabTitle = WebUtility.HtmlEncode(Strings.NewTab);
        var language = WebUtility.HtmlEncode(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        var background = AppTheme.GetCssColor("BackgroundColor");
        var surface = AppTheme.GetCssColor("SurfaceAltColor");
        var surfaceHover = AppTheme.GetCssColor("SurfaceHoverColor");
        var border = AppTheme.GetCssColor("BorderColor");
        var accent = AppTheme.GetCssColor("AccentColor");
        var textPrimary = AppTheme.GetCssColor("TextPrimaryColor");
        var textSecondary = AppTheme.GetCssColor("TextSecondaryColor");
        var fontFamily = WebUtility.HtmlEncode(AppTheme.GetFontFamily("UiFontFamily"));

        return BuildBody(
            language, newTabTitle, background, fontFamily, textPrimary, appTitle,
            surface, border, accent, surfaceHover, textSecondary, searchAction,
            placeholder, shortcuts.ToString());
    }

    public static (string EngineName, string Action) GetSearchConfiguration(string searchEngine)
    {
        var normalizedSearchEngine = searchEngine.ToUpperInvariant() switch
        {
            "BING" => "Bing",
            "DUCKDUCKGO" => "DuckDuckGo",
            _ => "Google",
        };
        var searchAction = normalizedSearchEngine switch
        {
            "Bing" => "https://www.bing.com/search",
            "DuckDuckGo" => "https://duckduckgo.com/",
            _ => "https://www.google.com/search",
        };
        return (normalizedSearchEngine, searchAction);
    }

    private static string BuildBody(
        string language, string newTabTitle, string background, string fontFamily,
        string textPrimary, string appTitle, string surface, string border,
        string accent, string surfaceHover, string textSecondary, string searchAction,
        string placeholder, string shortcuts) =>
        $$"""
            <!DOCTYPE html>
            <html lang="{{language}}">
            <head>
            <meta charset="utf-8">
            <title>{{newTabTitle}}</title>
            <style>
                html {
                    width: 100%;
                    height: 100%;
                    background: {{background}};
                }
                body {
                    margin: 0;
                    height: 100vh;
                    min-width: 0;
                    box-sizing: border-box;
                    overflow: hidden;
                    display: flex;
                    flex-direction: column;
                    align-items: center;
                    justify-content: center;
                    background: {{background}};
                    font-family: '{{fontFamily}}', sans-serif;
                    color: {{textPrimary}};
                }
                h1 {
                    font-size: 28px;
                    font-weight: 600;
                    margin: 0 0 28px;
                    letter-spacing: 0.02em;
                }
                form {
                    width: 100%;
                    max-width: 560px;
                    padding: 0 24px;
                    box-sizing: border-box;
                }
                input[type="text"] {
                    width: 100%;
                    box-sizing: border-box;
                    padding: 14px 20px;
                    border-radius: 999px;
                    border: 1px solid {{border}};
                    background: {{surface}};
                    color: {{textPrimary}};
                    font-size: 15px;
                    outline: none;
                }
                input[type="text"]:focus {
                    border-color: {{accent}};
                }
                .shortcuts {
                    display: flex;
                    flex-wrap: wrap;
                    gap: 10px;
                    justify-content: center;
                    max-width: 560px;
                    margin-top: 28px;
                }
                .shortcut {
                    padding: 8px 16px;
                    border-radius: 6px;
                    background: {{surface}};
                    color: {{textSecondary}};
                    text-decoration: none;
                    font-size: 13px;
                }
                .shortcut:hover {
                    background: {{surfaceHover}};
                    color: {{textPrimary}};
                }
                * {
                    scrollbar-width: thin;
                    scrollbar-color: {{textSecondary}} transparent;
                }
                *::-webkit-scrollbar {
                    width: 10px;
                    height: 10px;
                }
                *::-webkit-scrollbar-track {
                    background: transparent;
                }
                *::-webkit-scrollbar-thumb {
                    min-height: 32px;
                    border: 3px solid transparent;
                    border-radius: 999px;
                    background: {{textSecondary}};
                    background-clip: padding-box;
                }
                *::-webkit-scrollbar-thumb:hover {
                    background: {{accent}};
                    background-clip: padding-box;
                }
                @media (max-height: 230px) {
                    body { justify-content: flex-start; padding-top: 14px; }
                    h1, .shortcuts { display: none; }
                }
            </style>
            </head>
            <body>
                <h1>{{appTitle}}</h1>
                <form action="{{searchAction}}" method="GET">
                    <input type="text" name="q" autofocus placeholder="{{placeholder}}">
                </form>
                <div class="shortcuts">{{shortcuts}}</div>
            </body>
            </html>
            """;
}
