using System.ComponentModel;
using System.Runtime.CompilerServices;
using CefSharp.Wpf;
using Fursoy.Browser.App.Resources;

namespace Fursoy.Browser.App.Models;

public class TabItem : INotifyPropertyChanged
{
    public Guid Id { get; } = Guid.NewGuid();

    public ChromiumWebBrowser Browser { get; }

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

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    public TabItem()
    {
        Browser = new ChromiumWebBrowser();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
