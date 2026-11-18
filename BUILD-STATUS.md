# CoBrowser 1.0.5 — Windows-only — build and verification status

- Windows source: WPF + CefSharp/Chromium, original CoBrowser vector icon set, logo, multi-resolution ICO and embedded image resources.
- Windows startup: staged diagnostics, explicit CEF lifecycle ownership, single-instance lock and persistent user profile.
- Google email sign-in: Gmail and Google Workspace/external Google Account email input; Google system-browser authorization-code + PKCE, ephemeral loopback callback, verified email matching, and DPAPI-protected local refresh token/profile. OAuth app Client ID is publisher-side configuration only; the user does not enter it.
- Downloads: CefSharp download handler writes to `%APPDATA%\CoBrowser\Downloads`; toolbar flyout and Downloads tab share live progress, file size/speed, completion state, open-file and show-in-folder actions.
- Theme: Chrome-inspired tab/toolbar surfaces with Microsoft Bing blue/teal accents; persistent System, Light and Dark modes. Bing is the default search provider, with Google selectable in the browser menu.
- Electron setup: custom CoBrowser UI, destination picker, progress screen, shortcuts and uninstall command. Node.js/npm and network access are required to package it.
- Verification: XAML/XML, project XML, JSON, Python and workflow YAML syntax checks pass. A full Windows build and CefSharp runtime test were not run because the .NET SDK and Windows runtime are unavailable. Build on Windows using `docs/BUILD.md` before release.
