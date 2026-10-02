# Certificates for HTTPS: making a self-signed one for this server, the files that let other computers trust it, and the
# checks around it. Dot-sourced by Install.ps1 and Enable-Https.ps1; it needs Say, Warn, Heading and Fail
# from Helpers.ps1 loaded first.
#
# A self-signed certificate encrypts exactly as well as a bought one. What it can't do is vouch for itself: a browser
# shows a full-page warning until the computer has been told to trust it. So making it also writes a folder holding the
# public certificate (a .cer file - never the private key), a double-click script that trusts it on one computer, and
# plain instructions for doing it across a domain with Group Policy.

# The name staff are most likely to type first, then any other the computer answers to.
function ServerNames {
    $short = $env:COMPUTERNAME.ToLowerInvariant()
    $full = $null
    try { $full = [Net.Dns]::GetHostEntry("").HostName.ToLowerInvariant() } catch { }
    if ($full -and $full.Contains(".") -and $full -ne $short) { return ,@($full, $short) }
    return ,@($short)
}

# "helpdesk.school.org.uk, helpdesk" -> two lower-case names, no repeats.
function SplitNames([string]$text) {
    # The comma stops PowerShell turning a one-name list into a bare string, whose [0] would be its first letter.
    return ,@($text -split '[,;\s]+' | Where-Object { $_ } | ForEach-Object { $_.ToLowerInvariant() } | Select-Object -Unique)
}

# An error message, or nothing when the list is a usable set of names.
function CertificateNamesProblem([string[]]$names) {
    if (-not $names -or $names.Count -eq 0) { return "Give at least one name." }
    foreach ($name in $names) {
        $address = $null
        if ([Net.IPAddress]::TryParse($name, [ref]$address)) { return "$name is an IP address. Give a name - this computer's IP addresses are added automatically." }
        if ($name -notmatch '^(?=.{1,253}$)[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$') { return "'$name' isn't a valid name: letters, digits, hyphens and dots only, with no spaces." }
    }
    return $null
}

# The name to put in the helpdesk's address for a certificate that already exists: its own name when that is a usable host
# name, otherwise the first of its alternative names (which GetNameInfo("DnsName") returns in no particular order).
function CertificateHostName($certificate) {
    $simple = $certificate.GetNameInfo("SimpleName", $false)
    if ($simple -and -not (CertificateNamesProblem @($simple.ToLowerInvariant()))) { return $simple }
    $dns = $certificate.GetNameInfo("DnsName", $false)
    if ($dns) { return $dns }
    return $simple
}

# Issued by itself, so no certificate authority stands behind it and every computer must be told to trust it.
function IsSelfSigned($certificate) { return $certificate.Subject -eq $certificate.Issuer }

# Makes the certificate in the computer's Personal store and returns it. The names go in as the certificate's subject
# alternative names, which is what browsers check (the old "common name" alone isn't enough), and so do this computer's
# addresses, so https://10.0.0.5/ works once the certificate is trusted. The private key can't be exported.
function NewSchoolCertificate([string[]]$names, [string[]]$addresses = @(), [string]$storeLocation = "LocalMachine", [int]$years = 5) {
    $alternatives = @($names | ForEach-Object { "DNS=$_" }) + @($addresses | ForEach-Object { "IPAddress=$_" })
    $serverAuthentication = "2.5.29.37={text}1.3.6.1.5.5.7.3.1"
    $alternativeNames = "2.5.29.17={text}" + ($alternatives -join "&")
    return New-SelfSignedCertificate -Subject "CN=$($names[0])" -FriendlyName "EduHelpdesk ($($names[0]))" `
        -CertStoreLocation "Cert:\$storeLocation\My" -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
        -KeyExportPolicy NonExportable -KeyUsage DigitalSignature, KeyEncipherment -NotAfter (Get-Date).AddYears($years) `
        -TextExtension @($serverAuthentication, $alternativeNames)
}

# The folder other computers need: the public certificate, a script to trust it on one computer, and instructions.
# $address is the helpdesk's own address, port included, as staff will type it.
function WriteCertificateFiles($certificate, [string]$folder, [string[]]$names, [string[]]$addresses, [string]$address) {
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $safe = $names[0] -replace '[^A-Za-z0-9.-]', '_'
    $cerName = "EduHelpdesk-$safe.cer"
    $cer = Join-Path $folder $cerName
    Export-Certificate -Cert $certificate -FilePath $cer -Type CERT -Force | Out-Null

    $trust = Join-Path $folder "Trust-This-Certificate.cmd"
    $script = @"
@echo off
rem Makes this computer trust the EduHelpdesk certificate, so its browsers stop warning about $address
rem Double-click it: it asks Windows for administrator rights first. Keep it in the same folder as $cerName.
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -WorkingDirectory '%~dp0' -Verb RunAs"
    exit /b
)
certutil -addstore -f Root "%~dp0$cerName"
if %errorlevel% neq 0 (
    echo.
    echo That did not work. Make sure $cerName is in the same folder as this file.
    pause
    exit /b 1
)
echo.
echo This computer now trusts the EduHelpdesk certificate. Close the browser and open it again.
pause
"@
    [IO.File]::WriteAllText($trust, ($script -replace "`r?`n", "`r`n"), (New-Object Text.UTF8Encoding($false)))

    $readme = Join-Path $folder "READ-ME-FIRST.txt"
    $all = @($names) + @($addresses)
    $text = @"
EduHelpdesk certificate
=======================

This folder holds the public half of the certificate that encrypts the helpdesk's web address:

    $cerName
        The certificate itself. It is safe to copy anywhere: it can't be used to read anyone's traffic.
    Trust-This-Certificate.cmd
        Makes ONE computer trust it (see "One computer" below).

Staff type this address:   $address
Valid for these names:     $($all -join ", ")
Valid until:               $($certificate.NotAfter.ToString("d MMMM yyyy"))
Fingerprint (SHA-1):       $($certificate.Thumbprint)

Why this matters
----------------
The certificate was made by the helpdesk's installer, not by an authority your computers already trust. Until a
computer is told to trust it, its browser shows a full-page "your connection isn't private" warning, and desktop
notifications from the helpdesk may be blocked. Telling computers to trust it is a one-off job.

Every computer in the school (Group Policy, on a Windows domain)
----------------------------------------------------------------
1. On a domain controller, open Group Policy Management and create a policy linked to the computers' OU.
2. Edit it: Computer Configuration > Policies > Windows Settings > Security Settings > Public Key Policies >
   Trusted Root Certification Authorities.
3. Right-click > Import, and choose $cerName.
4. The computers pick it up at their next restart, or straight away after:  gpupdate /force
   Edge and Chrome use Windows' list, so they are done. Firefox keeps its own list: set the policy
   security.enterprise_roots.enabled to true, or import the file into Firefox by hand.

Computers managed with Intune
-----------------------------
Devices > Configuration > Create > Templates > Trusted certificate. Upload $cerName, destination store
"Computer certificate store - Root", and assign it to the devices.

One computer
------------
Copy this whole folder to the computer and double-click Trust-This-Certificate.cmd. Close and reopen the browser.

Phones, tablets and Chromebooks
-------------------------------
They need the .cer file installed too (Settings > Security > Install a certificate, or your device management
tool). Until then they show the warning.

Check it worked
---------------
Open $address in the browser. A padlock with no warning means this computer trusts it. The name must be one
of those above: a different name, or an address not listed, will still warn.

When it runs out
----------------
Before $($certificate.NotAfter.ToString("d MMMM yyyy")), run Enable-Https.cmd from the EduHelpdesk install folder again and choose to make
a new certificate, then trust the new .cer file the same way. The old one can then be removed from the policy.
"@
    [IO.File]::WriteAllText($readme, ($text -replace "`r?`n", "`r`n"), (New-Object Text.UTF8Encoding($false)))
    return [pscustomobject]@{ Folder = $folder; Cer = $cer; Trust = $trust; ReadMe = $readme }
}

# So a browser on the server itself, and anything else running there, trusts it. Needs administrator rights.
function TrustOnThisComputer([string]$cerPath) {
    Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
}

# What to tell whoever is installing, once the certificate exists.
function SayHowToTrust($certificate, $files, [string]$address) {
    Heading "Next: tell the school's computers to trust the certificate"
    Say "The helpdesk is using a self-signed certificate. It encrypts as well as a bought one, but browsers show a warning"
    Say "(and may block desktop notifications) on any computer that hasn't been told to trust it."
    Say "Everything needed is in $($files.Folder):"
    Say "  - $(Split-Path $files.Cer -Leaf)   the public certificate, to push out by Group Policy or Intune"
    Say "  - Trust-This-Certificate.cmd   double-click on one computer to trust it there"
    Say "  - READ-ME-FIRST.txt   the steps, in plain words"
    Say "Staff must type  $address  - the certificate only matches the names it was made for. It is valid until $($certificate.NotAfter.ToString('d MMM yyyy'))."
}
