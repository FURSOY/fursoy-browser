using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Fursoy.Browser.App.Resources;

namespace Fursoy.Browser.App.Models;

public enum DownloadState
{
    InProgress,
    Paused,
    Complete,
    Cancelled,
    Failed,
    Missing,
}

/// <summary>
/// Popup, iç downloads sayfası ve kalıcı geçmişin paylaştığı görünüm modeli.
/// CEF callback'i özellikle burada tutulmaz; yalnızca AppDownloadHandler'ın
/// çalışma zamanı tablosunda yaşar.
/// </summary>
public sealed class DownloadEntry : INotifyPropertyChanged
{
    public Guid HistoryId { get; init; } = Guid.NewGuid();
    public int RuntimeId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    private string _fullPath = string.Empty;
    private bool _fileExists;
    public string FullPath
    {
        get => _fullPath;
        set
        {
            if (!SetField(ref _fullPath, value)) return;
            _fileExists = !string.IsNullOrWhiteSpace(value) && File.Exists(value);
            NotifyCalculated();
        }
    }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;

    private DateTimeOffset? _finishedAt;
    public DateTimeOffset? FinishedAt
    {
        get => _finishedAt;
        set => SetField(ref _finishedAt, value);
    }

    private long _receivedBytes;
    public long ReceivedBytes
    {
        get => _receivedBytes;
        set { if (SetField(ref _receivedBytes, value)) NotifyCalculated(); }
    }

    private long _totalBytes;
    public long TotalBytes
    {
        get => _totalBytes;
        set { if (SetField(ref _totalBytes, value)) NotifyCalculated(); }
    }

    private long _currentSpeed;
    public long CurrentSpeed
    {
        get => _currentSpeed;
        set { if (SetField(ref _currentSpeed, value)) NotifyCalculated(); }
    }

    private DownloadState _state;
    public DownloadState State
    {
        get => _state;
        set
        {
            if (value is DownloadState.Complete or DownloadState.Missing)
                _fileExists = !string.IsNullOrWhiteSpace(FullPath) && File.Exists(FullPath);
            if (SetField(ref _state, value)) NotifyCalculated();
        }
    }

    public int ProgressPercent => TotalBytes > 0
        ? (int)Math.Clamp(ReceivedBytes * 100L / TotalBytes, 0, 100)
        : State == DownloadState.Complete ? 100 : 0;

    public bool HasKnownSize => TotalBytes > 0;
    public bool IsIndeterminate => IsActive && !HasKnownSize;
    public bool IsActive => State is DownloadState.InProgress or DownloadState.Paused;
    public bool IsPaused => State == DownloadState.Paused;
    public bool IsComplete => State == DownloadState.Complete;
    public bool CanPause => State == DownloadState.InProgress;
    public bool CanResume => State == DownloadState.Paused;
    public bool CanCancel => IsActive;
    public bool CanRetry => State is DownloadState.Failed or DownloadState.Cancelled;
    public bool CanOpen => IsComplete && _fileExists;
    public bool CanShowInFolder => _fileExists;
    public bool IsMissing => State == DownloadState.Missing || (IsComplete && !_fileExists);

    public string FileTypeLabel
    {
        get
        {
            var extension = Path.GetExtension(FileName).TrimStart('.');
            return string.IsNullOrWhiteSpace(extension) ? "FILE" : extension[..Math.Min(4, extension.Length)].ToUpperInvariant();
        }
    }

    public string SourceHost => Uri.TryCreate(SourceUrl, UriKind.Absolute, out var uri)
        ? uri.Host
        : SourceUrl;

    public string StatusText => State switch
    {
        DownloadState.Paused => $"{Strings.DownloadPaused} · {FormatBytes(ReceivedBytes)} / {FormatTotal()}",
        DownloadState.Complete => Strings.DownloadComplete,
        DownloadState.Cancelled => Strings.DownloadCancelled,
        DownloadState.Failed => Strings.DownloadFailed,
        DownloadState.Missing => Strings.DownloadFileMissing,
        _ => BuildProgressText(),
    };

    public void RefreshFileAvailability()
    {
        _fileExists = !string.IsNullOrWhiteSpace(FullPath) && File.Exists(FullPath);
        if (State == DownloadState.Complete && !_fileExists) State = DownloadState.Missing;
        else if (State == DownloadState.Missing && _fileExists) State = DownloadState.Complete;
        else NotifyCalculated();
    }

    private string BuildProgressText()
    {
        var size = $"{FormatBytes(ReceivedBytes)} / {FormatTotal()}";
        if (CurrentSpeed <= 0) return size;

        var speed = $"{FormatBytes(CurrentSpeed)}/s";
        if (TotalBytes <= ReceivedBytes) return $"{size} · {speed}";
        var seconds = (TotalBytes - ReceivedBytes) / (double)CurrentSpeed;
        var remaining = seconds < 60
            ? string.Format(Strings.DownloadSecondsRemainingFormat, Math.Max(1, (int)Math.Ceiling(seconds)))
            : string.Format(Strings.DownloadMinutesRemainingFormat, Math.Max(1, (int)Math.Ceiling(seconds / 60)));
        return $"{size} · {speed} · {remaining}";
    }

    private string FormatTotal() => TotalBytes > 0 ? FormatBytes(TotalBytes) : Strings.DownloadSizeUnknown;

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    private void NotifyCalculated()
    {
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(HasKnownSize));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanOpen));
        OnPropertyChanged(nameof(CanShowInFolder));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(StatusText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
