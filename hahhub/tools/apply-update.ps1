param([int]$ParentId=0,[switch]$Recover)
$ErrorActionPreference='Stop'
$job=(Get-Item -LiteralPath $PSScriptRoot).FullName
if((Split-Path $job -Leaf) -notmatch '^\.hahhub-update-([a-f0-9]{32})$'){throw 'Invalid update directory'}
$token=$Matches[1]
$app=Split-Path $job -Parent
$exe=Join-Path $app 'HAHHAN Studio.exe'
$next=Join-Path $job 'next'
$backup=Join-Path $job 'previous'
$journal=Join-Path $job 'applying.json'
$status=Join-Path $env:LOCALAPPDATA 'HAHHAN\Interaction Studio\last-update.txt'
[void][IO.Directory]::CreateDirectory((Split-Path $status -Parent))
function Safe-Path($base,$relative){
    $result=[IO.Path]::GetFullPath((Join-Path $base $relative))
    if(!$result.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid update path'}
    for($p=$result;$p -ne $base;$p=Split-Path $p -Parent){
        if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Unexpected file link'}
    }
    return $result
}
function Stop-Renderer {
    $hosts=@(Get-CimInstance Win32_Process -Filter "Name='HAHHAN Studio.exe'" | Where-Object {$_.ExecutablePath -eq $exe -and $_.CommandLine -match ' --host\b'})
    if($hosts.Count){
        $event=$null
        if([Threading.EventWaitHandle]::TryOpenExisting('Local\HAHHAN.Glass.Stop',[ref]$event)){try{[void]$event.Set()}finally{$event.Dispose()}}
        foreach($h in $hosts){$p=Get-Process -Id $h.ProcessId -ErrorAction SilentlyContinue;if($p -and !$p.WaitForExit(8000)){throw 'Renderer is busy'}}
    }
}
function Restore-Files {
    $entries=Get-Content -Raw -LiteralPath $journal | ConvertFrom-Json
    foreach($entry in $entries){
        $target=Safe-Path $app $entry.path
        $old=Safe-Path $backup $entry.path
        if([IO.File]::Exists($old)){
            if([IO.File]::Exists($target)){[IO.File]::Replace($old,$target,($old+'.discard'))}else{[IO.File]::Move($old,$target)}
        }elseif($entry.added -and [IO.File]::Exists($target)){[IO.File]::Delete($target)}
    }
    [IO.File]::Delete($journal)
    [IO.File]::WriteAllText($status,'restored')
}
$lock=[Threading.Mutex]::new($false,'Local\hahhub.Update.Apply')
$owned=$false;$clean=$false
try{
    try{$owned=$lock.WaitOne(0)}catch [Threading.AbandonedMutexException]{$owned=$true}
    if(!$owned){return}
    if($ParentId){$old=Get-Process -Id $ParentId -ErrorAction SilentlyContinue;if($old -and !$old.WaitForExit(15000)){throw 'Panel is still running'}}
    Stop-Renderer
    if($Recover){Restore-Files;Start-Process -FilePath $exe -ArgumentList '--restored' -WorkingDirectory $app;$clean=$true;return}
    $files=@(Get-ChildItem -LiteralPath $next -File -Recurse | ForEach-Object {
        $rel=$_.FullName.Substring($next.Length+1)
        [pscustomobject]@{path=$rel;added=![IO.File]::Exists((Safe-Path $app $rel))}
    } | Sort-Object @{Expression={$_.path -eq 'HAHHAN Studio.exe'}},path)
    if(!$files.Count){throw 'Empty update'}
    [IO.File]::WriteAllText(($journal+'.tmp'),(ConvertTo-Json -InputObject $files -Compress))
    [IO.File]::Move(($journal+'.tmp'),$journal)
    foreach($entry in $files){
        $target=Safe-Path $app $entry.path;$incoming=Safe-Path $next $entry.path
        [void][IO.Directory]::CreateDirectory((Split-Path $target -Parent))
        if($entry.added){[IO.File]::Move($incoming,$target)}else{
            $old=Safe-Path $backup $entry.path
            [void][IO.Directory]::CreateDirectory((Split-Path $old -Parent))
            [IO.File]::Replace($incoming,$target,$old)
        }
    }
    $ready=[Threading.EventWaitHandle]::new($false,[Threading.EventResetMode]::ManualReset,('Local\hahhub.Updated.'+$token))
    try{
        $launched=Start-Process -FilePath $exe -ArgumentList '--updated',$token -WorkingDirectory $app -PassThru
        if(!$ready.WaitOne(20000)){if(!$launched.HasExited){$launched.Kill();$launched.WaitForExit()};throw 'New version did not start'}
    }finally{$ready.Dispose()}
    [IO.File]::Delete($journal);[IO.File]::WriteAllText($status,'updated');$clean=$true
}catch{
    $why=$_.Exception.Message
    if(Test-Path -LiteralPath $journal){Stop-Renderer;Restore-Files;$clean=$true}
    if(!$Recover -and [IO.File]::Exists($exe)){Start-Process -FilePath $exe -ArgumentList '--restored' -WorkingDirectory $app}
    Write-Error $why -ErrorAction Continue
    exit 1
}finally{
    if($owned){$lock.ReleaseMutex()};$lock.Dispose()
    if($clean -and (Split-Path $job -Parent) -eq $app -and (Split-Path $job -Leaf) -eq ('.hahhub-update-'+$token)){
        Remove-Item -LiteralPath $job -Recurse -Force
    }
}
