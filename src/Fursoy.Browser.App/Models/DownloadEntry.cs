using System.ComponentModel;
using System.Runtime.CompilerServices;
using Fursoy.Browser.App.Resources;

namespace Fursoy.Browser.App.Models;

public class DownloadEntry : INotifyPropertyChanged
{
    public int Id { get; init; }
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;

    private long _receivedBytes;
    public long ReceivedBytes
    {
        get => _receivedBytes;
        set { SetField(ref _receivedBytes, value); RecalculateProgress(); }
    }

    private long _totalBytes;
    public long TotalBytes
    {
        get => _totalBytes;
        set { SetField(ref _totalBytes, value); RecalculateProgress(); }
    }

    private int _progressPercent;
    public int ProgressPercent
    {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    private bool _isComplete;
    public bool IsComplete
    {
        get => _isComplete;
        set { SetField(ref _isComplete, value); RecalculateProgress(); }
    }

    private bool _isCancelled;
    public bool IsCancelled
    {
        get => _isCancelled;
        set => SetField(ref _isCancelled, value);
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    private void RecalculateProgress()
    {
        ProgressPercent = TotalBytes > 0 ? (int)(ReceivedBytes * 100 / TotalBytes) : 0;
        StatusText = IsCancelled ? Strings.DownloadCancelled
            : IsComplete ? Strings.DownloadComplete
            : $"{FormatBytes(ReceivedBytes)} / {FormatBytes(TotalBytes)}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
