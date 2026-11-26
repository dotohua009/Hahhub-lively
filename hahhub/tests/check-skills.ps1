param([Parameter(Mandatory=$true)][string]$Frames,[Parameter(Mandatory=$true)][string]$Report,[switch]$FullKeyboard,[switch]$Measure,[switch]$Profile,[switch]$Diversity,[switch]$Library,[switch]$Thumbnails,[switch]$Boundary,[switch]$Weather,[switch]$Budget,[switch]$Keycaps,[switch]$Glitter,[ValidateSet("fracture","black-hole","ink","frost","tendrils","silk","prism","fuse","glitter","fog","snow")][string]$Effect)
$ErrorActionPreference='Stop'
if($FullKeyboard -and $Effect -and $Effect -notin @('frost','tendrils','silk','prism','fuse','glitter')){throw "No full keyboard movie for $Effect"}
$budgetName=if($Effect -eq 'frost'){'ice'}else{$Effect}
$root=Split-Path $PSScriptRoot -Parent
$release=Join-Path $root 'release'
$stage=Join-Path ([IO.Path]::GetTempPath()) ('hahhub-skill-check-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -Path "$release\*" -Destination $stage -Recurse
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs=@('System.dll','System.Core.dll','System.Drawing.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object {'/r:'+(Join-Path $fx $_)}
$refs+=@('/r:System.Net.Http.dll','/r:System.IO.Compression.dll',('/r:'+(Join-Path $stage 'Microsoft.Web.WebView2.Core.dll')))
$src=(Get-ChildItem "$root\src\*.cs").FullName
$exe=Join-Path $stage 'check.exe'
try{
    & "$fx\csc.exe" /nologo /unsafe+ /optimize+ /platform:x64 /target:exe /main:SkillSceneChecks "/out:$exe" $refs $src "$PSScriptRoot\FullKeyboardDemos.cs" "$PSScriptRoot\SkillSceneChecks.cs" "$PSScriptRoot\CursorBoundaryChecks.cs" "$PSScriptRoot\WeatherChecks.cs" "$PSScriptRoot\KeycapChecks.cs" "$PSScriptRoot\BudgetChecks.cs"
    if($LASTEXITCODE){throw 'Test build failed'}
    if($Glitter){& $exe $Frames $Report --glitter}elseif($Budget){if($Effect){& $exe $Frames $Report --budget $budgetName}else{& $exe $Frames $Report --budget}}elseif($Keycaps){& $exe $Frames $Report --keycaps}elseif($Weather){& $exe $Frames $Report --weather}elseif($Boundary){& $exe $Frames $Report --boundary}elseif($Thumbnails){& $exe $Frames $Report --thumbnails}elseif($Library){& $exe $Frames $Report --library}elseif($Diversity){& $exe $Frames $Report --diversity}elseif($Profile){& $exe $Frames $Report --profile}elseif($FullKeyboard){if($Effect){& $exe $Frames $Report --full $Effect}else{& $exe $Frames $Report --full}}elseif($Measure){& $exe $Frames $Report --measure}else{& $exe $Frames $Report}
    if($LASTEXITCODE){throw 'Skill scene checks failed'}
} finally {
    $resolved=(Resolve-Path -LiteralPath $stage).Path
    if([IO.Path]::GetDirectoryName($resolved) -eq [IO.Path]::GetTempPath().TrimEnd('\') -and [IO.Path]::GetFileName($resolved) -match '^hahhub-skill-check-[0-9a-f]{32}$'){
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
