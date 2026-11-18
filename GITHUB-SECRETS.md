# GitHub Actions secrets for CoBrowser Windows releases

Add these repository secrets under **Settings → Secrets and variables → Actions**:

- `GOOGLE_OAUTH_CLIENT_ID`: OAuth 2.0 Client ID created for a Google **Desktop app**.
- `GOOGLE_OAUTH_CLIENT_SECRET`: matching OAuth 2.0 Client Secret for that Desktop app.

The `CoBrowser Build & Release` workflow requires both values. On a tag push (`v*`) or manual workflow run, it publishes the Windows x64 browser, writes `google-oauth.json` into the publish output, packages the Electron setup executable, and creates a portable ZIP.

## Important security note

GitHub Actions encrypts repository secrets while stored by GitHub and masks them in normal logs. This does **not** keep a value secret after a workflow embeds it in a public installer or portable archive. Anyone who downloads a public desktop app can inspect its files or runtime and recover bundled credentials, even if the value is encrypted with a key shipped in the same app.

Google Desktop OAuth credentials are for installed/public clients and must not be treated as confidential server credentials. Do not reuse this value as a password or as a credential protecting private APIs. Use a backend-held secret for any genuinely confidential operation.
