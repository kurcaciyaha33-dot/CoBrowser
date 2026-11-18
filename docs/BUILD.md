# CoBrowser 1.0.1 build (Windows-only)

## Windows browser EXE

Run in PowerShell from the repository root. Requires Windows 10/11 x64 and .NET 8 SDK.

```powershell
dotnet clean .\CoBrowser.sln -c Release -p:CoBrowserPlatform=Windows
dotnet restore .\CoBrowser.sln
dotnet build .\CoBrowser.sln -c Release -p:CoBrowserPlatform=Windows
```

Output: `artifacts/windows/CoBrowser.exe` and required runtime files.

## Windows Electron installer EXE

Requires Node.js/npm. Build browser and installer together:

```powershell
dotnet build .\CoBrowser.sln -c Release -p:CoBrowserPlatform=Windows -p:BuildInstaller=true
```

Or build the installer separately after the browser EXE exists:

```powershell
dotnet build .\CoBrowser.sln -c Release -p:CoBrowserPlatform=Windows -p:BuildInstallerOnly=true
```

Output: `artifacts/installer/CoBrowser-Setup-1.0.1.exe`. The portable browser remains separately available in `artifacts/windows/`.

## Google OAuth (Windows)

Pass the public Google Desktop OAuth Client ID only:

```powershell
dotnet build .\CoBrowser.sln -c Release -p:CoBrowserPlatform=Windows -p:GoogleOAuthClientId=YOUR_CLIENT_ID.apps.googleusercontent.com
```

Alternatively set `GOOGLE_OAUTH_CLIENT_ID` or provide `windows/CoBrowser/google-oauth.json` based on the sample. Never embed a client secret in the public EXE or installer.

## GitHub Actions

`.github/workflows/build.yml` builds the Windows x64 portable package and Electron installer, and can publish those two artifacts to a GitHub Release. The workflow has only Windows build and release artifacts.
