using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CoBrowser;

public partial class NewTabPage : UserControl
{
    private readonly Action<string> _navigate;
    private readonly Action _signIn;
    private readonly bool _loginRequired;

    public NewTabPage(bool loginRequired, Action<string> navigate, Action signIn)
    {
        InitializeComponent();
        _loginRequired = loginRequired;
        _navigate = navigate;
        _signIn = signIn;
        LoginGatePanel.Visibility = loginRequired ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.IsEnabled = !loginRequired;
        SearchHint.Text = loginRequired ? "Sign in with Google before browsing" : "Search Bing or Google, or type a URL";
        ShortcutGrid.IsEnabled = !loginRequired;
        AccountButton.Content = loginRequired ? "Sign in" : "Google account";
    }

    private void SubmitSearch(bool lucky = false)
    {
        var query = SearchBox.Text.Trim();
        if (query.Length == 0 || _loginRequired) return;
        _navigate(lucky
            ? "https://www.google.com/search?btnI=1&q=" + Uri.EscapeDataString(query)
            : query);
    }

    private void Search_Click(object sender, RoutedEventArgs e) => SubmitSearch();
    private void Lucky_Click(object sender, RoutedEventArgs e) => SubmitSearch(true);
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { SubmitSearch(); e.Handled = true; }
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null) SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && !_loginRequired) _navigate(url);
    }
    private void Gmail_Click(object sender, RoutedEventArgs e) { if (!_loginRequired) _navigate("https://mail.google.com/"); }
    private void Images_Click(object sender, RoutedEventArgs e) { if (!_loginRequired) _navigate("https://www.google.com/imghp"); }
    private void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_loginRequired) _signIn();
        else _navigate("https://myaccount.google.com/");
    }
}
