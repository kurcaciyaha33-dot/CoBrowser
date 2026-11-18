# CoBrowser GitHub Release Automation

- Push a version tag such as `v1.0.6` to trigger the Windows release workflow.
- The workflow derives the release version from the tag and applies it to the installer and asset filenames.
- It builds x64 and ARM64 Setup EXE and portable ZIP artifacts, validates that all four files exist, then attaches them to the GitHub Release.
- GitHub automatically generates release notes from the repository changes and merged pull requests (`generate_release_notes: true`).
- `workflow_dispatch` can build artifacts manually; enable `create_release` to publish a release using the version in `installer/electron/package.json`.

Expected assets for version `1.0.6`:

- `CoBrowser-Setup-1.0.6-x64.exe`
- `CoBrowser-1.0.6-Windows-x64-portable.zip`
- `CoBrowser-Setup-1.0.6-arm64.exe`
- `CoBrowser-1.0.6-Windows-arm64-portable.zip`
