#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

$package = Join-Path $PSScriptRoot 'package'
$cert = Join-Path $package 'MiRemoteHidFilter.cer'
if (-not (Test-Path $cert)) { throw "Certificate not found: $cert" }

$secureBoot = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State' -Name UEFISecureBootEnabled -ErrorAction SilentlyContinue).UEFISecureBootEnabled
if ($secureBoot -eq 1) { throw 'Secure Boot is enabled. TESTSIGNING cannot be enabled until Secure Boot is disabled in UEFI.' }

Write-Host 'Installing the WDK test certificate into LocalMachine Trusted Root and Trusted Publishers...'
Import-Certificate -FilePath $cert -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
Import-Certificate -FilePath $cert -CertStoreLocation 'Cert:\LocalMachine\TrustedPublisher' | Out-Null

Write-Host 'Enabling Windows TESTSIGNING boot option...'
& bcdedit.exe /set testsigning on
if ($LASTEXITCODE -ne 0) { throw "bcdedit failed with exit code $LASTEXITCODE" }

Write-Host ''
Write-Host 'Prepared successfully. No driver has been installed yet.' -ForegroundColor Green
Write-Host 'Restart Windows, then run install-driver.bat as Administrator.' -ForegroundColor Yellow
