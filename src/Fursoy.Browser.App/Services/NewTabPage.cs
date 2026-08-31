using System.Collections.ObjectModel;
using System.Net;
using System.Text;
using Fursoy.Browser.App.Models;
using Fursoy.Browser.App.Resources;

namespace Fursoy.Browser.App.Services;

public static class NewTabPage
{
    public static string BuildHtml(ObservableCollection<Bookmark> bookmarks)
    {
        var shortcuts = new StringBuilder();
        foreach (var bookmark in bookmarks.Take(8))
        {
            var title = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(bookmark.Title) ? bookmark.Url : bookmark.Title);
            var url = WebUtility.HtmlEncode(bookmark.Url);
            shortcuts.Append($"<a class=\"shortcut\" href=\"{url}\">{title}</a>");
        }

        var appTitle = WebUtility.HtmlEncode(Strings.AppTitle);
        var placeholder = WebUtility.HtmlEncode(Strings.NewTabSearchPlaceholder);
        var newTabTitle = WebUtility.HtmlEncode(Strings.NewTab);

        return $$"""
            <!DOCTYPE html>
            <html lang="tr">
            <head>
            <meta charset="utf-8">
            <title>{{newTabTitle}}</title>
            <style>
                body {
                    margin: 0;
                    height: 100vh;
                    display: flex;
                    flex-direction: column;
                    align-items: center;
                    justify-content: center;
                    background: #000000;
                    font-family: 'Segoe UI', sans-serif;
                    color: #F2F2F2;
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
                }
                input[type="text"] {
                    width: 100%;
                    box-sizing: border-box;
                    padding: 14px 20px;
                    border-radius: 999px;
                    border: 1px solid #2A2B2D;
                    background: #1C1D1F;
                    color: #F2F2F2;
                    font-size: 15px;
                    outline: none;
                }
                input[type="text"]:focus {
                    border-color: #13ABBB;
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
                    background: #151617;
                    color: #9A9C9E;
                    text-decoration: none;
                    font-size: 13px;
                }
                .shortcut:hover {
                    background: #1C1D1F;
                    color: #F2F2F2;
                }
            </style>
            </head>
            <body>
                <h1>{{appTitle}}</h1>
                <form action="https://www.google.com/search" method="GET">
                    <input type="text" name="q" autofocus placeholder="{{placeholder}}">
                </form>
                <div class="shortcuts">{{shortcuts}}</div>
            </body>
            </html>
            """;
    }
}
