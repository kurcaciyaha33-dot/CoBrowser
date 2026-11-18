# CoBrowser 1.0.5 — Windows x64 / ARM64 source update

This source update includes popup-to-tab routing, direct downloads to the current user's Downloads folder, and packaging of the installer artwork.

## Validation performed in this environment
- JavaScript syntax checks passed for the Electron build, main process, and renderer scripts.
- Workflow asset names and version metadata were updated to 1.0.5.
- Installer package configuration now includes `assets/**/*`, which contains the setup window's logo images and ICO.
- A .NET build and Electron packaging run were not performed here because the .NET SDK is not installed in this environment.
- Windows runtime behavior, including CefSharp popup/download behavior and native ARM64 dependency support, must be confirmed by the Windows GitHub Actions build and runtime test.
