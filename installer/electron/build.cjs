'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const appDir = __dirname;
const root = path.resolve(appDir, '..', '..');
const args = process.argv.slice(2);
function argValue(name, fallback) {
  const i = args.indexOf(name);
  return i >= 0 && args[i + 1] ? args[i + 1] : fallback;
}
const arch = argValue('--arch', 'x64').toLowerCase();
if (!['x64', 'arm64'].includes(arch)) {
  console.error(`Unsupported architecture: ${arch}. Use x64 or arm64.`);
  process.exit(2);
}
const payload = path.resolve(argValue('--payload', path.join(root, 'artifacts', `windows-${arch}`)));
const destination = path.join(appDir, 'payload');
const icon = path.join(appDir, 'assets', 'cobrowser.ico');

if (!fs.existsSync(icon) || fs.statSync(icon).size < 1024) {
  console.error(`Installer icon is missing or invalid: ${icon}`);
  process.exit(2);
}
for (const file of ['CoBrowser.exe', 'libcef.dll', 'CefSharp.BrowserSubprocess.exe']) {
  if (!fs.existsSync(path.join(payload, file))) {
    console.error(`Required ${arch} runtime file is missing: ${path.join(payload, file)}`);
    console.error(`Publish the CoBrowser ${arch} runtime before building the installer.`);
    process.exit(2);
  }
}

fs.rmSync(destination, { recursive: true, force: true });
fs.cpSync(payload, destination, { recursive: true });
console.log(`Prepared ${arch} installer payload from: ${payload}`);
console.log(`Using installer icon: ${icon}`);

const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
let result = spawnSync(npm, ['install', '--no-audit', '--no-fund'], {
  cwd: appDir, stdio: 'inherit', shell: process.platform === 'win32'
});
if (result.status !== 0) process.exit(result.status || 1);

const packagePath = path.join(appDir, 'package.json');
const packageJson = JSON.parse(fs.readFileSync(packagePath, 'utf8'));
packageJson.build.icon = 'assets/cobrowser.ico';
packageJson.build.win.icon = 'assets/cobrowser.ico';
packageJson.build.portable.artifactName = `CoBrowser-Setup-1.0.1-${arch}.exe`;
fs.writeFileSync(packagePath, `${JSON.stringify(packageJson, null, 2)}\n`);

const builder = path.join(appDir, 'node_modules', 'electron-builder', 'cli.js');
result = spawnSync(process.execPath, [builder, '--win', 'portable', `--${arch}`, '--publish', 'never'], {
  cwd: appDir, stdio: 'inherit', shell: false
});
if (result.status !== 0) process.exit(result.status || 1);

const expected = path.join(appDir, 'dist', `CoBrowser-Setup-1.0.1-${arch}.exe`);
if (!fs.existsSync(expected) || fs.statSync(expected).size === 0) {
  console.error(`electron-builder did not create expected installer: ${expected}`);
  process.exit(3);
}
console.log(`Created ${arch} setup executable: ${expected}`);
