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

. (Join-Path $PSScriptRoot "Helpers.ps1")
. (Join-Path $PSScriptRoot "Certificate.ps1")

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


function CopyProgram([string]$target) {
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    # /MIR makes the folder match the package exactly; the install's own files are left out of the mirror.
    & robocopy $package $target /MIR /XF appsettings.Production.json install.json /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "Copying the program into $target failed (robocopy code $LASTEXITCODE)." }
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
        CertificateSelfSigned = [bool]$installed.CertificateSelfSigned; CertificateThumbprint = $installed.CertificateThumbprint
        CertificateFolder = $installed.CertificateFolder; CertificateStoreLocation = $installed.CertificateStoreLocation
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
# A trial run (-NoService) keeps to the current user's certificates and changes nothing for the whole computer.
$certificateStore = if ($NoService) { "CurrentUser" } else { "LocalMachine" }
$certificates = @(Get-ChildItem "Cert:\$certificateStore\My" | Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } | Sort-Object NotAfter -Descending)
$useHttps = $false; $certificate = $null
$makeCertificate = $false; $certificateNames = @()
Say "HTTPS encrypts passwords on the network. It needs a certificate for the name staff type in their browser."
if ($certificates.Count -gt 0) {
    $useHttps = AskYesNo "UseHttps" "Use HTTPS, with a certificate on this computer or one the installer makes?" $true
} else {
    Say "This computer has no certificate to use for HTTPS. The installer can make a self-signed one: it encrypts just as well as a"
    Say "bought one, but every school computer then has to be told to trust it. The installer writes the file and the steps for that."
    $useHttps = AskYesNo "UseHttps" "Use HTTPS, with a certificate the installer makes?" $false
    if (-not $useHttps) { Warn "Passwords will cross the network unencrypted until HTTPS is set up. Enable-Https.cmd, in this folder, does it later." }
}
if ($useHttps) {
    if ($unattended) {
        $thumbprint = ([string]$answers.CertificateThumbprint).Replace(" ", "")
        if ($thumbprint) {
            $certificate = $certificates | Where-Object { $_.Thumbprint -eq $thumbprint } | Select-Object -First 1
            if (-not $certificate) { Fail "The answers file's CertificateThumbprint doesn't match a certificate with a private key in Local Computer > Personal." }
        } elseif ($answers.CreateCertificate) { $makeCertificate = $true }
        else { Fail "UseHttps is true in the answers file, but there is no CertificateThumbprint and CreateCertificate isn't true." }
    } elseif ($certificates.Count -gt 0) {
        for ($i = 0; $i -lt $certificates.Count; $i++) {
            $c = $certificates[$i]
            Say ("{0}. {1}  (expires {2:d MMM yyyy}{3})" -f ($i + 1), $c.GetNameInfo("SimpleName", $false), $c.NotAfter, $(if (IsSelfSigned $c) { ", self-signed" } else { "" }))
        }
        Say ("{0}. Make a new self-signed certificate for this computer" -f ($certificates.Count + 1))
        $choice = [int](Ask "Certificate" "Which certificate" "1" {
            param($v) $n = 0; if (-not [int]::TryParse($v, [ref]$n) -or $n -lt 1 -or $n -gt ($certificates.Count + 1)) { "A number from 1 to $($certificates.Count + 1)." }
        })
        if ($choice -le $certificates.Count) { $certificate = $certificates[$choice - 1] } else { $makeCertificate = $true }
    } else { $makeCertificate = $true }
    if ($makeCertificate) {
        Say "The certificate only works for the names it is made for, so give the name staff will actually type - the one in the"
        Say "address bar, such as helpdesk.school.org.uk. This computer's own name and IP addresses are added as well."
        $certificateNames = SplitNames (Ask "CertificateName" "Name(s) staff will type, separated by commas" ((ServerNames) -join ", ") {
            param($v) CertificateNamesProblem (SplitNames $v)
        })
    }
}$defaultPort = if ($useHttps) { 443 } else { 80 }
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
    CertificateSubject = $(if ($certificate) { $certificate.GetNameInfo("SimpleName", $false) } elseif ($makeCertificate) { $certificateNames[0] } else { $null })
    CertificateName = $(if ($certificate) { CertificateHostName $certificate } elseif ($makeCertificate) { $certificateNames[0] } else { $null })
    CertificateSelfSigned = $(if ($certificate) { IsSelfSigned $certificate } else { $makeCertificate })
    CertificateThumbprint = $(if ($certificate) { $certificate.Thumbprint } else { $null })
    CertificateStoreLocation = $certificateStore; CertificateFolder = $null
    OpenFirewall = $openFirewall; FirewallPublic = $firewallPublic
}

Heading "Ready to install"
Say "Program:      $programFolder"
Say "Data:         $dataFolder$(if ($existingData) { ' (existing data kept)' } elseif ($ImportFrom) { " (copied from $ImportFrom)" })"
if (-not $keepsItsOwnSettings) { Say "School:       $schoolName   colours $primary / $accent$(if ($logo) { "   logo $logo" })" }
Say "Backups:      $backupFolder at ${backupHour}:00"
if ($useHttps) { Say "Certificate:  $(if ($makeCertificate) { "a new self-signed one for $($certificateNames -join ', '), valid 5 years" } else { "$($certificate.GetNameInfo('SimpleName', $false)), expires $($certificate.NotAfter.ToString('d MMM yyyy'))$(if ($settings.CertificateSelfSigned) { ', self-signed' })" })" }
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

$certificateFiles = $null
if ($makeCertificate) {
    Say "Making a self-signed certificate for $($certificateNames -join ', ')..."
    $addresses = @(LanAddresses)
    $certificate = NewSchoolCertificate $certificateNames $addresses $certificateStore
    $settings.CertificateThumbprint = $certificate.Thumbprint
    $settings.CertificateFolder = Join-Path $dataFolder "certificate"
    $certificateFiles = WriteCertificateFiles $certificate $settings.CertificateFolder $certificateNames $addresses (PublicUrl $settings)
    Say "Certificate files written to $($settings.CertificateFolder)."
    # The computer the helpdesk runs on trusts it too, so a browser here shows the padlock straight away.
    if (-not $NoService) { TrustOnThisComputer $certificateFiles.Cer; Say "This computer trusts it." }
}

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
if ($certificateFiles) { SayHowToTrust $certificate $certificateFiles (PublicUrl $settings) }
elseif ($settings.CertificateSelfSigned) { Warn "The certificate you chose is self-signed, so every school computer must be told to trust it or its browser will warn." }
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
