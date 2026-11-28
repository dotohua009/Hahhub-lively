# Download and run the same Windows x64 package used by hahhub's updater.
& {
    $ErrorActionPreference = 'Stop'
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or
        -not [Environment]::Is64BitOperatingSystem) {
        throw 'hahhub requires 64-bit Windows.'
    }

    $channel = 'https://raw.githubusercontent.com/dotohua009/Hahhub-lively/main/downloads/hahhub'
    $release = Invoke-RestMethod -Uri "$channel/update.json"
    if ($release.product -ne 'hahhub' -or $release.version -notmatch '^\d+\.\d+\.\d+$' -or
        $release.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or [long]$release.bytes -le 0) {
        throw 'Invalid hahhub release manifest.'
    }

    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $folderName = 'hahhub-' + [guid]::NewGuid().ToString('N')
    $stage = Join-Path $tempRoot $folderName
    $null = New-Item -ItemType Directory -Path $stage
    try {
        $archive = Join-Path $stage 'hahhub-windows-x64.zip'
        $url = "$channel/v$($release.version)/hahhub-windows-x64.zip"
        Write-Host "Downloading hahhub $($release.version) for Windows x64..."
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $archive
        if ((Get-Item -LiteralPath $archive).Length -ne [long]$release.bytes -or
            (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $release.sha256) {
            throw 'Downloaded package failed verification.'
        }

        $package = Join-Path $stage 'package'
        Expand-Archive -LiteralPath $archive -DestinationPath $package
        $installer = Join-Path $package 'install.ps1'
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer
        if ($LASTEXITCODE -ne 0) { throw 'Installation failed. See the installer output above.' }
        Write-Host "hahhub $($release.version) installed."
    } finally {
        $created = Get-Item -LiteralPath $stage
        if ($created.Parent.FullName.TrimEnd('\') -eq $tempRoot.TrimEnd('\') -and
            $created.Name -eq $folderName -and -not ($created.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            Remove-Item -LiteralPath $created.FullName -Recurse -Force
        }
    }
}
