using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CefSharp;
using Fursoy.Browser.App.Models;
using Fursoy.Browser.App.Resources;
using Fursoy.Browser.App.Services;

namespace Fursoy.Browser.App;

public partial class MainWindow : Window
{
    private const string HomeAddress = "https://www.google.com";

    private readonly ObservableCollection<TabItem> _tabs = new();
    private readonly BookmarkStore _bookmarkStore = new();
    private readonly HistoryStore _historyStore = new();
    private TabItem? _activeTab;

    public MainWindow()
    {
        InitializeComponent();
        TabStrip.ItemsSource = _tabs;
        BookmarksList.ItemsSource = _bookmarkStore.Bookmarks;
        HistoryList.ItemsSource = _historyStore.Entries;
        _bookmarkStore.Bookmarks.CollectionChanged += (_, _) => UpdateBookmarksEmptyState();
        _historyStore.Entries.CollectionChanged += (_, _) => UpdateHistoryEmptyState();
        UpdateBookmarksEmptyState();
        UpdateHistoryEmptyState();
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
            if (tab == _activeTab)
            {
                AddressBar.Text = tab.Address;
                StarButton.IsChecked = _bookmarkStore.Contains(tab.Address);
            }
        });
        tab.Browser.TitleChanged += (_, e) => Dispatcher.Invoke(() =>
        {
            tab.Title = string.IsNullOrWhiteSpace(e.NewValue as string) ? Strings.NewTab : (string)e.NewValue!;
        });
        tab.Browser.LoadingStateChanged += (_, e) =>
        {
            if (e.IsLoading) return;
            Dispatcher.Invoke(() => _historyStore.Add(tab.Address, tab.Title));
        };

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
        StarButton.IsChecked = _bookmarkStore.Contains(tab.Address);
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

    private void StarButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab == null) return;
        _bookmarkStore.Toggle(_activeTab.Address, _activeTab.Title);
    }

    private void BookmarksButton_Click(object sender, RoutedEventArgs e)
    {
        BookmarksPopup.IsOpen = !BookmarksPopup.IsOpen;
    }

    private void BookmarkRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Bookmark bookmark } || _activeTab == null) return;
        _activeTab.Browser.Address = bookmark.Url;
        BookmarksPopup.IsOpen = false;
    }

    private void RemoveBookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Bookmark bookmark }) return;
        _bookmarkStore.Remove(bookmark);
        if (_activeTab != null) StarButton.IsChecked = _bookmarkStore.Contains(_activeTab.Address);
    }

    private void UpdateBookmarksEmptyState()
    {
        BookmarksEmptyText.Visibility = _bookmarkStore.Bookmarks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryPopup.IsOpen = !HistoryPopup.IsOpen;
    }

    private void HistoryRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: HistoryEntry entry } || _activeTab == null) return;
        _activeTab.Browser.Address = entry.Url;
        HistoryPopup.IsOpen = false;
    }

    private void RemoveHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: HistoryEntry entry }) return;
        _historyStore.Remove(entry);
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e) => _historyStore.Clear();

    private void UpdateHistoryEmptyState()
    {
        HistoryEmptyText.Visibility = _historyStore.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
