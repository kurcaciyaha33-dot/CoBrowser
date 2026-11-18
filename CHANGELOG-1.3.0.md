# CoBrowser 1.3.0

- Updated CoBrowser product version across Windows app, Electron setup and .NET build output.
- Separated CEF root cache (`User Data`) from the persistent default profile (`User Data/Default`) to match Chromium profile layout while retaining the existing Default profile data.
- Added an explicit WPF Path alias in the code-behind so the vector icon control cannot collide with `System.IO.Path`.
- Replaced all installer UI marks with the same bundled CoBrowser artwork and fixed the Windows folder picker to use `LOCALAPPDATA` rather than an unsupported Electron `app.getPath()` key.
- Kept the custom vector toolbar icon set and included the PNG, SVG and ICO identity inside the source project and app package.
- Windows first-run sign-in remains a mandatory Google Desktop OAuth + PKCE connection to the CoBrowser application profile.

## Build verification
