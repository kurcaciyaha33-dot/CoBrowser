# CoBrowser 1.0.3

## Startup / CEF hardening

- Added explicit startup stage logging and full exception stack trace reporting.
- Added runtime file diagnostics for CefSharp/CEF files and locales directory.
- Switched to the documented Cef.Initialize overload with dependency checking enabled.
- Removed the hard-coded BrowserSubprocessPath override and use CefSharp's packaged default subprocess resolution.
- Kept the existing `%APPDATA%\\CoBrowser\\User Data` cache location to preserve the previous profile/cookies.
- Made the optional GPU fallback command-line argument handling null-safe.
- Added constructor-stage logging around WPF initialization, SQLite initialization, bookmark loading and first-tab creation.
- Made bookmark deserialization tolerate null entries from malformed/older JSON.

## Branding

- Bundled ICO, PNG mark and SVG logo in the Windows WPF project.
- The ICO is assigned to the executable and WPF window; the PNG mark is embedded for the in-app logo.
- No separate branding downloads are needed.

## Build

- Added a root `build-windows.ps1` so users can build from the repository root using `dotnet restore`, `dotnet build` and `dotnet publish`.
- Kept `windows/build.ps1` as a forwarder to the root build script.

## Verification status

Source/XML/asset structure was checked. The current environment has no .NET SDK and cannot execute a Windows WPF/CEF build; successful compilation and startup must be confirmed on Windows.
