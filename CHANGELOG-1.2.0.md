# CoBrowser 1.2.0

- Updated CoBrowser original icon/branding package and Aurora WPF color system.
- Added mandatory Windows first-run Google account link using OAuth desktop app flow, PKCE, system browser and DPAPI-protected session storage.
- Fixed the WPF runtime icon lookup to normalize PascalCase vector resource keys. The prior lowercase lookup (`Icon.close`) did not match `Icon.Close` and could fail during tab creation.
- Added Electron installer UI with location selection, progress screen and Start/Desktop shortcut creation.


## Build and release clarification

- Windows builds may supply a public Google Desktop OAuth Client ID with `-p:GoogleOAuthClientId=...`; no OAuth client secret is used.
