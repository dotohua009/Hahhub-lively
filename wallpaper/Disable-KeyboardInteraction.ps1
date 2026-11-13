$ErrorActionPreference = 'Stop'
$bridgePath = Join-Path $PSScriptRoot 'KeyBloomInput.exe'
if (Test-Path -LiteralPath $bridgePath) { Start-Process -FilePath $bridgePath -ArgumentList '--stop' -WindowStyle Hidden -Wait }
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'KeyBloom-Lively-Keyboard.lnk'
if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath }
Write-Output '后台键盘联动已停止；Lively 壁纸和校准参数保留。'
