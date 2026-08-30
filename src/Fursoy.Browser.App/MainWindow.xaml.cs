using System.Windows;
using System.Windows.Input;
using CefSharp;

namespace Fursoy.Browser.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Browser.AddressChanged += (_, e) => Dispatcher.Invoke(() => AddressBar.Text = e.NewValue as string);
    }

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        var input = AddressBar.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        var looksLikeUrl = input.Contains('.') && !input.Contains(' ');
        var address = looksLikeUrl
            ? (input.StartsWith("http://") || input.StartsWith("https://") ? input : $"https://{input}")
            : $"https://www.google.com/search?q={Uri.EscapeDataString(input)}";

        Browser.Address = address;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CanGoBack) Browser.Back();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CanGoForward) Browser.Forward();
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        Browser.Reload();
    }
}
