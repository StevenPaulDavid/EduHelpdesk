<#
.SYNOPSIS
    Installs EduHelpdesk as a Windows service that starts with the computer, restarts itself after a failure and keeps
    its data outside OneDrive.

.DESCRIPTION
    Run it from an elevated PowerShell (Run as administrator), after Publish.ps1 has put the app in -AppFolder.
    It will:
      1. Copy the existing data (database, attachments, sign-in keys, logo, template, logs) from -ImportFrom into
         -DataFolder - only when -DataFolder has no database yet, so a second run never overwrites live data.
      2. Write appsettings.Production.json next to the app: the data folder, the address to listen on and, with
         -CertificateSubject, the HTTPS certificate from the computer's certificate store.
      3. Register the service under its own virtual account (NT SERVICE\EduHelpdesk), give that account access to the
         data folder (and the certificate's private key), set it to restart after a failure, and start it.
      4. With -OpenFirewall, allow the port through Windows Firewall on domain and private networks.

    Stop the copy of the helpdesk you run today first (close `dotnet run`), so the database isn't mid-save while it
    is copied. The old data folder is left exactly as it was.

    -PrepareOnly does steps 1 and 2 and nothing else, to check them before touching Windows.

.EXAMPLE
    .\Install-Service.ps1 -ImportFrom "C:\Users\me\OneDrive\...\EduHelpdesk\App_Data" -Url "http://*:5277" -OpenFirewall

.EXAMPLE
    .\Install-Service.ps1 -ImportFrom "...\App_Data" -Url "https://*:443" -CertificateSubject "helpdesk.school.org.uk" -OpenFirewall
#>
[CmdletBinding()]
param(
    [string]$AppFolder = "C:\EduHelpdesk\app",
    [string]$DataFolder = "C:\EduHelpdesk\data",
    [string]$ImportFrom,
    [string]$Url = "http://*:5277",
    [string]$CertificateSubject,
    [string]$ServiceName = "EduHelpdesk",
    [switch]$OpenFirewall,
    [switch]$PrepareOnly
)
$ErrorActionPreference = "Stop"

function Say([string]$text) { Write-Host "  $text" }

$exe = Join-Path $AppFolder "EduHelpdesk.exe"
if (-not (Test-Path $exe)) { throw "No EduHelpdesk.exe in $AppFolder. Run Publish.ps1 first (or pass -AppFolder)." }
if ($DataFolder.TrimEnd('\').StartsWith($AppFolder.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Keep the data folder outside the app folder, so an update can't touch it."
}
if ($DataFolder -match '(?i)\\(OneDrive|Dropbox|Google Drive|iCloudDrive)') {
    throw "$DataFolder is inside a synced folder. SQLite databases get damaged by sync clients - choose a plain local folder."
}
$https = $Url.StartsWith("https://", [StringComparison]::OrdinalIgnoreCase)
if ($https -and -not $CertificateSubject) { throw "An https:// address needs -CertificateSubject: the name on a certificate in Local Computer > Personal." }
if (-not $PrepareOnly) {
    $identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Run this from PowerShell opened with 'Run as administrator'." }
    if (-not ((& dotnet --list-runtimes) -match '^Microsoft\.AspNetCore\.App 10\.')) { throw "The ASP.NET Core 10 runtime isn't installed. Install the 'ASP.NET Core Runtime 10' (Hosting Bundle) from https://dotnet.microsoft.com/download/dotnet/10.0 and run this again." }
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) { throw "A service called $ServiceName already exists. To update it, use Publish.ps1; to reinstall, run Uninstall-Service.ps1 first." }
}

Write-Host "1. Data folder: $DataFolder"
New-Item -ItemType Directory -Force -Path $DataFolder | Out-Null
$database = Join-Path $DataFolder "helpdesk.db"
if (Test-Path $database) {
    Say "It already holds a database - left as it is."
} elseif ($ImportFrom) {
    $source = Join-Path $ImportFrom "helpdesk.db"
    if (-not (Test-Path $source)) { throw "No helpdesk.db in $ImportFrom." }
    # Refuse to copy a database something still has open: a copy taken mid-save is a damaged copy.
    try { $lock = [IO.File]::Open($source, 'Open', 'Read', 'None'); $lock.Dispose() }
    catch { throw "$source is in use. Stop the helpdesk that is running from it (close dotnet run), then run this again." }
    # Everything in the data folder - database, attachments, quote files, sign-in keys, logo, template, logs and the
    # backups made so far - the same set the app itself copies when its data folder moves (DataLocation).
    foreach ($item in Get-ChildItem -LiteralPath $ImportFrom -Force) {
        if ($item.Name -like "helpdesk.db-*" -or $item.Name -like "*.moved-*") { continue }
        Copy-Item -LiteralPath $item.FullName -Destination $DataFolder -Recurse -Force
        Say "copied $($item.Name)$(if ($item.PSIsContainer) { '\' })"
    }
    Say "The old data in $ImportFrom is untouched. Once the service is working, it can be archived."
} else {
    Say "No database yet and nothing to import - the helpdesk will start as a new install."
}

Write-Host "2. Settings: $(Join-Path $AppFolder 'appsettings.Production.json')"
$endpoint = [ordered]@{ Url = $Url }
if ($https) { $endpoint.Certificate = [ordered]@{ Subject = $CertificateSubject; Store = "My"; Location = "LocalMachine"; AllowInvalid = $false } }
$settings = [ordered]@{
    EduHelpdesk = [ordered]@{ DataPath = $DataFolder; RequireHttps = $https }
    Kestrel = [ordered]@{ Endpoints = [ordered]@{ Main = $endpoint } }
}
$settings | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $AppFolder "appsettings.Production.json") -Encoding UTF8
Say "listens on $Url$(if ($https) { " with the certificate for $CertificateSubject" })"

if ($PrepareOnly) { Write-Host "Prepared only - no service installed."; return }

$account = "NT SERVICE\$ServiceName"
Write-Host "3. Service $ServiceName"
New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName "EduHelpdesk" -Description "The school's IT helpdesk (EduHelpdesk)." -StartupType Automatic | Out-Null
& sc.exe config $ServiceName obj= $account | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null
& icacls $DataFolder /grant "${account}:(OI)(CI)M" /T /Q | Out-Null
& icacls $AppFolder /grant "${account}:(OI)(CI)RX" /T /Q | Out-Null
Say "runs as $account, with change access to the data folder and read access to the app"
if ($https) {
    $cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -match [regex]::Escape($CertificateSubject) -and $_.HasPrivateKey } | Sort-Object NotAfter -Descending | Select-Object -First 1
    if (-not $cert) { throw "No certificate with a private key for $CertificateSubject in Local Computer > Personal." }
    try {
        $key = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
        $keyFile = Get-ChildItem -Path "$env:ProgramData\Microsoft\Crypto" -Recurse -Filter $key.Key.UniqueName -ErrorAction SilentlyContinue | Select-Object -First 1
        & icacls $keyFile.FullName /grant "${account}:R" /Q | Out-Null
        Say "the service can read the certificate's private key"
    } catch {
        Write-Warning "Couldn't give $account read access to the certificate's private key automatically. In certlm.msc, right-click the certificate > All Tasks > Manage Private Keys, and add $account with Read."
    }
}
if ($OpenFirewall) {
    $port = if ($Url -match ':(\d+)') { $Matches[1] } elseif ($https) { 443 } else { 80 }
    New-NetFirewallRule -DisplayName "EduHelpdesk ($port)" -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow -Profile Domain, Private | Out-Null
    Write-Host "4. Firewall: port $port open on domain and private networks"
}

Start-Service -Name $ServiceName
Start-Sleep -Seconds 5
$status = (Get-Service -Name $ServiceName).Status
Write-Host "The service is $status."
if ($status -ne "Running") { Write-Warning "It didn't stay running. Look in Event Viewer > Windows Logs > Application (source EduHelpdesk) and in $DataFolder\logs." }
else { Write-Host "Open $($Url.Replace('*', $env:COMPUTERNAME.ToLowerInvariant())) from another computer to check." }
