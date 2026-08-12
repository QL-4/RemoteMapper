#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

$package = Join-Path $PSScriptRoot 'package'
$inf = Join-Path $package 'MiRemoteHidFilter.inf'
$targetHardwareId = 'HID\{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&012717_PID&32b8_REV&00a4'

if (-not (Test-Path $inf)) { throw "Driver package not found: $inf" }

$bcd = (& bcdedit.exe /enum '{current}' 2>&1 | Out-String)
if ($bcd -notmatch '(?im)^testsigning\s+Yes\s*$') {
    throw 'TESTSIGNING is not active. Run prepare-test-mode.bat, restart Windows, then try again.'
}

$device = Get-PnpDevice -PresentOnly | Where-Object {
    $ids = (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_HardwareIds -ErrorAction SilentlyContinue).Data
    $ids -contains $targetHardwareId
} | Select-Object -First 1
if (-not $device) { throw "Target Xiaomi keyboard collection is not present: $targetHardwareId" }

Write-Host "Target device: $($device.FriendlyName)"
Write-Host "Instance:      $($device.InstanceId)"
Write-Host 'Staging and installing the device-specific extension INF...'
& pnputil.exe /add-driver $inf /install
if ($LASTEXITCODE -ne 0) { throw "pnputil failed with exit code $LASTEXITCODE" }

Write-Host ''
Write-Host 'Driver package installed. Restart Windows so the protected keyboard stack is rebuilt.' -ForegroundColor Green
Write-Host 'After restart, stop RemoteMic and run verify-keys.bat to verify VK_UP/F13-F20.' -ForegroundColor Yellow
