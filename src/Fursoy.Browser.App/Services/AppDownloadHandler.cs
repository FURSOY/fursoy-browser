using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CefSharp;
using Fursoy.Browser.App.Models;

namespace Fursoy.Browser.App.Services;

public class AppDownloadHandler : IDownloadHandler
{
    private readonly Dictionary<int, DownloadEntry> _byId = new();

    public ObservableCollection<DownloadEntry> Downloads { get; } = new();

    public bool CanDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, string url, string requestMethod) => true;

    public bool OnBeforeDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem,
        IBeforeDownloadCallback callback)
    {
        if (callback.IsDisposed) return true;

        using (callback)
        {
            var downloadsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloadsFolder);
            callback.Continue(Path.Combine(downloadsFolder, downloadItem.SuggestedFileName), showDialog: false);
        }

        return true;
    }

    public void OnDownloadUpdated(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem,
        IDownloadItemCallback callback)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (!_byId.TryGetValue(downloadItem.Id, out var entry))
            {
                entry = new DownloadEntry
                {
                    Id = downloadItem.Id,
                    FileName = downloadItem.SuggestedFileName,
                };
                _byId[downloadItem.Id] = entry;
                Downloads.Insert(0, entry);
            }

            entry.FullPath = downloadItem.FullPath;
            entry.ReceivedBytes = downloadItem.ReceivedBytes;
            entry.TotalBytes = downloadItem.TotalBytes;
            entry.IsComplete = downloadItem.IsComplete;
            entry.IsCancelled = downloadItem.IsCancelled;
        });
    }
}
