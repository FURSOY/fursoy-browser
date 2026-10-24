using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CefSharp;
using Fursoy.Browser.App.Models;
using Microsoft.Win32;

namespace Fursoy.Browser.App.Services;

public sealed class DownloadStartedEventArgs(IWebBrowser? sourceBrowser) : EventArgs
{
    public IWebBrowser? SourceBrowser { get; } = sourceBrowser;
}

/// <summary>
/// Tüm pencerelerin paylaştığı merkezi download servisi. CEF callback'leri
/// yalnızca çalışma zamanında tutulur; Downloads koleksiyonu kalıcı ve UI'a
/// uygun metadata içerir.
/// </summary>
public sealed class AppDownloadHandler : IDownloadHandler
{
    private sealed record DownloadTarget(string FinalPath, string TemporaryPath, bool OverwriteFinal);

    private sealed record DownloadSnapshot(
        int RuntimeId,
        IWebBrowser? SourceBrowser,
        string FileName,
        string SourceUrl,
        string FullPath,
        DateTimeOffset StartedAt,
        long ReceivedBytes,
        long TotalBytes,
        long CurrentSpeed,
        bool IsInProgress,
        bool IsComplete,
        bool IsCancelled);

    private readonly AppSettingsData _settings;
    private readonly string _historyPath;
    private readonly Dictionary<int, DownloadEntry> _byRuntimeId = new();
    private readonly ConcurrentDictionary<int, DownloadSnapshot> _pendingUpdates = new();
    private readonly ConcurrentDictionary<int, IDownloadItemCallback> _callbacks = new();
    private readonly ConcurrentDictionary<int, DownloadTarget> _targetsByRuntimeId = new();
    private readonly HashSet<int> _pausedIds = [];
    private readonly HashSet<int> _userCancelledIds = [];
    private readonly HashSet<string> _reservedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _headlessFlushLock = new();
    private int _flushScheduled;

    public ObservableCollection<DownloadEntry> Downloads { get; } = [];
    public ObservableCollection<DownloadEntry> RecentDownloads { get; } = [];

    public event EventHandler? DownloadsChanged;
    public event EventHandler<DownloadStartedEventArgs>? DownloadStarted;

    public AppDownloadHandler(AppSettingsData settings, string? historyPath = null)
    {
        _settings = settings;
        _historyPath = historyPath ?? DownloadHistoryStore.DefaultFilePath;
        foreach (var record in DownloadHistoryStore.Load(_historyPath)
                     .OrderByDescending(item => item.StartedAt)
                     .Take(500))
        {
            var entry = new DownloadEntry
            {
                HistoryId = record.HistoryId == Guid.Empty ? Guid.NewGuid() : record.HistoryId,
                FileName = record.FileName ?? string.Empty,
                SourceUrl = record.SourceUrl ?? string.Empty,
                FullPath = record.FullPath ?? string.Empty,
                StartedAt = record.StartedAt,
                FinishedAt = record.FinishedAt,
                ReceivedBytes = record.ReceivedBytes,
                TotalBytes = record.TotalBytes,
                State = record.State is DownloadState.InProgress or DownloadState.Paused
                    ? DownloadState.Failed
                    : record.State,
            };
            entry.RefreshFileAvailability();
            Downloads.Add(entry);
        }

        RebuildRecentDownloads();
    }

    public bool CanDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, string url, string requestMethod) => true;

    public bool OnBeforeDownload(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        DownloadItem downloadItem,
        IBeforeDownloadCallback callback)
    {
        if (callback.IsDisposed) return true;

        var directory = string.IsNullOrWhiteSpace(_settings.DownloadDirectory)
            ? DownloadPathPolicy.GetDefaultDownloadsFolder()
            : _settings.DownloadDirectory;
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            directory = DownloadPathPolicy.GetDefaultDownloadsFolder();
            Directory.CreateDirectory(directory);
        }
        var suggestedName = DownloadPathPolicy.SanitizeFileName(downloadItem.SuggestedFileName);

        if (!_settings.AskWhereToSaveDownloads)
        {
            var target = ReserveUniqueTarget(directory, suggestedName);
            _targetsByRuntimeId[downloadItem.Id] = target;
            using (callback)
                callback.Continue(target.TemporaryPath, showDialog: false);
            return true;
        }

        var application = Application.Current;
        var dispatcher = application?.Dispatcher;
        if (dispatcher == null)
        {
            var target = ReserveUniqueTarget(directory, suggestedName);
            _targetsByRuntimeId[downloadItem.Id] = target;
            using (callback)
                callback.Continue(target.TemporaryPath, showDialog: false);
            return true;
        }

        dispatcher.BeginInvoke(() =>
        {
            using (callback)
            {
                var dialog = new SaveFileDialog
                {
                    InitialDirectory = directory,
                    FileName = suggestedName,
                    AddExtension = true,
                    OverwritePrompt = true,
                    CheckPathExists = true,
                    Title = Resources.Strings.DownloadSaveDialogTitle,
                };
                if (dialog.ShowDialog(application!.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)) == true)
                {
                    var target = ReserveSelectedTarget(dialog.FileName);
                    _targetsByRuntimeId[downloadItem.Id] = target;
                    callback.Continue(target.TemporaryPath, showDialog: false);
                }
            }
        });
        return true;
    }

    public void OnDownloadUpdated(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        DownloadItem downloadItem,
        IDownloadItemCallback callback)
    {
        if (!callback.IsDisposed) _callbacks[downloadItem.Id] = callback;
        var runtimeTarget = _targetsByRuntimeId.GetValueOrDefault(downloadItem.Id);
        var fullPath = runtimeTarget?.FinalPath ?? NormalizeFinalPath(downloadItem.FullPath ?? string.Empty);
        var displayName = string.IsNullOrWhiteSpace(fullPath)
            ? DownloadPathPolicy.SanitizeFileName(downloadItem.SuggestedFileName)
            : Path.GetFileName(fullPath);
        _pendingUpdates[downloadItem.Id] = new DownloadSnapshot(
            downloadItem.Id,
            chromiumWebBrowser,
            displayName,
            downloadItem.Url ?? string.Empty,
            fullPath,
            downloadItem.StartTime is { } startTime ? new DateTimeOffset(startTime) : DateTimeOffset.Now,
            downloadItem.ReceivedBytes,
            downloadItem.TotalBytes,
            downloadItem.CurrentSpeed,
            downloadItem.IsInProgress,
            downloadItem.IsComplete,
            downloadItem.IsCancelled);
        ScheduleFlush(downloadItem.IsComplete || downloadItem.IsCancelled ? 0 : 100);
    }

    public bool Pause(Guid historyId) => InvokeRuntimeAction(historyId, callback => callback.Pause(), DownloadState.Paused);

    public bool Resume(Guid historyId) => InvokeRuntimeAction(historyId, callback => callback.Resume(), DownloadState.InProgress);

    public bool Cancel(Guid historyId)
    {
        var entry = Downloads.FirstOrDefault(item => item.HistoryId == historyId);
        if (entry == null || !entry.CanCancel || !_callbacks.TryGetValue(entry.RuntimeId, out var callback) || callback.IsDisposed)
            return false;
        _userCancelledIds.Add(entry.RuntimeId);
        callback.Cancel();
        UpdateEntryState(entry, DownloadState.Cancelled);
        return true;
    }

    public string? GetRetryUrl(Guid historyId) => Downloads
        .FirstOrDefault(item => item.HistoryId == historyId && item.CanRetry)?.SourceUrl;

    public bool Remove(Guid historyId)
    {
        var entry = Downloads.FirstOrDefault(item => item.HistoryId == historyId);
        if (entry == null || entry.IsActive) return false;
        Downloads.Remove(entry);
        RebuildRecentDownloads();
        PersistAndNotify();
        return true;
    }

    public void ClearHistory()
    {
        foreach (var entry in Downloads.Where(item => !item.IsActive).ToList()) Downloads.Remove(entry);
        RebuildRecentDownloads();
        PersistAndNotify();
    }

    public void RefreshFileAvailability()
    {
        foreach (var entry in Downloads) entry.RefreshFileAvailability();
        PersistAndNotify();
    }

    public int ActiveCount => Downloads.Count(item => item.IsActive);
    public bool HasIndeterminateProgress => Downloads.Any(item => item.IsActive && !item.HasKnownSize);

    public double AggregateProgress
    {
        get
        {
            var active = Downloads.Where(item => item.IsActive).ToList();
            if (active.Count == 0) return 0;
            var known = active.Where(item => item.TotalBytes > 0).ToList();
            return known.Count == 0 ? 0 : known.Average(item => item.ProgressPercent);
        }
    }

    private bool InvokeRuntimeAction(Guid historyId, Action<IDownloadItemCallback> action, DownloadState targetState)
    {
        var entry = Downloads.FirstOrDefault(item => item.HistoryId == historyId);
        if (entry == null || !_callbacks.TryGetValue(entry.RuntimeId, out var callback) || callback.IsDisposed) return false;
        if (targetState == DownloadState.Paused && !entry.CanPause ||
            targetState == DownloadState.InProgress && !entry.CanResume) return false;

        action(callback);
        if (targetState == DownloadState.Paused) _pausedIds.Add(entry.RuntimeId);
        else _pausedIds.Remove(entry.RuntimeId);
        UpdateEntryState(entry, targetState);
        return true;
    }

    private void ScheduleFlush(int delayMilliseconds)
    {
        if (Interlocked.Exchange(ref _flushScheduled, 1) != 0) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            lock (_headlessFlushLock) FlushPendingUpdates();
            return;
        }

        _ = Task.Run(async () =>
        {
            if (delayMilliseconds > 0) await Task.Delay(delayMilliseconds).ConfigureAwait(false);
            await dispatcher.InvokeAsync(FlushPendingUpdates);
        });
    }

    private void FlushPendingUpdates()
    {
        Interlocked.Exchange(ref _flushScheduled, 0);
        var terminalChange = false;
        var startedBrowsers = new HashSet<IWebBrowser?>(ReferenceEqualityComparer.Instance);
        foreach (var pair in _pendingUpdates.ToArray())
        {
            if (!_pendingUpdates.TryRemove(pair.Key, out var snapshot)) continue;
            if (!_byRuntimeId.TryGetValue(snapshot.RuntimeId, out var entry))
            {
                entry = new DownloadEntry
                {
                    RuntimeId = snapshot.RuntimeId,
                    FileName = snapshot.FileName,
                    SourceUrl = snapshot.SourceUrl,
                    FullPath = snapshot.FullPath,
                    StartedAt = snapshot.StartedAt,
                    State = DownloadState.InProgress,
                };
                _byRuntimeId[snapshot.RuntimeId] = entry;
                Downloads.Insert(0, entry);
                startedBrowsers.Add(snapshot.SourceBrowser);
            }

            entry.FileName = snapshot.FileName;
            entry.SourceUrl = snapshot.SourceUrl;
            entry.FullPath = snapshot.FullPath;
            entry.ReceivedBytes = snapshot.ReceivedBytes;
            entry.TotalBytes = snapshot.TotalBytes;
            entry.CurrentSpeed = snapshot.CurrentSpeed;

            var target = _targetsByRuntimeId.GetValueOrDefault(snapshot.RuntimeId);
            var completionSucceeded = !snapshot.IsComplete || target == null ||
                                      DownloadPathPolicy.TryFinalize(
                                          target.TemporaryPath,
                                          target.FinalPath,
                                          target.OverwriteFinal);
            var state = snapshot.IsComplete
                ? completionSucceeded ? DownloadState.Complete : DownloadState.Failed
                : snapshot.IsCancelled
                    ? (_userCancelledIds.Contains(snapshot.RuntimeId) || entry.State == DownloadState.Cancelled
                        ? DownloadState.Cancelled
                        : DownloadState.Failed)
                    : _pausedIds.Contains(snapshot.RuntimeId)
                        ? DownloadState.Paused
                        : DownloadState.InProgress;
            entry.State = state;
            if (state is DownloadState.Complete or DownloadState.Cancelled or DownloadState.Failed)
            {
                entry.FinishedAt ??= DateTimeOffset.Now;
                entry.CurrentSpeed = 0;
                _callbacks.TryRemove(snapshot.RuntimeId, out _);
                _pausedIds.Remove(snapshot.RuntimeId);
                _userCancelledIds.Remove(snapshot.RuntimeId);
                if (_targetsByRuntimeId.TryRemove(snapshot.RuntimeId, out var finishedTarget))
                {
                    if (state != DownloadState.Complete)
                        DownloadPathPolicy.DeletePartial(finishedTarget.TemporaryPath);
                    lock (_reservedPaths) _reservedPaths.Remove(finishedTarget.FinalPath);
                }
                terminalChange = true;
            }
        }

        RebuildRecentDownloads();
        if (terminalChange) DownloadHistoryStore.Save(Downloads, _historyPath);
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
        foreach (var sourceBrowser in startedBrowsers)
            DownloadStarted?.Invoke(this, new DownloadStartedEventArgs(sourceBrowser));
        if (!_pendingUpdates.IsEmpty) ScheduleFlush(100);
    }

    private void UpdateEntryState(DownloadEntry entry, DownloadState state)
    {
        entry.State = state;
        if (!entry.IsActive) entry.FinishedAt ??= DateTimeOffset.Now;
        RebuildRecentDownloads();
        if (!entry.IsActive) DownloadHistoryStore.Save(Downloads, _historyPath);
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildRecentDownloads()
    {
        var desired = Downloads.Take(5).ToList();
        if (RecentDownloads.SequenceEqual(desired)) return;
        RecentDownloads.Clear();
        foreach (var item in desired) RecentDownloads.Add(item);
    }

    private void PersistAndNotify()
    {
        DownloadHistoryStore.Save(Downloads, _historyPath);
        DownloadsChanged?.Invoke(this, EventArgs.Empty);
    }

    private DownloadTarget ReserveUniqueTarget(string directory, string suggestedName)
    {
        lock (_reservedPaths)
        {
            var path = DownloadPathPolicy.GetUniquePath(directory, suggestedName, _reservedPaths);
            _reservedPaths.Add(path);
            return new DownloadTarget(path, DownloadPathPolicy.GetTemporaryPath(path), OverwriteFinal: false);
        }
    }

    private DownloadTarget ReserveSelectedTarget(string selectedPath)
    {
        lock (_reservedPaths)
        {
            if (!_reservedPaths.Contains(selectedPath))
            {
                _reservedPaths.Add(selectedPath);
                return new DownloadTarget(
                    selectedPath,
                    DownloadPathPolicy.GetAvailableTemporaryPath(selectedPath),
                    OverwriteFinal: File.Exists(selectedPath));
            }

            var directory = Path.GetDirectoryName(selectedPath) ?? DownloadPathPolicy.GetDefaultDownloadsFolder();
            var path = DownloadPathPolicy.GetUniquePath(directory, Path.GetFileName(selectedPath), _reservedPaths);
            _reservedPaths.Add(path);
            return new DownloadTarget(path, DownloadPathPolicy.GetTemporaryPath(path), OverwriteFinal: false);
        }
    }

    private static string NormalizeFinalPath(string path)
    {
        return path.EndsWith(DownloadPathPolicy.PartialExtension, StringComparison.OrdinalIgnoreCase)
            ? path[..^DownloadPathPolicy.PartialExtension.Length]
            : path;
    }
}
