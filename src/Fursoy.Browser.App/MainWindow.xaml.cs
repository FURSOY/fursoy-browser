using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using CefSharp;
using Fursoy.Browser.App.Models;
using Fursoy.Browser.App.Controls;
using Fursoy.Browser.App.Resources;
using Fursoy.Browser.App.Services;
using Microsoft.Win32;
using Canvas = System.Windows.Controls.Canvas;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace Fursoy.Browser.App;

public partial class MainWindow : Window
{
    private const double CompactToolbarThreshold = 760;
    private const int RecentHistoryPopupLimit = 8;
    private readonly ObservableCollection<TabItem> _tabs = new();
    private readonly ObservableCollection<OmniboxSuggestion> _addressSuggestions = new();
    private readonly ObservableCollection<HistoryEntry> _recentHistory = new();
    private readonly Dictionary<TabItem, FindInPageState> _findStates = new();
    private readonly BookmarkStore _bookmarkStore;
    private readonly HistoryStore _historyStore;
    private readonly AppDownloadHandler _downloadHandler;
    private readonly AppSettingsData _appSettings;
    private readonly Stack<string> _closedTabAddresses = new();
    private readonly Dictionary<TabItem, (DependencyPropertyDescriptor Descriptor, EventHandler Handler)>
        _addressChangeHandlers = new();
    private readonly DispatcherTimer _sessionSaveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };
    private readonly DispatcherTimer _tabMaintenanceTimer = new()
    {
        Interval = TimeSpan.FromSeconds(30),
    };
    private TabItem? _activeTab;
    private TabItem? _draggedTab;
    private Point _tabDragStart;
    private bool _isDraggingTab;
    private int _dragOriginalIndex = -1;
    private int _dragDropIndex = -1;
    private double _dragGrabOffsetX;
    private FrameworkElement? _tabDragVisual;
    private FrameworkElement? _newTabDragVisual;
    private double _tabDragImageBaseLeft;
    private double _newTabDragImageBaseLeft;
    private double _tabDragRootOffsetX;
    private FrameworkElement? _draggedTabRoot;
    private bool _isClosing;
    private Action? _allowPermission;
    private Action? _denyPermission;
    private bool _isFullScreen;
    private WindowState _windowStateBeforeFullScreen;
    private ResizeMode _resizeModeBeforeFullScreen;
    private int _windowStateRevision;
    private int _tabActivationRevision;
    private ContextMenu? _browserContextMenu;
    private IRunContextMenuCallback? _browserContextMenuCallback;
    private bool _browserContextMenuSelectionMade;
    private bool _isUpdatingAddressBarProgrammatically;
    private bool _isUpdatingFindInputProgrammatically;
    private bool _focusFindInputOnOpen;
    private int _findFocusRevision;
    private bool _addressBarMouseFocusPending;
    private bool _addressBarMouseExceededDragThreshold;
    private bool _addressBarFocusExitedByTab;
    private bool _addressBarHasPendingRevert;
    private bool _addressSuggestionCommitPending;
    private bool _isAddressTextInputInProgress;
    private bool _isImeCompositionActive;
    private bool _suppressInlineCompletionOnce;
    private int _addressEditRevision;
    private DateTime _lastDownloadStartAnimationUtc = DateTime.MinValue;
    private Point _addressBarMouseDownPoint;
    private AddressSuggestionMode _addressSuggestionMode;
    private InlineAutocompleteState _inlineAutocompleteState = InlineAutocompleteState.Inactive();

    private enum AddressSuggestionMode
    {
        Closed,
        OnFocus,
        UserInput,
    }

    internal double MinimumChromeHeight
    {
        get
        {
            var height = TabBar.Height + NavigationBar.Height;
            if (BookmarksBar.Visibility == Visibility.Visible) height += BookmarksBar.Height;
            return height + WindowFrame.BorderThickness.Top + WindowFrame.BorderThickness.Bottom;
        }
    }

    public MainWindow() : this(restoreSession: true, persistWindowState: true, sharedStateFrom: null)
    {
    }

    private MainWindow(bool restoreSession, bool persistWindowState, MainWindow? sharedStateFrom = null)
    {
        _appSettings = sharedStateFrom?._appSettings ?? AppSettingsStore.Load();
        _bookmarkStore = sharedStateFrom?._bookmarkStore ?? new BookmarkStore();
        _historyStore = sharedStateFrom?._historyStore ?? new HistoryStore();
        _downloadHandler = sharedStateFrom?._downloadHandler ?? new AppDownloadHandler(_appSettings);
        InitializeComponent();
        ToolbarPopupAnchor.SetHorizontalAnchor(FindPanelPopup, AddressBarContainer);
        UpdateFindButtonVisibility();
        UpdateSearchEnginePlaceholders();
        AddressSuggestionsList.ItemsSource = _addressSuggestions;
        TextCompositionManager.AddPreviewTextInputStartHandler(AddressBar, AddressBar_PreviewTextInputStart);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(AddressBar, AddressBar_PreviewTextInputUpdate);
        TextCompositionManager.AddPreviewTextInputHandler(AddressBar, AddressBar_PreviewTextInput);
        DataObject.AddPastingHandler(AddressBar, AddressBar_Pasting);
        LocationChanged += (_, _) =>
        {
            RepositionAddressSuggestionsPopup();
            RepositionFindPanel();
        };
        Deactivated += (_, _) =>
        {
            CloseAddressSuggestions();
            SuspendFindPanelVisual();
        };
        Activated += (_, _) => SyncFindPanelVisibility();
        WindowMaximizeFix.Apply(this);
        if (persistWindowState) WindowStateStore.Apply(this);
        TabStrip.ItemsSource = _tabs;
        BookmarksList.ItemsSource = _bookmarkStore.Bookmarks;
        BookmarksBarList.ItemsSource = _bookmarkStore.Bookmarks;
        HistoryList.ItemsSource = _recentHistory;
        AllHistoryList.ItemsSource = _historyStore.Entries;
        DownloadsList.ItemsSource = _downloadHandler.RecentDownloads;
        BrowserHost.SizeChanged += (_, _) =>
        {
            UpdateTabWidths();
            RepositionFindPanel();
        };
        AddressBarContainer.SizeChanged += (_, _) => RepositionFindPanel();
        _bookmarkStore.Bookmarks.CollectionChanged += BookmarkCollectionChanged;
        _historyStore.Entries.CollectionChanged += HistoryCollectionChanged;
        _downloadHandler.Downloads.CollectionChanged += DownloadsCollectionChanged;
        _downloadHandler.DownloadsChanged += DownloadHandler_DownloadsChanged;
        _downloadHandler.DownloadStarted += DownloadHandler_DownloadStarted;
        _sessionSaveTimer.Tick += (_, _) =>
        {
            _sessionSaveTimer.Stop();
            SaveSession();
        };
        _tabMaintenanceTimer.Tick += async (_, _) => await MaintainBackgroundTabsAsync();
        _tabMaintenanceTimer.Start();
        Closing += (_, _) =>
        {
            DismissBrowserContextMenu();
            _isClosing = Application.Current.Windows.OfType<MainWindow>()
                .Any(window => !ReferenceEquals(window, this) && window.IsVisible);
            _sessionSaveTimer.Stop();
            _tabMaintenanceTimer.Stop();
            SaveSession();
        };
        Closed += (_, _) =>
        {
            _bookmarkStore.Bookmarks.CollectionChanged -= BookmarkCollectionChanged;
            _historyStore.Entries.CollectionChanged -= HistoryCollectionChanged;
            _downloadHandler.Downloads.CollectionChanged -= DownloadsCollectionChanged;
            _downloadHandler.DownloadsChanged -= DownloadHandler_DownloadsChanged;
            _downloadHandler.DownloadStarted -= DownloadHandler_DownloadStarted;
            // Başka pencereler çalışmaya devam ederken kapanan pencerenin
            // Chromium kaynaklarını uygulama kapanışına kadar sızdırma.
            if (!_isClosing) return;
            foreach (var tab in _tabs.ToList())
            {
                DetachAddressSubscription(tab);
                tab.Browser.Dispose();
            }
        };
        UpdateBookmarksEmptyState();
        UpdateHistoryEmptyState();
        _downloadHandler.RefreshFileAvailability();
        UpdateDownloadsEmptyState();
        UpdateDownloadToolbarState();
        StateChanged += MainWindow_StateChanged;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        MainWindow_StateChanged(this, EventArgs.Empty);

        if (restoreSession) RestoreSession();
    }

    private void BookmarkCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateBookmarksEmptyState();

    private void HistoryCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateHistoryEmptyState();
        RefreshRecentHistory();
    }

    private void DownloadsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateDownloadsEmptyState();

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        SyncFindPanelVisibility();

        // Art arda gelen büyüt/geri-al olaylarında eski WPF layout işleri sırada
        // kalabilir. Yalnızca en son pencere durumunun düzenini uygula.
        var revision = ++_windowStateRevision;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (revision != _windowStateRevision) return;

            RootPanel.InvalidateMeasure();
            RootPanel.InvalidateArrange();
            BrowserHost.InvalidateMeasure();
            BrowserHost.InvalidateArrange();
            _activeTab?.Browser.InvalidateMeasure();
            _activeTab?.Browser.InvalidateArrange();
            UpdateTabWidths();
            RepositionFindPanel();
        });
    }

    private static bool IsNewTabAddress(string address) =>
        string.IsNullOrEmpty(address) ||
        address.Equals("about:blank", StringComparison.OrdinalIgnoreCase) ||
        address.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

    private static bool IsDownloadsAddress(string? address) =>
        !string.IsNullOrWhiteSpace(address) &&
        address.TrimEnd('/').Equals("fursoy://downloads", StringComparison.OrdinalIgnoreCase);

    private string GetActiveTabExpectedAddress() =>
        _activeTab == null ? string.Empty : (IsNewTabAddress(_activeTab.Address) ? string.Empty : _activeTab.Address);

    private TabItem AddTab(
        string? address = null,
        bool activate = true,
        string? title = null,
        string? faviconUrl = null,
        bool isPinned = false,
        TabItem? opener = null,
        int? insertionIndex = null)
    {
        var tab = new TabItem
        {
            Address = address ?? string.Empty,
            Title = string.IsNullOrWhiteSpace(title) ? Strings.NewTab : title,
            FaviconUrl = faviconUrl,
            IsPinned = isPinned,
            OpenerTabId = opener?.Id,
            Owner = this,
        };

        // Oturumdan geri yüklenen yerel favicon'lar da ağ kullanmadan
        // omnibox önerilerinde bulunabilsin.
        FaviconCache.RememberCachedForPage(tab.Address, tab.FaviconUrl);

        ConfigureBrowser(tab);
        if (insertionIndex is { } requestedIndex)
            _tabs.Insert(Math.Clamp(requestedIndex, 0, _tabs.Count), tab);
        else
            _tabs.Add(tab);
        UpdateTabWidths();
        if (activate) SwitchTab(tab);
        ScheduleSessionSave();

        return tab;
    }

    private TabItem AddChildTab(TabItem opener, string address)
    {
        var openerIndex = _tabs.IndexOf(opener);
        if (openerIndex < 0) return AddTab(address);

        var insertionIndex = BrowserTabPolicy.CalculateOpenerInsertionIndex(
            _tabs.Select(tab => tab.OpenerTabId).ToList(),
            openerIndex,
            opener.Id,
            _tabs.Count(tab => tab.IsPinned));
        return AddTab(address, opener: opener, insertionIndex: insertionIndex);
    }

    private void ConfigureBrowser(TabItem tab)
    {
        var findState = GetOrCreateFindState(tab);
        var configuredBrowser = tab.Browser;
        tab.Browser.FocusHandler = new BrowserFocusHandler(() =>
            Application.Current.Dispatcher.BeginInvoke(() =>
                tab.Owner?.OnBrowserGotFocus(tab, configuredBrowser)));
        tab.Browser.FindHandler = new BrowserFindHandler((identifier, count, ordinal, finalUpdate) =>
            Application.Current.Dispatcher.BeginInvoke(() =>
                tab.Owner?.ApplyFindResult(tab, configuredBrowser, identifier, count, ordinal)));
        var addressProperty = DependencyPropertyDescriptor.FromProperty(
            CefSharp.Wpf.HwndHost.ChromiumWebBrowser.AddressProperty,
            typeof(CefSharp.Wpf.HwndHost.ChromiumWebBrowser));
        EventHandler addressChangedHandler = (_, _) => Application.Current.Dispatcher.Invoke(() =>
        {
            var browserAddress = tab.Browser.Address ?? string.Empty;
            // Chromium yeni örneği oluştururken, bizim başlangıç sayfamız
            // yüklenmeden önce geçici olarak about:blank bildirir. Bu geçici
            // adres boş yeni-sekme durumunu ezmemeli.
            if (!(browserAddress.Equals("about:blank", StringComparison.OrdinalIgnoreCase) &&
                  !tab.HasStartedNavigation && IsNewTabAddress(tab.Address)))
                tab.Address = browserAddress;
            tab.HasCertificateError = false;
            tab.Owner?.HandleTabAddressChanged(tab);
        });
        addressProperty.AddValueChanged(tab.Browser, addressChangedHandler);
        _addressChangeHandlers[tab] = (addressProperty, addressChangedHandler);
        tab.Browser.TitleChanged += (_, e) => Application.Current.Dispatcher.Invoke(() =>
        {
            tab.Title = string.IsNullOrWhiteSpace(e.NewValue as string) ? Strings.NewTab : (string)e.NewValue!;
            tab.Owner?.ScheduleSessionSave();
        });
        tab.Browser.LoadingStateChanged += (_, e) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            tab.IsLoading = e.IsLoading;
            var owner = tab.Owner;
            if (owner == null) return;
            if (e.IsLoading) owner.ResetFindResultsForNavigation(tab);
            if (ReferenceEquals(tab, owner._activeTab))
            {
                owner.UpdateLoadingUi(tab);
                owner.UpdateNavigationUi(tab, e.CanGoBack, e.CanGoForward);
            }
            if (!e.IsLoading && !IsDownloadsAddress(tab.Address)) owner._historyStore.Add(tab.Address, tab.Title);
        });
        tab.Browser.FrameLoadEnd += (_, e) =>
        {
            if (!e.Frame.IsMain) return;
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                tab.IsFirstVisualReady = true;
                var owner = tab.Owner;
                if (owner != null && ReferenceEquals(tab, owner._activeTab))
                    owner.RevealActiveBrowser(tab, owner._tabActivationRevision);
                owner?.RestartFindAfterNavigation(tab);
                if (owner != null && IsDownloadsAddress(tab.Address)) owner.PushDownloadsPageState(tab);
            });
        };

        tab.Browser.JavascriptMessageReceived += (_, e) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!IsDownloadsAddress(tab.Address)) return;
            try
            {
                var command = JsonSerializer.Deserialize<DownloadPageCommand>(
                    JsonSerializer.Serialize(e.Message),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (command != null) tab.Owner?.HandleDownloadsPageCommand(command);
            }
            catch (JsonException)
            {
                // Yalnızca Fursoy'un oluşturduğu iç sayfa mesajları işlenir.
            }
        });

        tab.Browser.DownloadHandler = _downloadHandler;
        tab.Browser.DisplayHandler = new FaviconDisplayHandler(faviconUrl => Application.Current.Dispatcher.BeginInvoke(async () =>
        {
            tab.FaviconUrl = faviconUrl == null
                ? null
                : await FaviconCache.GetOrCreateAsync(faviconUrl, tab.Address);
            tab.Owner?.ScheduleSessionSave();
        }));
        tab.Browser.LifeSpanHandler = new TabLifeSpanHandler(targetUrl => Application.Current.Dispatcher.BeginInvoke(() =>
            tab.Owner?.AddChildTab(tab, targetUrl)));
        tab.Browser.PermissionHandler = new BrowserPermissionHandler((origin, permission, allow, deny) =>
            Application.Current.Dispatcher.BeginInvoke(() =>
                tab.Owner?.ShowPermissionPrompt(origin, permission, allow, deny)));
        tab.Browser.RequestHandler = new BrowserRequestHandler(requestUrl => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            tab.HasCertificateError = true;
            if (tab.Owner?._activeTab == tab) tab.Owner.UpdateSecurityUi(tab);
        }));
        tab.Browser.KeyboardHandler = new BrowserKeyboardHandler(shortcut =>
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var owner = tab.Owner;
                if (owner == null) return;
                if ((shortcut is BrowserShortcut.FindInPage or BrowserShortcut.CloseFindInPage) &&
                    !ReferenceEquals(owner._activeTab, tab)) return;
                owner.ExecuteBrowserShortcut(shortcut);
            }), () => findState.IsOpen);
        tab.Browser.MenuHandler = new BrowserContextMenuHandler(
            () => tab.Owner?._appSettings.SearchEngine ?? "Google",
            () => IsNewTabAddress(tab.Address),
            address => Application.Current.Dispatcher.BeginInvoke(() => tab.Owner?.AddChildTab(tab, address)),
            (x, y, entries, callback) => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var owner = tab.Owner;
                if (owner == null)
                {
                    if (!callback.IsDisposed) callback.Cancel();
                    return;
                }

                owner.ShowBrowserContextMenu(tab, x, y, entries, callback);
            }));
        tab.Browser.Visibility = Visibility.Visible;
        tab.Browser.SetPresentationActive(false);
        tab.Browser.IsBrowserInitializedChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var owner = tab.Owner;
            if (owner != null && tab.Browser.IsBrowserInitialized)
            {
                owner.StartInitialNavigation(tab);
                if (ReferenceEquals(tab, owner._activeTab))
                    owner.RevealActiveBrowser(tab, owner._tabActivationRevision);
            }
        });
    }

    private void ShowBrowserContextMenu(
        TabItem tab,
        int x,
        int y,
        IReadOnlyList<BrowserContextMenuEntry> entries,
        IRunContextMenuCallback callback)
    {
        DismissBrowserContextMenu();
        _browserContextMenuCallback = callback;
        _browserContextMenuSelectionMade = false;

        var menu = new ContextMenu
        {
            Style = (Style)FindResource("BrowserContextMenuStyle"),
            PlacementTarget = tab.Browser,
            Placement = PlacementMode.RelativePoint,
            HorizontalOffset = x,
            VerticalOffset = y,
        };

        foreach (var entry in entries)
            menu.Items.Add(CreateBrowserContextMenuItem(entry, menu));

        menu.Closed += (_, _) => CompleteBrowserContextMenu(menu);
        _browserContextMenu = menu;
        menu.IsOpen = true;
    }

    private object CreateBrowserContextMenuItem(BrowserContextMenuEntry entry, ContextMenu ownerMenu)
    {
        if (entry.Kind.Equals("Separator", StringComparison.OrdinalIgnoreCase))
        {
            return new Separator
            {
                Style = (Style)FindResource("BrowserContextMenuSeparatorStyle"),
            };
        }

        var item = new MenuItem
        {
            Header = entry.Label,
            IsEnabled = entry.IsEnabled,
            IsCheckable = entry.Kind.Equals("Check", StringComparison.OrdinalIgnoreCase) ||
                          entry.Kind.Equals("Radio", StringComparison.OrdinalIgnoreCase),
            IsChecked = entry.IsChecked,
            InputGestureText = entry.Shortcut,
            Style = (Style)FindResource("BrowserContextMenuItemStyle"),
        };

        foreach (var child in entry.Children)
            item.Items.Add(CreateBrowserContextMenuItem(child, ownerMenu));

        if (entry.Children.Count == 0)
        {
            item.Click += (_, _) =>
            {
                if (_browserContextMenuCallback == null || _browserContextMenuCallback.IsDisposed) return;
                _browserContextMenuSelectionMade = true;
                _browserContextMenuCallback.Continue(entry.CommandId, CefEventFlags.None);
                ownerMenu.IsOpen = false;
            };
        }

        return item;
    }

    private void DismissBrowserContextMenu()
    {
        if (_browserContextMenu == null) return;
        _browserContextMenu.IsOpen = false;
    }

    private void CompleteBrowserContextMenu(ContextMenu menu)
    {
        if (!ReferenceEquals(_browserContextMenu, menu)) return;

        var callback = _browserContextMenuCallback;
        _browserContextMenu = null;
        _browserContextMenuCallback = null;
        if (!_browserContextMenuSelectionMade && callback is { IsDisposed: false }) callback.Cancel();
        _browserContextMenuSelectionMade = false;
    }

    private void HandleTabAddressChanged(TabItem tab)
    {
        ScheduleSessionSave();
        if (tab != _activeTab) return;

        SetAddressBarTextFromTab(tab);
        AddressBar.Tag = IsNewTabAddress(tab.Address);
        StarButton.IsChecked = _bookmarkStore.Contains(tab.Address);
        UpdateSecurityUi(tab);
    }

    private void EnsureBrowserAttached(TabItem tab)
    {
        if (tab.IsBrowserAttached) return;

        // HwndHost'u pencere/BrowserHost henüz gerçek boyutunu almadan eklemek,
        // başlangıçta sol üstte 1x1 siyah bir native pencere parlaması üretir.
        if (!BrowserHost.IsLoaded || BrowserHost.ActualWidth <= 1 || BrowserHost.ActualHeight <= 1)
        {
            if (tab.IsBrowserAttachPending) return;
            tab.IsBrowserAttachPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                tab.IsBrowserAttachPending = false;
                if (!ReferenceEquals(tab.Owner, this) || !_tabs.Contains(tab)) return;
                EnsureBrowserAttached(tab);
                if (ReferenceEquals(tab, _activeTab)) RevealActiveBrowser(tab, _tabActivationRevision);
            });
            return;
        }

        BrowserHost.Children.Add(tab.Browser);
        tab.IsBrowserAttached = true;
        tab.IsSuspended = false;
        StartInitialNavigation(tab);
    }

    private void StartInitialNavigation(TabItem tab)
    {
        if (!tab.IsBrowserAttached || !tab.Browser.IsBrowserInitialized || tab.HasStartedNavigation) return;
        tab.HasStartedNavigation = true;

        if (IsNewTabAddress(tab.Address))
        {
            tab.Browser.LoadHtml(NewTabPage.BuildHtml(_bookmarkStore.Bookmarks, _appSettings.SearchEngine));
        }
        else if (IsDownloadsAddress(tab.Address))
        {
            tab.Browser.LoadHtml(DownloadsPage.BuildHtml(_downloadHandler.Downloads), DownloadsPage.Address);
        }
        else
        {
            tab.Browser.Address = tab.Address;
        }
    }

    private void UpdateTabWidths()
    {
        if (_tabs.Count == 0) return;

        // 36: sekme arama, 32: yeni sekme, 138: pencere düğmeleri.
        // TabShape'in 18 px'lik omuz bindirmesi DisplayWidth'e dahildir.
        var availableWidth = Math.Max(0, ActualWidth - 206);
        const double pinnedEffectiveWidth = 48;
        var pinnedCount = _tabs.Count(tab => tab.IsPinned);
        var regularCount = _tabs.Count - pinnedCount;
        var remainingWidth = Math.Max(0, availableWidth - pinnedCount * pinnedEffectiveWidth);
        var effectiveWidth = regularCount == 0 ? pinnedEffectiveWidth : Math.Clamp(remainingWidth / regularCount, 54, 238);
        var displayWidth = effectiveWidth + 18;
        foreach (var tab in _tabs)
        {
            tab.DisplayWidth = tab.IsPinned ? pinnedEffectiveWidth + 18 : displayWidth;
        }
    }

    private void SwitchTab(TabItem tab)
    {
        if (ReferenceEquals(_activeTab, tab)) return;

        ResetTabDragState();
        _activeTab?.Browser?.CancelInteractiveOverlays();

        var activationRevision = ++_tabActivationRevision;
        if (_activeTab != null)
        {
            _activeTab.IsActive = false;
        }

        _activeTab = tab;
        tab.IsActive = true;
        ShowFindStateForActiveTab();
        tab.LastActivatedUtc = DateTime.UtcNow;
        EnsureBrowserAttached(tab);
        RevealActiveBrowser(tab, activationRevision);
        SetAddressBarTextFromTab(tab);
        AddressBar.Tag = IsNewTabAddress(tab.Address);
        StarButton.IsChecked = _bookmarkStore.Contains(tab.Address);
        UpdateSecurityUi(tab);
        UpdateLoadingUi(tab);
        UpdateNavigationUi(tab);
        UpdateTabSeparators();
        ScheduleSessionSave();
    }

    private FindInPageState GetOrCreateFindState(TabItem tab)
    {
        if (_findStates.TryGetValue(tab, out var state)) return state;
        state = new FindInPageState();
        _findStates.Add(tab, state);
        return state;
    }

    private void ShowFindStateForActiveTab()
    {
        ++_findFocusRevision;
        _focusFindInputOnOpen = false;
        if (_activeTab == null)
        {
            FindPanelPopup.IsOpen = false;
            UpdateFindButtonVisibility();
            return;
        }

        var state = GetOrCreateFindState(_activeTab);
        _isUpdatingFindInputProgrammatically = true;
        try { FindInput.Text = state.Query; }
        finally { _isUpdatingFindInputProgrammatically = false; }
        FindCounter.Text = state.Counter;
        SyncFindPanelVisibility();
    }

    private void SyncFindPanelVisibility()
    {
        var shouldShow = IsActive && IsVisible && WindowState != WindowState.Minimized &&
                         _activeTab != null && _findStates.TryGetValue(_activeTab, out var state) &&
                         state.IsOpen;
        if (FindPanelPopup.IsOpen != shouldShow) FindPanelPopup.IsOpen = shouldShow;
        UpdateFindButtonVisibility();
    }

    private void RepositionFindPanel()
    {
        if (!FindPanelPopup.IsOpen) return;
        // Yerleşim callback'ini yeniden çalıştır; koordinat hâlâ layout'tan hesaplanır.
        var offset = FindPanelPopup.HorizontalOffset;
        FindPanelPopup.HorizontalOffset = offset + 0.01;
        FindPanelPopup.HorizontalOffset = offset;
    }

    private void SuspendFindPanelVisual()
    {
        ++_findFocusRevision;
        _focusFindInputOnOpen = false;
        FindPanelPopup.IsOpen = false;
        // Sekmedeki query ve Chromium eşleşmeleri korunur; yalnızca ayrı popup HWND gizlenir.
    }

    private void UpdateFindButtonVisibility()
    {
        var findOpen = _activeTab != null && _findStates.TryGetValue(_activeTab, out var state) &&
                       state.IsOpen;
        var visible = _appSettings.ShowFindButtonInToolbar || findOpen;
        FindPageButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenFindPanel()
    {
        if (_activeTab == null) return;
        var state = GetOrCreateFindState(_activeTab);
        var wasOpen = state.IsOpen;
        state.IsOpen = true;
        _isUpdatingFindInputProgrammatically = true;
        try { FindInput.Text = state.Query; }
        finally { _isUpdatingFindInputProgrammatically = false; }
        FindCounter.Text = state.Counter;
        _focusFindInputOnOpen = true;
        SyncFindPanelVisibility();
        FocusFindInputAndSelectAll();
        if (!wasOpen && state.Query.Length > 0 && _activeTab.Browser.IsBrowserInitialized)
            _activeTab.Browser.GetBrowserHost()?.Find(
                state.Query, forward: true, matchCase: false, findNext: false);
    }

    private void FindPanelPopup_Opened(object sender, EventArgs e)
    {
        if (_focusFindInputOnOpen) TryFocusFindInput(++_findFocusRevision, 0);
    }

    private void FocusFindInputAndSelectAll()
    {
        var revision = ++_findFocusRevision;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => TryFocusFindInput(revision, 0));
    }

    private void TryFocusFindInput(int revision, int attempt)
    {
        if (revision != _findFocusRevision || !FindPanelPopup.IsOpen || _activeTab == null ||
            !GetOrCreateFindState(_activeTab).IsOpen) return;

        var source = PresentationSource.FromVisual(FindInput) as HwndSource;
        if (source?.Handle is { } handle && handle != IntPtr.Zero)
        {
            if (GetNativeKeyboardFocus() != handle) SetNativeKeyboardFocus(handle);
            Keyboard.Focus(FindInput);
            if (ReferenceEquals(Keyboard.FocusedElement, FindInput) &&
                GetNativeKeyboardFocus() == handle)
            {
                FindInput.SelectAll();
                _focusFindInputOnOpen = false;
                return;
            }
        }

        if (attempt < 4)
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
                () => TryFocusFindInput(revision, attempt + 1));
    }

    [DllImport("user32.dll", EntryPoint = "SetFocus")]
    private static extern IntPtr SetNativeKeyboardFocus(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "GetFocus")]
    private static extern IntPtr GetNativeKeyboardFocus();

    private void OnBrowserGotFocus(TabItem tab, CefSharp.Wpf.HwndHost.ChromiumWebBrowser browser)
    {
        if (!ReferenceEquals(tab, _activeTab) || !ReferenceEquals(tab.Browser, browser) ||
            !FindInput.IsKeyboardFocused) return;
        var popupHandle = (PresentationSource.FromVisual(FindInput) as HwndSource)?.Handle;
        if (popupHandle == GetNativeKeyboardFocus()) return;
        ++_findFocusRevision;
        Keyboard.ClearFocus();
        tab.Browser.GetBrowserHost()?.SetFocus(true);
    }

    private void CloseFindPanel()
    {
        if (_activeTab == null) return;
        var state = GetOrCreateFindState(_activeTab);
        state.IsOpen = false;
        state.ResetResults();
        if (_activeTab.Browser.IsBrowserInitialized)
            _activeTab.Browser.GetBrowserHost()?.StopFinding(true);
        ++_findFocusRevision;
        _focusFindInputOnOpen = false;
        SyncFindPanelVisibility();
        FindCounter.Text = state.Counter;
        _activeTab.Browser.GetBrowserHost()?.SetFocus(true);
    }

    private void FindInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isUpdatingFindInputProgrammatically || _activeTab == null) return;
        var state = GetOrCreateFindState(_activeTab);
        if (!state.IsOpen) return;
        state.Query = FindInput.Text;
        state.ResetResults();
        FindCounter.Text = state.Counter;
        if (!_activeTab.Browser.IsBrowserInitialized) return;
        var host = _activeTab.Browser.GetBrowserHost();
        if (state.Query.Length == 0) host?.StopFinding(true);
        else host?.Find(state.Query, forward: true, matchCase: false, findNext: false);
    }

    private void FindInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                FindNextMatch(!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
                break;
            case Key.Down:
                FindNextMatch(true);
                e.Handled = true;
                break;
            case Key.Up:
                FindNextMatch(false);
                e.Handled = true;
                break;
            case Key.Escape:
                CloseFindPanel();
                e.Handled = true;
                break;
        }
    }

    private void FindNextMatch(bool forward)
    {
        if (_activeTab == null || !_activeTab.Browser.IsBrowserInitialized) return;
        var state = GetOrCreateFindState(_activeTab);
        if (!state.IsOpen || state.Query.Length == 0) return;
        _activeTab.Browser.GetBrowserHost()?.Find(
            state.Query, forward, matchCase: false, findNext: true);
    }

    private void FindPageButton_Click(object sender, RoutedEventArgs e) => OpenFindPanel();
    private void FindPreviousButton_Click(object sender, RoutedEventArgs e) => FindNextMatch(false);
    private void FindNextButton_Click(object sender, RoutedEventArgs e) => FindNextMatch(true);
    private void FindCloseButton_Click(object sender, RoutedEventArgs e) => CloseFindPanel();

    private void ApplyFindResult(TabItem tab, CefSharp.Wpf.HwndHost.ChromiumWebBrowser browser,
        int identifier, int count, int ordinal)
    {
        if (!_tabs.Contains(tab) || !ReferenceEquals(tab.Browser, browser) ||
            !_findStates.TryGetValue(tab, out var state) || !state.IsOpen ||
            tab.Browser.IsLoading) return;
        if (!state.ApplyResult(identifier, count, ordinal)) return;
        if (ReferenceEquals(_activeTab, tab)) FindCounter.Text = state.Counter;
    }

    private void ResetFindResultsForNavigation(TabItem tab)
    {
        if (!_findStates.TryGetValue(tab, out var state)) return;
        state.ResetResults();
        if (ReferenceEquals(_activeTab, tab)) FindCounter.Text = state.Counter;
    }

    private void RestartFindAfterNavigation(TabItem tab)
    {
        if (!_findStates.TryGetValue(tab, out var state) || !state.IsOpen ||
            state.Query.Length == 0 || !tab.Browser.IsBrowserInitialized) return;
        tab.Browser.GetBrowserHost()?.Find(
            state.Query, forward: true, matchCase: false, findNext: false);
    }

    private void RemoveFindState(TabItem tab)
    {
        if (!_findStates.Remove(tab, out var state)) return;
        state.IsOpen = false;
        if (ReferenceEquals(tab, _activeTab)) SyncFindPanelVisibility();
        if (tab.Browser.IsBrowserInitialized)
            tab.Browser.GetBrowserHost()?.StopFinding(true);
    }

    private void RevealActiveBrowser(TabItem tab, int activationRevision)
    {
        if (!tab.Browser.IsBrowserInitialized) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (activationRevision != _tabActivationRevision || !ReferenceEquals(tab, _activeTab)) return;

            // 1. Yeni aktif sekmeyi en üst Z-Index katmanına getir ve görünür yap
            BrowserHost.UpdateLayout();
            System.Windows.Controls.Panel.SetZIndex(tab.Browser, 10);
            tab.Browser.SetPresentationActive(true);
            tab.Browser.InvalidateArrange();
            BrowserHost.UpdateLayout();

            // 2. Eski sekmeleri hemen gizlemek yerine arkada (ZIndex = 0) tut;
            // yeni sekmenin ilk karesi boyandıktan sonra pasif sekmeleri deaktive et.
            foreach (var other in _tabs.Where(other =>
                         !ReferenceEquals(other, tab) && other.IsBrowserAttached))
            {
                System.Windows.Controls.Panel.SetZIndex(other.Browser, 0);
            }
            BrowserHost.UpdateLayout();

            if (ReferenceEquals(tab, _activeTab) && !AddressBar.IsKeyboardFocusWithin)
            {
                tab.Browser.Focus();
                tab.Browser.GetBrowserHost()?.SetFocus(true);
            }

            // Yeni sekmenin render çıktısı oluştuktan sonra arka plandaki eski sekmelerin görünürlüğünü kapat (z-order swap)
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
            {
                if (activationRevision != _tabActivationRevision || !ReferenceEquals(tab, _activeTab)) return;

                foreach (var other in _tabs.Where(other =>
                             !ReferenceEquals(other, tab) && other.IsBrowserAttached))
                {
                    other.Browser.SetPresentationActive(false);
                }
                BrowserHost.UpdateLayout();
            });
        });
    }

    /// <summary>
    /// Chrome yalnızca bitişik iki PASİF sekmenin arasına ayraç çizer;
    /// aktif sekmeye komşu olan ayraçlar gizlenir.
    /// </summary>
    private void UpdateTabSeparators()
    {
        for (var i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].ShowLeftSeparator = i > 0 && !_tabs[i].IsActive && !_tabs[i - 1].IsActive;
            _tabs[i].ShowRightSeparator = i == _tabs.Count - 1 && !_tabs[i].IsActive;
        }
    }

    private async Task MaintainBackgroundTabsAsync()
    {
        foreach (var tab in _tabs.Where(tab => tab.IsBrowserAttached && tab.Browser.IsBrowserInitialized).ToList())
        {
            try
            {
                var response = await tab.Browser.EvaluateScriptAsync(
                    "Array.from(document.querySelectorAll('audio,video')).some(m => !m.paused && !m.ended && !m.muted && m.volume > 0)");
                tab.IsAudible = response.Success && response.Result is true;
            }
            catch
            {
                // Sayfa kapanırken/renderer yeniden başlarken bir sonraki turda tekrar dene.
            }
        }

        var nowUtc = DateTime.UtcNow;
        var idleThreshold = TimeSpan.FromMinutes(10);
        foreach (var tab in _tabs.Where(tab =>
                     BrowserTabPolicy.ShouldSuspend(
                         ReferenceEquals(tab, _activeTab),
                         tab.IsPinned,
                         tab.IsAudible,
                         tab.Browser.IsLoading,
                         tab.IsBrowserAttached,
                         tab.LastActivatedUtc,
                         nowUtc,
                         idleThreshold)).ToList())
        {
            SuspendTab(tab);
        }
    }

    private void SuspendTab(TabItem tab)
    {
        if (!tab.IsBrowserAttached || ReferenceEquals(tab, _activeTab)) return;

        DetachAddressSubscription(tab);
        BrowserHost.Children.Remove(tab.Browser);
        tab.ResetBrowser();
        ConfigureBrowser(tab);
    }

    private void DetachAddressSubscription(TabItem tab)
    {
        RemoveFindState(tab);
        tab.Browser.FindHandler = null;
        if (_addressChangeHandlers.Remove(tab, out var addressSubscription))
        {
            addressSubscription.Descriptor.RemoveValueChanged(tab.Browser, addressSubscription.Handler);
        }
    }

    private void CloseTab(TabItem tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0) return;

        _closedTabAddresses.Push(IsNewTabAddress(tab.Address) ? string.Empty : tab.Address);

        DetachAddressSubscription(tab);

        BrowserHost.Children.Remove(tab.Browser);
        tab.Browser.Dispose();
        _tabs.RemoveAt(index);
        UpdateTabWidths();
        ScheduleSessionSave();

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
        else
        {
            UpdateTabSeparators();
        }
    }

    private void AddressBar_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsInitialized || _isUpdatingAddressBarProgrammatically) return;
        if (!AddressBar.IsKeyboardFocusWithin)
        {
            _inlineAutocompleteState = InlineAutocompleteState.Inactive(AddressBar.Text);
            CloseAddressSuggestions();
            return;
        }

        // Text composition tamamlanmadan düzenleme alanına seçili bir suffix
        // eklemek IME'nin kendi composition aralığını bozar.
        if (_isAddressTextInputInProgress || _isImeCompositionActive) return;

        _addressBarHasPendingRevert = false;
        var origin = _suppressInlineCompletionOnce
            ? OmniboxTextChangeOrigin.Paste
            : OmniboxTextChangeOrigin.UserTyping;
        _suppressInlineCompletionOnce = false;
        ProcessAddressBarUserEdit(AddressBar.Text, origin);
    }

    private void AddressBar_PreviewTextInputStart(object sender, TextCompositionEventArgs e)
    {
        _isAddressTextInputInProgress = true;
        _isImeCompositionActive = true;

        // Normal klavye girişi de WPF text-composition hattından geçer. Aktif
        // suffix'i önce gerçek kullanıcı metnine indirgemek yeni karakterin
        // doğru caret konumuna eklenmesini sağlar.
        if (_inlineAutocompleteState.IsInlineCompletionActive)
            CancelInlineCompletion(restoreUserText: true, caretIndex: _inlineAutocompleteState.UserText.Length);
    }

    private void AddressBar_PreviewTextInputUpdate(object sender, TextCompositionEventArgs e)
    {
        _isAddressTextInputInProgress = true;
        _isImeCompositionActive = true;
    }

    private void AddressBar_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Final PreviewTextInput'ta composition bitmiştir, fakat TextBox metni
        // henüz değiştirmemiştir. In-progress bayrağı dispatcher işine kadar
        // açık kalarak ara TextChanged olayının suffix üretmesini engeller.
        _isImeCompositionActive = false;
        var revision = ++_addressEditRevision;

        // TextBox kendi TextInput işlemini ve seçim güncellemesini bitirdikten
        // sonra suffix'i uygula; aksi halde WPF seçimi tekrar caret'e çevirir.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (revision != _addressEditRevision) return;
            _isAddressTextInputInProgress = false;
            if (!IsLoaded ||
                !AddressBar.IsKeyboardFocusWithin ||
                _isImeCompositionActive)
            {
                return;
            }

            ProcessAddressBarUserEdit(AddressBar.Text, OmniboxTextChangeOrigin.UserTyping);
        });
    }

    private void AddressBar_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (_inlineAutocompleteState.IsInlineCompletionActive)
            CancelInlineCompletion(restoreUserText: true, caretIndex: _inlineAutocompleteState.UserText.Length);
        _suppressInlineCompletionOnce = true;
    }

    private void AddressBar_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Mouse ile ilk focus, click mi yoksa metin seçme sürüklemesi mi olduğu
        // mouse-up'ta anlaşıldıktan sonra tamamlanır.
        if (_addressBarMouseFocusPending) return;

        if (AddressBarInteractionPolicy.ShouldSelectAllAfterTabReturn(
                _addressBarFocusExitedByTab,
                _addressBarMouseFocusPending))
        {
            _addressBarFocusExitedByTab = false;
            // Chrome'da Tab traversal ile omnibox'a geri gelmek mevcut adresi
            // tümü seçili düzenleme durumuna getirir.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (!AddressBar.IsKeyboardFocusWithin) return;
                AddressBar.SelectAll();
                ShowOnFocusAddressSuggestions();
            });
            return;
        }

        ShowOnFocusAddressSuggestions();
    }

    private void AddressBar_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ++_addressEditRevision;
        _isAddressTextInputInProgress = false;
        _isImeCompositionActive = false;
        _addressBarMouseFocusPending = false;
        _addressBarMouseExceededDragThreshold = false;
    }

    private void AddressBarContainer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(
                FindVisualAncestor<System.Windows.Controls.TextBox>(e.OriginalSource as DependencyObject),
                AddressBar))
        {
            return;
        }

        var hadKeyboardFocus = AddressBar.IsKeyboardFocusWithin;
        _addressBarFocusExitedByTab = false;
        if (hadKeyboardFocus && _inlineAutocompleteState.IsInlineCompletionActive)
        {
            // TextBox click'i bundan sonra doğal caret konumunu hesaplar.
            CancelInlineCompletion(
                restoreUserText: true,
                caretIndex: _inlineAutocompleteState.UserText.Length);
        }
        _addressBarMouseFocusPending = !hadKeyboardFocus;
        _addressBarMouseExceededDragThreshold = false;
        _addressBarMouseDownPoint = e.GetPosition(AddressBar);
    }

    private void AddressBar_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_addressBarMouseFocusPending || e.LeftButton != MouseButtonState.Pressed) return;

        var current = e.GetPosition(AddressBar);
        if (Math.Abs(current.X - _addressBarMouseDownPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(current.Y - _addressBarMouseDownPoint.Y) >= SystemParameters.MinimumVerticalDragDistance)
        {
            _addressBarMouseExceededDragThreshold = true;
        }
    }

    private void AddressBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_addressBarMouseFocusPending) return;

        var shouldSelectAll = AddressBarInteractionPolicy.ShouldSelectAllAfterMouseFocus(
            hadKeyboardFocus: false,
            _addressBarMouseExceededDragThreshold);
        _addressBarMouseFocusPending = false;
        _addressBarMouseExceededDragThreshold = false;

        // TextBox'ın kendi mouse-up caret/seçim işlemi tamamlandıktan sonra
        // yalnızca düz bir ilk tıklamada tüm metni seç.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!AddressBar.IsKeyboardFocusWithin) return;
            if (shouldSelectAll) AddressBar.SelectAll();
            ShowOnFocusAddressSuggestions();
        });
    }

    private void AddressBar_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab &&
            Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            var tabDecision = OmniboxTabNavigationPolicy.Resolve(
                AddressSuggestionsPopup.IsOpen,
                _addressSuggestions.Count,
                AddressSuggestionsList.SelectedIndex,
                reverse: Keyboard.Modifiers == ModifierKeys.Shift);

            PrepareAddressBarForTabNavigation();
            if (tabDecision.ClosePopup) CloseAddressSuggestions();

            if (tabDecision.Handled)
            {
                AddressSuggestionsList.SelectedIndex = tabDecision.SelectedIndex;
                AddressSuggestionsList.ScrollIntoView(AddressSuggestionsList.SelectedItem);
                e.Handled = true;
            }
            else
            {
                // Normal WPF focus traversal bu noktadan sonra çalışır. State
                // önceden temizlendiği için geri dönüşte seçili suffix kalmaz.
                _addressBarFocusExitedByTab = tabDecision.ExitAddressBar;
            }
            return;
        }

        if (e.Key is Key.Down or Key.Up && AddressSuggestionsPopup.IsOpen && _addressSuggestions.Count > 0)
        {
            MoveAddressSuggestionSelection(e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back && Keyboard.Modifiers == ModifierKeys.None)
        {
            string userText;
            int caretIndex;
            if (_inlineAutocompleteState.IsInlineCompletionActive)
            {
                userText = InlineAutocompleteInteractionPolicy.BackspaceText(
                    _inlineAutocompleteState,
                    AddressBar.Text);
                caretIndex = userText.Length;
            }
            else if (AddressBar.SelectionLength > 0)
            {
                caretIndex = AddressBar.SelectionStart;
                userText = AddressBar.Text.Remove(AddressBar.SelectionStart, AddressBar.SelectionLength);
            }
            else if (AddressBar.CaretIndex > 0)
            {
                caretIndex = AddressBar.CaretIndex - 1;
                userText = AddressBar.Text.Remove(caretIndex, 1);
            }
            else
            {
                return;
            }

            SetAddressBarUserText(userText, caretIndex);
            ProcessAddressBarUserEdit(userText, OmniboxTextChangeOrigin.Backspace);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && _inlineAutocompleteState.IsInlineCompletionActive)
        {
            var userText = _inlineAutocompleteState.UserText;
            CancelInlineCompletion(restoreUserText: true, caretIndex: userText.Length);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left && _inlineAutocompleteState.IsInlineCompletionActive)
        {
            var userText = _inlineAutocompleteState.UserText;
            CancelInlineCompletion(restoreUserText: true, caretIndex: Math.Max(0, userText.Length - 1));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right && _inlineAutocompleteState.IsInlineCompletionActive)
        {
            AcceptInlineCompletionInEditor();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            var expectedAddress = GetActiveTabExpectedAddress();
            var isModified = !string.Equals(AddressBar.Text, expectedAddress, StringComparison.Ordinal);

            if (_inlineAutocompleteState.IsInlineCompletionActive)
            {
                var userText = _inlineAutocompleteState.UserText;
                CancelInlineCompletion(restoreUserText: true, caretIndex: userText.Length);
            }
            CloseAddressSuggestions();

            if (isModified && _addressBarHasPendingRevert)
            {
                // 2. ESC Basımı: Kullanıcının yazdığı geçici metin iptal edilip orijinal URL'e geri dönmeli.
                _addressBarHasPendingRevert = false;
                if (_activeTab != null)
                {
                    SetAddressBarTextFromTab(_activeTab);
                }
                FocusActiveContentArea();
            }
            else if (isModified)
            {
                // 1. ESC Basımı: Metin silinmemeli/orijinal URL'e dönmemeli; sadece adres çubuğundaki odak kaldırılmalı (blur),
                // inline tamamlama ve öneri açılır penceresi kapatılmalı, odak doğrudan webview alanına aktarılmalı.
                _addressBarHasPendingRevert = true;
                FocusActiveContentArea();
            }
            else
            {
                _addressBarHasPendingRevert = false;
                FocusActiveContentArea();
            }

            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter || _activeTab == null) return;

        var selectedSuggestion = AddressSuggestionsPopup.IsOpen
            ? AddressSuggestionsList.SelectedItem as OmniboxSuggestion
            : null;
        var decision = OmniboxEnterResolver.Resolve(
            selectedSuggestion,
            _inlineAutocompleteState,
            _inlineAutocompleteState.IsInlineCompletionActive
                ? _inlineAutocompleteState.UserText
                : AddressBar.Text,
            _appSettings.SearchEngine);

        if (decision.Kind == OmniboxEnterActionKind.SelectedSuggestion && decision.Suggestion != null)
        {
            e.Handled = true;
            QueueAddressSuggestionCommit(decision.Suggestion);
            return;
        }

        if (decision.Kind == OmniboxEnterActionKind.None || decision.DestinationUrl == null) return;

        e.Handled = true;
        NavigateActiveTabFromAddressBar(decision.DestinationUrl);
    }

    private void ShowOnFocusAddressSuggestions()
    {
        if (_addressSuggestionMode == AddressSuggestionMode.OnFocus && AddressSuggestionsPopup.IsOpen) return;
        RefreshAddressSuggestions(string.Empty, AddressSuggestionMode.OnFocus);
    }

    private void FocusAddressBarAndSelectAll()
    {
        _addressBarFocusExitedByTab = false;
        _addressBarMouseFocusPending = false;
        _addressBarMouseExceededDragThreshold = false;
        if (_inlineAutocompleteState.IsInlineCompletionActive)
            AcceptInlineCompletionInEditor(refreshSuggestions: false);
        AddressBar.Focus();
        if (AddressBarInteractionPolicy.ShouldSelectAllForShortcut())
            AddressBar.SelectAll();
        ShowOnFocusAddressSuggestions();
    }

    private void FocusActiveContentArea()
    {
        Keyboard.ClearFocus();
        if (_activeTab is { IsBrowserAttached: true, Browser.IsBrowserInitialized: true } activeTab)
        {
            activeTab.Browser.Focus();
            activeTab.Browser.GetBrowserHost()?.SetFocus(true);
        }
        else
        {
            BrowserHost.Focus();
        }
    }

    private void PrepareAddressBarForTabNavigation()
    {
        var userText = _inlineAutocompleteState.IsInlineCompletionActive
            ? _inlineAutocompleteState.UserText
            : AddressBar.Text;
        SetAddressBarUserText(userText, userText.Length);
    }

    private void RefreshAddressSuggestions(
        string query,
        AddressSuggestionMode mode,
        IReadOnlyList<OmniboxSuggestionCandidate>? existingCandidates = null)
    {
        var candidates = existingCandidates ?? BuildAddressSuggestionCandidates();
        var matches = mode == AddressSuggestionMode.OnFocus
            ? OmniboxSuggestionService.FindOnFocus(candidates)
            : OmniboxSuggestionService.FindWithVerbatim(
                query,
                candidates,
                DateTime.UtcNow,
                _appSettings.SearchEngine,
                string.Format(
                    CultureInfo.CurrentUICulture,
                    Strings.SearchWithEngine,
                    _appSettings.SearchEngine),
                Strings.GoToAddress);

        _addressSuggestionMode = mode;
        _addressSuggestions.Clear();
        foreach (var match in matches)
            _addressSuggestions.Add(OmniboxSuggestionPresentation.Prepare(match));
        AddressSuggestionsList.SelectedIndex = -1;
        AddressSuggestionsPopup.IsOpen = _addressSuggestions.Count > 0;
        if (AddressSuggestionsPopup.IsOpen) RepositionAddressSuggestionsPopup();
        else _addressSuggestionMode = AddressSuggestionMode.Closed;
    }

    private void ProcessAddressBarUserEdit(string userText, OmniboxTextChangeOrigin origin)
    {
        if (_isUpdatingAddressBarProgrammatically || !AddressBar.IsKeyboardFocusWithin) return;

        _inlineAutocompleteState = InlineAutocompleteState.Inactive(userText);
        var candidates = BuildAddressSuggestionCandidates();
        RefreshAddressSuggestions(userText, AddressSuggestionMode.UserInput, candidates);

        if (!InlineAutocompleteInteractionPolicy.AllowsCompletion(origin) ||
            _isImeCompositionActive ||
            AddressBar.SelectionLength != 0 ||
            AddressBar.CaretIndex != AddressBar.Text.Length)
        {
            return;
        }

        var completion = InlineAutocompleteService.Find(userText, candidates);
        if (!completion.IsInlineCompletionActive) return;

        ApplyInlineCompletion(completion);
    }

    private void ApplyInlineCompletion(InlineAutocompleteState completion)
    {
        _inlineAutocompleteState = completion;
        _isUpdatingAddressBarProgrammatically = true;
        try
        {
            AddressBar.Text = completion.FillText;
            AddressBar.Select(completion.UserText.Length, completion.InlineSuffix.Length);
        }
        finally
        {
            _isUpdatingAddressBarProgrammatically = false;
        }
    }

    private void CancelInlineCompletion(bool restoreUserText, int caretIndex)
    {
        if (!_inlineAutocompleteState.IsInlineCompletionActive) return;

        var userText = _inlineAutocompleteState.UserText;
        _inlineAutocompleteState = InlineAutocompleteState.Inactive(userText);
        if (!restoreUserText) return;

        SetAddressBarUserText(userText, caretIndex);
    }

    private void AcceptInlineCompletionInEditor(bool refreshSuggestions = true)
    {
        if (!_inlineAutocompleteState.IsInlineCompletionActive) return;

        var fillText = _inlineAutocompleteState.FillText;
        _inlineAutocompleteState = InlineAutocompleteState.Inactive(fillText);
        SetAddressBarUserText(fillText, fillText.Length);
        if (refreshSuggestions)
            RefreshAddressSuggestions(fillText, AddressSuggestionMode.UserInput);
    }

    private void SetAddressBarUserText(string text, int caretIndex)
    {
        _inlineAutocompleteState = InlineAutocompleteState.Inactive(text);
        _isUpdatingAddressBarProgrammatically = true;
        try
        {
            AddressBar.Text = text;
            AddressBar.CaretIndex = Math.Clamp(caretIndex, 0, text.Length);
            AddressBar.SelectionLength = 0;
        }
        finally
        {
            _isUpdatingAddressBarProgrammatically = false;
        }
    }

    private List<OmniboxSuggestionCandidate> BuildAddressSuggestionCandidates()
    {
        var candidates = new List<OmniboxSuggestionCandidate>();
        var sourceOrder = 0;

        foreach (var window in Application.Current.Windows.OfType<MainWindow>())
        {
            foreach (var tab in window._tabs)
            {
                if (IsNewTabAddress(tab.Address)) continue;
                candidates.Add(new OmniboxSuggestionCandidate(
                    tab.Title,
                    tab.Address,
                    OmniboxSuggestionSource.OpenTab,
                    tab.LastActivatedUtc,
                    tab.Id,
                    sourceOrder++));
            }
        }

        foreach (var bookmark in _bookmarkStore.Bookmarks)
        {
            candidates.Add(new OmniboxSuggestionCandidate(
                bookmark.Title,
                bookmark.Url,
                OmniboxSuggestionSource.Bookmark,
                SourceOrder: sourceOrder++));
        }

        foreach (var entry in _historyStore.Entries)
        {
            candidates.Add(new OmniboxSuggestionCandidate(
                entry.Title,
                entry.Url,
                OmniboxSuggestionSource.History,
                entry.VisitedAt,
                SourceOrder: sourceOrder++));
        }

        return candidates;
    }

    private void MoveAddressSuggestionSelection(int offset)
    {
        if (_addressSuggestions.Count == 0) return;
        var current = AddressSuggestionsList.SelectedIndex;
        var next = current < 0
            ? (offset > 0 ? 0 : _addressSuggestions.Count - 1)
            : (current + offset + _addressSuggestions.Count) % _addressSuggestions.Count;
        AddressSuggestionsList.SelectedIndex = next;
        AddressSuggestionsList.ScrollIntoView(AddressSuggestionsList.SelectedItem);
    }

    private void AddressSuggestion_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Satır içindeki geçmiş-sil düğmesi kendi eylemini gerçekleştirmeli;
        // ListBox'ın önizleme olayı düğmeden önce gezinmeyi başlatmamalı.
        if (FindVisualAncestor<System.Windows.Controls.Primitives.ButtonBase>(
                e.OriginalSource as DependencyObject) != null)
        {
            return;
        }

        var item = FindVisualAncestor<System.Windows.Controls.ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not OmniboxSuggestion suggestion) return;
        AddressSuggestionsList.SelectedItem = suggestion;
        // Popup'ın kendi HWND/görsel ağacını MouseDown route edilirken sökme.
        // Commit, MouseUp olayı bittikten sonra yapılır.
        e.Handled = true;
    }

    private void AddressSuggestion_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualAncestor<System.Windows.Controls.Primitives.ButtonBase>(
                e.OriginalSource as DependencyObject) != null)
        {
            return;
        }

        var item = FindVisualAncestor<System.Windows.Controls.ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not OmniboxSuggestion suggestion) return;

        e.Handled = true;
        QueueAddressSuggestionCommit(suggestion);
    }

    private void QueueAddressSuggestionCommit(OmniboxSuggestion suggestion)
    {
        if (_addressSuggestionCommitPending) return;
        _addressSuggestionCommitPending = true;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _addressSuggestionCommitPending = false;
            if (!IsLoaded || !IsVisible) return;
            AcceptAddressSuggestion(suggestion);
        });
    }

    private void RemoveAddressSuggestionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement
            {
                Tag: OmniboxSuggestion { Source: OmniboxSuggestionSource.History } suggestion,
            })
        {
            return;
        }

        e.Handled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!IsLoaded) return;
            var query = _inlineAutocompleteState.IsInlineCompletionActive
                ? _inlineAutocompleteState.UserText
                : AddressBar.Text;
            if (_inlineAutocompleteState.IsInlineCompletionActive)
                CancelInlineCompletion(restoreUserText: true, caretIndex: query.Length);
            _historyStore.RemoveByUrl(suggestion.Url);
            AddressBar.Focus();
            RefreshAddressSuggestions(query, AddressSuggestionMode.UserInput);
        });
    }

    private void AcceptAddressSuggestion(OmniboxSuggestion suggestion)
    {
        var target = suggestion.Source == OmniboxSuggestionSource.OpenTab && suggestion.OpenTabId.HasValue
            ? Application.Current.Windows
                .OfType<MainWindow>()
                .Where(window => window.IsLoaded && window.IsVisible)
                .SelectMany(window => window._tabs)
                .FirstOrDefault(tab => tab.Id == suggestion.OpenTabId.Value)
            : null;

        _inlineAutocompleteState = InlineAutocompleteState.Inactive(AddressBar.Text);
        CloseAddressSuggestions();
        Keyboard.ClearFocus();

        if (target?.Owner is { } targetOwner && targetOwner._tabs.Contains(target))
        {
            targetOwner.Activate();
            targetOwner.SwitchTab(target);
            if (target.IsBrowserAttached && target.Browser.IsBrowserInitialized)
            {
                target.Browser.Focus();
                target.Browser.GetBrowserHost()?.SetFocus(true);
            }
            return;
        }

        NavigateActiveTabFromAddressBar(suggestion.Url, closePopupAndClearFocus: false);
    }

    private void NavigateActiveTabFromAddressBar(
        string destinationUrl,
        bool closePopupAndClearFocus = true)
    {
        if (closePopupAndClearFocus)
        {
            _addressBarHasPendingRevert = false;
            _inlineAutocompleteState = InlineAutocompleteState.Inactive(AddressBar.Text);
            CloseAddressSuggestions();
            Keyboard.ClearFocus();
        }

        if (IsDownloadsAddress(destinationUrl))
        {
            OpenDownloadsPage();
            return;
        }

        var activeTab = _activeTab;
        if (activeTab == null ||
            !ReferenceEquals(activeTab.Owner, this) ||
            !_tabs.Contains(activeTab))
        {
            return;
        }

        activeTab.Browser.Address = destinationUrl;
        if (!activeTab.IsBrowserAttached || !activeTab.Browser.IsBrowserInitialized) return;

        activeTab.Browser.Focus();
        activeTab.Browser.GetBrowserHost()?.SetFocus(true);
    }

    private void CloseAddressSuggestions()
    {
        AddressSuggestionsPopup.IsOpen = false;
        AddressSuggestionsList.SelectedIndex = -1;
        _addressSuggestionMode = AddressSuggestionMode.Closed;
    }

    private void AddressSuggestionsPopup_Closed(object? sender, EventArgs e)
    {
        AddressSuggestionsList.SelectedIndex = -1;
        _addressSuggestionMode = AddressSuggestionMode.Closed;
    }

    private void SetAddressBarTextFromTab(TabItem tab)
    {
        CloseAddressSuggestions();
        _addressBarHasPendingRevert = false;
        ++_addressEditRevision;
        _isAddressTextInputInProgress = false;
        _isImeCompositionActive = false;
        _isUpdatingAddressBarProgrammatically = true;
        try
        {
            var text = IsNewTabAddress(tab.Address) ? string.Empty : tab.Address;
            _inlineAutocompleteState = InlineAutocompleteState.Inactive(text);
            AddressBar.Text = text;
        }
        finally
        {
            _isUpdatingAddressBarProgrammatically = false;
        }
    }

    private void RepositionAddressSuggestionsPopup()
    {
        if (!AddressSuggestionsPopup.IsOpen) return;
        var offset = AddressSuggestionsPopup.VerticalOffset;
        AddressSuggestionsPopup.VerticalOffset = offset + 0.01;
        AddressSuggestionsPopup.VerticalOffset = offset;
    }

    private void RestoreSession()
    {
        if (!_appSettings.RestoreSession)
        {
            AddTab();
            return;
        }

        var session = BrowserSessionStore.Load();
        if (session?.Tabs.Count is not > 0)
        {
            AddTab();
            return;
        }

        for (var index = 0; index < session.Tabs.Count; index++)
        {
            var address = session.Tabs[index];
            var title = index < session.Titles.Count ? session.Titles[index] : null;
            var favicon = index < session.Favicons.Count ? session.Favicons[index] : null;
            AddTab(
                string.IsNullOrWhiteSpace(address) ? null : address,
                activate: false,
                title: title,
                faviconUrl: favicon,
                isPinned: session.PinnedIndices.Contains(index));
        }

        SwitchTab(_tabs[session.ActiveIndex]);
    }

    private void ScheduleSessionSave()
    {
        _sessionSaveTimer.Stop();
        _sessionSaveTimer.Start();
    }

    private void SaveSession()
    {
        var windows = Application.Current.Windows
            .OfType<MainWindow>()
            .Where(window => !window._isClosing && window._tabs.Count > 0)
            .OrderByDescending(window => window.IsActive)
            .ToList();
        if (windows.Count == 0 && _tabs.Count > 0) windows.Add(this);

        var tabs = windows.SelectMany(window => window._tabs).ToList();
        var activeTab = windows.Select(window => window._activeTab).FirstOrDefault(tab => tab != null);
        BrowserSessionStore.Save(
            tabs.Select(tab => IsNewTabAddress(tab.Address) ? string.Empty : tab.Address),
            tabs.Select(tab => tab.Title),
            tabs.Select(tab => tab.FaviconUrl),
            tabs.Select((tab, index) => (tab, index)).Where(item => item.tab.IsPinned).Select(item => item.index),
            activeTab == null ? 0 : tabs.IndexOf(activeTab));
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (e.Key == Key.Tab && (_draggedTab != null || DetachedTabDragSession.IsActive))
        {
            // Fareyle sekme taşınırken WPF odak sırasına girip siyah focus
            // çerçeveleri üretme; sürükleme bitene kadar Tab ailesini yut.
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && (_draggedTab != null || DetachedTabDragSession.IsActive))
        {
            Mouse.Capture(null);
            if (DetachedTabDragSession.IsActive) DetachedTabDragSession.Cancel();
            ResetTabDragState();
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.L)
        {
            FocusAddressBarAndSelectAll();
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.F)
        {
            OpenFindPanel();
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.T)
        {
            AddTab();
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.W && _activeTab != null)
        {
            CloseTab(_activeTab);
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.T)
        {
            ReopenLastClosedTab();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Tab)
        {
            SelectRelativeTab(shift ? -1 : 1);
            e.Handled = true;
        }
        else if (!shift && ((ctrl && e.Key == Key.R) || e.Key == Key.F5))
        {
            if (_activeTab != null) ReloadTab(_activeTab, ignoreCache: false);
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.R)
        {
            if (_activeTab != null) ReloadTab(_activeTab, ignoreCache: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _addressBarHasPendingRevert)
        {
            _addressBarHasPendingRevert = false;
            if (_activeTab != null)
            {
                SetAddressBarTextFromTab(_activeTab);
            }
            FocusActiveContentArea();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && FindPanelPopup.IsOpen)
        {
            CloseFindPanel();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isFullScreen)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.H)
        {
            OpenHistoryPopup();
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.J)
        {
            OpenDownloadsPage();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.SystemKey == Key.Left)
        {
            if (_activeTab?.Browser.CanGoBack == true) _activeTab.Browser.Back();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.SystemKey == Key.Right)
        {
            if (_activeTab?.Browser.CanGoForward == true) _activeTab.Browser.Forward();
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key is >= Key.D1 and <= Key.D8)
        {
            var index = e.Key - Key.D1;
            if (index < _tabs.Count) SwitchTab(_tabs[index]);
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.D9 && _tabs.Count > 0)
        {
            SwitchTab(_tabs[^1]);
            e.Handled = true;
        }
    }

    private void SelectRelativeTab(int offset)
    {
        if (_activeTab == null || _tabs.Count < 2) return;
        var currentIndex = _tabs.IndexOf(_activeTab);
        var nextIndex = (currentIndex + offset + _tabs.Count) % _tabs.Count;
        SwitchTab(_tabs[nextIndex]);
    }

    private void ExecuteBrowserShortcut(BrowserShortcut shortcut)
    {
        switch (shortcut)
        {
            case BrowserShortcut.Reload when _activeTab != null:
                ReloadTab(_activeTab, ignoreCache: false);
                break;
            case BrowserShortcut.HardReload when _activeTab != null:
                ReloadTab(_activeTab, ignoreCache: true);
                break;
            case BrowserShortcut.NextTab:
                SelectRelativeTab(1);
                break;
            case BrowserShortcut.PreviousTab:
                SelectRelativeTab(-1);
                break;
            case BrowserShortcut.NewTab:
                AddTab();
                break;
            case BrowserShortcut.CloseTab when _activeTab != null:
                CloseTab(_activeTab);
                break;
            case BrowserShortcut.ReopenClosedTab:
                ReopenLastClosedTab();
                break;
            case BrowserShortcut.FocusAddressBar:
                FocusAddressBarAndSelectAll();
                break;
            case BrowserShortcut.FindInPage:
                OpenFindPanel();
                break;
            case BrowserShortcut.CloseFindInPage when FindPanelPopup.IsOpen:
                CloseFindPanel();
                break;
            case BrowserShortcut.History:
                OpenHistoryPopup();
                break;
            case BrowserShortcut.Downloads:
                OpenDownloadsPage();
                break;
            case BrowserShortcut.Back when _activeTab?.Browser.CanGoBack == true:
                _activeTab.Browser.Back();
                break;
            case BrowserShortcut.Forward when _activeTab?.Browser.CanGoForward == true:
                _activeTab.Browser.Forward();
                break;
            case BrowserShortcut.ToggleFullScreen:
                ToggleFullScreen();
                break;
            case BrowserShortcut.SelectLastTab when _tabs.Count > 0:
                SwitchTab(_tabs[^1]);
                break;
            case BrowserShortcut.SelectTab1:
            case BrowserShortcut.SelectTab2:
            case BrowserShortcut.SelectTab3:
            case BrowserShortcut.SelectTab4:
            case BrowserShortcut.SelectTab5:
            case BrowserShortcut.SelectTab6:
            case BrowserShortcut.SelectTab7:
            case BrowserShortcut.SelectTab8:
                var index = (int)shortcut - (int)BrowserShortcut.SelectTab1;
                if (index < _tabs.Count) SwitchTab(_tabs[index]);
                break;
        }
    }

    private void ToggleFullScreen()
    {
        if (!_isFullScreen)
        {
            _isFullScreen = true;
            _windowStateBeforeFullScreen = WindowState;
            _resizeModeBeforeFullScreen = ResizeMode;
            WindowMaximizeFix.SetIsFullScreen(this, true);
            WindowState = WindowState.Normal;
            TabBar.Visibility = Visibility.Collapsed;
            NavigationBar.Visibility = Visibility.Collapsed;
            BookmarksBar.Visibility = Visibility.Collapsed;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowMaximizeFix.SetIsFullScreen(this, false);
            TabBar.Visibility = Visibility.Visible;
            NavigationBar.Visibility = Visibility.Visible;
            ResizeMode = _resizeModeBeforeFullScreen;
            WindowState = _windowStateBeforeFullScreen;
            UpdateBookmarksEmptyState();
            _isFullScreen = false;
        }
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) => AddTab();

    private void TabArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            FindVisualAncestor<System.Windows.Controls.Button>(e.OriginalSource as DependencyObject) != null ||
            FindTabFromVisual(e.OriginalSource as DependencyObject) != null)
            return;

        if (e.ClickCount == 2)
        {
            MaximizeButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        ReleaseCapture();
        SendMessage(handle, 0x00A1, new IntPtr(2), IntPtr.Zero); // WM_NCLBUTTONDOWN / HTCAPTION
        e.Handled = true;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private void TabBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedTab != null && _isDraggingTab)
        {
            CompleteTabDrag(_draggedTab);
        }

        Mouse.Capture(null);
        ResetTabDragState();
    }

    private void TabRoot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TabItem tab }) return;
        // Chrome'da arka plandaki bir sekmenin kapat/ses düğmesi sekmeyi önce
        // etkinleştirmez; düğmenin kendi eylemi doğrudan gerçekleşir.
        if (FindVisualAncestor<System.Windows.Controls.Button>(e.OriginalSource as DependencyObject) != null) return;
        _draggedTab = tab;
        _tabDragStart = e.GetPosition(TabStrip);
        _dragGrabOffsetX = e.GetPosition((IInputElement)sender).X;
        _dragOriginalIndex = _tabs.IndexOf(tab);
        _dragDropIndex = _dragOriginalIndex;
        _isDraggingTab = false;
        Mouse.Capture((IInputElement)sender, CaptureMode.SubTree);
        SwitchTab(tab);
    }

    private void TabRoot_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Released && _draggedTab != null)
            ResetTabDragState();
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = current switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
                ContentElement contentElement =>
                    ContentOperations.GetParent(contentElement) ??
                    (contentElement as FrameworkContentElement)?.Parent,
                _ => LogicalTreeHelper.GetParent(current),
            };
        }

        return null;
    }

    private static TabItem? FindTabFromVisual(DependencyObject? current)
    {
        while (current != null)
        {
            if (current is FrameworkElement { Tag: TabItem tab }) return tab;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void TabRoot_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedTab == null || e.LeftButton != MouseButtonState.Pressed) return;

        var pointer = e.GetPosition(TabStrip);
        if (!_isDraggingTab &&
            Math.Abs(pointer.X - _tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pointer.Y - _tabDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDraggingTab = true;
        _draggedTab.IsBeingDragged = true;
        SetDraggedTabLayer(_draggedTab, raised: true);
        BeginTabDragVisual(_draggedTab);
        Mouse.OverrideCursor = Cursors.Arrow;

        var insideOwnStrip = pointer.Y >= -20 && pointer.Y <= TabBar.ActualHeight + 20;
        if (insideOwnStrip)
        {
            UpdateInternalDrag(_draggedTab, pointer);
        }
        else
        {
            var screenPoint = PointToScreen(e.GetPosition(this));
            UpdateOutsideDrag(_draggedTab, screenPoint);
        }
    }

    private void UpdateInternalDrag(TabItem tab, Point pointer)
    {
        var rawOffset = pointer.X - _tabDragStart.X;
        var container = TabStrip.ItemContainerGenerator.ContainerFromItem(tab) as FrameworkElement;
        if (container != null)
        {
            var baseLeft = container.TranslatePoint(new Point(), TabStrip).X;
            var tabAreaRight = TabArea.TranslatePoint(new Point(TabArea.ActualWidth, 0), TabStrip).X;
            var plusSpace = NewTabButton.ActualWidth + NewTabButton.Margin.Left + NewTabButton.Margin.Right;
            var maximumRightOffset = BrowserTabPolicy.CalculateMaximumRightDragOffset(
                baseLeft, container.ActualWidth, tabAreaRight, plusSpace);
            rawOffset = Math.Clamp(rawOffset, -baseLeft, maximumRightOffset);
        }

        if (_tabDragVisual != null) Canvas.SetLeft(_tabDragVisual, _tabDragImageBaseLeft + rawOffset);
        else SetTabOffsetImmediate(tab, rawOffset);
        _dragDropIndex = CalculateInternalDropIndex(tab, rawOffset);
        ApplyInternalGap(tab, _dragDropIndex);
        UpdateNewTabButtonOffset(tab, rawOffset);
    }

    private void UpdateNewTabButtonOffset(TabItem draggedTab, double draggedOffset)
    {
        if (TabStrip.ItemContainerGenerator.ContainerFromItem(draggedTab) is not FrameworkElement container)
            return;
        var draggedLeft = container.TranslatePoint(new Point(), TabStrip).X + draggedOffset;
        var plusBaseLeft = NewTabButton.TranslatePoint(new Point(), TabStrip).X;
        var tabAreaRight = TabArea.TranslatePoint(new Point(TabArea.ActualWidth, 0), TabStrip).X;
        var offset = BrowserTabPolicy.CalculateTrailingButtonOffset(
            draggedLeft,
            container.ActualWidth,
            plusBaseLeft,
            NewTabButton.ActualWidth,
            tabAreaRight,
            _dragDropIndex == _tabs.Count - 1);
        NewTabButtonOffsetTransform.X = 0;
        if (_newTabDragVisual == null) return;
        if (offset > 0.25)
        {
            NewTabButton.Opacity = 0;
            _newTabDragVisual.Visibility = Visibility.Visible;
            Canvas.SetLeft(_newTabDragVisual, _newTabDragImageBaseLeft + offset);
        }
        else
        {
            NewTabButton.Opacity = 1;
            _newTabDragVisual.Visibility = Visibility.Collapsed;
        }
    }

    private int CalculateInternalDropIndex(TabItem draggedTab, double draggedOffset)
    {
        if (TabStrip.ItemContainerGenerator.ContainerFromItem(draggedTab) is not FrameworkElement draggedContainer)
            return _dragOriginalIndex;

        var draggedLeft = draggedContainer.TranslatePoint(new Point(), TabStrip).X;
        var draggedCenter = draggedLeft + draggedOffset + draggedContainer.ActualWidth / 2;
        var slotCenters = new List<double>();
        foreach (var tab in _tabs)
        {
            if (TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is not FrameworkElement container) continue;
            var center = container.TranslatePoint(new Point(), TabStrip).X + container.ActualWidth / 2;
            slotCenters.Add(center);
        }
        var insertionIndex = BrowserTabPolicy.CalculateDragSlotIndex(
            draggedCenter, slotCenters, _dragOriginalIndex);

        var pinnedAfterRemoval = _tabs.Count(tab => tab.IsPinned) - (draggedTab.IsPinned ? 1 : 0);
        return BrowserTabPolicy.ConstrainInsertionIndex(
            insertionIndex,
            Math.Max(0, _tabs.Count - 1),
            pinnedAfterRemoval,
            draggedTab.IsPinned);
    }

    private void ApplyInternalGap(TabItem draggedTab, int dropIndex)
    {
        var gapWidth = GetTabSlotWidth(draggedTab);
        for (var index = 0; index < _tabs.Count; index++)
        {
            var tab = _tabs[index];
            if (ReferenceEquals(tab, draggedTab)) continue;

            var offset = BrowserTabPolicy.GetInternalGapOffset(
                index, _dragOriginalIndex, dropIndex, gapWidth);
            SetTabLayer(tab, Math.Abs(offset) > 0.25 ? 50 : 0);
            AnimateTabOffset(tab, offset);
        }
    }

    private void UpdateOutsideDrag(TabItem tab, Point screenPoint)
    {
        var grabOffset = _dragGrabOffsetX;
        Mouse.Capture(null);
        ResetTabDragState();
        DetachedTabDragSession.Start(this, tab, screenPoint, grabOffset);
    }

    private void ContinueTransferredTabDrag(
        TabItem tab,
        Point screenPoint,
        double grabOffsetX,
        bool allowLayoutRetry = true)
    {
        if (!ReferenceEquals(tab.Owner, this) || !_tabs.Contains(tab)) return;
        if (!DetachedTabDragSession.IsLeftButtonPressed())
        {
            ResetTabDragState();
            return;
        }
        Activate();
        TabStrip.UpdateLayout();

        var root = GetTabRoot(tab);
        var container = TabStrip.ItemContainerGenerator.ContainerFromItem(tab) as FrameworkElement;
        if (root == null || container == null)
        {
            // TransferTabTo koleksiyonu eşzamanlı değiştirir; WPF kapsayıcıyı bir
            // sonraki layout turunda ürettiyse fiziksel mouse basışı sürerken devam et.
            if (allowLayoutRetry)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    () => ContinueTransferredTabDrag(
                        tab,
                        screenPoint,
                        grabOffsetX,
                        allowLayoutRetry: false));
            }
            return;
        }

        var pointer = TabStrip.PointFromScreen(screenPoint);
        var tabLeft = container.TranslatePoint(new Point(), TabStrip).X;
        _draggedTab = tab;
        _dragGrabOffsetX = Math.Clamp(grabOffsetX, 0, Math.Max(0, container.ActualWidth));
        _tabDragStart = new Point(tabLeft + _dragGrabOffsetX, pointer.Y);
        _dragOriginalIndex = _tabs.IndexOf(tab);
        _dragDropIndex = _dragOriginalIndex;
        _isDraggingTab = true;
        tab.IsBeingDragged = true;
        SetDraggedTabLayer(tab, raised: true);
        BeginTabDragVisual(tab);
        Mouse.OverrideCursor = Cursors.Arrow;
        Mouse.Capture(root, CaptureMode.SubTree);
        UpdateInternalDrag(tab, pointer);
    }

    private void CompleteTabDrag(TabItem tab)
    {
        CommitInternalTabDrop(tab);
    }

    private void CommitInternalTabDrop(TabItem tab)
    {
        var positions = CaptureTabVisualPositions();
        var oldIndex = _tabs.IndexOf(tab);
        ResetAllTabOffsets(animated: false);
        tab.IsBeingDragged = false;
        if (oldIndex >= 0 && _dragDropIndex >= 0 && oldIndex != _dragDropIndex)
            _tabs.Move(oldIndex, _dragDropIndex);
        AnimateTabsFromVisualPositions(positions);
        UpdateTabSeparators();
        ScheduleSessionSave();
    }

    private MainWindow CreateDetachedWindowForDrag(TabItem tab, Point screenPoint)
    {
        var dipPoint = ScreenPixelsToDip(screenPoint);
        var detachedWidth = WindowState == WindowState.Maximized ? RestoreBounds.Width : ActualWidth;
        var detachedHeight = WindowState == WindowState.Maximized ? RestoreBounds.Height : ActualHeight;

        if (_tabs.Count == 1)
        {
            WindowState = WindowState.Normal;
            Width = Math.Max(MinWidth, detachedWidth);
            Height = Math.Max(MinHeight, detachedHeight);
            Left = dipPoint.X - Math.Min(180, tab.DisplayWidth / 2);
            Top = dipPoint.Y - 18;
            return this;
        }

        var detachedWindow = new MainWindow(restoreSession: false, persistWindowState: false, sharedStateFrom: this)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Width = Math.Max(MinWidth, detachedWidth),
            Height = Math.Max(MinHeight, detachedHeight),
            Left = dipPoint.X - Math.Min(180, tab.DisplayWidth / 2),
            Top = dipPoint.Y - 18,
            WindowState = WindowState.Normal,
        };
        TransferTabTo(detachedWindow, tab, insertionIndex: 0, closeEmptySource: false);
        detachedWindow.Show();
        detachedWindow.Activate();
        return detachedWindow;
    }

    private MainWindow? FindMergeTarget(Point screenPoint)
    {
        return Application.Current.Windows
            .OfType<MainWindow>()
            .Where(window => !ReferenceEquals(window, this) && window.IsVisible && window.WindowState != WindowState.Minimized)
            .FirstOrDefault(window => window.IsPointInsideTabBar(screenPoint));
    }

    private bool IsPointInsideTabBar(Point screenPoint)
    {
        var point = TabBar.PointFromScreen(screenPoint);
        return point.X >= 0 && point.X <= TabBar.ActualWidth && point.Y >= 0 && point.Y <= TabBar.ActualHeight;
    }

    private int GetTabInsertionIndex(Point screenPoint, bool isPinned)
    {
        var point = TabStrip.PointFromScreen(screenPoint);
        var index = _tabs.Count;
        for (var candidate = 0; candidate < _tabs.Count; candidate++)
        {
            if (TabStrip.ItemContainerGenerator.ContainerFromIndex(candidate) is not FrameworkElement container) continue;
            var left = container.TranslatePoint(new Point(), TabStrip).X;
            if (point.X < left + container.ActualWidth / 2)
            {
                index = candidate;
                break;
            }
        }

        var pinnedCount = _tabs.Count(item => item.IsPinned);
        return BrowserTabPolicy.ConstrainInsertionIndex(index, _tabs.Count, pinnedCount, isPinned);
    }

    private double GetTabSlotWidth(TabItem tab)
    {
        if (TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is FrameworkElement container && container.ActualWidth > 0)
            return container.ActualWidth;
        return Math.Max(30, tab.DisplayWidth - 18);
    }

    private FrameworkElement? GetTabRoot(TabItem tab)
    {
        if (TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is not FrameworkElement container) return null;
        return container.FindName("TabRoot") as FrameworkElement ??
               FindVisualDescendantByName(container, "TabRoot");
    }

    private static FrameworkElement? FindVisualDescendantByName(DependencyObject parent, string name)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element && element.Name == name) return element;
            var nested = FindVisualDescendantByName(child, name);
            if (nested != null) return nested;
        }

        return null;
    }

    private void BeginTabDragVisual(TabItem tab)
    {
        if (_tabDragVisual != null) return;
        var root = GetTabRoot(tab);
        var container = TabStrip.ItemContainerGenerator.ContainerFromItem(tab) as FrameworkElement;
        if (root == null || container == null || container.ActualWidth <= 0) return;

        root.UpdateLayout();
        NewTabButton.UpdateLayout();
        var origin = container.TranslatePoint(new Point(), TabDragOverlay);
        var tabBarOrigin = TabBar.TranslatePoint(new Point(), TabDragOverlay);
        _draggedTabRoot = root;
        _tabDragImageBaseLeft = origin.X;
        _tabDragRootOffsetX = root.TranslatePoint(new Point(), container).X;
        _tabDragVisual = new System.Windows.Controls.ContentPresenter
        {
            Content = tab,
            ContentTemplate = TabStrip.ItemTemplate,
            Width = container.ActualWidth,
            Height = TabBar.ActualHeight,
            ClipToBounds = false,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_tabDragVisual, origin.X);
        Canvas.SetTop(_tabDragVisual, tabBarOrigin.Y);
        TabDragOverlay.Children.Add(_tabDragVisual);

        var plusOrigin = NewTabButton.TranslatePoint(new Point(), TabDragOverlay);
        _newTabDragImageBaseLeft = plusOrigin.X;
        var plusPath = new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource("IconAdd"),
            Fill = (Brush)FindResource("TextPrimaryBrush"),
            Stretch = Stretch.Uniform,
        };
        _newTabDragVisual = new System.Windows.Controls.Viewbox
        {
            Child = plusPath,
            Width = 10,
            Height = 10,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_newTabDragVisual, plusOrigin.X + (NewTabButton.ActualWidth - 10) / 2);
        Canvas.SetTop(_newTabDragVisual, plusOrigin.Y + (NewTabButton.ActualHeight - 10) / 2);
        _newTabDragImageBaseLeft += (NewTabButton.ActualWidth - 10) / 2;
        TabDragOverlay.Children.Add(_newTabDragVisual);

        // Yer tutucu layout'ta kalsın; kullanıcı yalnızca kırpılmayan, tam
        // opak üst katman kopyasını görsün.
        root.Opacity = 0;
        SetTabOffsetImmediate(tab, 0);
    }

    private void EndTabDragVisual()
    {
        if (_tabDragVisual != null) TabDragOverlay.Children.Remove(_tabDragVisual);
        if (_newTabDragVisual != null) TabDragOverlay.Children.Remove(_newTabDragVisual);
        TabDragOverlay.Children.Clear();
        if (_draggedTabRoot != null) _draggedTabRoot.Opacity = 1;
        NewTabButton.Opacity = 1;
        _tabDragVisual = null;
        _newTabDragVisual = null;
        _draggedTabRoot = null;
    }

    private void SetTabOffsetImmediate(TabItem tab, double offset)
    {
        if (GetTabRoot(tab)?.RenderTransform is TranslateTransform transform)
            transform.BeginAnimation(TranslateTransform.XProperty, null);
        tab.VisualOffsetX = offset;
    }

    private void SetDraggedTabLayer(TabItem tab, bool raised)
    {
        SetTabLayer(tab, raised ? 100 : 0);
    }

    private void SetTabLayer(TabItem tab, int layer)
    {
        if (TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is UIElement container)
            System.Windows.Controls.Panel.SetZIndex(container, layer);
    }

    private void AnimateTabOffset(TabItem tab, double offset)
    {
        if (Math.Abs(tab.VisualOffsetX - offset) < 0.25) return;
        var transform = GetTabRoot(tab)?.RenderTransform as TranslateTransform;
        var current = transform?.X ?? tab.VisualOffsetX;
        tab.VisualOffsetX = offset;
        transform?.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(current, offset, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            });
    }

    private Dictionary<TabItem, double> CaptureTabVisualPositions()
    {
        var positions = _tabs
            .Select(tab => (tab, root: GetTabRoot(tab)))
            .Where(item => item.root != null)
            .ToDictionary(item => item.tab, item => item.root!.TranslatePoint(new Point(), TabStrip).X);
        if (_draggedTab != null && _tabDragVisual != null)
        {
            positions[_draggedTab] = TabDragOverlay.TranslatePoint(
                new Point(Canvas.GetLeft(_tabDragVisual) + _tabDragRootOffsetX, 0), TabStrip).X;
        }
        return positions;
    }

    private void AnimateTabsFromVisualPositions(IReadOnlyDictionary<TabItem, double> positions)
    {
        TabStrip.UpdateLayout();
        foreach (var tab in _tabs)
        {
            var root = GetTabRoot(tab);
            if (root?.RenderTransform is not TranslateTransform transform ||
                !positions.TryGetValue(tab, out var previousX)) continue;
            var currentX = root.TranslatePoint(new Point(), TabStrip).X;
            var offset = previousX - currentX;
            if (Math.Abs(offset) < 0.5) continue;
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop,
                });
        }
    }

    private void ResetAllTabOffsets(bool animated)
    {
        foreach (var tab in _tabs)
        {
            if (!ReferenceEquals(tab, _draggedTab)) SetTabLayer(tab, 0);
            if (animated) AnimateTabOffset(tab, 0);
            else SetTabOffsetImmediate(tab, 0);
        }
    }

    private void TransferTabTo(
        MainWindow target,
        TabItem tab,
        int insertionIndex,
        bool closeEmptySource)
    {
        var oldIndex = _tabs.IndexOf(tab);
        if (oldIndex < 0 || ReferenceEquals(target, this)) return;

        var oldPositions = CaptureTabVisualPositions();
        var oldWidths = _tabs
            .Where(item => !ReferenceEquals(item, tab))
            .Select(item => (item, root: GetTabRoot(item)))
            .Where(item => item.root != null)
            .ToDictionary(item => item.item, item => item.root!.ActualWidth);

        var wasActive = ReferenceEquals(_activeTab, tab);
        tab.Browser.SetPresentationActive(false);
        BrowserHost.Children.Remove(tab.Browser);
        tab.IsBrowserAttached = false;
        _tabs.RemoveAt(oldIndex);

        if (_addressChangeHandlers.Remove(tab, out var subscription))
            target._addressChangeHandlers[tab] = subscription;
        if (_findStates.Remove(tab, out var findState))
            target._findStates[tab] = findState;

        if (wasActive)
        {
            _activeTab = null;
            if (_tabs.Count > 0) SwitchTab(_tabs[Math.Min(oldIndex, _tabs.Count - 1)]);
            else FindPanelPopup.IsOpen = false;
        }

        tab.Owner = target;
        tab.Browser.DownloadHandler = target._downloadHandler;
        target._tabs.Insert(Math.Clamp(insertionIndex, 0, target._tabs.Count), tab);
        target.UpdateTabWidths();
        target.SwitchTab(tab);
        UpdateTabWidths();
        TabStrip.UpdateLayout();
        AnimateTabsFromVisualPositions(oldPositions);
        AnimateTabWidthsFrom(oldWidths);
        UpdateTabSeparators();
        target.UpdateTabSeparators();
        ScheduleSessionSave();

        if (closeEmptySource && _tabs.Count == 0) Close();
    }

    private Point ScreenPixelsToDip(Point screenPoint)
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformFromDevice.Transform(screenPoint) ?? screenPoint;
    }

    private void ResetTabDragState()
    {
        ResetAllTabOffsets(animated: false);
        NewTabButtonOffsetTransform.X = 0;
        EndTabDragVisual();
        if (_draggedTab != null)
        {
            SetDraggedTabLayer(_draggedTab, raised: false);
            _draggedTab.IsBeingDragged = false;
        }
        Mouse.OverrideCursor = null;
        if (Mouse.Captured != null)
            Mouse.Capture(null);
        _draggedTab = null;
        _isDraggingTab = false;
        _dragOriginalIndex = -1;
        _dragDropIndex = -1;
    }

    private void AnimateTabWidthsFrom(IReadOnlyDictionary<TabItem, double> oldWidths)
    {
        foreach (var tab in _tabs)
        {
            var root = GetTabRoot(tab);
            if (root == null || !oldWidths.TryGetValue(tab, out var oldWidth)) continue;
            var newWidth = tab.DisplayWidth;
            if (Math.Abs(oldWidth - newWidth) < 0.5) continue;
            root.BeginAnimation(
                FrameworkElement.WidthProperty,
                new DoubleAnimation(oldWidth, newWidth, TimeSpan.FromMilliseconds(190))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop,
                });
        }
    }

    private static class DetachedTabDragSession
    {
        private const int WmNcLeftButtonDown = 0x00A1;
        private const int WmCancelMode = 0x001F;
        private const int HtCaption = 2;
        private const int VkLeftButton = 0x01;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private static bool _isActive;

        public static bool IsActive => _isActive;

        public static void Start(MainWindow source, TabItem tab, Point screenPoint, double grabOffsetX)
        {
            if (_isActive) return;
            _isActive = true;
            try
            {
                var floatingWindow = source.CreateDetachedWindowForDrag(tab, screenPoint);
                var handle = new WindowInteropHelper(floatingWindow).Handle;
                if (handle == IntPtr.Zero) return;

                // Pencereyi bir kez sekmenin tutulduğu noktanın altına yerleştir;
                // bundan sonraki bütün taşıma ve Snap davranışı Windows'a aittir.
                SetWindowPos(
                    handle,
                    IntPtr.Zero,
                    (int)Math.Round(screenPoint.X - 44 - grabOffsetX),
                    (int)Math.Round(screenPoint.Y - 17),
                    0,
                    0,
                    SwpNoSize | SwpNoZOrder | SwpNoActivate);

                Mouse.OverrideCursor = Cursors.Arrow;
                ReleaseCapture();

                MainWindow? liveMergeTarget = null;
                var liveMergePoint = screenPoint;
                var mergeMonitor = new DispatcherTimer(DispatcherPriority.Input)
                {
                    Interval = TimeSpan.FromMilliseconds(12),
                };
                void DetectMergeTarget()
                {
                    if (liveMergeTarget != null || !IsLeftButtonPressed() ||
                        !GetCursorPos(out var currentCursor)) return;

                    var currentPoint = new Point(currentCursor.X, currentCursor.Y);
                    var candidate = floatingWindow.FindMergeTarget(currentPoint);
                    if (candidate == null) return;

                    liveMergeTarget = candidate;
                    liveMergePoint = currentPoint;
                    // DefWindowProc'un modal pencere taşıma döngüsünü mouse-up
                    // beklemeden bitir. Fiziksel sol tuş hâlâ basılı kalır.
                    PostMessage(handle, WmCancelMode, IntPtr.Zero, IntPtr.Zero);
                }

                EventHandler locationChanged = (_, _) => DetectMergeTarget();
                floatingWindow.LocationChanged += locationChanged;
                mergeMonitor.Tick += (_, _) => DetectMergeTarget();
                mergeMonitor.Start();
                SendMessage(handle, WmNcLeftButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
                mergeMonitor.Stop();
                floatingWindow.LocationChanged -= locationChanged;

                if (liveMergeTarget != null && tab.Owner != null && IsLeftButtonPressed())
                {
                    var liveTabSource = tab.Owner;
                    var liveInsertionIndex = liveMergeTarget.GetTabInsertionIndex(liveMergePoint, tab.IsPinned);
                    liveTabSource.TransferTabTo(
                        liveMergeTarget,
                        tab,
                        liveInsertionIndex,
                        closeEmptySource: true);
                    liveMergeTarget.ContinueTransferredTabDrag(tab, liveMergePoint, grabOffsetX);
                    return;
                }

                // Native taşıma döngüsü fare bırakıldığında döner. Başka bir
                // Fursoy penceresinin sekme şeridine bırakıldıysa sekmeyi aktar.
                // Windows pencereyi Snap ile büyüttüyse alttaki eski pencerenin
                // şeridini yanlışlıkla bir birleştirme hedefi sayma.
                if (tab.Owner == null || IsZoomed(handle) || !GetCursorPos(out var cursor)) return;
                var dropPoint = new Point(cursor.X, cursor.Y);
                var target = floatingWindow.FindMergeTarget(dropPoint);
                if (target == null) return;
                var tabSource = tab.Owner;
                var insertionIndex = target.GetTabInsertionIndex(dropPoint, tab.IsPinned);
                tabSource.TransferTabTo(target, tab, insertionIndex, closeEmptySource: true);
            }
            finally
            {
                _isActive = false;
                Mouse.OverrideCursor = null;
            }
        }

        public static void Cancel()
        {
            // Windows'un taşıma döngüsü Escape'i doğrudan işler.
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        public static bool IsLeftButtonPressed() =>
            (GetAsyncKeyState(VkLeftButton) & 0x8000) != 0;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(
            IntPtr window,
            int message,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsZoomed(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

    }

    private void TabRoot_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || sender is not FrameworkElement { Tag: TabItem tab }) return;
        CloseTab(tab);
        e.Handled = true;
    }

    private void TabBarContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { Items.Count: > 1 } menu && menu.Items[1] is MenuItem reopenItem)
            reopenItem.IsEnabled = _closedTabAddresses.Count > 0;
    }

    private void TabContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu { DataContext: TabItem tab } menu) return;
        var index = _tabs.IndexOf(tab);
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is not string action) continue;
            item.IsEnabled = action switch
            {
                "MoveToWindow" => _tabs.Count > 1,
                "CloseOthers" => _tabs.Count > 1,
                "CloseRight" => index >= 0 && index < _tabs.Count - 1,
                _ => item.IsEnabled,
            };
        }
    }

    private void TabBarNewTabMenuItem_Click(object sender, RoutedEventArgs e) => AddTab();

    private void ReopenClosedTabMenuItem_Click(object sender, RoutedEventArgs e)
        => ReopenLastClosedTab();

    private void ReopenLastClosedTab()
    {
        if (_closedTabAddresses.TryPop(out var address))
            AddTab(string.IsNullOrEmpty(address) ? null : address);
    }

    private void NewTabToRightMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabItem tab }) return;
        var targetIndex = tab.IsPinned ? _tabs.Count(item => item.IsPinned) : _tabs.IndexOf(tab) + 1;
        var newTab = AddTab();
        var oldIndex = _tabs.IndexOf(newTab);
        if (targetIndex >= 0 && oldIndex != targetIndex) _tabs.Move(oldIndex, targetIndex);
        UpdateTabWidths();
        UpdateTabSeparators();
        ScheduleSessionSave();
    }

    private void ReloadTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabItem tab }) ReloadTab(tab, ignoreCache: false);
    }

    private void DuplicateTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabItem tab }) return;
        var duplicate = AddTab(
            IsNewTabAddress(tab.Address) ? null : tab.Address,
            title: tab.Title,
            faviconUrl: tab.FaviconUrl);
        var sourceIndex = _tabs.IndexOf(tab);
        var targetIndex = tab.IsPinned ? _tabs.Count(item => item.IsPinned) : sourceIndex + 1;
        var duplicateIndex = _tabs.IndexOf(duplicate);
        if (sourceIndex >= 0 && duplicateIndex != targetIndex)
            _tabs.Move(duplicateIndex, targetIndex);
        UpdateTabWidths();
        UpdateTabSeparators();
        ScheduleSessionSave();
    }

    private void MoveTabToNewWindowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabItem tab } || _tabs.Count <= 1) return;
        var screenPoint = PointToScreen(new Point(Math.Min(180, ActualWidth / 2), 18));
        screenPoint.Offset(30, 30);
        CreateDetachedWindowForDrag(tab, screenPoint);
    }

    private void CloseTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabItem tab }) CloseTab(tab);
    }

    private void CloseOtherTabsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabItem tab }) return;
        foreach (var other in _tabs.Where(other => !ReferenceEquals(other, tab)).ToList())
            CloseTab(other);
    }

    private void CloseTabsToRightMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabItem tab }) return;
        var index = _tabs.IndexOf(tab);
        if (index < 0) return;
        foreach (var other in _tabs.Skip(index + 1).Reverse().ToList())
            CloseTab(other);
    }

    private void PinTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabItem tab } || tab.IsPinned) return;
        var oldIndex = _tabs.IndexOf(tab);
        var targetIndex = _tabs.Count(item => item.IsPinned);
        tab.IsPinned = true;
        if (oldIndex != targetIndex) _tabs.Move(oldIndex, targetIndex);
        UpdateTabWidths();
        UpdateTabSeparators();
        ScheduleSessionSave();
    }

    private void UnpinTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabItem tab } || !tab.IsPinned) return;
        tab.IsPinned = false;
        var oldIndex = _tabs.IndexOf(tab);
        var targetIndex = _tabs.Count(item => item.IsPinned);
        if (oldIndex != targetIndex) _tabs.Move(oldIndex, targetIndex);
        UpdateTabWidths();
        UpdateTabSeparators();
        ScheduleSessionSave();
    }

    private void MuteTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabItem tab }) ToggleTabMute(tab);
    }

    private void AudioTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TabItem tab }) ToggleTabMute(tab);
        e.Handled = true;
    }

    private static void ToggleTabMute(TabItem tab)
    {
        tab.IsMuted = !tab.IsMuted;
        tab.Browser.GetBrowserHost()?.SetAudioMuted(tab.IsMuted);
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

    private void UpdateNavigationUi(
        TabItem tab,
        bool? canGoBack = null,
        bool? canGoForward = null)
    {
        if (!ReferenceEquals(tab, _activeTab)) return;
        BackButton.IsEnabled = canGoBack ?? tab.Browser.CanGoBack;
        ForwardButton.IsEnabled = canGoForward ?? tab.Browser.CanGoForward;
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab == null) return;
        if (_activeTab.IsLoading) _activeTab.Browser.Stop();
        else ReloadTab(_activeTab, ignoreCache: false);
    }

    private void ReloadTab(TabItem tab, bool ignoreCache)
    {
        if (!ReferenceEquals(tab.Owner, this)) return;
        tab.Browser.Reload(ignoreCache);
    }

    private void UpdateLoadingUi(TabItem tab)
    {
        if (!ReferenceEquals(tab, _activeTab)) return;
        ReloadIcon.Visibility = tab.IsLoading ? Visibility.Collapsed : Visibility.Visible;
        StopLoadingIcon.Visibility = tab.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        ReloadButton.ToolTip = tab.IsLoading ? Strings.StopLoadingTooltip : Strings.ReloadTooltip;
    }

    private void StarButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab == null) return;
        _bookmarkStore.Toggle(_activeTab.Address, _activeTab.Title);
    }

    private void BookmarksButton_Click(object sender, RoutedEventArgs e)
    {
        BookmarksPopup.PlacementTarget = sender as UIElement ?? BookmarksButton;
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

    private void BookmarksBarItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Bookmark bookmark } || _activeTab == null) return;
        _activeTab.Browser.Address = bookmark.Url;
    }

    private void UpdateBookmarksEmptyState()
    {
        var hasBookmarks = _bookmarkStore.Bookmarks.Count > 0;
        BookmarksEmptyText.Visibility = hasBookmarks ? Visibility.Collapsed : Visibility.Visible;
        BookmarksBar.Visibility = hasBookmarks ? Visibility.Visible : Visibility.Collapsed;
        // Web sayfasının kendisi minimum yüksekliğe dahil değildir. Yalnızca
        // o anda görünür olan tarayıcı Chrome'u (sekme/araç/yer imi şeritleri)
        // korunur; WM_GETMINMAXINFO bunu fiziksel piksele dönüştürür.
        MinHeight = MinimumChromeHeight;
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryPopup.IsOpen)
        {
            HistoryPopup.IsOpen = false;
            return;
        }

        HistoryPopup.PlacementTarget = sender as UIElement ?? MenuButton;
        OpenHistoryPopup();
    }

    private void OpenHistoryPopup()
    {
        RefreshRecentHistory();
        AllHistoryPopup.IsOpen = false;
        HistoryPopup.IsOpen = true;
    }

    private void RefreshRecentHistory()
    {
        var recent = HistoryStore.TakeRecent(_historyStore.Entries, RecentHistoryPopupLimit);
        _recentHistory.Clear();
        foreach (var entry in recent) _recentHistory.Add(entry);
    }

    private void HistoryRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: HistoryEntry entry } || _activeTab == null) return;
        _activeTab.Browser.Address = entry.Url;
        HistoryPopup.IsOpen = false;
        AllHistoryPopup.IsOpen = false;
    }

    private void RemoveHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: HistoryEntry entry }) return;
        _historyStore.Remove(entry);
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e) => _historyStore.Clear();

    private void ShowAllHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryPopup.IsOpen = false;
        AllHistoryPopup.IsOpen = true;
    }

    private void UpdateHistoryEmptyState()
    {
        var visibility = _historyStore.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyText.Visibility = visibility;
        AllHistoryEmptyText.Visibility = visibility;
    }

    private void DownloadsButton_Click(object sender, RoutedEventArgs e)
    {
        _downloadHandler.RefreshFileAvailability();
        DownloadsPopup.PlacementTarget = sender as UIElement ?? DownloadsButton;
        DownloadsPopup.IsOpen = !DownloadsPopup.IsOpen;
    }

    private void DownloadHandler_DownloadsChanged(object? sender, EventArgs e)
    {
        UpdateDownloadsEmptyState();
        UpdateDownloadToolbarState();
        BroadcastDownloadsPageState();
    }

    private void DownloadHandler_DownloadStarted(object? sender, DownloadStartedEventArgs e)
    {
        if (e.SourceBrowser != null && !_tabs.Any(tab => ReferenceEquals(tab.Browser, e.SourceBrowser))) return;

        var now = DateTime.UtcNow;
        if (now - _lastDownloadStartAnimationUtc >= TimeSpan.FromMilliseconds(180))
        {
            _lastDownloadStartAnimationUtc = now;
            PlayDownloadStartedAnimation();
        }

        ShowAutomaticDownloadsPopup();
    }

    private void PlayDownloadStartedAnimation()
    {

        DownloadToolbarIconTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimationUsingKeyFrames
            {
                KeyFrames =
                {
                    new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                    new EasingDoubleKeyFrame(2.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))),
                    new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    },
                },
            },
            HandoffBehavior.SnapshotAndReplace);

        var iconScale = new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new EasingDoubleKeyFrame(0.82, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new EasingDoubleKeyFrame(1.14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                },
                new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(430)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                },
            },
        };
        DownloadToolbarIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, iconScale, HandoffBehavior.SnapshotAndReplace);
        DownloadToolbarIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, iconScale, HandoffBehavior.SnapshotAndReplace);

        DownloadStartPulse.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(560))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            },
            HandoffBehavior.SnapshotAndReplace);
        var pulseScale = new DoubleAnimation(0.5, 1.3, TimeSpan.FromMilliseconds(560))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        DownloadStartPulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseScale, HandoffBehavior.SnapshotAndReplace);
        DownloadStartPulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseScale, HandoffBehavior.SnapshotAndReplace);
    }

    private void ShowAutomaticDownloadsPopup()
    {
        if (DownloadsPopup.IsOpen) return;

        DownloadsPopup.PlacementTarget = DownloadsButton.Visibility == Visibility.Visible
            ? DownloadsButton
            : ToolbarOverflowButton;
        DownloadsPopup.IsOpen = true;
    }

    private void NavigationBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < CompactToolbarThreshold;
        ExtensionDivider.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DownloadsButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        TranslateButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        FeatureDivider.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        BookmarksButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ProfileContainer.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ToolbarOverflowButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        ToolbarRightGroup.Width = compact ? 108 : 251;
    }

    private void ToolbarOverflowButton_Click(object sender, RoutedEventArgs e)
    {
        if (ToolbarOverflowButton.ContextMenu == null) return;
        ToolbarOverflowButton.ContextMenu.PlacementTarget = ToolbarOverflowButton;
        ToolbarOverflowButton.ContextMenu.Placement = PlacementMode.Bottom;
        ToolbarOverflowButton.ContextMenu.IsOpen = true;
    }

    private void OverflowDownloadsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ToolbarOverflowButton.ContextMenu != null) ToolbarOverflowButton.ContextMenu.IsOpen = false;
        DownloadsPopup.PlacementTarget = ToolbarOverflowButton;
        DownloadsPopup.IsOpen = true;
    }

    private void OverflowBookmarksMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ToolbarOverflowButton.ContextMenu != null) ToolbarOverflowButton.ContextMenu.IsOpen = false;
        BookmarksPopup.PlacementTarget = ToolbarOverflowButton;
        BookmarksPopup.IsOpen = true;
    }

    private void DownloadOpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DownloadEntry entry }) return;
        OpenDownloadedFile(entry);
    }

    private void DownloadShowInFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DownloadEntry entry }) return;
        ShowDownloadInFolder(entry);
    }

    private void DownloadPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DownloadEntry entry }) _downloadHandler.Pause(entry.HistoryId);
    }

    private void DownloadResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DownloadEntry entry }) _downloadHandler.Resume(entry.HistoryId);
    }

    private void DownloadCancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DownloadEntry entry }) _downloadHandler.Cancel(entry.HistoryId);
    }

    private void DownloadRetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DownloadEntry entry }) RetryDownload(entry.HistoryId);
    }

    private void DownloadRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DownloadEntry entry }) _downloadHandler.Remove(entry.HistoryId);
    }

    private void ShowAllDownloadsButton_Click(object sender, MouseButtonEventArgs e)
    {
        DownloadsPopup.IsOpen = false;
        OpenDownloadsPage();
    }

    private void OpenDownloadedFile(DownloadEntry entry)
    {
        entry.RefreshFileAvailability();
        if (!entry.CanOpen) return;

        var extension = Path.GetExtension(entry.FullPath);
        string[] executableExtensions = [".exe", ".msi", ".bat", ".cmd", ".ps1", ".dll", ".scr", ".com"];
        if (executableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) &&
            MessageBox.Show(
                this,
                Strings.DownloadExecutableWarning,
                Strings.DownloadExecutableWarningTitle,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
    }

    private static void ShowDownloadInFolder(DownloadEntry entry)
    {
        entry.RefreshFileAvailability();
        if (!entry.CanShowInFolder) return;
        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add("/select,");
        startInfo.ArgumentList.Add(entry.FullPath);
        Process.Start(startInfo);
    }

    private void RetryDownload(Guid historyId)
    {
        var sourceUrl = _downloadHandler.GetRetryUrl(historyId);
        if (string.IsNullOrWhiteSpace(sourceUrl)) return;
        var browser = _activeTab?.Browser;
        if (browser?.IsBrowserInitialized == true) browser.StartDownload(sourceUrl);
    }

    private void UpdateDownloadsEmptyState()
    {
        DownloadsEmptyText.Visibility = _downloadHandler.Downloads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDownloadToolbarState()
    {
        var activeCount = _downloadHandler.ActiveCount;
        DownloadProgressIndicator.Visibility = activeCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadProgressIndicator.Value = _downloadHandler.AggregateProgress;
        DownloadProgressIndicator.IsIndeterminate = _downloadHandler.HasIndeterminateProgress;
        DownloadActiveCountTextContainer.Visibility = activeCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        DownloadActiveCountText.Visibility = activeCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        DownloadActiveCountText.Text = activeCount.ToString(CultureInfo.CurrentCulture);
    }

    private void OpenDownloadsPage()
    {
        var existing = Application.Current.Windows
            .OfType<MainWindow>()
            .Where(window => window.IsLoaded && window.IsVisible)
            .SelectMany(window => window._tabs)
            .FirstOrDefault(tab => IsDownloadsAddress(tab.Address));
        if (existing?.Owner is { } owner)
        {
            owner.Activate();
            owner.SwitchTab(existing);
            return;
        }

        AddTab(DownloadsPage.Address, title: Strings.Downloads);
    }

    private void HandleDownloadsPageCommand(DownloadPageCommand command)
    {
        if (!string.Equals(command.Scope, "downloads", StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(command.Action, "clear", StringComparison.OrdinalIgnoreCase))
        {
            _downloadHandler.ClearHistory();
            return;
        }

        if (!Guid.TryParse(command.Id, out var id)) return;
        switch (command.Action?.ToLowerInvariant())
        {
            case "pause": _downloadHandler.Pause(id); break;
            case "resume": _downloadHandler.Resume(id); break;
            case "cancel": _downloadHandler.Cancel(id); break;
            case "remove": _downloadHandler.Remove(id); break;
            case "retry": RetryDownload(id); break;
            case "open":
                if (_downloadHandler.Downloads.FirstOrDefault(item => item.HistoryId == id) is { } openEntry)
                    OpenDownloadedFile(openEntry);
                break;
            case "show":
                if (_downloadHandler.Downloads.FirstOrDefault(item => item.HistoryId == id) is { } showEntry)
                    ShowDownloadInFolder(showEntry);
                break;
        }
    }

    private void BroadcastDownloadsPageState()
    {
        foreach (var tab in _tabs.Where(tab => IsDownloadsAddress(tab.Address) && tab.IsBrowserAttached))
            PushDownloadsPageState(tab);
    }

    private void PushDownloadsPageState(TabItem tab)
    {
        if (!tab.Browser.IsBrowserInitialized || !IsDownloadsAddress(tab.Address)) return;
        var json = DownloadsPage.Serialize(_downloadHandler.Downloads);
        tab.Browser.ExecuteScriptAsync($"window.fursoyDownloads?.setData({json});");
    }

    private void TabSearchButton_Click(object sender, RoutedEventArgs e)
    {
        TabSearchList.ItemsSource = _tabs;
        TabSearchPopup.IsOpen = !TabSearchPopup.IsOpen;
    }

    private void TabSearchRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TabItem tab }) return;
        SwitchTab(tab);
        TabSearchPopup.IsOpen = false;
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = !MenuPopup.IsOpen;
    }

    private void MenuNewTab_Click(object sender, MouseButtonEventArgs e)
    {
        MenuPopup.IsOpen = false;
        AddTab();
    }

    private void MenuBookmarks_Click(object sender, MouseButtonEventArgs e)
    {
        MenuPopup.IsOpen = false;
        BookmarksPopup.IsOpen = true;
    }

    private void MenuHistory_Click(object sender, MouseButtonEventArgs e)
    {
        MenuPopup.IsOpen = false;
        HistoryPopup.PlacementTarget = MenuButton;
        OpenHistoryPopup();
    }

    private void MenuDownloads_Click(object sender, MouseButtonEventArgs e)
    {
        MenuPopup.IsOpen = false;
        OpenDownloadsPage();
    }

    private void MenuSettings_Click(object sender, MouseButtonEventArgs e)
    {
        MenuPopup.IsOpen = false;
        RestoreSessionCheckBox.IsChecked = _appSettings.RestoreSession;
        AskWhereToSaveDownloadsCheckBox.IsChecked = _appSettings.AskWhereToSaveDownloads;
        ShowFindButtonCheckBox.IsChecked = _appSettings.ShowFindButtonInToolbar;
        DownloadLocationText.Text = _appSettings.DownloadDirectory;
        SettingsPopup.IsOpen = true;
    }

    private void SiteControls_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeTab == null || IsNewTabAddress(_activeTab.Address)) return;
        UpdateSecurityUi(_activeTab);
        SiteInfoPopup.IsOpen = !SiteInfoPopup.IsOpen;
    }

    private void UpdateSecurityUi(TabItem tab)
    {
        var secure = Uri.TryCreate(tab.Address, UriKind.Absolute, out var uri) &&
                     uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                     !tab.HasCertificateError;
        SiteSecurityText.Text = tab.HasCertificateError
            ? Strings.CertificateError
            : secure ? Strings.SecureConnection : Strings.NotSecure;
        SiteAddressText.Text = Uri.TryCreate(tab.Address, UriKind.Absolute, out uri)
            ? uri.Host
            : tab.Address;
        SiteControls.ToolTip = Strings.SitePermissions;
    }

    private void ShowPermissionPrompt(string origin, string permission, Action allow, Action deny)
    {
        _denyPermission?.Invoke();
        _allowPermission = allow;
        _denyPermission = deny;
        PermissionOriginText.Text = origin;
        PermissionTypeText.Text = LocalizePermission(permission);
        PermissionPopup.IsOpen = true;
    }

    private static string LocalizePermission(string permission)
    {
        var labels = new List<string>();
        void AddWhen(string token, string label)
        {
            if (permission.Contains(token, StringComparison.OrdinalIgnoreCase) && !labels.Contains(label))
                labels.Add(label);
        }

        AddWhen("Video", Strings.Camera);
        AddWhen("Camera", Strings.Camera);
        AddWhen("Audio", Strings.Microphone);
        AddWhen("Microphone", Strings.Microphone);
        AddWhen("Geo", Strings.Location);
        AddWhen("Location", Strings.Location);
        AddWhen("Notification", Strings.Notifications);
        AddWhen("Clipboard", Strings.Clipboard);
        return labels.Count == 0 ? Strings.SitePermissions : string.Join(", ", labels);
    }

    private void AllowPermissionButton_Click(object sender, RoutedEventArgs e)
    {
        var action = _allowPermission;
        ClearPermissionPrompt();
        action?.Invoke();
    }

    private void DenyPermissionButton_Click(object sender, RoutedEventArgs e)
    {
        var action = _denyPermission;
        ClearPermissionPrompt();
        action?.Invoke();
    }

    private void ClearPermissionPrompt()
    {
        PermissionPopup.IsOpen = false;
        _allowPermission = null;
        _denyPermission = null;
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string theme }) return;
        _appSettings.Theme = theme;
        AppSettingsStore.Save(_appSettings);
        ThemeManager.Apply(theme);
        foreach (var window in Application.Current.Windows.OfType<MainWindow>())
        {
            foreach (var tab in window._tabs.Where(tab => tab.IsBrowserAttached && IsDownloadsAddress(tab.Address)))
                tab.Browser.LoadHtml(DownloadsPage.BuildHtml(_downloadHandler.Downloads), DownloadsPage.Address);
        }
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string language }) return;
        _appSettings.Language = language;
        AppSettingsStore.Save(_appSettings);
        RestartWindowForAppearanceChange();
    }

    private void SearchEngineButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string searchEngine }) return;
        _appSettings.SearchEngine = searchEngine;
        AppSettingsStore.Save(_appSettings);
        UpdateSearchEnginePlaceholders();
        foreach (var tab in _tabs.Where(tab => tab.IsBrowserAttached && IsNewTabAddress(tab.Address)))
            tab.Browser.LoadHtml(NewTabPage.BuildHtml(_bookmarkStore.Bookmarks, _appSettings.SearchEngine));
        SettingsPopup.IsOpen = false;
    }

    private void UpdateSearchEnginePlaceholders()
    {
        var engine = NewTabPage.GetSearchConfiguration(_appSettings.SearchEngine).EngineName;
        AddressBarPlaceholderText.Text = string.Format(
            CultureInfo.CurrentUICulture, Strings.AddressBarPlaceholderFormat, engine);
    }

    private void RestoreSessionCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _appSettings.RestoreSession = RestoreSessionCheckBox.IsChecked == true;
        AppSettingsStore.Save(_appSettings);
    }

    private void ShowFindButtonCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _appSettings.ShowFindButtonInToolbar = ShowFindButtonCheckBox.IsChecked == true;
        AppSettingsStore.Save(_appSettings);
        foreach (var window in Application.Current.Windows.OfType<MainWindow>())
            window.UpdateFindButtonVisibility();
    }

    private void ChooseDownloadLocationButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Strings.ChooseDownloadLocation,
            InitialDirectory = Directory.Exists(_appSettings.DownloadDirectory)
                ? _appSettings.DownloadDirectory
                : DownloadPathPolicy.GetDefaultDownloadsFolder(),
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true) return;
        _appSettings.DownloadDirectory = dialog.FolderName;
        DownloadLocationText.Text = dialog.FolderName;
        AppSettingsStore.Save(_appSettings);
    }

    private void AskWhereToSaveDownloadsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _appSettings.AskWhereToSaveDownloads = AskWhereToSaveDownloadsCheckBox.IsChecked == true;
        AppSettingsStore.Save(_appSettings);
    }

    private void RestartWindowForAppearanceChange()
    {
        SaveSession();
        SettingsPopup.IsOpen = false;
        var culture = CultureInfo.GetCultureInfo(_appSettings.Language);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Strings.Culture = culture;
        ThemeManager.Apply(_appSettings.Theme);

        var replacement = new MainWindow
        {
            Left = Left,
            Top = Top,
            Width = ActualWidth,
            Height = ActualHeight,
            WindowState = WindowState,
        };
        Application.Current.MainWindow = replacement;
        replacement.Show();
        Close();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
