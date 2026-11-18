# Google email sign-in

## What users enter

The CoBrowser sign-in screen asks for a **Google email address**. It accepts Gmail addresses and other email addresses used with Google Accounts, including Google Workspace accounts. CoBrowser starts Google's official authorization in the Windows system browser, pre-fills the entered address, and verifies that Google returned the same verified email. Users do not enter a Google account ID, OAuth Client ID, password, or passkey inside CoBrowser.

## Publisher setup (required once per app)

Google requires an app using Sign in with Google to have its own OAuth application registration. This is app configuration, not a user's account identifier. Create an OAuth client of type **Desktop app**, then download its JSON file. Keep the `client_id` and `client_secret` fields together. CoBrowser accepts Google's original downloaded JSON format (`installed` object) and a flat `{ "client_id": "...", "client_secret": "..." }` format. The desktop client secret is a native-app credential and must not be treated as a server secret, but avoid publishing your local config in source control.

Place your already-created `google-oauth.json` beside `CoBrowser.exe` or in `%APPDATA%\CoBrowser\google-oauth.json`. Do not replace it with the sample file. If you only have a Client ID, download the full JSON again from Google Cloud Console → Google Auth Platform → Clients → your Desktop client → Download JSON. A web application client or an ID-only file can cause `invalid_client` / `client_secret is missing` at token exchange.

A build without a configured app OAuth Client ID cannot authenticate users. This is required by Google's OAuth protocol and cannot be replaced with the user's email address. The user-facing login still asks only for that email.

## Technical flow and privacy boundary

CoBrowser uses the installed-desktop authorization-code flow with PKCE and a temporary IPv4 loopback callback. It requests only `openid email profile`, checks the returned verified email against the typed address, and stores the CoBrowser identity link and refresh token protected with Windows DPAPI for the current Windows account. The Google password and passkey are entered only on Google's website in the system browser.

This links a Google identity to the local CoBrowser application profile. It does not import Chrome cookies/passwords, automatically sign the separate CefSharp website profile into Gmail/YouTube, or provide Chrome Sync.


## Why Google sign-in opens an external browser

Google blocks OAuth authorization in embedded webviews for security (embedded user-agent / `disallowed_useragent`). CoBrowser therefore cannot safely host the Google password/consent page inside CefSharp or an in-app browser panel. The supported desktop flow uses the system browser and a loopback callback back to CoBrowser. This protects the user's Google password and passkey and avoids a flow that Google may reject.



## Credential protection

SHA-256 is a one-way hash, not encryption, and must not be used to store a secret that needs to be recovered for OAuth token exchange. Windows uses DPAPI for the local session. Client IDs are public identifiers; client secrets embedded in a public desktop package are extractable and cannot be made secret by hashing or obfuscation.
