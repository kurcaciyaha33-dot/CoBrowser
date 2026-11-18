# GitHub Actions Secrets

Required repository secrets for the current Windows x64 + ARM64 release workflow:

- `GOOGLE_OAUTH_CLIENT_ID` — Google OAuth Desktop app client ID.
- `GOOGLE_OAUTH_CLIENT_SECRET` — Desktop app client secret used by the current CoBrowser OAuth implementation.

The workflow creates both architectures and packages four release assets:

- `CoBrowser-Setup-1.0.1-x64.exe`
- `CoBrowser-Windows-x64-portable.zip`
- `CoBrowser-Setup-1.0.1-arm64.exe`
- `CoBrowser-Windows-arm64-portable.zip`

Security note: GitHub Actions encrypts repository secrets at rest, but this workflow places the OAuth credential JSON into the published application payload so the public app can use it. Anyone who downloads and inspects the EXE/ZIP can recover those values. Obfuscation or encrypting the JSON with a key embedded in the app does not make a client secret confidential. For a genuinely confidential credential, use a backend that keeps the secret server-side.

The ARM64 publish also requires every native dependency (especially CefSharp/CEF) to ship Windows ARM64 assets. If the dependency does not support `win-arm64`, the ARM64 job will fail at `dotnet publish`; do not relabel an x64 binary as ARM64.
