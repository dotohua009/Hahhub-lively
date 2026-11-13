$ErrorActionPreference = 'Stop'
$bridgePath = Join-Path $PSScriptRoot 'KeyBloomInput.exe'
if (!(Test-Path -LiteralPath $bridgePath) -or !(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'LivelyInfo.json'))) { throw '请在 Lively 壁纸目录中运行此脚本。' }
$startupFolder = [Environment]::GetFolderPath('Startup')
$shellObject = New-Object -ComObject WScript.Shell
$shortcut = $shellObject.CreateShortcut((Join-Path $startupFolder 'KeyBloom-Lively-Keyboard.lnk'))
$shortcut.TargetPath = $bridgePath
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.WindowStyle = 7
$shortcut.Description = 'IPI 84 壁纸即时按键转发，仅该壁纸运行时启用，不记录文字。'
$shortcut.Save()
Start-Process -FilePath $bridgePath -WindowStyle Hidden
Write-Output '已启用键盘联动，并设置为当前用户登录时启动。切换到其他壁纸后停止采集按键。'
