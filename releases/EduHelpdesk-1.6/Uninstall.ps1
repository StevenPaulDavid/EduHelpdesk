<#
.SYNOPSIS
    Removes EduHelpdesk from this computer: the service, its firewall rule and the program folder. The data folder -
    database, attachments, backups - is left in place unless you say otherwise, so it can be reinstalled or moved.
    Double-click Uninstall.cmd rather than running this directly.
#>
[CmdletBinding()]
param([switch]$KeepProgram)
$ErrorActionPreference = "Stop"
$ServiceName = "EduHelpdesk"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Run Uninstall.cmd, or this script from PowerShell opened with 'Run as administrator'." }

$service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
if (-not $service) { Write-Host "EduHelpdesk isn't installed as a service on this computer."; return }
$programFolder = Split-Path ($service.PathName.Trim('"')) -Parent
$dataFolder = $null
$record = Join-Path $programFolder "install.json"
if (Test-Path $record) { $dataFolder = (Get-Content $record -Raw | ConvertFrom-Json).DataFolder }

Write-Host ""
Write-Host "This removes the EduHelpdesk service and the program in $programFolder." -ForegroundColor Cyan
if ($dataFolder) { Write-Host "The data in $dataFolder is kept." }
if ((Read-Host "  Carry on? (y/n) [n]") -notmatch '^(y|yes)$') { Write-Host "Nothing was changed."; return }

if ($service.State -ne "Stopped") {
    Stop-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(90))
}
& sc.exe delete $ServiceName | Out-Null
Get-NetFirewallRule -DisplayName "EduHelpdesk (*)" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Write-Host "  Service and firewall rule removed."
if (-not $KeepProgram) {
    Remove-Item -LiteralPath $programFolder -Recurse -Force
    if (Test-Path "$programFolder.previous") { Remove-Item -LiteralPath "$programFolder.previous" -Recurse -Force }
    Write-Host "  Program folder removed."
}
if ($dataFolder) {
    Write-Host ""
    Write-Host "The data is still in $dataFolder. Keep it to reinstall later (the installer offers to use it), or delete it" -ForegroundColor Yellow
    Write-Host "yourself once you are sure - it holds tickets and personal data, so dispose of it as your data policy says." -ForegroundColor Yellow
}
