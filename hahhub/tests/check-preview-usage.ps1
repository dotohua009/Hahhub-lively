param([Parameter(Mandatory=$true)][string]$ReportDir,[string]$SchedulerPath,[string]$EffectId)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$report=[IO.Path]::GetFullPath($ReportDir)
New-Item -ItemType Directory -Path $report -Force | Out-Null
if(Test-Path (Join-Path $report 'fixture.jsonl')){throw 'Use a new report directory for each run.'}
$stage=Join-Path ([IO.Path]::GetTempPath()) ('hahhub-usage-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    Copy-Item -Path "$root\release\*" -Destination $stage -Recurse
    [IO.File]::WriteAllText((Join-Path $report 'build-sha256.txt'),(Get-FileHash -LiteralPath (Join-Path $stage 'HAHHAN Studio.exe')).Hash)
    $prefs=New-Item -ItemType Directory -Path (Join-Path $stage 'config')
    $data=Join-Path $env:LOCALAPPDATA 'HAHHAN\Interaction Studio'
    Get-ChildItem -Path "$data\*" -Include 'alignment-*.json','blackhole-*.txt','glass-rim.txt','glass.json','language.txt','output.json','skills.json','wallpaper-integration.txt' -File | Copy-Item -Destination $prefs.FullName
    [IO.File]::WriteAllText((Join-Path $prefs.FullName 'fps.txt'),'60')
    if($EffectId){[IO.File]::WriteAllText((Join-Path $prefs.FullName 'preview-test-effect.txt'),$EffectId)}
    $fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $refs=@('System.dll','System.Core.dll','System.Drawing.dll','System.Management.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object {'/r:'+(Join-Path $fx $_)}
    $exe=Join-Path $stage 'PreviewUsage.exe'
    & "$fx\csc.exe" /nologo /optimize+ /platform:x64 /target:winexe "/out:$exe" $refs "$PSScriptRoot\PreviewUsage.cs"
    if($LASTEXITCODE){throw 'Preview measurement build failed'}
    if(!$SchedulerPath){
        $traceProject=New-Item -ItemType Directory -Path (Join-Path $stage 'trace-project')
        Copy-Item -LiteralPath "$PSScriptRoot\PreviewCpuTrace.cs","$PSScriptRoot\PreviewCpuTrace.csproj" -Destination $traceProject.FullName
        $build=Join-Path $stage 'trace-bin'
        & dotnet publish (Join-Path $traceProject.FullName 'PreviewCpuTrace.csproj') -c Release -o $build --nologo
        if($LASTEXITCODE){throw 'CPU scheduling observer build failed'}
        $SchedulerPath=Join-Path $build 'PreviewCpuTrace.exe'
    }
    $run=Start-Process -FilePath $exe -ArgumentList ('"'+$prefs.FullName+'" "'+$report+'"') -WorkingDirectory $stage -PassThru
    $trace=$null
    if($SchedulerPath){
        $trace=Start-Process -FilePath (Resolve-Path -LiteralPath $SchedulerPath).Path -ArgumentList ($run.Id.ToString()+' "'+$report+'"') -WindowStyle Hidden -PassThru
    }
    $run.WaitForExit()
    if($trace){
        $trace.WaitForExit()
        if($trace.ExitCode -or !(Test-Path (Join-Path $report 'cpu-end.txt'))){throw 'CPU scheduling trace did not finish'}
    }
    if($run.ExitCode -or !(Test-Path (Join-Path $report 'complete'))){Get-ChildItem "$report\error-*.txt" | Get-Content;throw 'Preview measurement did not finish'}
} finally {
    $resolved=(Resolve-Path -LiteralPath $stage).Path
    if([IO.Path]::GetDirectoryName($resolved) -eq [IO.Path]::GetTempPath().TrimEnd('\') -and [IO.Path]::GetFileName($resolved) -match '^hahhub-usage-[0-9a-f]{32}$'){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
