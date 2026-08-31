using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CefSharp;
using Fursoy.Browser.App.Models;
using Fursoy.Browser.App.Resources;

namespace Fursoy.Browser.App;

public partial class MainWindow : Window
{
    private const string HomeAddress = "https://www.google.com";

    private readonly ObservableCollection<TabItem> _tabs = new();
    private TabItem? _activeTab;

    public MainWindow()
    {
        InitializeComponent();
        TabStrip.ItemsSource = _tabs;
        StateChanged += MainWindow_StateChanged;

        AddTab(HomeAddress);
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        var resizeBorder = SystemParameters.WindowResizeBorderThickness;
        RootPanel.Margin = WindowState == WindowState.Maximized
            ? new Thickness(resizeBorder.Left, resizeBorder.Top, resizeBorder.Right, resizeBorder.Bottom)
            : new Thickness(0);

        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private TabItem AddTab(string address)
    {
        var tab = new TabItem(address);
        tab.Browser.AddressChanged += (_, e) => Dispatcher.Invoke(() =>
        {
            tab.Address = e.NewValue as string ?? string.Empty;
            if (tab == _activeTab) AddressBar.Text = tab.Address;
        });
        tab.Browser.TitleChanged += (_, e) => Dispatcher.Invoke(() =>
        {
            tab.Title = string.IsNullOrWhiteSpace(e.NewValue as string) ? Strings.NewTab : (string)e.NewValue!;
        });

        BrowserHost.Children.Add(tab.Browser);
        tab.Browser.Visibility = Visibility.Collapsed;

        _tabs.Add(tab);
        SwitchTab(tab);
        return tab;
    }

    private void SwitchTab(TabItem tab)
    {
        if (_activeTab != null)
        {
            _activeTab.IsActive = false;
            _activeTab.Browser.Visibility = Visibility.Collapsed;
        }

        _activeTab = tab;
        tab.IsActive = true;
        tab.Browser.Visibility = Visibility.Visible;
        AddressBar.Text = tab.Address;
    }

    private void CloseTab(TabItem tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0) return;

        BrowserHost.Children.Remove(tab.Browser);
        tab.Browser.Dispose();
        _tabs.RemoveAt(index);

        if (_tabs.Count == 0)
        {
            Close();
            return;
        }

        if (_activeTab == tab)
        {
            _activeTab = null;
            var nextIndex = Math.Min(index, _tabs.Count - 1);
            SwitchTab(_tabs[nextIndex]);
        }
    }

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _activeTab == null) return;

        var input = AddressBar.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        var looksLikeUrl = input.Contains('.') && !input.Contains(' ');
        var address = looksLikeUrl
            ? (input.StartsWith("http://") || input.StartsWith("https://") ? input : $"https://{input}")
            : $"https://www.google.com/search?q={Uri.EscapeDataString(input)}";

        _activeTab.Browser.Address = address;
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) => AddTab(HomeAddress);

    private void TabBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TabItem tab }) SwitchTab(tab);
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TabItem tab }) CloseTab(tab);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.Browser.CanGoBack == true) _activeTab.Browser.Back();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.Browser.CanGoForward == true) _activeTab.Browser.Forward();
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        _activeTab?.Browser.Reload();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
