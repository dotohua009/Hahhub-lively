# Exercise the production helper in a temporary app directory, including failed startup.
$ErrorActionPreference='Stop'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('hahhub-test-'+[guid]::NewGuid().ToString('N'))
$originalData=$env:LOCALAPPDATA
try{
    [void][IO.Directory]::CreateDirectory($fixture)
    $env:LOCALAPPDATA=$fixture
    $app=Join-Path $fixture 'app'
    [void][IO.Directory]::CreateDirectory($app)
    $exe=Join-Path $app 'HAHHAN Studio.exe'
    Add-Type -OutputAssembly $exe -OutputType WindowsApplication -TypeDefinition @'
using System.Threading;
static class Demo {
    static void Main(string[] args){
        if(args.Length==2 && args[0]=="--updated")
            using(var ready=EventWaitHandle.OpenExisting(@"Local\hahhub.Updated."+args[1]))ready.Set();
    }
}
'@
    $originalHash=(Get-FileHash $exe).Hash
    [IO.File]::WriteAllText((Join-Path $app 'profile.txt'),'retain')
    foreach($broken in $false,$true){
        $job=Join-Path $app ('.hahhub-update-'+[guid]::NewGuid().ToString('N'))
        $next=Join-Path $job 'next'
        [void][IO.Directory]::CreateDirectory($next)
        [IO.File]::WriteAllText((Join-Path $app 'Studio.xaml'),'previous')
        [IO.File]::WriteAllText((Join-Path $next 'Studio.xaml'),'updated')
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\tools\apply-update.ps1') -Destination (Join-Path $job 'apply.ps1')
        if($broken){[IO.File]::WriteAllText((Join-Path $next 'HAHHAN Studio.exe'),'invalid')}
        else{Copy-Item -LiteralPath $exe -Destination $next}
        $log=Join-Path $fixture 'update-error.txt'
        $run=Start-Process powershell -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $job 'apply.ps1')+'"') -WorkingDirectory $fixture -WindowStyle Hidden -RedirectStandardError $log -Wait -PassThru
        if(($broken -and $run.ExitCode -eq 0) -or (!$broken -and $run.ExitCode -ne 0)){throw (Get-Content -Raw $log)}
        $expect=if($broken){'previous'}else{'updated'}
        if([IO.File]::ReadAllText((Join-Path $app 'Studio.xaml')) -ne $expect -or (Get-FileHash $exe).Hash -ne $originalHash){throw 'File replacement failed'}
        if([IO.File]::ReadAllText((Join-Path $app 'profile.txt')) -ne 'retain'){throw 'Profile changed'}
    }
    'Verified successful installation, startup handshake, rollback and preserved profile.'
}finally{
    $env:LOCALAPPDATA=$originalData
    $item=Get-Item -LiteralPath $fixture -ErrorAction SilentlyContinue
    if($item -and $item.Parent.FullName -eq [IO.Path]::GetTempPath().TrimEnd('\') -and $item.Name -match '^hahhub-test-[a-f0-9]{32}$'){
        Remove-Item -LiteralPath $item.FullName -Recurse -Force
    }
}
