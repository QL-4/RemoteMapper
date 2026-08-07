#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

$originalName = 'MiRemoteHidFilter.inf'
$drivers = Get-WindowsDriver -Online -All | Where-Object {
    $_.OriginalFileName -and ([System.IO.Path]::GetFileName($_.OriginalFileName) -ieq $originalName)
}

if (-not $drivers) {
    Write-Host 'MiRemoteHidFilter package is not present in the driver store.'
    exit 0
}

foreach ($driver in $drivers) {
    Write-Host "Removing $($driver.Driver) ($($driver.OriginalFileName))..."
    & pnputil.exe /delete-driver $driver.Driver /uninstall /force
    if ($LASTEXITCODE -ne 0) { throw "pnputil failed for $($driver.Driver), exit code $LASTEXITCODE" }
}

Write-Host ''
Write-Host 'Driver package removed. Restart Windows to rebuild the keyboard stack without the filter.' -ForegroundColor Green
Write-Host 'This script deliberately leaves TESTSIGNING and the certificate unchanged until recovery is confirmed.' -ForegroundColor Yellow
