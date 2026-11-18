# CoBrowser 1.0.5

**Developer:** CoCo X Who?  
**Windows:** .NET 8 WPF + CefSharp 156 / Chromium  
**Branding:** unified Chrome-inspired browser layout with Microsoft Bing blue/teal visual accents

CoBrowser is a Windows-only Chromium-based desktop browser built with WPF and CefSharp. It does not use the CoBrowser Search/VCC APIs and does not wrap an external Chrome app.

## Repository layout

```text
CoBrowser/
├── CoBrowser.sln                 # Single dotnet build entry point
├── build/                        # .NET build orchestrator
├── windows/CoBrowser/            # Native WPF + CefSharp browser
├── installer/electron/            # Electron GUI installer
├── assets/                        # Original logo, ICO, PNG and SVG icon set
└── docs/                          # Build, Google sign-in and platform notes
```

## Windows build with dotnet (no PowerShell build script)

Requirements: Windows 10/11 x64, .NET 8 SDK, and network access to restore NuGet packages. For the Electron installer, also install Node.js/npm.

From the repository root, run the standard .NET clean/restore/build sequence:

```powershell
dotnet clean .\CoBrowser.sln -c Release -p:CoBrowserPlatform=Windows
dotnet restore .\CoBrowser.sln
dotnet build .\CoBrowser.sln -c Release -p:CoBrowserPlatform=Windows
```

The Windows OAuth service explicitly imports `System.IO` for its file and callback-stream operations. The solution build target then publishes the Windows CefSharp runtime into `artifacts/windows`.

To preconfigure the public Google Desktop OAuth Client ID in a release build (so end users do not need to type it on first launch), pass the **Client ID only**. Never pass a client secret:

```powershell
dotnet build .\CoBrowser.sln -c Release -p:CoBrowserPlatform=Windows -p:GoogleOAuthClientId=YOUR_CLIENT_ID.apps.googleusercontent.com
```

The same value can be supplied using the `GOOGLE_OAUTH_CLIENT_ID` environment variable. It is public app configuration, not a secret. Or copy `windows/CoBrowser/google-oauth.sample.json` to `windows/CoBrowser/google-oauth.json` and replace its sample value before building.

The login window itself asks users only for their Google email. The OAuth Client ID is a required app-registration setting supplied once by the publisher; it is not a user's Google Auth ID. A build with no configured OAuth application cannot complete Google sign-in.

Published browser payload:

```text
artifacts/windows/CoBrowser.exe
```

Build the Electron setup application too (Node.js/npm required):

```powershell
dotnet build .\CoBrowser.sln -c Release -p:CoBrowserPlatform=Windows -p:BuildInstaller=true
```

Installer output:

```text
artifacts/installer/CoBrowser-Setup-1.0.5-x64.exe
```

The Electron installer is a self-contained Electron setup UI packaged as a Windows executable. It copies the published browser payload, creates Start Menu and optional Desktop shortcuts, and leaves the CoBrowser profile under `%APPDATA%\CoBrowser` untouched. It does not require a `.ps1` build script; Node.js/npm are invoked from the .NET build target.

## Google sign-in on Windows

The Windows app requires a successful CoBrowser Google sign-in before the browser window opens. Users type their **Google email** and select **Next**; Gmail and Google Workspace/external Google Accounts can be used. Google's own page then opens in the system browser with the email pre-filled, and CoBrowser accepts only the same verified account. The user-facing UI does not request a Google Auth ID or OAuth Client ID. The publisher bundles the public Desktop app OAuth Client ID once (`windows/CoBrowser/google-oauth.json`, see `google-oauth.sample.json`, or `-p:GoogleOAuthClientId=...`). Create the app's client in Google Cloud Console:

https://console.cloud.google.com/auth/clients

The sign-in flow opens the system browser, uses OAuth authorization code + PKCE + a loopback callback, requests only `openid email profile`, verifies the returned account, and stores the CoBrowser account identity plus refresh token encrypted with Windows DPAPI. The Client ID is public configuration; no client secret is embedded or requested.

Important: this connects the Google identity to the **CoBrowser application profile**. It does not import Chrome cookies/passwords or automatically sign the separate Chromium website profile into Gmail/YouTube, and it does not provide Chrome Sync. Google OAuth must use the system browser for a native desktop app; embedded WebViews are not a supported way to sign users into Google.


## Downloads

The Windows browser handles Chromium downloads and saves them under `%APPDATA%\CoBrowser\Downloads`. Select the download-arrow toolbar button for recent progress, or press **Ctrl+J** to open the Downloads tab. The page includes progress, file size/speed, Open, Show in folder, Clear completed, and Open downloads folder actions.

## Data and safety

Windows profile: `%APPDATA%\CoBrowser\User Data`  
Windows app data: `%APPDATA%\CoBrowser`  
Downloads: `%APPDATA%\CoBrowser\Downloads` with completion history in `download-history.json`.  
Google token: DPAPI-encrypted `google-auth.bin` under app data.  
The build never imports data from an installed Google Chrome profile.

<<<<<<< HEAD
## Version 1.5.0 notes

- Fixed the missing `System.IO` import in Google OAuth callback/session file operations and clarified callback request parsing.
- Sign-in asks users for a Google email only; OAuth app configuration remains publisher-side.
- Added a functioning download handler, recent-download flyout and dedicated Downloads tab with progress and file actions.
- Continued the Chrome + Microsoft Bing theme: Chrome dark neutrals with Bing blue/teal accents.
- Details: `CHANGELOG-1.5.0.md`.

### Earlier 1.3.1 notes

- Single `dotnet` solution entry point for the Windows browser and Electron installer packaging.
- Updated CoBrowser identity/icon resources and WPF visual theme.
- Mandatory first-run Google identity link on Windows using desktop OAuth + PKCE.
- Electron setup application with destination picker, progress UI and shortcuts.
- 

## CoBrowser 1.0.5
=======
## CoBrowser 1.0.1
>>>>>>> 1f6ab7a (Update README.md)

- Product version corrected to 1.0.5 across Windows metadata, installer and release assets.
- Unified visual direction: Chromium/Chrome-inspired tab and toolbar layout with Microsoft Bing blue/teal accents.
- Added persistent System, Light and Dark appearance modes. System follows the Windows app appearance setting and reacts to appearance changes.
- Bing is the default address-bar search provider; Google remains selectable from the browser menu.
- Existing tabs, history, bookmarks, downloads, restore-closed-tab, zoom, find-in-page, developer tools and GPU compatibility controls remain available.
- Windows portable build and Electron setup remain separate artifacts.

Publisher-local OAuth JSON files are intentionally excluded from installer payloads and release archives.
