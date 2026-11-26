param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dest=(New-Item -ItemType Directory -Path $Destination -Force).FullName
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs=@('System.dll','System.Core.dll','System.Drawing.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object {'/r:'+(Join-Path $fx $_)}
$refs+=@('/r:System.Net.Http.dll','/r:System.IO.Compression.dll',('/r:'+(Join-Path $root 'release/Microsoft.Web.WebView2.Core.dll')))
$src=(Get-ChildItem "$root\src\*.cs").FullName
& "$fx/csc.exe" /nologo /unsafe+ /optimize+ /platform:x64 /target:winexe /main:KeyboardAudition "/out:$dest\hahhub Preview.exe" "/win32icon:$root\src\studio.ico" "/win32manifest:$root\src\overlay.manifest" $refs $src "$root\tests\FullKeyboardDemos.cs" "$root\tests\KeyboardAudition.cs"
if($LASTEXITCODE){throw 'Preview build failed'}
foreach($asset in 'effects/hahhan-glass/glass-data.js','effects/hahhan-glass/cracked-glass-LICENSE.txt','effects/hahhub-materials/skills.png','effects/hahhub-materials/fuse-blocks.png','LICENSE','NOTICE.md'){
    $path=Join-Path $dest $asset
    [IO.Directory]::CreateDirectory((Split-Path $path -Parent)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $asset) -Destination $path -Force
}
