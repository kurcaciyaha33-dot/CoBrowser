# CoBrowser 1.5.0 update

Windows updates include a real CefSharp download pipeline, a recent-download toolbar flyout, a native Downloads tab, progress/size/speed reporting, open-file and show-in-folder actions, and a clear-completed action. Downloads are saved under `%APPDATA%\\CoBrowser\\Downloads`.

The sign-in dialog now exposes only a Google email field. Gmail and Google Workspace addresses are accepted; authentication happens on Google's own page in the system browser. The publisher must configure the OAuth Desktop application Client ID in `google-oauth.json` or as `GoogleOAuthClientId` during the build. This is app registration configuration and is not a Google account ID; Google requires it for secure OAuth sign-in.

See `CHANGELOG-1.5.0.md`, `docs/GOOGLE-SIGN-IN.md`, and `docs/BUILD.md`.
