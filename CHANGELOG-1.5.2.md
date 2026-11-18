# CoBrowser 1.5.2

- Fixed Google OAuth token exchange by reading both Google's downloaded Desktop OAuth JSON (`installed.client_id` / `installed.client_secret`) and CoBrowser's flat credential format.
- Includes `client_secret` in the authorization-code token exchange and shows a direct setup error when the secret is missing.
- Startup appearance now follows the Windows AppsUseLightTheme system setting; the existing menu toggle still switches between light and dark themes.
- Clarified that Google OAuth must use the supported system-browser flow. Google blocks embedded webviews for account authorization, so the Google password and consent screen cannot be hosted inside CefSharp.
