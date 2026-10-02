<#
.SYNOPSIS
    Switches an installed EduHelpdesk to HTTPS, or replaces its certificate.

.DESCRIPTION
    Double-click Enable-Https.cmd rather than running this directly: it asks Windows for administrator rights first.

    The installer only asks about HTTPS on a new install, and an upgrade keeps whatever was chosen, so this is how an
    install that started on plain HTTP moves to HTTPS - and how a certificate that is about to expire is replaced.

    It asks which certificate to use: one already in this computer's Local Computer > Personal store, or a new
    self-signed one it makes. A self-signed certificate encrypts as well as a bought one, but every school computer has to
    be told to trust it, so making one also writes a folder holding the public certificate, a double-click script that
    trusts it on one computer, and the steps for doing it across a domain. It then changes the program's settings,
    lets the service read the certificate's key, opens the firewall for the new port, restarts the service and checks it
    answers. If it doesn't, the old settings are put back.

    The data isn't touched. The old http:// address stops working, so staff need the new one.

.PARAMETER CertificateThumbprint
    Use the certificate with this thumbprint from Local Computer > Personal, without asking.
.PARAMETER CertificateName
    Make a new self-signed certificate for this name (several can be separated by commas), without asking.
.PARAMETER Port
    The port for HTTPS (443 if it is free, otherwise 5443) - asked for when left out.
.PARAMETER Yes
    Don't ask to confirm.
.PARAMETER ProgramFolder
    For testing: the program folder of an install made with  Install.cmd -NoService.
.PARAMETER NoService
    For testing: changes the settings files only - no service, firewall, machine-wide certificate or administrator
    rights, and the current user's certificate store in place of the computer's.
#>
[CmdletBinding()]
param(
    [string]$CertificateThumbprint,
    [string]$CertificateName,
    [int]$Port,
    [switch]$Yes,
    [string]$ProgramFolder,
    [switch]$NoService
)
$ErrorActionPreference = "Stop"
$ServiceName = "EduHelpdesk"
. (Join-Path $PSScriptRoot "Helpers.ps1")
. (Join-Path $PSScriptRoot "Certificate.ps1")
function Fail([string]$text) { Write-Host ""; Write-Host "  $text" -ForegroundColor Red; throw "Stopped: $text" }

function Ask([string]$question, [string]$default, [scriptblock]$check) {
    while ($true) {
        $value = Read-Host $(if ($default) { "  $question [$default]" } else { "  $question" })
        if ([string]::IsNullOrWhiteSpace($value)) { $value = $default }
        $value = if ($null -eq $value) { "" } else { $value.Trim().Trim('"') }
        $problem = if ($check) { & $check $value } else { $null }
        if (-not $problem) { return $value }
        Warn $problem
    }
}

Write-Host ""
Write-Host "EduHelpdesk: switch to HTTPS" -ForegroundColor White
if (-not $NoService) {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Fail "Run Enable-Https.cmd, or this script from PowerShell opened with 'Run as administrator'." }
}

# ---- What is installed ----------------------------------------------------------------------------------------------

$service = if ($NoService) { $null } else { Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue }
if ($service) { $ProgramFolder = Split-Path ($service.PathName.Trim('"')) -Parent }
if (-not $ProgramFolder) { Fail "EduHelpdesk isn't installed as a service on this computer. Run Install.cmd first." }
$record = Join-Path $ProgramFolder "install.json"
$appSettings = Join-Path $ProgramFolder "appsettings.Production.json"
if (-not (Test-Path $record) -or -not (Test-Path $appSettings)) { Fail "$ProgramFolder has no install.json or appsettings.Production.json, so it wasn't installed by this installer. Edit the settings by hand - see INSTALL.md." }
$installed = Get-Content $record -Raw | ConvertFrom-Json
$dataFolder = [string]$installed.DataFolder
$certificateStore = if ($NoService) { "CurrentUser" } else { "LocalMachine" }

if ($installed.UseHttps) {
    Say "This install already uses HTTPS ($($installed.CertificateName)). Carrying on replaces its certificate."
} else {
    Say "This install uses plain HTTP, so passwords cross the network unencrypted."
}

# ---- Which certificate ----------------------------------------------------------------------------------------------

$certificates = @(Get-ChildItem "Cert:\$certificateStore\My" | Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } | Sort-Object NotAfter -Descending)
$certificate = $null; $makeCertificate = $false; $certificateNames = @()
if ($CertificateThumbprint) {
    $wanted = $CertificateThumbprint.Replace(" ", "")
    $certificate = $certificates | Where-Object { $_.Thumbprint -eq $wanted } | Select-Object -First 1
    if (-not $certificate) { Fail "No certificate with that thumbprint and a private key is in Local Computer > Personal." }
} elseif ($CertificateName) {
    $certificateNames = SplitNames $CertificateName
    if (CertificateNamesProblem $certificateNames) { Fail (CertificateNamesProblem $certificateNames) }
    $makeCertificate = $true
} else {
    Heading "Which certificate?"
    Say "It must be for the name staff will type in their browser."
    for ($i = 0; $i -lt $certificates.Count; $i++) {
        $c = $certificates[$i]
        Say ("{0}. {1}  (expires {2:d MMM yyyy}{3})" -f ($i + 1), $c.GetNameInfo("SimpleName", $false), $c.NotAfter, $(if (IsSelfSigned $c) { ", self-signed" } else { "" }))
    }
    Say ("{0}. Make a new self-signed certificate for this computer" -f ($certificates.Count + 1))
    $choice = [int](Ask "Which one" $(if ($certificates.Count -gt 0) { "1" } else { "$($certificates.Count + 1)" }) {
        param($v) $n = 0; if (-not [int]::TryParse($v, [ref]$n) -or $n -lt 1 -or $n -gt ($certificates.Count + 1)) { "A number from 1 to $($certificates.Count + 1)." }
    })
    if ($choice -le $certificates.Count) { $certificate = $certificates[$choice - 1] } else { $makeCertificate = $true }
    if ($makeCertificate) {
        Say "The certificate only works for the names it is made for, so give the name staff will actually type - the one in the"
        Say "address bar, such as helpdesk.school.org.uk. This computer's own name and IP addresses are added as well."
        $certificateNames = SplitNames (Ask "Name(s) staff will type, separated by commas" ((ServerNames) -join ", ") { param($v) CertificateNamesProblem (SplitNames $v) })
    }
}

# ---- Port ------------------------------------------------------------------------------------------------------------

# The helpdesk's own service holding a port doesn't count against it: it is about to be restarted.
function PortBusy([int]$port) {
    $listening = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
    if ($listening.Count -eq 0) { return $false }
    return -not ($service -and ($listening | Where-Object { $_.OwningProcess -eq $service.ProcessId }))
}
if (-not $Port) {
    $default = if (PortBusy 443) { 5443 } else { 443 }
    $Port = [int](Ask "Port" "$default" {
        param($v) $n = 0
        if (-not [int]::TryParse($v, [ref]$n) -or $n -lt 1 -or $n -gt 65535) { return "A port number from 1 to 65535." }
        if (PortBusy $n) { return "Something else on this computer is already using port $n. Choose another." }
    })
} elseif (PortBusy $Port) { Fail "Something else on this computer is already using port $Port." }

$hadRule = $null -ne (Get-NetFirewallRule -DisplayName "EduHelpdesk (*)" -ErrorAction SilentlyContinue)
$openFirewall = if ($null -ne $installed.OpenFirewall) { [bool]$installed.OpenFirewall } else { $hadRule }
$firewallPublic = if ($null -ne $installed.FirewallPublic) { [bool]$installed.FirewallPublic } else { (ConnectedNetworkCategories) -contains "Public" }

$settings = [pscustomobject]@{
    DataFolder = $dataFolder; UseHttps = $true; Port = $Port
    CertificateSubject = $(if ($certificate) { $certificate.GetNameInfo("SimpleName", $false) } else { $certificateNames[0] })
    CertificateName = $(if ($certificate) { CertificateHostName $certificate } else { $certificateNames[0] })
    CertificateSelfSigned = $(if ($certificate) { IsSelfSigned $certificate } else { $true })
    CertificateThumbprint = $(if ($certificate) { $certificate.Thumbprint } else { $null })
    CertificateStoreLocation = $certificateStore; CertificateFolder = $null
    OpenFirewall = $openFirewall; FirewallPublic = $firewallPublic
}

Heading "Ready"
Say "Certificate:  $(if ($makeCertificate) { "a new self-signed one for $($certificateNames -join ', '), valid 5 years" } else { "$($certificate.GetNameInfo('SimpleName', $false)), expires $($certificate.NotAfter.ToString('d MMM yyyy'))$(if ($settings.CertificateSelfSigned) { ', self-signed' })" })"
Say "New address:  $(PublicUrl $settings)"
Say "The service restarts (a few seconds). The old http:// address stops working, so staff need the new one."
if (-not $Yes -and (Ask "Switch to HTTPS now? (y/n)" "y" { param($v) if ($v -notmatch '^(y|yes|n|no)$') { "Answer y or n." } }) -notmatch '^(y|yes)$') { Fail "Nothing was changed." }

# ---- Doing it --------------------------------------------------------------------------------------------------------

Heading "Switching"
$undo = @{ AppSettings = [IO.File]::ReadAllBytes($appSettings); Record = [IO.File]::ReadAllBytes($record) }
$certificateFiles = $null
if ($service -and $service.State -ne "Stopped") {
    Say "Stopping the service..."
    Stop-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(90))
}
try {
    if ($makeCertificate) {
        Say "Making a self-signed certificate for $($certificateNames -join ', ')..."
        $addresses = @(LanAddresses)
        $certificate = NewSchoolCertificate $certificateNames $addresses $certificateStore
        $settings.CertificateThumbprint = $certificate.Thumbprint
        $settings.CertificateFolder = Join-Path $dataFolder "certificate"
        $certificateFiles = WriteCertificateFiles $certificate $settings.CertificateFolder $certificateNames $addresses (PublicUrl $settings)
        Say "Certificate files written to $($settings.CertificateFolder)."
        if (-not $NoService) { TrustOnThisComputer $certificateFiles.Cer; Say "This computer trusts it." }
    }
    WriteAppSettings $ProgramFolder $settings
    WriteInstallRecord $ProgramFolder $settings ([version]$installed.Version)
    Say "Settings written."
    if ($NoService) { Heading "Changed the settings only (-NoService)."; return }
    if ($certificate) { GrantCertificateKey $certificate "NT SERVICE\$ServiceName" }
    if ($openFirewall) { OpenFirewall (Join-Path $ProgramFolder "EduHelpdesk.exe") $Port $firewallPublic }
    Say "Starting the service..."
    Start-Service -Name $ServiceName
    if (-not (WaitUntilAnswering (LocalUrl $settings))) { throw "The site isn't answering with the new certificate." }
} catch {
    Warn "$($_.Exception.Message) Putting the previous settings back."
    [IO.File]::WriteAllBytes($appSettings, $undo.AppSettings)
    [IO.File]::WriteAllBytes($record, $undo.Record)
    if ($service) {
        Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
        Start-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($openFirewall) { OpenFirewall (Join-Path $ProgramFolder "EduHelpdesk.exe") ([int]$installed.Port) $firewallPublic }
    }
    Fail "HTTPS wasn't switched on, and the helpdesk is back as it was. Look in $dataFolder\logs and Event Viewer > Windows Logs > Application (source EduHelpdesk), then try again."
}

Heading "EduHelpdesk is now at $(PublicUrl $settings)"
SayHowToReachIt $settings
if ($certificateFiles) { SayHowToTrust $certificate $certificateFiles (PublicUrl $settings) }
elseif ($settings.CertificateSelfSigned) { Warn "The certificate you chose is self-signed, so every school computer must be told to trust it or its browser will warn." }
Say "Then, in the helpdesk: Settings > Sign-in security > Helpdesk address, so the quick start guides print the new address."
