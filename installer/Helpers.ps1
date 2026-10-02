# Functions shared by Install.ps1 and Enable-Https.ps1: saying things, the address people use, the settings files, the
# firewall rule and the certificate key. Dot-sourced, not run on its own.

function Heading([string]$text) { Write-Host ""; Write-Host $text -ForegroundColor Cyan }
function Say([string]$text) { Write-Host "  $text" }
function Warn([string]$text) { Write-Host "  ! $text" -ForegroundColor Yellow }
function Fail([string]$text) { Write-Host ""; Write-Host "  $text" -ForegroundColor Red; throw "Install stopped: $text" }

function PortInUse([int]$port) {
    return $null -ne (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
}

function WaitUntilAnswering([string]$url) {
    # The certificate may not match "localhost", or may be the school's own; this only checks the site is up.
    # Windows PowerShell 5.1 can't use a script block for this: the callback runs on a thread with no PowerShell in it,
    # the handshake dies, and "the site isn't answering" is reported for every HTTPS install. A compiled one works.
    $skipCertificateCheck = $PSVersionTable.PSVersion.Major -ge 6
    if (-not $skipCertificateCheck) {
        if (-not ("EduHelpdeskInstaller.AcceptAnyCertificate" -as [type])) {
            Add-Type -TypeDefinition @"
namespace EduHelpdeskInstaller {
    public static class AcceptAnyCertificate {
        public static readonly System.Net.Security.RemoteCertificateValidationCallback Callback = (sender, certificate, chain, errors) => true;
    }
}
"@
        }
        [Net.ServicePointManager]::ServerCertificateValidationCallback = [EduHelpdeskInstaller.AcceptAnyCertificate]::Callback
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    for ($i = 0; $i -lt 30; $i++) {
        try {
            $response = if ($skipCertificateCheck) { Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5 -SkipCertificateCheck } else { Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5 }
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

function WriteAppSettings([string]$programFolder, $settings) {
    $endpoint = [ordered]@{ Url = $(if ($settings.UseHttps) { "https://*:$($settings.Port)" } else { "http://*:$($settings.Port)" }) }
    # AllowInvalid is for a certificate no authority vouches for - one the installer made, or the school's own. Kestrel
    # otherwise refuses to load it, because it only takes a certificate whose chain it can verify; the browsers still
    # check it, so this relaxes nothing for the people using the helpdesk.
    $location = if ($settings.CertificateStoreLocation) { [string]$settings.CertificateStoreLocation } else { "LocalMachine" }
    if ($settings.UseHttps) { $endpoint.Certificate = [ordered]@{ Subject = $settings.CertificateSubject; Store = "My"; Location = $location; AllowInvalid = [bool]$settings.CertificateSelfSigned } }
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
        CertificateSelfSigned = [bool]$settings.CertificateSelfSigned
        CertificateThumbprint = $settings.CertificateThumbprint
        CertificateFolder = $settings.CertificateFolder
        CertificateStoreLocation = $(if ($settings.CertificateStoreLocation) { [string]$settings.CertificateStoreLocation } else { "LocalMachine" })
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
