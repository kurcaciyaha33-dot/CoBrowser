using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace CoBrowser.Auth;

/// <summary>
/// Google OAuth for an installed Windows desktop app. It uses the system browser,
/// loopback redirect and PKCE. It never reads passwords or imports Chrome cookies.
/// </summary>
public sealed class GoogleOAuthService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataRoot;
    private readonly string _settingsPath;
    private readonly string _sessionPath;

    public GoogleOAuthService(string dataRoot)
    {
        _dataRoot = dataRoot;
        Directory.CreateDirectory(_dataRoot);
        _settingsPath = Path.Combine(_dataRoot, "google-oauth.json");
        _sessionPath = Path.Combine(_dataRoot, "google-auth.bin");
    }

    public string SettingsPath => _settingsPath;
    public string? ReadConfiguredClientId() => LoadOAuthSettings()?.ClientId;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(LoadOAuthSettings()?.ClientId);

    public void SaveClientId(string clientId)
    {
        clientId = clientId.Trim();
        if (!clientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Enter a Google OAuth Desktop application Client ID ending in .apps.googleusercontent.com.");

        var json = JsonSerializer.Serialize(new { client_id = clientId }, JsonOptions);
        WriteAtomic(_settingsPath, json);
    }

    public GoogleUserProfile? TryLoadSavedProfile()
    {
        try
        {
            if (!File.Exists(_sessionPath)) return null;
            var protectedBytes = File.ReadAllBytes(_sessionPath);
            var jsonBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var session = JsonSerializer.Deserialize<GoogleStoredSession>(jsonBytes, JsonOptions);
            if (session is null || string.IsNullOrWhiteSpace(session.RefreshToken) ||
                string.IsNullOrWhiteSpace(session.User.Email) || !session.User.EmailVerified)
                return null;
            return session.User;
        }
        catch (CryptographicException ex)
        {
            App.LogException(ex, "Unprotect Google account session");
            return null;
        }
        catch (Exception ex)
        {
            App.LogException(ex, "Read Google account session");
            return null;
        }
    }

    /// <summary>
    /// Validates an email typed by the user and returns it trimmed. Any domain is allowed
    /// because Google Workspace accounts are not limited to gmail.com.
    /// </summary>
    public static bool TryNormalizeEmail(string? input, out string email)
    {
        email = string.Empty;
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 254) return false;
        try
        {
            var address = new System.Net.Mail.MailAddress(value);
            if (!string.Equals(address.Address, value, StringComparison.Ordinal)) return false;
            if (!address.Host.Contains('.')) return false;
        }
        catch (FormatException) { return false; }
        email = value;
        return true;
    }

    /// <summary>
    /// Compares two Google account emails. Case is ignored; for gmail.com/googlemail.com dots and
    /// +tags in the local part are ignored too because Google treats them as the same mailbox.
    /// </summary>
    public static bool EmailsMatch(string? a, string? b)
    {
        static string Canon(string? value)
        {
            var v = (value ?? string.Empty).Trim().ToLowerInvariant();
            var at = v.LastIndexOf('@');
            if (at <= 0) return v;
            var local = v[..at];
            var domain = v[(at + 1)..];
            if (domain is "gmail.com" or "googlemail.com")
            {
                var plus = local.IndexOf('+');
                if (plus >= 0) local = local[..plus];
                local = local.Replace(".", string.Empty);
                domain = "gmail.com";
            }
            return local + "@" + domain;
        }
        return Canon(a).Length > 0 && Canon(a) == Canon(b);
    }

    /// <param name="loginHint">The email the user typed. Google pre-fills it, and the signed-in account must match it.</param>
    public async Task<GoogleUserProfile> SignInAsync(string? loginHint = null, CancellationToken cancellationToken = default)
    {
        var oauth = LoadOAuthSettings();
        var clientId = oauth?.ClientId;
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Google sign-in is not configured. Add the Desktop OAuth credentials JSON (including client_id and client_secret) as google-oauth.json beside CoBrowser.exe or in the CoBrowser app data folder.");
        if (string.IsNullOrWhiteSpace(oauth!.ClientSecret))
            throw new InvalidOperationException("Google OAuth client_secret is missing. Download the OAuth client JSON for an application type 'Desktop app' from Google Cloud Console and place its contents in google-oauth.json. CoBrowser supports the original Google JSON format and the flat client_id/client_secret format.");

        var (listener, redirectUri) = StartLoopbackListener();
        using (listener)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromMinutes(4));
            var state = Base64Url(RandomNumberGenerator.GetBytes(32));
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid email profile",
                ["state"] = state,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["access_type"] = "offline",
                ["prompt"] = "consent"
            };
            if (!string.IsNullOrWhiteSpace(loginHint))
                parameters["login_hint"] = loginHint.Trim();
            var authorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth?" +
                string.Join("&", parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));

            App.Log("Starting Google OAuth using external system browser and loopback PKCE callback.");
            Process.Start(new ProcessStartInfo(authorizationUrl) { UseShellExecute = true });

            var callback = await WaitForCallbackAsync(listener, timeout.Token).ConfigureAwait(false);
            if (!string.Equals(callback.State, state, StringComparison.Ordinal))
                throw new InvalidOperationException("Google sign-in callback state did not match. The response was rejected.");
            if (!string.IsNullOrWhiteSpace(callback.Error))
                throw new InvalidOperationException("Google sign-in was not completed: " + callback.Error);
            if (string.IsNullOrWhiteSpace(callback.Code))
                throw new InvalidOperationException("Google did not return an authorization code.");

            var token = await ExchangeCodeAsync(clientId, oauth!.ClientSecret, redirectUri, callback.Code, verifier, timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token.AccessToken))
                throw new InvalidOperationException("Google returned an empty access token.");

            var user = await GetUserProfileAsync(token.AccessToken, timeout.Token).ConfigureAwait(false);
            if (!user.EmailVerified)
                throw new InvalidOperationException("Google did not report this email address as verified.");
            if (!string.IsNullOrWhiteSpace(loginHint) && !EmailsMatch(loginHint, user.Email))
                throw new InvalidOperationException(
                    $"You entered {loginHint.Trim()}, but Google signed in {user.Email}. Nothing was saved. Check the email or choose the right Google account.");

            var old = TryReadStoredSession();
            var refreshToken = string.IsNullOrWhiteSpace(token.RefreshToken) ? old?.RefreshToken : token.RefreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new InvalidOperationException("Google did not return a refresh token. Remove CoBrowser access from your Google Account and try signing in again.");

            var stored = new GoogleStoredSession(
                user,
                refreshToken,
                token.AccessToken,
                DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn, 60)));
            SaveSession(stored);
            App.Log($"Google OAuth completed successfully for {user.Email}");
            return user;
        }
    }

    public void SignOut()
    {
        try
        {
            if (File.Exists(_sessionPath)) File.Delete(_sessionPath);
            App.Log("Google account disconnected from CoBrowser profile.");
        }
        catch (Exception ex) { App.LogException(ex, "Sign out Google account"); throw; }
    }

    private OAuthClientSettings? LoadOAuthSettings()
    {
        // Supports both Google's downloaded Desktop OAuth JSON (installed.client_id/client_secret)
        // and CoBrowser's flat { client_id, client_secret } configuration.
        foreach (var candidate in new[] { _settingsPath, Path.Combine(AppContext.BaseDirectory, "google-oauth.json") })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                using var document = JsonDocument.Parse(File.ReadAllText(candidate));
                var root = document.RootElement;
                var source = root;
                if (root.TryGetProperty("installed", out var installed)) source = installed;
                else if (root.TryGetProperty("web", out var web)) source = web;
                var clientId = GetJsonString(source, "client_id")?.Trim();
                var clientSecret = GetJsonString(source, "client_secret")?.Trim();
                if (!string.IsNullOrWhiteSpace(clientId) && clientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
                    return new OAuthClientSettings(clientId, clientSecret);
            }
            catch (Exception ex) { App.LogException(ex, "Read Google OAuth settings from " + candidate); }
        }

        var environmentClientId = Environment.GetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_ID")?.Trim();
        var environmentSecret = Environment.GetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_SECRET")?.Trim();
        if (!string.IsNullOrWhiteSpace(environmentClientId) && environmentClientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
            return new OAuthClientSettings(environmentClientId, environmentSecret);
        return null;
    }

    private static string? GetJsonString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private (TcpListener Listener, string RedirectUri) StartLoopbackListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (listener, $"http://127.0.0.1:{port}");
    }

    private static async Task<OAuthCallback> WaitForCallbackAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
            string? line;
            do { line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false); }
            while (!string.IsNullOrEmpty(line));

            // Explicit char-array overload avoids ambiguity across SDK/compiler versions.
            string[] requestParts = requestLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (requestParts.Length < 2 || !Uri.TryCreate("http://127.0.0.1" + requestParts[1], UriKind.Absolute, out var requestUri))
            {
                await WriteCallbackResponseAsync(stream, false).ConfigureAwait(false);
                continue;
            }

            if (!requestUri.AbsolutePath.Equals("/", StringComparison.Ordinal))
            {
                await WriteCallbackResponseAsync(stream, false).ConfigureAwait(false);
                continue;
            }

            var query = HttpUtility.ParseQueryString(requestUri.Query);
            var callback = new OAuthCallback(query["code"], query["state"], query["error"]);
            await WriteCallbackResponseAsync(stream, true).ConfigureAwait(false);
            return callback;
        }
    }

    private static async Task WriteCallbackResponseAsync(NetworkStream stream, bool received)
    {
        var title = received ? "CoBrowser sign-in received" : "CoBrowser";
        var body = received
            ? "<h1>You're all set</h1><p>Google sign-in response received. Return to CoBrowser to finish connecting your account.</p>"
            : "<h1>CoBrowser</h1><p>This local callback is only used to complete a sign-in that you started in CoBrowser.</p>";
        var html = $"<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width'><title>{title}</title><style>body{{margin:0;background:#10192c;color:#eef5ff;font:16px Segoe UI,Arial;display:grid;place-items:center;min-height:100vh}}main{{max-width:480px;margin:24px;padding:36px;border:1px solid #344967;border-radius:22px;background:#18253b}}h1{{font-size:26px}}p{{line-height:1.6;color:#b8c8df}}</style></head><body><main>{body}</main></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header).ConfigureAwait(false);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static async Task<GoogleTokenResponse> ExchangeCodeAsync(string clientId, string clientSecret, string redirectUri, string code, string verifier, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        });
        using var response = await Http.PostAsync("https://oauth2.googleapis.com/token", content, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Google token exchange failed (" + (int)response.StatusCode + "): " + SafeError(json));
        return JsonSerializer.Deserialize<GoogleTokenResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Google returned an unreadable token response.");
    }

    private static async Task<GoogleUserProfile> GetUserProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Google user profile request failed (" + (int)response.StatusCode + "): " + SafeError(json));
        var profile = JsonSerializer.Deserialize<GoogleUserProfile>(json, JsonOptions)
            ?? throw new InvalidOperationException("Google returned an unreadable account profile.");
        if (string.IsNullOrWhiteSpace(profile.Subject) || string.IsNullOrWhiteSpace(profile.Email))
            throw new InvalidOperationException("Google profile did not include a verified account identifier and email.");
        return profile;
    }

    private GoogleStoredSession? TryReadStoredSession()
    {
        try
        {
            if (!File.Exists(_sessionPath)) return null;
            var json = ProtectedData.Unprotect(File.ReadAllBytes(_sessionPath), null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<GoogleStoredSession>(json, JsonOptions);
        }
        catch { return null; }
    }

    private void SaveSession(GoogleStoredSession session)
    {
        var raw = JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions);
        var protectedBytes = ProtectedData.Protect(raw, optionalEntropy: null, DataProtectionScope.CurrentUser);
        var temp = _sessionPath + ".tmp";
        File.WriteAllBytes(temp, protectedBytes);
        File.Move(temp, _sessionPath, overwrite: true);
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string SafeError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error_description", out var description)) return description.GetString() ?? "Unknown error";
            if (document.RootElement.TryGetProperty("error", out var error)) return error.GetString() ?? "Unknown error";
        }
        catch { }
        return json.Length > 500 ? json[..500] : json;
    }

    private sealed record OAuthClientSettings(string ClientId, string? ClientSecret);
    private sealed record GoogleStoredSession(GoogleUserProfile User, string RefreshToken, string AccessToken, DateTimeOffset AccessTokenExpiresAt);
    private sealed record GoogleTokenResponse([property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("scope")] string? Scope);
    private sealed record OAuthCallback(string? Code, string? State, string? Error);
}

public sealed record GoogleUserProfile(
    [property: JsonPropertyName("sub")] string Subject,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("email_verified")] bool EmailVerified,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("given_name")] string? GivenName,
    [property: JsonPropertyName("picture")] string? Picture);
