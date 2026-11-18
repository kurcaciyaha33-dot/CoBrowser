# CoBrowser 1.0.5 Windows build

The browser writes downloaded files to `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")`, which resolves to `C:\Users\<current-user>\Downloads` for the Windows account running CoBrowser.

Popup URLs are intercepted by CefSharp's RequestHandler `OnOpenUrlFromTab` and opened with the existing tab system.

Build and release are handled by `.github/workflows/build.yml`. The workflow validates the native CefSharp runtime for each RID; ARM64 will fail explicitly if the selected CefSharp package does not contain compatible ARM64 native assets.
