# CoBrowser 1.0.2 — Reliability patch

- Clean exit markers are now cleared after a graceful startup failure as well as after a normal CEF shutdown.
- An unclean termination marker is kept only when the process does not reach a clean exit or CEF shutdown fails, so the next launch recovery prompt is more meaningful.
- Version metadata updated to 1.0.2.

# CoBrowser — Stability & native file viewing update

## Crash / lifecycle fixes

- Disabled CefSharp's automatic `Cef.Shutdown()` hook before the first WPF Chromium control is created. CoBrowser now owns shutdown and calls `Cef.Shutdown()` once after browser controls are disposed.
- Set CEF `CachePath` to the common `RootCachePath` root so Chromium uses its normal `Default` profile directory beneath `User Data`.
- Kept inactive tab browser controls in the WPF visual tree and toggled visibility rather than repeatedly removing/re-adding controls.
- Guarded CEF callbacks against tab/window disposal and WPF dispatcher shutdown.
- Added renderer termination logging and a recovery hint in the status area.
- Added process-level, task-level and WPF dispatcher exception logging.
- Added a single-process mutex to prevent profile corruption from two separate CoBrowser instances.
- Added an optional GPU fallback controlled by `%APPDATA%\CoBrowser\disable-gpu.flag`.
- Added safer SQLite writes and history logging errors.

## File support and developer tools

- Added `Ctrl+O` / **Open file…** and drag-and-drop local file opening.
- Added a read-only code/text viewer with line numbers, language labels, basic token colorization and an in-file filter.
- Added common source extensions for C/C++, C#, Java, Kotlin, JS/TS, Python, Go, Rust, Ruby, PHP, Swift, Dart, Lua, PowerShell, SQL, JSON, YAML, TOML, XML, CSS, Markdown, and more.
- HTML/XHTML and image/media formats are routed directly to Chromium.
- SVG and PNG open as browser content; XML opens in the source viewer.
- DLL/EXE/PDB and other selected binaries open in a safe hex/metadata inspector. Binary files are never executed by CoBrowser.
- Added `Ctrl+U` / **View page source**.

## Branding and UI

- Added original CoBrowser logo mark, SVG primary and monochrome logos, SVG favicon, PNG toolbar/start-page mark, and multi-resolution Windows ICO.
- Updated the dark palette and start-page brand header.

## Honest limitations

- The source viewer reads text; it does not execute or semantically compile every programming language.
- Binary inspection is a limited header/metadata/hex preview, not full DLL decompilation/disassembly.
- CEF renderer recovery is logged; a renderer crash does not necessarily mean the whole CoBrowser process has crashed.
- This environment did not contain the Windows .NET SDK, so the WPF project could not be compiled here. A Windows build must confirm the final package APIs and runtime behavior.


## Stability / file-viewer hardening

- Updated the renderer-crash callback to the CefSharp current API signature (status, native error code, and message).
- Dispose CefSharp controls before detaching them from the WPF visual tree.
- Avoid disposing the SQLite write semaphore while async history writes may still be releasing it.
- Removed SQLite shared-cache mode; WAL + busy timeout handle concurrent local connections.
- Route local file paths entered in the address bar through the same file viewer as Ctrl+O.
- Bound code-viewer HTML to 50,000 lines / 2,000,000 characters and avoid regex highlighting on very long minified lines.
- Add PE machine / section / optional-header metadata to the safe DLL/EXE inspector.
- Add a menu toggle for GPU compatibility mode (restart required).
- Separate CefSharp's native `cef.log` from CoBrowser's application `cobrowser.log`, and rotate the app log at 4 MiB.
- Keep generic text files readable as plain text even when their programming language extension is unknown.


## File support clarified

- Any detected text file can be opened in the read-only text/code viewer, even if its extension is not in the language list. Known extensions receive a language label and basic keyword/string/number coloring; unknown extensions use Plain text.
- SVG is rendered as an image/vector by Chromium; Ctrl+U on a local SVG opens its XML source. PNG and common image formats are rendered by Chromium.
- DLL/EXE inspection shows safe PE metadata where available, managed assembly identity where readable, file version resources, and a 1-KiB hex/ASCII preview. It does not execute, load, decompile, or fully disassemble binary files.

## 1.5.1 build fix
- Corrected CefSharp `DownloadHandler.OnBeforeDownload` override to return `bool` and report whether the download callback was handled.
- No OAuth configuration file or credentials are bundled; an existing local `windows/CoBrowser/google-oauth.json` remains local to the developer's checkout.
