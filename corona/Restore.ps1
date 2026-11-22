param([string]$InstallInfoPath = (Join-Path $PSScriptRoot 'install-info.json'))
$ErrorActionPreference = 'Stop'
$install = Get-Content -LiteralPath $InstallInfoPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (@(Get-Process -Name 'ra3_1.12.game','ra3_1.13.game','RA3' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw '请先退出红警3，再运行回退脚本。'
}
$currentPlugin = Join-Path $install.GameData 'CnCFpsUnlocker.dll'
$backupPlugin = Join-Path $install.Backup 'CnCFpsUnlocker.dll'
if ((Get-FileHash -LiteralPath $backupPlugin -Algorithm SHA256).Hash -ne $install.OriginalSha256) {
    throw '备份校验失败，未修改游戏文件。'
}
$currentHash = (Get-FileHash -LiteralPath $currentPlugin -Algorithm SHA256).Hash
if ($currentHash -eq $install.OriginalSha256) { '已经是原插件，无需回退。'; return }
if ($currentHash -ne $install.BridgeSha256) {
    throw '当前插件已被其他操作更改，为避免覆盖，未执行回退。'
}
Copy-Item -LiteralPath $backupPlugin -Destination $currentPlugin -Force
$iniPath = Join-Path $install.GameData 'SAGEUnlocked.ini'
$text = [IO.File]::ReadAllText($iniPath)
$text = [regex]::Replace($text, '(?m)^autozoom=[^\r\n]*', 'autozoom=0')
[IO.File]::WriteAllText($iniPath, $text, [Text.UTF8Encoding]::new($false))
'已恢复原版 SAGE Unlocked，现有帧率和手动缩放范围设置已保留。'
