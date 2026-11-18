# CoBrowser 1.0.5

- Chromium popup requests (`window.open`, `target=_blank`, and links opened in a new window) are routed into a new CoBrowser tab instead of a separate window.
- Browser downloads are saved directly in the signed-in Windows user's `Downloads` folder.
- Download callbacks now log the selected destination and no longer silently fall back to an ambiguous relative filename.
- Fixed the setup's missing internal brand images by including the installer assets directory in the packaged Electron application.
- Updated product, installer, and release asset version metadata to 1.0.5.
