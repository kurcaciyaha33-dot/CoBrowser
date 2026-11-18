'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const appDir = __dirname;
const root = path.resolve(appDir, '..', '..');
const args = process.argv.slice(2);
const at = args.indexOf('--payload');
const payloadArg = at >= 0 ? args[at + 1] : path.join(root, 'artifacts', 'windows');
const payload = path.resolve(payloadArg);
const destination = path.join(appDir, 'payload');

for (const file of ['CoBrowser.exe', 'libcef.dll', 'CefSharp.BrowserSubprocess.exe']) {
  if (!fs.existsSync(path.join(payload, file))) {
    console.error(`Required Windows runtime file is missing: ${path.join(payload, file)}`);
    console.error('Build the Windows browser first with dotnet build CoBrowser.sln -c Release -p:CoBrowserPlatform=Windows.');
    process.exit(2);
  }
}

fs.rmSync(destination, { recursive: true, force: true });
fs.cpSync(payload, destination, { recursive: true });
// Keep the publisher OAuth configuration generated during dotnet publish in the installer payload.
console.log(`Prepared installer payload from: ${payload}`);

const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
let result = spawnSync(npm, ['install'], { cwd: appDir, stdio: 'inherit', shell: process.platform === 'win32' });
if (result.status !== 0) process.exit(result.status || 1);
result = spawnSync(process.execPath, [path.join(appDir, 'node_modules', 'electron-builder', 'cli.js'), '--win', 'portable', '--x64', '--publish', 'never'], { cwd: appDir, stdio: 'inherit', shell: process.platform === 'win32' });
process.exit(result.status ?? 1);
