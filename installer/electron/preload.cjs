'use strict';
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('coInstaller', {
  defaultFolder: () => ipcRenderer.invoke('installer:default-folder'),
  chooseFolder: () => ipcRenderer.invoke('installer:choose-folder'),
  install: (folder, desktopShortcut) => ipcRenderer.invoke('installer:install', folder, desktopShortcut),
  launch: (exePath) => ipcRenderer.invoke('installer:launch', exePath),
  platform: () => ipcRenderer.invoke('installer:platform'),
  minimize: () => ipcRenderer.send('installer:minimize'),
  close: () => ipcRenderer.send('installer:close'),
  onStage: (callback) => {
    const handler = (_event, data) => callback(data);
    ipcRenderer.on('installer:stage', handler);
    return () => ipcRenderer.removeListener('installer:stage', handler);
  }
});
