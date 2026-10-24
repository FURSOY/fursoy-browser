using System.ComponentModel;
using System.Runtime.CompilerServices;
using CefSharp;
using Fursoy.Browser.App.Controls;
using Fursoy.Browser.App.Resources;
using Fursoy.Browser.App.Services;

namespace Fursoy.Browser.App.Models;

public class TabItem : INotifyPropertyChanged
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid? OpenerTabId { get; set; }

    public ThemedChromiumWebBrowser Browser { get; private set; }

    private string _title = Strings.NewTab;
    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    private string _address = string.Empty;
    public string Address
    {
        get => _address;
        set => SetField(ref _address, value);
    }

    private string? _faviconUrl;
    public string? FaviconUrl
    {
        get => _faviconUrl;
        set => SetField(ref _faviconUrl, value);
    }

    private bool _isPinned;
    public bool IsPinned
    {
        get => _isPinned;
        set => SetField(ref _isPinned, value);
    }

    private bool _isAudible;
    public bool IsAudible
    {
        get => _isAudible;
        set => SetField(ref _isAudible, value);
    }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
    }

    private bool _isSuspended;
    public bool IsSuspended
    {
        get => _isSuspended;
        set => SetField(ref _isSuspended, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetField(ref _isLoading, value);
    }

    private bool _hasCertificateError;
    public bool HasCertificateError
    {
        get => _hasCertificateError;
        set => SetField(ref _hasCertificateError, value);
    }

    private bool _isBeingDragged;
    public bool IsBeingDragged
    {
        get => _isBeingDragged;
        set => SetField(ref _isBeingDragged, value);
    }

    private double _visualOffsetX;
    public double VisualOffsetX
    {
        get => _visualOffsetX;
        set => SetField(ref _visualOffsetX, value);
    }

    internal MainWindow? Owner { get; set; }

    public bool IsBrowserAttached { get; set; }

    public bool IsBrowserAttachPending { get; set; }

    public bool HasStartedNavigation { get; set; }

    public bool IsFirstVisualReady { get; set; }

    public DateTime LastActivatedUtc { get; set; } = DateTime.UtcNow;

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    private double _displayWidth = 256;
    public double DisplayWidth
    {
        get => _displayWidth;
        set => SetField(ref _displayWidth, value);
    }

    /// <summary>
    /// Chrome bitişik iki PASİF sekmenin arasına ince bir dikey ayraç çizer;
    /// aktif sekmeye komşu olan ayraçlar gizlenir. Bu yüzden her sekme kendi
    /// solundaki ayracın görünürlüğünü ayrı tutar.
    /// </summary>
    private bool _showLeftSeparator;
    public bool ShowLeftSeparator
    {
        get => _showLeftSeparator;
        set => SetField(ref _showLeftSeparator, value);
    }

    private bool _showRightSeparator;
    public bool ShowRightSeparator
    {
        get => _showRightSeparator;
        set => SetField(ref _showRightSeparator, value);
    }

    public TabItem()
    {
        Browser = CreateBrowser();
    }

    public void ResetBrowser()
    {
        Browser.Dispose();
        Browser = CreateBrowser();
        IsBrowserAttached = false;
        HasStartedNavigation = false;
        IsFirstVisualReady = false;
        IsSuspended = true;
        IsAudible = false;
        IsLoading = false;
    }

    private static ThemedChromiumWebBrowser CreateBrowser() =>
        new()
        {
            BrowserSettings = new BrowserSettings
            {
                // Windowed Chromium opak bir arka planla doğrudan kendi HWND'sine
                // çizer; tema rengiyle birebir eşitleyerek geçişlerde parlamayı önler.
                BackgroundColor = AppTheme.GetArgb("BackgroundColor"),
            },
        };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
