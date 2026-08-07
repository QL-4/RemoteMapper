#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

$certPath = Join-Path $PSScriptRoot 'package\MiRemoteHidFilter.cer'
if (-not (Test-Path $certPath)) { throw "Certificate not found: $certPath" }
$certificate = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $certPath
$thumbprint = $certificate.Thumbprint

$installed = Get-WindowsDriver -Online -All | Where-Object {
    $_.OriginalFileName -and ([System.IO.Path]::GetFileName($_.OriginalFileName) -ieq 'MiRemoteHidFilter.inf')
}
if ($installed) {
    throw 'MiRemoteHidFilter is still installed. Run uninstall-driver.bat and restart before disabling TESTSIGNING.'
}

Write-Host 'Disabling Windows TESTSIGNING boot option...'
& bcdedit.exe /set testsigning off
if ($LASTEXITCODE -ne 0) { throw "bcdedit failed with exit code $LASTEXITCODE" }

Write-Host 'Removing the WDK test certificate from LocalMachine trust stores...'
foreach ($store in @('Root','TrustedPublisher')) {
    Get-ChildItem "Cert:\LocalMachine\$store" | Where-Object Thumbprint -eq $thumbprint | Remove-Item -Force
}

Write-Host ''
Write-Host 'Normal code-integrity mode restored. Restart Windows to apply it.' -ForegroundColor Green
