using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CoBrowser.Auth;

namespace CoBrowser;

/// <summary>
/// Chrome-style sign-in: users enter only their Google email. Google verifies the account in the
/// system browser; application OAuth configuration is supplied by the publisher, not the user.
/// </summary>
public partial class LoginWindow : Window
{
    private static readonly Brush FocusBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0xB4, 0xF8)));
    private static readonly Brush IdleBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x5F, 0x63, 0x68)));
    private static readonly Brush ErrorBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0x8B, 0x82)));

    private readonly GoogleOAuthService _auth;
    private bool _hasError;

    public GoogleUserProfile? AuthenticatedUser { get; private set; }

    public LoginWindow(GoogleOAuthService auth, string? prefillEmail = null)
    {
        InitializeComponent();
        _auth = auth;

        if (!string.IsNullOrWhiteSpace(prefillEmail)) EmailBox.Text = prefillEmail.Trim();
        if (!_auth.IsConfigured)
        {
            ConfigurationNotice.Visibility = Visibility.Visible;
            NextButton.IsEnabled = false;
            StatusText.Text = "This app build is waiting for publisher configuration.";
        }

        Loaded += (_, _) => { EmailBox.Focus(); EmailBox.CaretIndex = EmailBox.Text.Length; UpdateOutline(); };
    }

    private static Brush Freeze(SolidColorBrush brush) { brush.Freeze(); return brush; }

    private void UpdateOutline()
    {
        var brush = _hasError ? ErrorBrush : (EmailBox.IsKeyboardFocused ? FocusBrush : IdleBrush);
        EmailOutline.BorderBrush = brush;
        EmailOutline.BorderThickness = new Thickness(EmailBox.IsKeyboardFocused || _hasError ? 2 : 1);
        EmailLabel.Foreground = brush;
    }

    private void ShowEmailError(string message)
    {
        _hasError = true;
        EmailError.Text = message;
        EmailError.Visibility = Visibility.Visible;
        UpdateOutline();
    }

    private void ClearEmailError()
    {
        _hasError = false;
        EmailError.Visibility = Visibility.Collapsed;
        UpdateOutline();
    }

    private void EmailBox_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) => UpdateOutline();

    private void EmailBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_hasError) ClearEmailError();
    }

    private void EmailBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && NextButton.IsEnabled)
        {
            e.Handled = true;
            NextButton_Click(sender, e);
        }
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (!GoogleOAuthService.TryNormalizeEmail(EmailBox.Text, out var email))
        {
            ShowEmailError(string.IsNullOrWhiteSpace(EmailBox.Text)
                ? "Enter your Google email."
                : "Enter a valid email address, for example name@gmail.com.");
            EmailBox.Focus();
            return;
        }

        if (!_auth.IsConfigured)
        {
            ConfigurationNotice.Visibility = Visibility.Visible;
            StatusText.Foreground = ErrorBrush;
            StatusText.Text = "Google sign-in is not configured in this app build. Contact the publisher.";
            return;
        }

        NextButton.IsEnabled = false;
        EmailBox.IsEnabled = false;
        var oldCaption = NextButton.Content;
        NextButton.Content = "Waiting…";
        StatusText.Foreground = (Brush)FindResource("MutedBrush");
        StatusText.Text = "Google is opening in your browser. Finish signing in there, then come back here.";
        try
        {
            AuthenticatedUser = await _auth.SignInAsync(email);
            StatusText.Text = "Signed in as " + AuthenticatedUser.Email;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Foreground = ErrorBrush;
            StatusText.Text = "Sign-in timed out or was cancelled. Select Next to try again.";
        }
        catch (Exception ex)
        {
            App.LogException(ex, "Google sign-in dialog");
            StatusText.Foreground = ErrorBrush;
            StatusText.Text = ex.Message;
        }
        finally
        {
            if (IsVisible)
            {
                NextButton.IsEnabled = true;
                EmailBox.IsEnabled = true;
                NextButton.Content = oldCaption;
            }
        }
    }

    private void ForgotEmail_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://accounts.google.com/signin/v2/usernamerecovery");

    private void CreateAccount_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://accounts.google.com/signup");

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { App.LogException(ex, "Open " + url); }
    }
}
