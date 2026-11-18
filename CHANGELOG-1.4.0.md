# CoBrowser 1.4.0

## Login uses the Google email
- New Chrome-style sign-in window: type your Google email, select **Next**. Google opens in the system browser with the email pre-filled (`login_hint`).
- CoBrowser verifies that the account Google signs in matches the email typed (case-insensitive; dots and `+tags` ignored for gmail.com). On a mismatch nothing is saved.
- The "Google OAuth Desktop Client ID" box is gone from the user-facing screen. The Client ID is publisher configuration: bundle `google-oauth.json` (see `google-oauth.sample.json`), pass `-p:GoogleOAuthClientId=...`, or use the **Publisher setup** panel, which only appears automatically when no Client ID is bundled.
- Added **Forgot email?** and **Create account** links (open Google's own pages).
- The profile menu's "Reconnect" is now "Switch Google account" and pre-fills the last email.

## Chrome x Bing theme
- Chrome dark neutrals (`#202124` frame, `#35363A` toolbar and active tab, pill omnibox with blue focus ring) and a four-colour Chrome accent line under the toolbar.
- Bing-style blue-to-teal gradient for primary buttons, the profile avatar and the new-tab search button.
- New tab page: soft Bing-style colour glow, large rounded search box, gradient Search button, Bing shortcut added.
- Light and dark theme toggle updated to the same palette.

## Unchanged

## Verification
- `GoogleOAuthService.cs` compiles on .NET 8 and the new email helpers passed 13 unit checks.
- The WPF windows could not be built or viewed in the authoring environment (no Windows, no NuGet access). Build with `dotnet build` on Windows and review the sign-in window and new tab page visually.
