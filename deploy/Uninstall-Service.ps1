<#
.SYNOPSIS
    Stops and removes the EduHelpdesk Windows service. The app folder and the data folder are left as they are.
    Run from an elevated PowerShell.
#>
[CmdletBinding()]
param([string]$ServiceName = "EduHelpdesk")
$ErrorActionPreference = "Stop"
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) { Write-Host "There is no $ServiceName service."; return }
if ($service.Status -ne "Stopped") { Stop-Service -Name $ServiceName; $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(60)) }
& sc.exe delete $ServiceName | Out-Null
Get-NetFirewallRule -DisplayName "EduHelpdesk (*)" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Write-Host "Removed the $ServiceName service. The data folder is untouched."
