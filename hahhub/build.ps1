$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$dest=Join-Path $root 'release'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs=@('System.dll','System.Core.dll','System.Drawing.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/r:'+(Join-Path $fx $_) }
$src=(Get-ChildItem -LiteralPath "$root\src" -Filter '*.cs').FullName
& "$root\tools\webview2.ps1" -Destination $dest
$refs+='/r:'+(Join-Path $dest 'Microsoft.Web.WebView2.Core.dll')
$refs+=@('/r:System.Net.Http.dll','/r:System.IO.Compression.dll')
& "$fx\csc.exe" /nologo /unsafe+ /optimize+ /platform:x64 /target:winexe "/win32icon:$root\src\studio.ico" "/win32manifest:$root\src\overlay.manifest" "/out:$dest\HAHHAN Studio.exe" $refs $src
if($LASTEXITCODE){throw '构建失败'}
Copy-Item -LiteralPath "$root\tools\apply-update.ps1" -Destination $dest -Force
Copy-Item -LiteralPath "$root\src\Studio.xaml","$root\src\cover.jpg","$root\src\donation.png","$root\src\studio.ico","$root\LICENSE","$root\README.md","$root\NOTICE.md","$root\install.ps1" -Destination $dest -Force
Copy-Item -LiteralPath "$root\effects" -Destination $dest -Recurse -Force
foreach($effect in 'hahhan-glass','hahhan-blackhole','hahhub-ink'){Copy-Item -LiteralPath "$root\LICENSE","$root\NOTICE.md" -Destination "$dest\effects\$effect" -Force}

