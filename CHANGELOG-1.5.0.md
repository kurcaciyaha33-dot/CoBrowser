# CoBrowser 1.5.0

## Windows browser

- Fixed the Google OAuth service compile errors by explicitly importing `System.IO` and using an explicit string-array split for the loopback callback request line.
- The sign-in screen now asks for a Google email only. Gmail and Google Workspace/external Google accounts are supported through Google's system-browser OAuth flow; users never enter an account ID, OAuth Client ID, or password into CoBrowser.
- Removed publisher OAuth configuration fields from the user sign-in screen. The publisher supplies the public Desktop OAuth application Client ID through `google-oauth.json`, the build property, or the runtime environment variable.
- Added a CefSharp download handler that saves files to `%APPDATA%\\CoBrowser\\Downloads`, chooses collision-safe file names, and tracks download progress, size, speed, completion, and cancellation.
- Added a toolbar download panel with recent activity, a dedicated Downloads tab/page, open-file and show-in-folder actions, a downloads-folder shortcut, and clear-completed support.
- Kept the Chrome dark-surface layout with Microsoft Bing blue/teal accents.

## Build and verification

- Windows build version is 1.5.0.
- The project source and XAML were statically checked in the editing environment. A full `dotnet build` could not be run here because the .NET SDK and Windows WPF/CEF runtime are not installed in this environment; verify on Windows with the build commands in `docs/BUILD.md`.
