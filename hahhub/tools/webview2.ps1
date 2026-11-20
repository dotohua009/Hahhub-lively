param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
$version='1.0.4258.31'
$name="microsoft.web.webview2.$version.nupkg"
$cache=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget\packages'}
$package=Join-Path $cache "microsoft.web.webview2\$version\$name"
if(!(Test-Path -LiteralPath $package)){
    $package=Join-Path ([IO.Path]::GetTempPath()) $name
    if(!(Test-Path -LiteralPath $package)){
        Invoke-WebRequest -UseBasicParsing -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$version/$name" -OutFile $package
    }
}
if((Get-FileHash -LiteralPath $package).Hash -ne '56F7F4B8BF9AEE4B8EFEFBBDD4F67D5F74EBD1B100ED0806DA71BF76AF481AA9'){throw 'WebView2 SDK 文件校验失败'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$files=@{
    'lib/net462/Microsoft.Web.WebView2.Core.dll'='Microsoft.Web.WebView2.Core.dll'
    'runtimes/win-x64/native/WebView2Loader.dll'='WebView2Loader.dll'
    'LICENSE.txt'='WebView2-LICENSE.txt'
    'NOTICE.txt'='WebView2-NOTICE.txt'
}
$zip=[IO.Compression.ZipFile]::OpenRead($package)
try{foreach($path in $files.Keys){
    [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry($path),(Join-Path $Destination $files[$path]),$true)
}}finally{$zip.Dispose()}
