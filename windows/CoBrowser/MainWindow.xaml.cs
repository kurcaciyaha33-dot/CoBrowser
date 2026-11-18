using System.Diagnostics;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapePath = System.Windows.Shapes.Path;
using System.Windows.Threading;
using Microsoft.Win32;
using CefSharp;
using CefSharp.Wpf;
using CoBrowser.Database;
using CoBrowser.Services;
using CoBrowser.Auth;
using CoBrowser.Downloads;

namespace CoBrowser;

public partial class MainWindow : Window
{
    private readonly HistoryStore _history = new();
    private readonly Dictionary<TabItem, ChromiumWebBrowser> _browsers = new();
    private readonly Dictionary<TabItem, FrameworkElement> _views = new();
    private readonly Dictionary<TabItem, Image> _tabFavicons = new();
    private static readonly HttpClient FaviconHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly Stack<string> _closedTabs = new();
    private readonly List<BookmarkEntry> _bookmarks = new();
    private readonly string _bookmarkPath = Path.Combine(App.DataRoot, "bookmarks.json");
    private bool _changingAddress;
    private bool _isDark;
    private string _themeMode = "System";
    private string _searchProvider = "Bing";
    private readonly string _settingsPath = Path.Combine(App.DataRoot, "browser-settings.json");
    private bool _isFullscreen;
    private bool _isClosing;
    private WindowStyle _normalWindowStyle = WindowStyle.SingleBorderWindow;
    private WindowState _normalWindowState = WindowState.Normal;
    private double _zoomLevel;
    private bool _googleLoginAcknowledged;
    private readonly GoogleOAuthService _authService;
    private static readonly string UserDownloadsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    private static readonly DownloadManager SharedDownloadManager = new(UserDownloadsDirectory);
    private readonly DownloadManager _downloadManager = SharedDownloadManager;
    private readonly NotifyCollectionChangedEventHandler _downloadCollectionChanged;
    private GoogleUserProfile _googleUser;

    public MainWindow(GoogleOAuthService authService, GoogleUserProfile googleUser)
    {
        _authService = authService;
        _googleUser = googleUser;
        _googleLoginAcknowledged = !string.IsNullOrWhiteSpace(googleUser.Email);
        _downloadCollectionChanged = (_, _) =>
            DownloadsFlyoutEmpty.Visibility = _downloadManager.Downloads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var stage = "Initialize WPF components";
        try
        {
            App.Log("MainWindow constructor: " + stage);
            InitializeComponent();
            LoadBrowserSettings();
            ApplyTheme(_themeMode == "Dark" || (_themeMode == "System" && ReadSystemDarkMode()));
            SystemEvents.UserPreferenceChanged += SystemThemeChanged;
            DownloadsFlyoutList.ItemsSource = _downloadManager.Downloads;
            _downloadManager.Downloads.CollectionChanged += _downloadCollectionChanged;
            DownloadsFlyoutEmpty.Visibility = _downloadManager.Downloads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateProfileButton();

            stage = "Initialize browsing history database";
            App.Log("MainWindow constructor: " + stage);
            _history.Initialize();

            stage = "Load bookmarks";
            App.Log("MainWindow constructor: " + stage);
            LoadBookmarks();

            stage = "Create initial tab";
            App.Log("MainWindow constructor: " + stage);
            AddTab();
            App.Log("MainWindow constructor completed successfully.");
        }
        catch (Exception ex)
        {
            App.LogException(ex, "MainWindow constructor stage: " + stage);
            throw;
        }
    }

    private TabItem? CurrentTab => Tabs.SelectedItem as TabItem;
    private ChromiumWebBrowser? CurrentBrowser => CurrentTab is { } tab && _browsers.TryGetValue(tab, out var browser) ? browser : null;

    private void AddTab(string? initialUrl = null)
    {
        if (_isClosing) return;

        var tab = new TabItem { MinWidth = 125, MaxWidth = 225 };
        var header = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
        var favicon = new Image
        {
            Width = 16, Height = 16, Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _tabFavicons[tab] = favicon;
        var title = new TextBlock
        {
            Text = "New Tab",
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 155
        };
        var close = new Button
        {
            Content = MakeIcon("close", 12),
            Style = (Style)FindResource("TabCloseButton"),
            Tag = tab,
            ToolTip = "Close tab"
        };
        close.Click += (_, _) => CloseTab(tab);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var titleHost = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titleHost.Children.Add(favicon);
        titleHost.Children.Add(title);
        header.Children.Add(titleHost);
        tab.Header = header;
        tab.Tag = title;

        Tabs.Items.Add(tab);
        Tabs.SelectedItem = tab;

        if (string.IsNullOrWhiteSpace(initialUrl))
            ShowNewTab(tab, !_googleLoginAcknowledged);
        else
            CreateBrowser(tab, initialUrl);

        AttachSelectedView();
        UpdateNavigationState();
    }

    private void ShowNewTab(TabItem tab, bool loginRequired)
    {
        DisposeBrowserForTab(tab);
        var startPage = new NewTabPage(
            loginRequired,
            input => NavigateInTab(tab, input),
            SignInWithGoogle);
        _views[tab] = startPage;
        BrowserHost.Children.Add(startPage);
        startPage.Visibility = Visibility.Hidden;
        SetTabTitle(tab, "New Tab");
        tab.ToolTip = "New Tab";
        if (Tabs.SelectedItem == tab) AttachSelectedView();
    }

    private void CreateBrowser(TabItem tab, string url)
    {
        if (!_googleLoginAcknowledged)
        {
            StatusText.Text = "Sign in with Google before browsing";
            ShowNewTab(tab, loginRequired: true);
            return;
        }

        DisposeBrowserForTab(tab);

        var browser = new ChromiumWebBrowser { Address = "about:blank" };
        browser.DownloadHandler = new CoBrowserDownloadHandler(_downloadManager);
        browser.LifeSpanHandler = new CoBrowserLifeSpanHandler(targetUrl => PostToUi(() =>
        {
            if (_isClosing || string.IsNullOrWhiteSpace(targetUrl)) return;
            AddTab(targetUrl);
        }));
        browser.RequestHandler = new CrashRecoveryRequestHandler(status => PostToUi(() =>
        {
            if (!IsLiveTabBrowser(tab, browser)) return;
            if (Tabs.SelectedItem == tab)
                StatusText.Text = "This page stopped unexpectedly. Press Reload to recover.";
            App.Log($"Renderer recovery available for tab; termination status={status}");
        }));
        _browsers[tab] = browser;
        _views[tab] = browser;
        browser.Visibility = Visibility.Hidden;
        tab.ToolTip = url;
        BrowserHost.Children.Add(browser);

        browser.TitleChanged += (_, _) => PostToUi(() =>
        {
            if (!IsLiveTabBrowser(tab, browser)) return;
            var title = string.IsNullOrWhiteSpace(browser.Title) ? "New Tab" : browser.Title;
            SetTabTitle(tab, title);
            if (Tabs.SelectedItem == tab) StatusText.Text = title;
        });

        browser.AddressChanged += (_, _) => PostToUi(() =>
        {
            if (!IsLiveTabBrowser(tab, browser)) return;
            var address = browser.Address ?? string.Empty;
            tab.ToolTip = address;
            if (Tabs.SelectedItem == tab)
            {
                _changingAddress = true;
                AddressBox.Text = address;
                _changingAddress = false;
                UpdateBookmarkButton();
            }
        });

        browser.LoadingStateChanged += (_, args) =>
        {
            var canGoBack = args.CanGoBack;
            var canGoForward = args.CanGoForward;
            var isLoading = args.IsLoading;
            PostToUi(() =>
            {
                if (!IsLiveTabBrowser(tab, browser) || Tabs.SelectedItem != tab) return;
                BackButton.IsEnabled = canGoBack;
                ForwardButton.IsEnabled = canGoForward;
                ReloadButton.Content = isLoading ? MakeIcon("close", 16) : MakeIcon("reload", 17);
                ReloadButton.ToolTip = isLoading ? "Stop loading" : "Reload";
                ReloadButton.FontFamily = new FontFamily("Segoe UI");
                StatusText.Text = isLoading ? "Loading page…" : "Ready";
            });
        };

        browser.LoadError += (_, args) =>
        {
            var failedUrl = args.FailedUrl ?? string.Empty;
            var errorCode = args.ErrorCode.ToString();
            if (errorCode.Contains("Aborted", StringComparison.OrdinalIgnoreCase)) return;
            PostToUi(() =>
            {
                if (!IsLiveTabBrowser(tab, browser)) return;
                if (Tabs.SelectedItem == tab)
                    StatusText.Text = "Page error: " + errorCode;
                App.Log($"LoadError [{errorCode}] {failedUrl}");
            });
        };

        browser.FrameLoadEnd += (_, args) =>
        {
            try
            {
                if (!args.Frame.IsMain) return;
                var pageUrl = args.Frame.Url;
                if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri) ||
                    (pageUri.Scheme != Uri.UriSchemeHttp && pageUri.Scheme != Uri.UriSchemeHttps)) return;

                PostToUi(() =>
                {
                    if (!IsLiveTabBrowser(tab, browser)) return;
                    _ = AddHistorySafelyAsync(pageUrl, browser.Title ?? string.Empty);
                    _ = UpdateTabFaviconAsync(tab, browser, pageUrl);
                });
            }
            catch (Exception ex)
            {
                App.LogException(ex, "CEF FrameLoadEnd callback");
            }
        };

        AttachSelectedView();
        if (Tabs.SelectedItem == tab)
        {
            _changingAddress = true;
            AddressBox.Text = url;
            _changingAddress = false;
        }

        try { browser.Address = url; }
        catch (Exception ex)
        {
            App.LogException(ex, "Set browser address");
            StatusText.Text = "Unable to navigate. Check the browser log.";
        }
    }

    private async Task UpdateTabFaviconAsync(TabItem tab, ChromiumWebBrowser browser, string pageUrl)
    {
        try
        {
            if (!_tabFavicons.TryGetValue(tab, out var target)) return;
            var script = "(() => { const l = document.querySelector('link[rel~=\"icon\"]'); return l?.href || new URL('/favicon.ico', location.origin).href; })()";
            var result = await browser.EvaluateScriptAsync(script).ConfigureAwait(false);
            var iconUrl = result.Success && result.Result is string value && Uri.TryCreate(value, UriKind.Absolute, out var parsed)
                ? parsed : new Uri(new Uri(pageUrl), "/favicon.ico");
            if (iconUrl.Scheme is not ("http" or "https")) return;
            using var response = await FaviconHttp.GetAsync(iconUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.Length > 512 * 1024) return;
            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(bytes))
            {
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 32; bitmap.StreamSource = stream; bitmap.EndInit();
            }
            if (bitmap.CanFreeze) bitmap.Freeze();
            PostToUi(() =>
            {
                if (IsLiveTabBrowser(tab, browser) && _tabFavicons.TryGetValue(tab, out var image))
                    image.Source = bitmap;
            });
        }
        catch (Exception ex) { App.LogException(ex, "Load website favicon"); }
    }

    private async Task AddHistorySafelyAsync(string url, string title)
    {
        try { await _history.AddAsync(url, title).ConfigureAwait(false); }
        catch (Exception ex) { App.LogException(ex, "History write"); }
    }

    private bool IsLiveTabBrowser(TabItem tab, ChromiumWebBrowser browser) =>
        !_isClosing && Tabs.Items.Contains(tab) && _browsers.TryGetValue(tab, out var current) && ReferenceEquals(current, browser);

    private void PostToUi(Action action)
    {
        if (_isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        try
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
                try { action(); }
                catch (Exception ex) { App.LogException(ex, "WPF browser callback"); }
            }));
        }
        catch (Exception ex) { App.LogException(ex, "Dispatch browser callback"); }
    }

    private void DisposeBrowserForTab(TabItem tab)
    {
        _views.TryGetValue(tab, out var view);
        if (_browsers.Remove(tab, out var browser))
        {
            // Dispose the native Chromium control before detaching it from WPF's visual tree.
            // Detaching first can race CefSharp's WPF unload/disposal callbacks.
            try { browser.Dispose(); }
            catch (Exception ex) { App.LogException(ex, "Dispose browser tab"); }
        }

        _views.Remove(tab);
        _tabFavicons.Remove(tab);
        if (view is DownloadsPage downloadsPage) downloadsPage.Dispose();
        if (view is not null && BrowserHost.Children.Contains(view))
            BrowserHost.Children.Remove(view);
    }

    private void SetTabTitle(TabItem tab, string value)
    {
        if (tab.Tag is TextBlock titleBlock)
        {
            var safeTitle = string.IsNullOrWhiteSpace(value) ? "New Tab" : value.Trim();
            titleBlock.Text = safeTitle.Length > 30 ? safeTitle[..30] + "…" : safeTitle;
        }
    }

    private void AttachSelectedView()
    {
        if (_isClosing) return;
        foreach (UIElement child in BrowserHost.Children)
            child.Visibility = Visibility.Hidden;

        if (CurrentTab is { } selected && _views.TryGetValue(selected, out var view))
        {
            if (!BrowserHost.Children.Contains(view)) BrowserHost.Children.Add(view);
            view.Visibility = Visibility.Visible;
        }

        if (CurrentBrowser is { } browser)
        {
            _changingAddress = true;
            AddressBox.Text = browser.Address ?? string.Empty;
            _changingAddress = false;
            StatusText.Text = string.IsNullOrWhiteSpace(browser.Address) ? "Ready" : (browser.IsLoading ? "Loading page…" : "Ready");
        }
        else
        {
            _changingAddress = true;
            AddressBox.Text = string.Empty;
            _changingAddress = false;
            StatusText.Text = "CoBrowser start page";
        }

        UpdateNavigationState();
        UpdateBookmarkButton();
    }

    private void NavigateInTab(TabItem tab, string input)
    {
        if (!Tabs.Items.Contains(tab)) return;
        Tabs.SelectedItem = tab;
        Navigate(input);
    }

    private void Navigate(string input)
    {
        var value = input.Trim();
        if (value.Length == 0 || CurrentTab is not { } tab) return;

        try
        {
            var target = ResolveInputTarget(value);
            if (!_googleLoginAcknowledged)
            {
                StatusText.Text = "Sign in with Google before browsing";
                if (CurrentTab is { } lockedTab) ShowNewTab(lockedTab, loginRequired: true);
                return;
            }

            if (CurrentBrowser is { } browser)
                browser.Load(target);
            else
                CreateBrowser(tab, target);

            if (Tabs.SelectedItem == tab)
            {
                _changingAddress = true;
                AddressBox.Text = target;
                _changingAddress = false;
            }
        }
        catch (Exception ex)
        {
            App.LogException(ex, "Navigate");
            StatusText.Text = "Navigation failed. See cobrowser.log.";
        }
    }

    private string ResolveInputTarget(string input)
    {
        var value = input.Trim();
        if (value.Length == 0) return string.Empty;

        // File paths entered directly in the omnibox use the same safe file routing as Open File.
        if (File.Exists(value)) return LocalFileViewer.CreateNavigationTarget(value);
        if (Uri.TryCreate(value, UriKind.Absolute, out var fileUri) && fileUri.IsFile && File.Exists(fileUri.LocalPath))
            return LocalFileViewer.CreateNavigationTarget(fileUri.LocalPath);

        return ResolveAddressOrSearch(value);
    }

    private string ResolveAddressOrSearch(string input)
    {
        var value = input.Trim();
        if (value.Length == 0) return string.Empty;

        if (Uri.TryCreate(value, UriKind.Absolute, out var explicitUri) &&
            (explicitUri.Scheme == Uri.UriSchemeHttp || explicitUri.Scheme == Uri.UriSchemeHttps ||
             explicitUri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase) ||
             explicitUri.Scheme.Equals("view-source", StringComparison.OrdinalIgnoreCase)))
            return explicitUri.ToString();

        // Treat hostnames, IPv4/IPv6 literals, localhost and host:port as URLs; everything else is a search query.
        if (!value.Any(char.IsWhiteSpace) && !value.StartsWith('?'))
        {
            var candidate = "https://" + value;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var candidateUri))
            {
                var host = candidateUri.Host.Trim('[', ']');
                var looksLikeHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                                    IPAddress.TryParse(host, out _) ||
                                    host.Contains('.') || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
                if (looksLikeHost && candidateUri.Port is > 0 and <= 65535)
                    return candidateUri.ToString();
            }
        }

        var searchBase = _searchProvider.Equals("Google", StringComparison.OrdinalIgnoreCase)
            ? "https://www.google.com/search?q="
            : "https://www.bing.com/search?q=";
        return searchBase + Uri.EscapeDataString(value);
    }

    private void SignInWithGoogle()
    {
        try
        {
            var loginWindow = new LoginWindow(_authService, _googleUser?.Email) { Owner = this };
            if (loginWindow.ShowDialog() != true || loginWindow.AuthenticatedUser is null) return;
            _googleUser = loginWindow.AuthenticatedUser;
            _googleLoginAcknowledged = true;
            UpdateProfileButton();
            if (CurrentTab is { } activeTab) ShowNewTab(activeTab, loginRequired: false);
            StatusText.Text = "Connected to CoBrowser as " + _googleUser.Email;
        }
        catch (Exception ex)
        {
            App.LogException(ex, "Re-authenticate Google account");
            MessageBox.Show(this, ex.Message, "Google account", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SignOutOfCoBrowser()
    {
        try
        {
            _authService.SignOut();
            _googleLoginAcknowledged = false;
            foreach (var tab in Tabs.Items.OfType<TabItem>().ToList())
                ShowNewTab(tab, loginRequired: true);
            UpdateProfileButton();
            StatusText.Text = "Google account disconnected. Sign in to continue browsing.";
        }
        catch (Exception ex)
        {
            App.LogException(ex, "Disconnect Google account");
            MessageBox.Show(this, ex.Message, "Google account", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateProfileButton()
    {
        if (ProfileInitialText is not null)
        {
            var userName = _googleUser?.Name;
            var initial = _googleLoginAcknowledged && !string.IsNullOrWhiteSpace(userName)
                ? userName.Trim()[0].ToString().ToUpperInvariant()
                : "C";
            ProfileInitialText.Text = initial;
        }
        if (ProfileButton is not null)
            ProfileButton.ToolTip = _googleLoginAcknowledged
                ? $"CoBrowser account\n{_googleUser?.Name ?? "Signed in"}\n{_googleUser?.Email ?? string.Empty}"
                : "Sign in with Google";
    }

    private void CloseTab(TabItem tab)
    {
        if (!Tabs.Items.Contains(tab)) return;
        if (_browsers.TryGetValue(tab, out var current) && !string.IsNullOrWhiteSpace(current.Address))
            _closedTabs.Push(current.Address);

        var oldIndex = Tabs.Items.IndexOf(tab);
        DisposeBrowserForTab(tab);
        Tabs.Items.Remove(tab);

        if (Tabs.Items.Count == 0)
        {
            AddTab();
            return;
        }
        Tabs.SelectedIndex = Math.Clamp(oldIndex - 1, 0, Tabs.Items.Count - 1);
        AttachSelectedView();
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => AddTab();
    private void Back_Click(object sender, RoutedEventArgs e) { if (CurrentBrowser is { IsBrowserInitialized: true } browser && browser.CanGoBack) browser.Back(); }
    private void Forward_Click(object sender, RoutedEventArgs e) { if (CurrentBrowser is { IsBrowserInitialized: true } browser && browser.CanGoForward) browser.Forward(); }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentBrowser is not { IsBrowserInitialized: true } browser) return;
        if (browser.IsLoading) browser.Stop(); else browser.Reload();
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab is { } tab) ShowNewTab(tab, !_googleLoginAcknowledged);
    }

    private void AddressSearch_Click(object sender, RoutedEventArgs e) => Navigate(AddressBox.Text);

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Navigate(AddressBox.Text);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void AddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_changingAddress) AddressBox.SelectAll();
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Tabs) && !_isClosing) AttachSelectedView();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; return; }
        if (e.Key == Key.F12) { if (CurrentBrowser?.IsBrowserInitialized == true) CurrentBrowser.ShowDevTools(); e.Handled = true; return; }
        if (!ctrl) return;

        switch (e.Key)
        {
            case Key.T when shift: RestoreClosedTab(); e.Handled = true; break;
            case Key.T: AddTab(); e.Handled = true; break;
            case Key.W: if (CurrentTab is { } tab) CloseTab(tab); e.Handled = true; break;
            case Key.L: AddressBox.Focus(); AddressBox.SelectAll(); e.Handled = true; break;
            case Key.R: Reload_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.D: Bookmark_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.H: ShowHistory(); e.Handled = true; break;
            case Key.J: OpenDownloads(); e.Handled = true; break;
            case Key.O: OpenFile(); e.Handled = true; break;
            case Key.U: ViewSource(); e.Handled = true; break;
            case Key.Tab: SelectRelativeTab(shift ? -1 : 1); e.Handled = true; break;
            case Key.N when shift: OpenNewWindow(); e.Handled = true; break;
            case Key.F: ShowFindBar(); e.Handled = true; break;
            case Key.OemPlus: ChangeZoom(0.1); e.Handled = true; break;
            case Key.OemMinus: ChangeZoom(-0.1); e.Handled = true; break;
            case Key.D0: _zoomLevel = 0; ApplyZoom(); e.Handled = true; break;
        }
    }

    private void SelectRelativeTab(int direction)
    {
        if (Tabs.Items.Count < 2) return;
        var index = Tabs.SelectedIndex + direction;
        if (index < 0) index = Tabs.Items.Count - 1;
        if (index >= Tabs.Items.Count) index = 0;
        Tabs.SelectedIndex = index;
    }

    private void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        var browser = CurrentBrowser;
        if (browser is null || string.IsNullOrWhiteSpace(browser.Address)) return;
        var existing = _bookmarks.FirstOrDefault(b => b.Url.Equals(browser.Address, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) _bookmarks.Remove(existing);
        else _bookmarks.Add(new BookmarkEntry(browser.Title ?? browser.Address, browser.Address));
        SaveBookmarks();
        UpdateBookmarkButton();
        StatusText.Text = existing is null ? "Bookmark saved" : "Bookmark removed";
    }

    private void UpdateBookmarkButton()
    {
        var url = CurrentBrowser?.Address;
        var saved = url is not null && _bookmarks.Any(b => b.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
        var bookmarkIcon = MakeIcon("bookmark", 17);
        bookmarkIcon.Stroke = saved ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("IconBrush");
        BookmarkButton.Content = bookmarkIcon;
    }

    private void LoadBookmarks()
    {
        try
        {
            if (!File.Exists(_bookmarkPath)) return;
            var entries = JsonSerializer.Deserialize<List<BookmarkEntry?>>(File.ReadAllText(_bookmarkPath));
            if (entries is not null)
            {
                _bookmarks.AddRange(entries
                    .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Url))
                    .Select(x => x!));
            }
        }
        catch (Exception ex)
        {
            _bookmarks.Clear();
            App.LogException(ex, "Load bookmarks");
        }
    }

    private void SaveBookmarks()
    {
        try
        {
            Directory.CreateDirectory(App.DataRoot);
            var tempPath = _bookmarkPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_bookmarks, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, _bookmarkPath, overwrite: true);
        }
        catch (Exception ex)
        {
            App.LogException(ex, "Save bookmarks");
            MessageBox.Show("Could not save bookmarks. Check cobrowser.log.\n\n" + ex.Message,
                "CoBrowser bookmarks", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        AddMenu(menu, "New tab", () => AddTab(), "Ctrl+T");
        AddMenu(menu, "New window", OpenNewWindow, "Ctrl+Shift+N");
        AddMenu(menu, "Open file…", OpenFile, "Ctrl+O");
        AddMenu(menu, "Restore closed tab", RestoreClosedTab, "Ctrl+Shift+T", _closedTabs.Count > 0);
        menu.Items.Add(new Separator());
        AddMenu(menu, "History", ShowHistory, "Ctrl+H");
        AddMenu(menu, "Bookmarks", ShowBookmarks, "Ctrl+D");
        AddMenu(menu, "Downloads", OpenDownloads, "Ctrl+J");
        AddMenu(menu, "Open downloads folder", OpenDownloadsFolder);
        menu.Items.Add(new Separator());
        AddMenu(menu, "View page source", ViewSource, "Ctrl+U", CurrentBrowser is not null);
        AddMenu(menu, "Zoom in", () => ChangeZoom(0.1), "Ctrl++");
        AddMenu(menu, "Zoom out", () => ChangeZoom(-0.1), "Ctrl+-");
        AddMenu(menu, "Reset zoom", () => { _zoomLevel = 0; ApplyZoom(); }, "Ctrl+0");
        AddMenu(menu, "Find on page", ShowFindBar, "Ctrl+F");
        AddMenu(menu, "Developer tools", () => { if (CurrentBrowser is { IsBrowserInitialized: true } browser) browser.ShowDevTools(); }, "F12");
        AddMenu(menu, File.Exists(Path.Combine(App.DataRoot, "disable-gpu.flag")) ? "Re-enable GPU acceleration (restart)" : "Enable GPU compatibility mode (restart)", ToggleGpuCompatibility);
        menu.Items.Add(new Separator());
        var appearanceMenu = new MenuItem { Header = "Appearance" };
        AddThemeOption(appearanceMenu, "System", "Follow Windows appearance");
        AddThemeOption(appearanceMenu, "Light", "Light appearance");
        AddThemeOption(appearanceMenu, "Dark", "Dark appearance");
        menu.Items.Add(appearanceMenu);

        var searchMenu = new MenuItem { Header = "Search engine" };
        AddSearchOption(searchMenu, "Bing");
        AddSearchOption(searchMenu, "Google");
        menu.Items.Add(searchMenu);
        AddMenu(menu, "About CoBrowser", ShowAbout);
        menu.Items.Add(new Separator());
        AddMenu(menu, "Open CoBrowser data folder", () => OpenFolder(App.DataRoot));
        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
    }

    private static void AddMenu(ContextMenu menu, string label, Action action, string? gesture = null, bool enabled = true)
    {
        var item = new MenuItem { Header = label, InputGestureText = gesture, IsEnabled = enabled };
        item.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { App.LogException(ex, "Menu action: " + label); }
        };
        menu.Items.Add(item);
    }

    private void ToggleGpuCompatibility()
    {
        var flag = Path.Combine(App.DataRoot, "disable-gpu.flag");
        try
        {
            if (File.Exists(flag))
            {
                File.Delete(flag);
                MessageBox.Show("GPU acceleration has been re-enabled. Restart CoBrowser to apply the change.",
                    "CoBrowser graphics", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                File.WriteAllText(flag, "Disable GPU acceleration for compatibility with unstable graphics drivers.");
                MessageBox.Show("GPU acceleration will be disabled at the next start. Restart CoBrowser to apply the compatibility mode.",
                    "CoBrowser graphics", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            App.LogException(ex, "Toggle GPU compatibility mode");
            MessageBox.Show("Could not update graphics settings.\n\n" + ex.Message, "CoBrowser graphics",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        if (_googleLoginAcknowledged)
        {
            menu.Items.Add(new MenuItem { Header = _googleUser.Name ?? "Google account", IsEnabled = false });
            menu.Items.Add(new MenuItem { Header = _googleUser.Email, IsEnabled = false });
            menu.Items.Add(new Separator());
            AddMenu(menu, "Manage Google Account", () => AddTab("https://myaccount.google.com/"));
            AddMenu(menu, "Switch Google account", SignInWithGoogle);
            AddMenu(menu, "Disconnect from CoBrowser", SignOutOfCoBrowser);
        }
        else
        {
            AddMenu(menu, "Sign in with Google", SignInWithGoogle);
        }
        menu.Items.Add(new Separator());
        AddMenu(menu, "Open CoBrowser profile folder", () => OpenFolder(App.DataRoot));
        menu.PlacementTarget = ProfileButton;
        menu.IsOpen = true;
    }

    private void ShowHistory()
    {
        try
        {
            var rows = _history.GetRecent(60);
            if (rows.Count == 0)
            {
                MessageBox.Show("No browsing history yet.", "CoBrowser History", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var text = string.Join(Environment.NewLine + Environment.NewLine,
                rows.Take(30).Select(x => $"{x.VisitedAt.LocalDateTime:g}\n{x.Title}\n{x.Url}"));
            var choice = MessageBox.Show(text + "\n\nOpen history database folder?", "Recent browsing history",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (choice == MessageBoxResult.Yes) OpenFolder(Path.GetDirectoryName(_history.DatabasePath)!);
        }
        catch (Exception ex)
        {
            App.LogException(ex, "Show history");
            MessageBox.Show("Could not read browsing history. Check cobrowser.log.\n\n" + ex.Message,
                "CoBrowser History", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowBookmarks()
    {
        var menu = new ContextMenu();
        if (_bookmarks.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No bookmarks yet", IsEnabled = false });
        else
        {
            foreach (var bookmark in _bookmarks.ToList())
            {
                var item = new MenuItem { Header = bookmark.Title, ToolTip = bookmark.Url, Tag = bookmark.Url };
                item.Click += (_, _) => Navigate(item.Tag?.ToString() ?? string.Empty);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            AddMenu(menu, "Remove all bookmarks", () =>
            {
                if (MessageBox.Show("Remove all bookmarks?", "Bookmarks", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _bookmarks.Clear(); SaveBookmarks(); UpdateBookmarkButton();
                }
            });
        }
        menu.PlacementTarget = BookmarkButton;
        menu.IsOpen = true;
    }

    private void DownloadsButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadsPopup.IsOpen = !DownloadsPopup.IsOpen;
    }

    private void OpenDownloadsPage_Click(object sender, RoutedEventArgs e)
    {
        DownloadsPopup.IsOpen = false;
        OpenDownloads();
    }

    private void OpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
    {
        DownloadsPopup.IsOpen = false;
        OpenDownloadsFolder();
    }

    private void OpenDownloads()
    {
        AddTab();
        if (CurrentTab is not { } tab) return;

        DisposeBrowserForTab(tab);
        var page = new DownloadsPage(_downloadManager);
        _views[tab] = page;
        BrowserHost.Children.Add(page);
        SetTabTitle(tab, "Downloads");
        tab.ToolTip = "Downloads";
        AttachSelectedView();
        StatusText.Text = $"Downloads · {_downloadManager.Downloads.Count} items";
    }

    private void OpenDownloadsFolder()
    {
        Directory.CreateDirectory(_downloadManager.DownloadDirectory);
        OpenFolder(_downloadManager.DownloadDirectory);
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void OpenNewWindow()
    {
        var window = new MainWindow(_authService, _googleUser);
        window.Show();
    }

    private void RestoreClosedTab()
    {
        if (_closedTabs.TryPop(out var url)) AddTab(url);
    }

    private void ChangeZoom(double delta)
    {
        _zoomLevel = Math.Clamp(_zoomLevel + delta, -5, 5);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (CurrentBrowser?.IsBrowserInitialized == true) CurrentBrowser.SetZoomLevel(_zoomLevel);
        ZoomText.Text = $"{Math.Round(Math.Pow(1.2, _zoomLevel) * 100)}%";
    }

    private void ShowFindBar()
    {
        if (CurrentBrowser?.IsBrowserInitialized != true) return;
        var dialog = new FindDialog { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.SearchText))
            CurrentBrowser.Find(dialog.SearchText, true, false, false);
    }

    private void ViewSource()
    {
        if (CurrentBrowser?.IsBrowserInitialized != true) return;
        var address = CurrentBrowser.Address;
        if (string.IsNullOrWhiteSpace(address)) return;
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            var path = uri.LocalPath;
            if (File.Exists(path)) OpenLocalFile(path, forceCodeViewer: true);
            return;
        }
        if (address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            AddTab("view-source:" + address);
    }

    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a file in CoBrowser",
            Filter = "All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) OpenLocalFile(dialog.FileName);
    }

    private void OpenLocalFile(string path, bool forceCodeViewer = false)
    {
        try
        {
            if (!_googleLoginAcknowledged)
            {
                MessageBox.Show("Complete the Google sign-in step before opening local files.",
                    "CoBrowser first-run setup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var original = Path.GetFullPath(path);
            var target = forceCodeViewer
                ? LocalFileViewer.CreateSourceSnapshot(original)
                : LocalFileViewer.CreateNavigationTarget(original);
            Navigate(target);
        }
        catch (Exception ex)
        {
            App.LogException(ex, "Open local file");
            MessageBox.Show("CoBrowser could not open this file.\n\n" + ex.Message + "\n\nLog: " + App.LogPath,
                "Open file error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && File.Exists(files[0]))
        {
            OpenLocalFile(files[0]);
            e.Handled = true;
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            "CoBrowser 1.0.5\nCreator: CoCo X Who?\nWindows engine: Chromium Embedded Framework (CEF) via CefSharp\nProfile: " + Path.Combine(App.DataRoot, "User Data") +
            "\n\nText/source files open in the read-only code viewer. SVG/PNG and supported documents are rendered by Chromium. DLL/EXE files are inspected as binary and are never executed or decompiled by CoBrowser.\n\nLogs: " + App.LogPath,
            "About CoBrowser", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _normalWindowStyle = WindowStyle;
            _normalWindowState = WindowState;
            _isFullscreen = true;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else
        {
            _isFullscreen = false;
            WindowStyle = _normalWindowStyle;
            WindowState = _normalWindowState;
        }
    }

    private static bool ReadSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0;
        }
        catch { return false; }
    }

    private void AddThemeOption(MenuItem parent, string mode, string description)
    {
        var item = new MenuItem { Header = mode, ToolTip = description, IsCheckable = true, IsChecked = _themeMode == mode };
        item.Click += (_, _) => SetThemeMode(mode);
        parent.Items.Add(item);
    }

    private void AddSearchOption(MenuItem parent, string provider)
    {
        var item = new MenuItem { Header = provider, IsCheckable = true, IsChecked = _searchProvider.Equals(provider, StringComparison.OrdinalIgnoreCase) };
        item.Click += (_, _) =>
        {
            _searchProvider = provider;
            SaveBrowserSettings();
            StatusText.Text = $"Search engine: {provider}";
        };
        parent.Items.Add(item);
    }

    private void SetThemeMode(string mode)
    {
        _themeMode = mode;
        SaveBrowserSettings();
        ApplyTheme(mode == "Dark" || (mode == "System" && ReadSystemDarkMode()));
    }

    private void LoadBrowserSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath));
            var root = document.RootElement;
            if (root.TryGetProperty("theme", out var theme) && theme.ValueKind == JsonValueKind.String)
            {
                var value = theme.GetString();
                if (value is "System" or "Light" or "Dark") _themeMode = value;
            }
            if (root.TryGetProperty("searchProvider", out var provider) && provider.ValueKind == JsonValueKind.String)
            {
                var value = provider.GetString();
                if (value is "Bing" or "Google") _searchProvider = value;
            }
        }
        catch (Exception ex) { App.LogException(ex, "Load browser settings"); }
    }

    private void SaveBrowserSettings()
    {
        try
        {
            Directory.CreateDirectory(App.DataRoot);
            var json = JsonSerializer.Serialize(new { theme = _themeMode, searchProvider = _searchProvider }, new JsonSerializerOptions { WriteIndented = true });
            var temp = _settingsPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _settingsPath, true);
        }
        catch (Exception ex) { App.LogException(ex, "Save browser settings"); }
    }

    private void SystemThemeChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || _themeMode != "System" || _isClosing) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_isClosing && _themeMode == "System") ApplyTheme(ReadSystemDarkMode());
        }));
    }

    private void ApplyTheme(bool dark)
    {
        _isDark = dark;
        // Chrome neutrals with Bing-blue accents in system, dark and light modes.
        SetBrush("WindowBrush", _isDark ? "#202124" : "#FFFFFF");
        SetBrush("TabStripBrush", _isDark ? "#202124" : "#DEE1E6");
        SetBrush("ToolbarBrush", _isDark ? "#35363A" : "#FFFFFF");
        SetBrush("SurfaceBrush", _isDark ? "#292A2D" : "#F1F3F4");
        SetBrush("ActiveTabBrush", _isDark ? "#35363A" : "#FFFFFF");
        SetBrush("OmniboxBrush", _isDark ? "#202124" : "#F1F3F4");
        SetBrush("TextBrush", _isDark ? "#E8EAED" : "#202124");
        SetBrush("MutedBrush", _isDark ? "#9AA0A6" : "#5F6368");
        SetBrush("BorderBrushCo", _isDark ? "#4B4D52" : "#DADCE0");
        SetBrush("AccentBrush", _isDark ? "#8AB4F8" : "#0B6FCB");
        SetBrush("HoverBrush", _isDark ? "#44474B" : "#E8EAED");
        SetBrush("ButtonSurfaceBrush", _isDark ? "#3C4043" : "#F8F9FA");
        SetBrush("IconBrush", _isDark ? "#BDC1C6" : "#5F6368");
        StatusText.Text = _isDark ? "Dark appearance" : "Light appearance";
    }

    private void SetBrush(string key, string color) =>
        Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    private ShapePath MakeIcon(string key, double size)
    {
        // Resource keys in App.xaml use PascalCase (Icon.Close, Icon.Reload, etc.).
        // Normalize here so tab close/reload/bookmark icons cannot fail during first window construction.
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("An icon key is required.", nameof(key));

        var normalizedKey = char.ToUpperInvariant(key[0]) + key[1..];
        if (TryFindResource("Icon." + normalizedKey) is not Geometry geometry)
            throw new InvalidOperationException($"CoBrowser vector icon resource 'Icon.{normalizedKey}' was not found.");
        if (TryFindResource("IconBrush") is not Brush iconBrush)
            iconBrush = Brushes.LightSteelBlue;

        return new ShapePath
        {
            Data = geometry,
            Stroke = iconBrush,
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform
        };
    }

    private void UpdateNavigationState()
    {
        BackButton.IsEnabled = CurrentBrowser?.CanGoBack == true;
        ForwardButton.IsEnabled = CurrentBrowser?.CanGoForward == true;
        ReloadButton.IsEnabled = CurrentBrowser is not null;
        if (CurrentBrowser is null) ZoomText.Text = "100%";
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosing) return;
        _isClosing = true;
        _downloadManager.Downloads.CollectionChanged -= _downloadCollectionChanged;
        SystemEvents.UserPreferenceChanged -= SystemThemeChanged;

        // Stop callbacks from reaching WPF controls, then dispose every browser before Cef.Shutdown.
        foreach (var browser in _browsers.Values.ToList())
        {
            try { browser.Dispose(); }
            catch (Exception ex) { App.LogException(ex, "Window shutdown browser disposal"); }
        }
        _browsers.Clear();
        foreach (var downloadsPage in _views.Values.OfType<DownloadsPage>()) downloadsPage.Dispose();
        _views.Clear();
        BrowserHost.Children.Clear();
        _history.Dispose();
        App.Log("MainWindow closing: all CefSharp controls disposed.");
    }

    private sealed record BookmarkEntry(string Title, string Url);
}
