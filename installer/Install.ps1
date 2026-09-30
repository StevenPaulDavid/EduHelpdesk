<#
.SYNOPSIS
    Installs EduHelpdesk on this computer, or upgrades an existing install to the version in this folder.

.DESCRIPTION
    Double-click Install.cmd rather than running this directly: it asks Windows for administrator rights first.

    A NEW INSTALL asks a few questions - where to keep the data, the school's name and colours, a logo, where the
    nightly backups go, HTTPS and the port - then copies the program to C:\Program Files\EduHelpdesk, registers the
    EduHelpdesk Windows service (it starts with the computer and restarts itself after a failure), opens the firewall
    if asked, starts it and checks it answers. Everything it needs is in this folder, the .NET runtime included:
    nothing is downloaded.

    AN UPGRADE (EduHelpdesk is already installed) asks nothing: it stops the service, zips the data into the backups
    folder, swaps in the new program files, keeps the settings and data, starts it again and checks it answers. If
    the new version doesn't start, the previous one is put back.

    For a trust installing at several schools, fill in install-answers.json (see install-answers.example.json) and
    run:  Install.cmd -AnswersFile install-answers.json   - no questions are asked.

.PARAMETER AnswersFile
    A JSON file of answers, for installing without questions.
.PARAMETER ImportFrom
    An existing EduHelpdesk data folder to bring across (moving to a new machine, or out of a trial install). Copied
    only into an empty data folder; the original is left untouched.
.PARAMETER ProgramFolder
    Where to put the program on a new install (C:\Program Files\EduHelpdesk unless given).
.PARAMETER NoService
    For testing the installer: copies the program and writes the settings, but registers no service, changes no
    permissions or firewall and needs no administrator rights.
#>
[CmdletBinding()]
param(
    [string]$AnswersFile,
    [string]$ImportFrom,
    [string]$ProgramFolder,
    [switch]$NoService
)
$ErrorActionPreference = "Stop"
# PowerShell names aren't case-sensitive, so the parameter is copied before $programFolder below overwrites it.
$requestedProgramFolder = $ProgramFolder
$ServiceName = "EduHelpdesk"
$here = $PSScriptRoot
$package = Join-Path $here "app"
$newVersion = [version](Get-Content (Join-Path $here "VERSION.txt") -Raw).Trim()
$DefaultProgramFolder = Join-Path $env:ProgramFiles "EduHelpdesk"
$DefaultDataFolder = "C:\EduHelpdesk\Data"
$BootstrapEmail = "admin@eduhelpdesk.local"
$BootstrapPassword = "ChangeMe123!"

# ---- Helpers -------------------------------------------------------------------------------------------------------

function Heading([string]$text) { Write-Host ""; Write-Host $text -ForegroundColor Cyan }
function Say([string]$text) { Write-Host "  $text" }
function Warn([string]$text) { Write-Host "  ! $text" -ForegroundColor Yellow }
function Fail([string]$text) { Write-Host ""; Write-Host "  $text" -ForegroundColor Red; throw "Install stopped: $text" }

$answers = $null
if ($AnswersFile) {
    if (-not (Test-Path $AnswersFile)) { Fail "The answers file $AnswersFile doesn't exist." }
    $answers = Get-Content $AnswersFile -Raw | ConvertFrom-Json
}
$unattended = $null -ne $answers

# The answer from the answers file if there is one; otherwise the question, with the default in brackets. Each answer
# goes through $check, which returns an error message or nothing - a bad answer is asked again, or stops an
# unattended install.
function Ask([string]$name, [string]$question, [string]$default, [scriptblock]$check) {
    while ($true) {
        if ($unattended) {
            $value = $default
            if ($answers.PSObject.Properties.Name -contains $name -and $null -ne $answers.$name) { $value = [string]$answers.$name }
        } else {
            $prompt = if ($default) { "  $question [$default]" } else { "  $question" }
            $value = Read-Host $prompt
            if ([string]::IsNullOrWhiteSpace($value)) { $value = $default }
        }
        $value = if ($null -eq $value) { "" } else { $value.Trim().Trim('"') }
        $problem = if ($check) { & $check $value } else { $null }
        if (-not $problem) { return $value }
        if ($unattended) { Fail "The answer for $name ('$value') won't do: $problem" }
        Warn $problem
    }
}

function AskYesNo([string]$name, [string]$question, [bool]$default) {
    $answer = Ask $name "$question (y/n)" $(if ($default) { "y" } else { "n" }) {
        param($v) if ($v -notmatch '^(y|yes|n|no|true|false)$') { "Answer y or n." }
    }
    return $answer -match '^(y|yes|true)$'
}

function SyncedFolderProblem([string]$path) {
    if ($path -match '(?i)\\(OneDrive|Dropbox|Google Drive|iCloudDrive|Box Sync)') { return "That's inside a synced folder. Sync clients damage live databases - choose a plain local folder." }
    return $null
}

function PortInUse([int]$port) {
    return $null -ne (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
}

function WaitUntilAnswering([string]$url) {
    # The certificate may not match "localhost", or may be the school's own; this only checks the site is up.
    if ($PSVersionTable.PSVersion.Major -lt 6) { [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true } }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    for ($i = 0; $i -lt 30; $i++) {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return $true }
        } catch { }
        Start-Sleep -Seconds 2
    }
    return $false
}

function LocalUrl($settings) {
    $scheme = if ($settings.UseHttps) { "https" } else { "http" }
    return "${scheme}://localhost:$($settings.Port)/Login"
}

function PublicUrl($settings) {
    $scheme = if ($settings.UseHttps) { "https" } else { "http" }
    $name = if ($settings.UseHttps -and $settings.CertificateName) { $settings.CertificateName } else { $env:COMPUTERNAME.ToLowerInvariant() }
    $default = ($settings.UseHttps -and $settings.Port -eq 443) -or (-not $settings.UseHttps -and $settings.Port -eq 80)
    if ($default) { return "${scheme}://$name/" } else { return "${scheme}://${name}:$($settings.Port)/" }
}

function CopyProgram([string]$target) {
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    # /MIR makes the folder match the package exactly; the install's own files are left out of the mirror.
    & robocopy $package $target /MIR /XF appsettings.Production.json install.json /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "Copying the program into $target failed (robocopy code $LASTEXITCODE)." }
}

function WriteAppSettings([string]$programFolder, $settings) {
    $endpoint = [ordered]@{ Url = $(if ($settings.UseHttps) { "https://*:$($settings.Port)" } else { "http://*:$($settings.Port)" }) }
    if ($settings.UseHttps) { $endpoint.Certificate = [ordered]@{ Subject = $settings.CertificateSubject; Store = "My"; Location = "LocalMachine"; AllowInvalid = $false } }
    $json = [ordered]@{
        EduHelpdesk = [ordered]@{ DataPath = $settings.DataFolder; RequireHttps = [bool]$settings.UseHttps }
        Kestrel = [ordered]@{ Endpoints = [ordered]@{ Main = $endpoint } }
    }
    $json | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $programFolder "appsettings.Production.json") -Encoding UTF8
}

function WriteInstallRecord([string]$programFolder, $settings, [version]$version) {
    [ordered]@{
        Version = $version.ToString()
        DataFolder = $settings.DataFolder
        UseHttps = [bool]$settings.UseHttps
        Port = [int]$settings.Port
        CertificateName = $settings.CertificateName
        OpenFirewall = [bool]$settings.OpenFirewall
        FirewallPublic = [bool]$settings.FirewallPublic
        InstalledAt = (Get-Date).ToString("s")
    } | ConvertTo-Json | Set-Content -Path (Join-Path $programFolder "install.json") -Encoding UTF8
}

# The kinds of network this computer is on right now: Domain, Private and/or Public.
function ConnectedNetworkCategories {
    return @(Get-NetConnectionProfile -ErrorAction SilentlyContinue | ForEach-Object { [string]$_.NetworkCategory } | Select-Object -Unique)
}

# Lets other computers reach the helpdesk. The rule names both the port and the program, so Windows also counts the
# program itself as allowed and never pops up its "allow access" prompt for it. Public networks only when asked: a
# school server is normally on the domain network, but Windows files an unrecognised network as Public, and there it
# blocks everything not explicitly allowed - which is how "works on this PC, not from others" happens.
function OpenFirewall([string]$exe, [int]$port, [bool]$includePublic) {
    $profiles = @("Domain", "Private")
    if ($includePublic) { $profiles += "Public" }
    Get-NetFirewallRule -DisplayName "EduHelpdesk (*)" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    # A cancelled "allow access" prompt leaves a rule blocking the program, and a block beats any allow.
    $blocked = @(Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue | Where-Object { $_.Program -ieq $exe } |
        Get-NetFirewallRule | Where-Object { $_.Direction -eq "Inbound" -and $_.Action -eq "Block" })
    if ($blocked.Count -gt 0) { $blocked | Remove-NetFirewallRule; Say "Removed $($blocked.Count) firewall rule(s) Windows had made to block EduHelpdesk." }
    New-NetFirewallRule -DisplayName "EduHelpdesk ($port)" -Description "Lets other computers reach the EduHelpdesk website. Made by the EduHelpdesk installer." `
        -Direction Inbound -Protocol TCP -LocalPort $port -Program $exe -Action Allow -Profile $profiles | Out-Null
    Say "Firewall: port $port is open to EduHelpdesk on $($profiles -join ', ') networks."
}

# The addresses other computers can use when the computer's name doesn't resolve for them.
function LanAddresses {
    return @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" -and $_.AddressState -eq "Preferred" } |
        Select-Object -ExpandProperty IPAddress)
}

function SayHowToReachIt($settings) {
    $scheme = if ($settings.UseHttps) { "https" } else { "http" }
    $default = ($settings.UseHttps -and $settings.Port -eq 443) -or (-not $settings.UseHttps -and $settings.Port -eq 80)
    foreach ($address in LanAddresses) { Say "Or by address: ${scheme}://$address$(if (-not $default) { ":$($settings.Port)" })/" }
}

function GrantCertificateKey($certificate, [string]$account) {
    try {
        $key = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate)
        $file = Get-ChildItem -Path "$env:ProgramData\Microsoft\Crypto" -Recurse -Filter $key.Key.UniqueName -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $file) { throw "key file not found" }
        & icacls $file.FullName /grant "${account}:R" /Q | Out-Null
        Say "The service can read the certificate's private key."
    } catch {
        Warn "Couldn't give $account access to the certificate's private key automatically. In certlm.msc, right-click the certificate > All Tasks > Manage Private Keys, add $account with Read, then restart the EduHelpdesk service."
    }
}

# ---- Before anything else ------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "EduHelpdesk $newVersion installer" -ForegroundColor White
if (-not [Environment]::Is64BitOperatingSystem) { Fail "EduHelpdesk needs 64-bit Windows (Windows 10 or Server 2016, or newer)." }
if (-not (Test-Path (Join-Path $package "EduHelpdesk.exe"))) { Fail "The app folder is missing from beside this script. Unzip the whole EduHelpdesk zip and run Install.cmd from there." }
# A zip downloaded from the internet marks every file inside as "from the internet"; the installer vouches for them.
Get-ChildItem -LiteralPath $here -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
if (-not $NoService) {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Fail "Run Install.cmd, or this script from PowerShell opened with 'Run as administrator'." }
}
$logFolder = Join-Path $env:ProgramData "EduHelpdesk"
New-Item -ItemType Directory -Force -Path $logFolder | Out-Null
$log = Join-Path $logFolder ("install-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
Start-Transcript -Path $log | Out-Null

try {

# ---- Upgrade ------------------------------------------------------------------------------------------------------

# -NoService is for trying the installer out, so it leaves any real service on this computer out of it.
$service = if ($NoService) { $null } else { Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue }
$programFolder = $DefaultProgramFolder
if ($service) { $programFolder = Split-Path ($service.PathName.Trim('"')) -Parent }
elseif ($requestedProgramFolder) { $programFolder = $requestedProgramFolder }
elseif ($unattended -and $answers.ProgramFolder) { $programFolder = [string]$answers.ProgramFolder }
$record = Join-Path $programFolder "install.json"

if (($service -or $NoService) -and (Test-Path $record)) {
    $installed = Get-Content $record -Raw | ConvertFrom-Json
    $oldVersion = [version]$installed.Version
    Heading "EduHelpdesk $oldVersion is installed in $programFolder. Upgrading it to $newVersion."
    if ($newVersion -lt $oldVersion) {
        if ($unattended -or -not (AskYesNo "Downgrade" "This zip is OLDER than what's installed. Install it anyway?" $false)) { Fail "Nothing was changed: this zip ($newVersion) is older than the installed version ($oldVersion)." }
    }
    $dataFolder = [string]$installed.DataFolder

    if ($service -and $service.State -ne "Stopped") {
        Say "Stopping the service..."
        Stop-Service -Name $ServiceName
        (Get-Service -Name $ServiceName).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(90))
    }

    # A copy of the data as it was, before the new version touches it (its first start may upgrade the database).
    $backups = Join-Path $dataFolder "backups"
    New-Item -ItemType Directory -Force -Path $backups | Out-Null
    $backup = Join-Path $backups ("before-upgrade-{0}-to-{1}-{2:yyyyMMdd-HHmmss}.zip" -f $oldVersion, $newVersion, (Get-Date))
    $items = Get-ChildItem -LiteralPath $dataFolder -Force | Where-Object { $_.Name -notin @("backups", "logs") }
    Compress-Archive -Path $items.FullName -DestinationPath $backup
    Say "Data backed up to $backup"

    $previous = "$programFolder.previous"
    if (Test-Path $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
    Copy-Item -LiteralPath $programFolder -Destination $previous -Recurse
    CopyProgram $programFolder
    # Installs from before these were recorded: the firewall was opened if its rule is there, and Public networks are
    # included if this computer is on one now - which repairs "works here but not from other PCs".
    $hadRule = $null -ne (Get-NetFirewallRule -DisplayName "EduHelpdesk (*)" -ErrorAction SilentlyContinue)
    $openFirewall = if ($null -ne $installed.OpenFirewall) { [bool]$installed.OpenFirewall } else { $hadRule }
    $firewallPublic = if ($null -ne $installed.FirewallPublic) { [bool]$installed.FirewallPublic } else { (ConnectedNetworkCategories) -contains "Public" }
    $settings = [pscustomobject]@{ DataFolder = $dataFolder; UseHttps = [bool]$installed.UseHttps; Port = [int]$installed.Port; CertificateName = $installed.CertificateName
        OpenFirewall = $openFirewall; FirewallPublic = $firewallPublic }
    WriteInstallRecord $programFolder $settings $newVersion
    # The address for quick start guides, for installs from before it was stored. The app only uses it if none is set.
    $firstRunFile = Join-Path $dataFolder "install-settings.json"
    if (-not (Test-Path $firstRunFile)) { [ordered]@{ SiteAddress = (PublicUrl $settings) } | ConvertTo-Json | Set-Content -Path $firstRunFile -Encoding UTF8 }
    Say "Program files replaced (the previous version is kept in $previous)."

    if ($NoService) { Heading "Upgraded the files only (-NoService)."; return }
    if ($openFirewall) { OpenFirewall (Join-Path $programFolder "EduHelpdesk.exe") $settings.Port $firewallPublic }

    Say "Starting the service..."
    Start-Service -Name $ServiceName
    if (WaitUntilAnswering (LocalUrl $settings)) {
        Heading "EduHelpdesk is now version $newVersion and running at $(PublicUrl $settings)"
        SayHowToReachIt $settings
        Say "The data was upgraded in place; the backup above is there if anything looks wrong."
    } else {
        Warn "Version $newVersion didn't start answering, so the previous version is being put back."
        Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
        (Get-Service -Name $ServiceName).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(90))
        & robocopy $previous $programFolder /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
        Start-Service -Name $ServiceName
        Fail "The upgrade was rolled back to $oldVersion. Look at $dataFolder\logs and Event Viewer > Windows Logs > Application, then try again."
    }
    return
}
if ($service) { Fail "An EduHelpdesk service exists but $record is missing, so it wasn't installed by this installer. Remove it (Uninstall.cmd or 'sc.exe delete EduHelpdesk') and run this again." }

# ---- New install: the questions ------------------------------------------------------------------------------------

Heading "Where should EduHelpdesk keep its data?"
Say "The database, attachments and sign-in keys. A local folder on a drive that is backed up - not OneDrive or similar."
$dataFolder = Ask "DataFolder" "Data folder" $DefaultDataFolder {
    param($v)
    if (-not [IO.Path]::IsPathRooted($v)) { return "Give a full path, such as D:\EduHelpdesk\Data." }
    if (SyncedFolderProblem $v) { return (SyncedFolderProblem $v) }
    if ($v.TrimEnd('\').StartsWith($programFolder.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) { return "Keep the data out of the program folder ($programFolder), so an upgrade can't touch it." }
}
$existingData = Test-Path (Join-Path $dataFolder "helpdesk.db")
if ($existingData) {
    Say "There is already an EduHelpdesk database in $dataFolder."
    if (-not (AskYesNo "UseExistingData" "Use it (everything in it stays as it is)?" $true)) { Fail "Nothing was changed. Choose an empty folder, or move that data somewhere else first." }
} elseif ($ImportFrom) {
    if (-not (Test-Path (Join-Path $ImportFrom "helpdesk.db"))) { Fail "There's no helpdesk.db in $ImportFrom to import." }
    Say "The data will be copied from $ImportFrom."
}
$keepsItsOwnSettings = $existingData -or $ImportFrom

$schoolName = ""; $primary = ""; $accent = ""; $logo = ""
if (-not $keepsItsOwnSettings) {
    Heading "The school"
    $schoolName = Ask "SchoolName" "School name, as it should appear in the helpdesk and on printouts" "EduHelpdesk" {
        param($v) if ($v.Length -lt 2 -or $v.Length -gt 80) { "Between 2 and 80 characters." }
    }
    Say "Colours as #RRGGBB. The main colour is for buttons and headings; the accent is a pale shade behind highlights."
    $colour = { param($v) if ($v -notmatch '^#[0-9A-Fa-f]{6}$') { "Use the #RRGGBB form, such as #1F4E79." } }
    $primary = Ask "PrimaryColor" "Main colour" "#067A78" $colour
    $accent = Ask "AccentColor" "Accent colour" "#E8F0EF" $colour
    $logo = Ask "LogoPath" "Logo: full path to a PNG of the crest or logo (Enter to skip)" "" {
        param($v)
        if ($v -eq "") { return }
        if (-not (Test-Path $v -PathType Leaf)) { return "There's no file at $v." }
        $bytes = [IO.File]::ReadAllBytes($v)
        if ($bytes.Length -lt 8 -or $bytes[0] -ne 0x89 -or $bytes[1] -ne 0x50 -or $bytes[2] -ne 0x4E -or $bytes[3] -ne 0x47) { return "That isn't a PNG. Save the logo as .png and try again." }
        if ($bytes.Length -gt 2MB) { return "Keep the logo under 2 MB." }
    }
}

Heading "Backups"
Say "A backup is made every night. Best on another drive or a network share, so it survives this drive failing."
Say "(For a share, the computer account - $env:COMPUTERNAME`$ - needs permission to write there.)"
$backupFolder = Ask "BackupFolder" "Backup folder" (Join-Path $dataFolder "backups") {
    param($v) if (-not [IO.Path]::IsPathRooted($v) -and -not $v.StartsWith("\\")) { "Give a full path or a \\server\share path." }
}
$backupHour = [int](Ask "BackupHour" "Hour the backup runs (0-23)" "2" {
    param($v) $n = 0; if (-not [int]::TryParse($v, [ref]$n) -or $n -lt 0 -or $n -gt 23) { "A whole number from 0 to 23." }
})

Heading "How people reach it"
$certificates = @(Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } | Sort-Object NotAfter -Descending)
$useHttps = $false; $certificate = $null
if ($certificates.Count -gt 0) {
    Say "HTTPS encrypts passwords on the network. It needs a certificate for this server's name in Local Computer > Personal."
    $useHttps = AskYesNo "UseHttps" "Use HTTPS with one of this computer's certificates?" $true
} else {
    Say "This computer has no certificate to use for HTTPS, so the helpdesk will use plain HTTP."
    Warn "Passwords will cross the network unencrypted until a certificate is added. The install guide explains how."
    if ($unattended -and $answers.UseHttps) { Fail "UseHttps is true in the answers file, but this computer has no certificate with a private key." }
}
if ($useHttps) {
    if ($unattended) {
        $certificate = $certificates | Where-Object { $_.Thumbprint -eq ([string]$answers.CertificateThumbprint).Replace(" ", "") } | Select-Object -First 1
        if (-not $certificate) { Fail "The answers file's CertificateThumbprint doesn't match a certificate with a private key in Local Computer > Personal." }
    } else {
        for ($i = 0; $i -lt $certificates.Count; $i++) {
            $c = $certificates[$i]
            Say ("{0}. {1}  (expires {2:d MMM yyyy})" -f ($i + 1), $c.GetNameInfo("SimpleName", $false), $c.NotAfter)
        }
        $choice = [int](Ask "Certificate" "Which certificate" "1" {
            param($v) $n = 0; if (-not [int]::TryParse($v, [ref]$n) -or $n -lt 1 -or $n -gt $certificates.Count) { "A number from 1 to $($certificates.Count)." }
        })
        $certificate = $certificates[$choice - 1]
    }
}
$defaultPort = if ($useHttps) { 443 } else { 80 }
if (PortInUse $defaultPort) { $defaultPort = if ($useHttps) { 5443 } else { 5277 } }
$port = [int](Ask "Port" "Port" "$defaultPort" {
    param($v) $n = 0
    if (-not [int]::TryParse($v, [ref]$n) -or $n -lt 1 -or $n -gt 65535) { return "A port number from 1 to 65535." }
    if (PortInUse $n) { return "Something on this computer is already using port $n. Choose another." }
})
$openFirewall = AskYesNo "OpenFirewall" "Let other computers on the school network reach it (opens the port in Windows Firewall)?" $true
$firewallPublic = $false
if ($openFirewall -and (ConnectedNetworkCategories) -contains "Public") {
    Say "Windows has this computer's network down as Public, and on a Public network it blocks other computers unless told otherwise."
    Say "Best is to make it Private (Settings > Network & internet > the network > Private), or join the computer to the school's domain."
    $firewallPublic = AskYesNo "AllowOnPublicNetwork" "Open the port on Public networks too, so other computers can reach it now?" $true
}

$settings = [pscustomobject]@{
    DataFolder = $dataFolder; UseHttps = $useHttps; Port = $port
    CertificateSubject = $(if ($certificate) { $certificate.GetNameInfo("SimpleName", $false) } else { $null })
    CertificateName = $(if ($certificate) { $certificate.GetNameInfo("DnsName", $false) } else { $null })
    OpenFirewall = $openFirewall; FirewallPublic = $firewallPublic
}

Heading "Ready to install"
Say "Program:      $programFolder"
Say "Data:         $dataFolder$(if ($existingData) { ' (existing data kept)' } elseif ($ImportFrom) { " (copied from $ImportFrom)" })"
if (-not $keepsItsOwnSettings) { Say "School:       $schoolName   colours $primary / $accent$(if ($logo) { "   logo $logo" })" }
Say "Backups:      $backupFolder at ${backupHour}:00"
Say "Address:      $(PublicUrl $settings)$(if ($openFirewall) { "   (firewall opened$(if ($firewallPublic) { ', Public networks included' }))" })"
if (-not $unattended -and -not (AskYesNo "Confirm" "Install now?" $true)) { Fail "Nothing was installed." }

# ---- New install: doing it -----------------------------------------------------------------------------------------

Heading "Installing"
New-Item -ItemType Directory -Force -Path $dataFolder | Out-Null
if (-not $existingData -and $ImportFrom) {
    try { $lock = [IO.File]::Open((Join-Path $ImportFrom "helpdesk.db"), 'Open', 'Read', 'None'); $lock.Dispose() }
    catch { Fail "$ImportFrom\helpdesk.db is in use. Stop the helpdesk running from it, then run the installer again." }
    foreach ($item in Get-ChildItem -LiteralPath $ImportFrom -Force) {
        # helpdesk.db-wal (write-ahead log) can hold the newest changes if that copy wasn't shut down cleanly, so it
        # travels with the database; the -shm index beside it is rebuilt by SQLite and is left behind.
        if ($item.Name -eq "helpdesk.db-shm" -or $item.Name -like "*.moved-*") { continue }
        Copy-Item -LiteralPath $item.FullName -Destination $dataFolder -Recurse -Force
    }
    Say "Copied the data from $ImportFrom (the original is untouched)."
}

# Answers that belong in the database; the app applies them on its first start (Services/InstallSettings.cs).
$firstRun = [ordered]@{ BackupFolder = $backupFolder; BackupHour = $backupHour; SiteAddress = (PublicUrl $settings) }
if (-not $keepsItsOwnSettings) { $firstRun.SchoolName = $schoolName; $firstRun.PrimaryColor = $primary; $firstRun.AccentColor = $accent; $firstRun.LogoPath = $logo }
$firstRun | ConvertTo-Json | Set-Content -Path (Join-Path $dataFolder "install-settings.json") -Encoding UTF8

CopyProgram $programFolder
WriteAppSettings $programFolder $settings
WriteInstallRecord $programFolder $settings $newVersion
Say "Program copied to $programFolder."

if ($NoService) { Heading "Installed the files only (-NoService). Run $programFolder\EduHelpdesk.exe to try it."; return }

$account = "NT SERVICE\$ServiceName"
New-Service -Name $ServiceName -BinaryPathName "`"$(Join-Path $programFolder 'EduHelpdesk.exe')`"" -DisplayName "EduHelpdesk" `
    -Description "The school's IT helpdesk (EduHelpdesk $newVersion)." -StartupType Automatic | Out-Null
& sc.exe config $ServiceName obj= $account | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null
& icacls $dataFolder /grant "${account}:(OI)(CI)M" /T /Q | Out-Null
& icacls $programFolder /grant "${account}:(OI)(CI)RX" /T /Q | Out-Null
if (-not $backupFolder.StartsWith("\\") -and -not $backupFolder.StartsWith($dataFolder, [StringComparison]::OrdinalIgnoreCase)) {
    New-Item -ItemType Directory -Force -Path $backupFolder | Out-Null
    & icacls $backupFolder /grant "${account}:(OI)(CI)M" /T /Q | Out-Null
}
if ($logo) { & icacls $logo /grant "${account}:R" /Q | Out-Null }
Say "Service registered: runs as $account, starts with Windows, restarts after a failure."
if ($certificate) { GrantCertificateKey $certificate $account }
if ($openFirewall) { OpenFirewall (Join-Path $programFolder "EduHelpdesk.exe") $port $firewallPublic }

Say "Starting the service..."
Start-Service -Name $ServiceName
if (-not (WaitUntilAnswering (LocalUrl $settings))) {
    Fail "The service started but the site isn't answering. Look in $dataFolder\logs and Event Viewer > Windows Logs > Application (source EduHelpdesk). The install log is $log."
}

Heading "EduHelpdesk $newVersion is running at $(PublicUrl $settings)"
SayHowToReachIt $settings
if (-not $openFirewall) { Warn "The firewall wasn't opened, so only this computer can reach it. Run the installer again from a newer zip, or open port $port in Windows Firewall." }
if (-not $keepsItsOwnSettings) {
    Say "Sign in straight away as $BootstrapEmail with the password $BootstrapPassword - you'll be asked"
    Say "to choose your own. Until you do, anyone on the network who has read the guide could sign in first."
    Say "It starts with example tickets and assets to show how things work. Settings > Go live & reset removes them."
}
Say "Then: Settings > Sign-in security (two-step sign-in), and Settings > Backups & data to check the first backup."
Say "The install log is $log."

} finally {
    Stop-Transcript | Out-Null
}
