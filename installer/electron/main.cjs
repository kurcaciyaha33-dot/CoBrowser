'use strict';

const { app, BrowserWindow, dialog, ipcMain, shell } = require('electron');
const { spawnSync } = require('node:child_process');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');

let win;
let isInstalling = false;
const isPackaged = app.isPackaged;
const payloadRoot = isPackaged
  ? path.join(process.resourcesPath, 'payload')
  : path.join(__dirname, 'payload');

function sendStage(title, detail, progress = null) {
  if (win && !win.isDestroyed()) win.webContents.send('installer:stage', { title, detail, progress });
}

function createWindow() {
  win = new BrowserWindow({
    width: 820,
    height: 610,
    minWidth: 720,
    minHeight: 560,
    frame: false,
    roundedCorners: true,
    backgroundColor: '#0d1423',
    autoHideMenuBar: true,
    show: false,
    icon: path.join(__dirname, 'assets', 'cobrowser.ico'),
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true
    }
  });

  win.loadFile(path.join(__dirname, 'renderer', 'index.html'));
  win.once('ready-to-show', () => win.show());
  win.on('closed', () => { win = null; });
}

app.whenReady().then(() => {
  createWindow();
  app.on('activate', () => { if (BrowserWindow.getAllWindows().length === 0) createWindow(); });
});

app.on('window-all-closed', () => { if (process.platform !== 'darwin') app.quit(); });

function getWindowsLocalAppData() {
  // Electron's documented app.getPath names do not include `localAppData`.
  // Windows exposes this known-folder path through LOCALAPPDATA.
  return process.env.LOCALAPPDATA || path.join(app.getPath('home'), 'AppData', 'Local');
}

ipcMain.handle('installer:default-folder', () => path.join(getWindowsLocalAppData(), 'Programs', 'CoBrowser'));

ipcMain.handle('installer:choose-folder', async () => {
  const result = await dialog.showOpenDialog(win, {
    title: 'Choose CoBrowser install folder',
    defaultPath: path.join(getWindowsLocalAppData(), 'Programs', 'CoBrowser'),
    properties: ['openDirectory', 'createDirectory']
  });
  return result.canceled ? null : result.filePaths[0];
});

async function copyTree(source, destination) {
  await fs.mkdir(destination, { recursive: true });
  const entries = await fs.readdir(source, { withFileTypes: true });
  for (const entry of entries) {
    const from = path.join(source, entry.name);
    const to = path.join(destination, entry.name);
    if (entry.isDirectory()) await copyTree(from, to);
    else if (entry.isFile()) await fs.copyFile(from, to);
  }
}

ipcMain.handle('installer:install', async (_event, targetPath, createDesktopShortcut) => {
  if (isInstalling) throw new Error('Installation is already running.');
  isInstalling = true;
  try {
    if (!targetPath || typeof targetPath !== 'string') throw new Error('Choose an installation folder.');
    const sourceExe = path.join(payloadRoot, 'CoBrowser.exe');
    const sourceCef = path.join(payloadRoot, 'libcef.dll');
    if (!(await exists(sourceExe)) || !(await exists(sourceCef))) {
      throw new Error('Windows browser payload is missing. Build Windows first with: dotnet build CoBrowser.sln -c Release -p:CoBrowserPlatform=Windows');
    }

    sendStage('Preparing installation', 'Checking the Chromium runtime and preparing your folder…', 12);
    await fs.mkdir(targetPath, { recursive: true });
    sendStage('Installing CoBrowser', 'Copying the browser, Chromium runtime and original CoBrowser assets…', 34);
    await copyTree(payloadRoot, targetPath);

    const exePath = path.join(targetPath, 'CoBrowser.exe');
    const startMenu = path.join(app.getPath('appData'), 'Microsoft', 'Windows', 'Start Menu', 'Programs', 'CoBrowser');
    await fs.mkdir(startMenu, { recursive: true });
    sendStage('Creating shortcuts', 'Adding CoBrowser to your Start menu…', 78);
    const startShortcut = path.join(startMenu, 'CoBrowser.lnk');
    const startOk = shell.writeShortcutLink(startShortcut, 'create', {
      target: exePath,
      description: 'CoBrowser — Chromium browser by CoCo X Who?',
      icon: exePath,
      iconIndex: 0,
      cwd: targetPath
    });
    if (!startOk) throw new Error('Windows could not create the Start menu shortcut.');

    if (createDesktopShortcut) {
      const desktopShortcut = path.join(app.getPath('desktop'), 'CoBrowser.lnk');
      shell.writeShortcutLink(desktopShortcut, 'create', {
        target: exePath,
        description: 'CoBrowser — Chromium browser',
        icon: exePath,
        iconIndex: 0,
        cwd: targetPath
      });
    }

    const uninstallerPath = path.join(targetPath, 'Uninstall-CoBrowser.cmd');
    const uninstaller = [
      '@echo off',
      'setlocal',
      String.raw`set "INSTALL_DIR=%~dp0"`,
      String.raw`set "START_MENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs\CoBrowser"`,
      String.raw`del /f /q "%START_MENU%\CoBrowser.lnk" >nul 2>&1`,
      String.raw`rmdir "%START_MENU%" >nul 2>&1`,
      String.raw`del /f /q "%USERPROFILE%\Desktop\CoBrowser.lnk" >nul 2>&1`,
      String.raw`reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\CoBrowser" /f >nul 2>&1`,
      String.raw`set "UNINSTALL_HELPER=%TEMP%\CoBrowser-uninstall-%RANDOM%.cmd"`,
      '(',
      'echo @echo off',
      'echo timeout /t 3 /nobreak ^>nul',
      'echo rmdir /s /q "%%~1"',
      'echo del /f /q "%%~f0"',
      ') > "%UNINSTALL_HELPER%"',
      'start "" /b "%UNINSTALL_HELPER%" "%INSTALL_DIR%"',
      ''
    ].join('\r\n');
    await fs.writeFile(uninstallerPath, uninstaller, 'utf8');

    const uninstallKey = String.raw`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\CoBrowser`;
    const registryValues = [
      ['DisplayName', 'CoBrowser'],
      ['DisplayVersion', '1.0.1'],
      ['Publisher', 'CoCo X Who?'],
      ['InstallLocation', targetPath],
      ['DisplayIcon', exePath],
      ['UninstallString', `\"${uninstallerPath}\"`]
    ];
    for (const [name, value] of registryValues) {
      const result = spawnSync('reg.exe', ['add', uninstallKey, '/v', name, '/t', 'REG_SZ', '/d', value, '/f'], { windowsHide: true });
      if (result.status !== 0) console.warn(`Could not register uninstall value ${name}; manual uninstall remains available at ${uninstallerPath}`);
    }

    sendStage('Finishing up', 'CoBrowser has been installed. Your browser profile remains in AppData and is not overwritten.', 100);
    return { ok: true, installPath: targetPath, exePath, startMenuShortcut: startShortcut };
  } finally {
    isInstalling = false;
  }
});

ipcMain.handle('installer:launch', async (_event, exePath) => {
  if (!exePath || !(await exists(exePath))) throw new Error('CoBrowser.exe was not found in the install folder.');
  const { spawn } = require('node:child_process');
  const child = spawn(exePath, [], { detached: true, stdio: 'ignore', cwd: path.dirname(exePath) });
  child.unref();
  app.quit();
  return true;
});

ipcMain.on('installer:minimize', () => win?.minimize());
ipcMain.on('installer:close', () => { if (!isInstalling) app.quit(); });
ipcMain.handle('installer:platform', () => ({ platform: process.platform, arch: process.arch, hostname: os.hostname() }));

async function exists(filePath) {
  try { await fs.access(filePath); return true; } catch { return false; }
}
