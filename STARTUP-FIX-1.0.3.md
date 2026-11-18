# Startup error troubleshooting — CoBrowser 1.0.3

This update improves the startup path and diagnostic output. It cannot prove the exact cause of an earlier `NullReferenceException` without that machine's stack trace.

1. Extract this whole package into a new folder.
2. Do not delete `%APPDATA%\CoBrowser\User Data`.
3. Build from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-windows.ps1
```

4. Start `Desktop\CoBrowser-Build\CoBrowser.exe`.
5. If it still fails, share:
   - The `STARTUP FAILURE` section from `%APPDATA%\CoBrowser\cobrowser.log`.
   - The last 100 lines of `%APPDATA%\CoBrowser\cef.log`.

Commands to collect logs:

```powershell
Get-Content "$env:APPDATA\CoBrowser\cobrowser.log" -Tail 150
Get-Content "$env:APPDATA\CoBrowser\cef.log" -Tail 150
```
