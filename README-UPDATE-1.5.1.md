# CoBrowser 1.5.1

## Fix
- Fixed the CefSharp download handler override: `OnBeforeDownload` returns `bool` as required by the installed CefSharp API.
- Returns `true` when the download callback is handled and `false` when the callback is already disposed.
- No `google-oauth.json` is included in this source archive. Keep your existing local OAuth configuration in `windows/CoBrowser/google-oauth.json`; it is not replaced or overwritten by this update.

## Windows build
Run from the repository root in PowerShell:

```powershell
dotnet build .\CoBrowser.sln -c Release -p:CoBrowserPlatform=Windows
```

The build target detects the existing local `windows/CoBrowser/google-oauth.json` and copies it into publish output. Do not commit or share that local file if you do not intend to distribute the public OAuth client configuration.
