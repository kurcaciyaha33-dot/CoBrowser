'use strict';
const $ = (id) => document.getElementById(id);
let installedExe = null;

function showScreen(id) {
  for (const screen of document.querySelectorAll('.screen')) screen.classList.toggle('active', screen.id === id);
}

async function bootstrap() {
  const platform = await window.coInstaller.platform();
  if (platform.platform !== 'win32') {
    $('install').disabled = true;
    $('stageDetail').textContent = 'The Windows installer must run on Windows.';
  }
  const base = await window.coInstaller.defaultFolder();
  $('installPath').value = base || 'C:\\Users\\Public\\Programs\\CoBrowser';
}

$('browse').addEventListener('click', async () => {
  const folder = await window.coInstaller.chooseFolder();
  if (folder) $('installPath').value = folder;
});
$('minimize').addEventListener('click', () => window.coInstaller.minimize());
$('close').addEventListener('click', () => window.coInstaller.close());
$('finish').addEventListener('click', () => window.coInstaller.close());

window.coInstaller.onStage(({ title, detail, progress }) => {
  $('stageTitle').textContent = title || 'Installing CoBrowser';
  $('stageDetail').textContent = detail || '';
  if (typeof progress === 'number') {
    $('progressFill').style.width = `${progress}%`;
    $('progressPercent').textContent = `${progress}%`;
  }
});

$('install').addEventListener('click', async () => {
  const folder = $('installPath').value.trim();
  if (!folder) return;
  $('install').disabled = true;
  showScreen('progress');
  try {
    const result = await window.coInstaller.install(folder, $('desktopShortcut').checked);
    installedExe = result.exePath;
    $('installedPath').textContent = result.installPath;
    showScreen('complete');
  } catch (error) {
    showScreen('welcome');
    $('install').disabled = false;
    alert(error.message || String(error));
  }
});

$('launch').addEventListener('click', async () => {
  if (!installedExe) return;
  $('launch').disabled = true;
  try { await window.coInstaller.launch(installedExe); }
  catch (error) { $('launch').disabled = false; alert(error.message || String(error)); }
});

bootstrap();
