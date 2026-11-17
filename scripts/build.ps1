$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$source=Join-Path $projectRoot 'wallpaper'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path -LiteralPath $compiler)){throw 'The x64 .NET Framework C# compiler is required.'}
$output=Join-Path $source 'KeyBloomInput.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Management.dll ("/out:$output") (Join-Path $source 'KeyBloomInput.cs')
if($LASTEXITCODE -ne 0){throw 'Input bridge compilation failed.'}
$sleepOutput=Join-Path $source 'KeyBloomSleep.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll ("/out:$sleepOutput") (Join-Path $source 'KeyBloomSleep.cs')
if($LASTEXITCODE -ne 0){throw 'Sleep helper compilation failed.'}
$dist=Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$archive=Join-Path $dist 'KeyBloom-IPI84-v1.3.2.zip'
$files=@('index.html','layout.js','calibration.js','calibration.json','effects.js','games.js','defaults.js','surfaces.js','audio.js','LivelyInfo.json','LivelyProperties.json','thumbnail.jpg','KeyBloomInput.cs','KeyBloomInput.exe','KeyBloomSleep.cs','KeyBloomSleep.exe','关闭键盘屏.cmd','开启键盘屏.cmd','Enable-KeyboardInteraction.ps1','Disable-KeyboardInteraction.ps1','使用说明.md','LICENSE','THIRD_PARTY_NOTICES.md')
Compress-Archive -LiteralPath ($files|ForEach-Object {Join-Path $source $_}) -DestinationPath $archive -Force
$hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($archive+'.sha256') -Value ($hash+'  '+[IO.Path]::GetFileName($archive)) -Encoding ASCII
Write-Output $archive
Write-Output $hash
