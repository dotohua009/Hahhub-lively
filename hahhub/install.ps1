$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$app=Join-Path $env:LOCALAPPDATA 'HAHHAN\Interaction Studio\app'
$source=Join-Path $base 'HAHHAN Studio.exe'
if(!(Test-Path -LiteralPath $source)){throw '请从软件发行包运行安装脚本。'}
$exe=Join-Path $app 'HAHHAN Studio.exe'
$upgrade=Test-Path -LiteralPath $exe
$refresh=!(Test-Path -LiteralPath $exe) -or ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $exe).Hash)
$hosts=@(Get-CimInstance Win32_Process -Filter "Name='HAHHAN Studio.exe'" | Where-Object {$_.ExecutablePath -eq $exe})
if($refresh -and ($hosts|Where-Object {$_.CommandLine -notmatch ' --host\b'})){throw '请右键系统托盘的 H 图标，选择「Quit hahhub」后更新；旧版请关闭工坊窗口。'}
if($refresh -and ($hosts|Where-Object {$_.CommandLine -match ' --host\b'})){
    Start-Process -FilePath $exe -ArgumentList '--stop-host' -WindowStyle Hidden -Wait
    foreach($old in ($hosts|Where-Object {$_.CommandLine -match ' --host\b'})){
        $p=Get-Process -Id $old.ProcessId -ErrorAction SilentlyContinue
        if($p -and !$p.WaitForExit(6000)){throw '碎屏后台未退出，请稍后重试。'}
    }
}
New-Item -ItemType Directory -Path $app -Force | Out-Null
if($refresh){Copy-Item -LiteralPath $source -Destination $exe -Force}
foreach($n in 'Studio.xaml','cover.jpg','donation.png','studio.ico','README.md','NOTICE.md','LICENSE','apply-update.ps1'){Copy-Item -LiteralPath (Join-Path $base $n) -Destination $app -Force}
foreach($n in 'Microsoft.Web.WebView2.Core.dll','WebView2Loader.dll','WebView2-LICENSE.txt','WebView2-NOTICE.txt'){Copy-Item -LiteralPath (Join-Path $base $n) -Destination $app -Force}
Copy-Item -LiteralPath "$base\effects" -Destination $app -Recurse -Force
# Move retired bundled effects out of the installed library, preserving their files.
$effectsRoot=[IO.Path]::GetFullPath((Join-Path $app 'effects'))
foreach($key in 'bubble','ink','fold','recall'){
    $pack=Join-Path $effectsRoot "hahhan-$key"
    if(!(Test-Path -LiteralPath $pack -PathType Container)){continue}
    $item=Get-Item -LiteralPath $pack
    if($item.Parent.FullName -ne $effectsRoot -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){continue}
    $info=Join-Path $pack 'LivelyInfo.json'
    if(!(Test-Path -LiteralPath $info) -or (Get-Content -Raw -Encoding UTF8 -LiteralPath $info | ConvertFrom-Json).Author -ne 'HAHHAN'){continue}
    $archive=Join-Path (Split-Path -Parent $app) 'retired-effects'
    New-Item -ItemType Directory -Force -Path $archive | Out-Null
    Move-Item -LiteralPath $item.FullName -Destination (Join-Path $archive ($item.Name+'.'+[guid]::NewGuid().ToString('N')))
}
# Import the old Studio choices once. All preferences for the standalone overlay live in its own directory.
if($upgrade){
    $data=Split-Path -Parent $app
    $legacy=Join-Path $env:LOCALAPPDATA 'Lively Wallpaper'
    $layoutFile=Join-Path $legacy 'WallpaperLayout.json'
    $legacySettings=Join-Path $legacy 'Settings.json'
    if(Test-Path -LiteralPath $legacySettings){
        try{
            $library=(Get-Content -Raw -Encoding UTF8 -LiteralPath $legacySettings | ConvertFrom-Json).WallpaperDir
            $oldPack=Join-Path $library 'wallpapers\hahhan-glass'
            $ownInfo=Join-Path $oldPack 'LivelyInfo.json'
            if((Test-Path $ownInfo) -and ((Get-Content -Raw -Encoding UTF8 $ownInfo|ConvertFrom-Json).Author -eq 'HAHHAN')){
                $choiceFile=Join-Path $data 'output.json'
                if(!(Test-Path $choiceFile) -and (Test-Path $layoutFile)){
                    $old=@(Get-Content -Raw -Encoding UTF8 $layoutFile | ConvertFrom-Json | ForEach-Object {$_} | Where-Object {
                        $_.LivelyScreen.DeviceName -eq '\\.\DISPLAY3' -and $_.LivelyScreen.DeviceId.StartsWith('\\?\DISPLAY#HIV1230#',[StringComparison]::OrdinalIgnoreCase) -and
                        (Split-Path $_.LivelyInfoPath -Leaf) -in @('ipi84-key-bloom','hahhan-glass')
                    } | ForEach-Object {$_.LivelyScreen.DeviceId} | Sort-Object -Unique)
                    if($old.Count -eq 1){[IO.File]::WriteAllText($choiceFile,([ordered]@{Id=$old[0];Label='屏幕 3 · IPI 84';Enabled=$false}|ConvertTo-Json))}
                }
                $tuning=Join-Path $data 'glass.json'
                $oldTuning=Join-Path $library 'SaveData\wpdata\hahhan-glass\3\LivelyProperties.json'
                if(!(Test-Path $tuning) -and (Test-Path $oldTuning)){
                    $v=Get-Content -Raw -Encoding UTF8 $oldTuning|ConvertFrom-Json
                    if($v.density.value -in (30..100) -and $v.spread.value -in (45..150) -and $v.heal.value -ge 500 -and $v.heal.value -le 4000){
                        [IO.File]::WriteAllText($tuning,(@{density=[int]$v.density.value;spread=[int]$v.spread.value;heal=[int]$v.heal.value}|ConvertTo-Json))
                    }
                }
                $template=Get-Content -Raw -Encoding UTF8 -LiteralPath "$base\effects\hahhan-glass\LivelyProperties.json" | ConvertFrom-Json
                $props=@(Join-Path $oldPack 'LivelyProperties.json')
                $saved=Join-Path $library 'SaveData\wpdata\hahhan-glass'
                if(Test-Path -LiteralPath $saved){$props+=@(Get-ChildItem -LiteralPath $saved -Filter LivelyProperties.json -File -Recurse | ForEach-Object {$_.FullName})}
                foreach($file in $props){
                    if(!(Test-Path -LiteralPath $file)){continue}
                    $settings=Get-Content -Raw -Encoding UTF8 -LiteralPath $file | ConvertFrom-Json
                    $changed=$false
                    foreach($key in 'crackSize','delay','fps'){
                        if(!$settings.PSObject.Properties[$key]){$settings | Add-Member -NotePropertyName $key -NotePropertyValue $template.$key;$changed=$true}
                    }
                    if($changed){$tmp=$file+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
                        try{[IO.File]::WriteAllText($tmp,($settings|ConvertTo-Json -Depth 12));[IO.File]::Replace($tmp,$file,[System.Management.Automation.Language.NullString]::Value)}finally{if(Test-Path -LiteralPath $tmp){Remove-Item -LiteralPath $tmp}}
                    }
                }
                foreach($asset in 'glass-data.js','glass.js','runtime.js','NOTICE.md','cracked-glass-LICENSE.txt'){
                    Copy-Item -LiteralPath (Join-Path "$base\effects\hahhan-glass" $asset) -Destination (Join-Path $oldPack $asset) -Force
                }
            }
        }catch{Write-Warning "先前的 Lively 参数未能全部导入。可在交互工坊选择显示器并保存参数。"}
    }
}
$ws=New-Object -ComObject WScript.Shell
function New-StudioShortcut($folder,$previousName,$arguments){
    $link=Join-Path $folder 'hahhub.lnk'
    $shortcut=$ws.CreateShortcut($link)
    if((Test-Path -LiteralPath $link) -and $shortcut.TargetPath -ne $exe){throw "此快捷方式已指向其他程序：$link"}
    $shortcut.TargetPath=$exe
    $shortcut.IconLocation=(Join-Path $app 'studio.ico')+',0'
    $shortcut.Arguments=$arguments
    $shortcut.WorkingDirectory=$app
    $shortcut.Save()
    $previous=Join-Path $folder $previousName
    if((Test-Path -LiteralPath $previous) -and $ws.CreateShortcut($previous).TargetPath -eq $exe){
        Remove-Item -LiteralPath $previous
    }
}
New-StudioShortcut ([Environment]::GetFolderPath('DesktopDirectory')) 'HAHHAN 交互工坊.lnk' ''
New-StudioShortcut ([Environment]::GetFolderPath('Startup')) 'HAHHAN 碎屏交互层.lnk' '--tray'
Start-Process -FilePath $exe -ArgumentList '--tray' -WorkingDirectory $app -WindowStyle Hidden
Write-Output "已安装 $app"


