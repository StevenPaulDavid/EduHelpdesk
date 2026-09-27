<#
.SYNOPSIS
    Builds a release copy of EduHelpdesk into the folder the service runs from.

.DESCRIPTION
    Run from anywhere; it finds the project next to this script. If the EduHelpdesk service is installed and running,
    it is stopped for the copy and started again afterwards, so this is also how to update a running install. The
    data folder is never touched - it lives elsewhere (see Install-Service.ps1) - and appsettings.Production.json in
    the target folder, which holds this install's settings, is kept.

.EXAMPLE
    .\Publish.ps1
    .\Publish.ps1 -AppFolder D:\EduHelpdesk\app
#>
[CmdletBinding()]
param(
    [string]$AppFolder = "C:\EduHelpdesk\app",
    [string]$ServiceName = "EduHelpdesk"
)
$ErrorActionPreference = "Stop"
$project = Join-Path (Split-Path $PSScriptRoot -Parent) "EduHelpdesk.csproj"
if (-not (Test-Path $project)) { throw "Can't find EduHelpdesk.csproj next to the deploy folder ($project)." }

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$wasRunning = $service -and $service.Status -eq "Running"
if ($wasRunning) {
    Write-Host "Stopping the $ServiceName service for the update..."
    Stop-Service -Name $ServiceName
    $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(60))
}

# Keep this install's own settings across the copy.
$settings = Join-Path $AppFolder "appsettings.Production.json"
$keep = if (Test-Path $settings) { Get-Content $settings -Raw } else { $null }

Write-Host "Publishing to $AppFolder ..."
& dotnet publish $project -c Release -o $AppFolder --nologo
if ($LASTEXITCODE -ne 0) {
    if ($wasRunning) { Start-Service -Name $ServiceName }
    throw "dotnet publish failed; nothing was changed in a way that matters (the service has been started again if it was running)."
}
if ($null -ne $keep) { Set-Content -Path $settings -Value $keep -Encoding UTF8 -NoNewline }

if ($wasRunning) {
    Write-Host "Starting the $ServiceName service..."
    Start-Service -Name $ServiceName
}
Write-Host "Done."
