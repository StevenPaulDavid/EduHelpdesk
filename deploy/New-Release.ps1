<#
.SYNOPSIS
    Makes the zip you share with other schools: releases\EduHelpdesk-<version>.zip.

.DESCRIPTION
    Run it from any PowerShell on the development machine (no administrator rights needed):
        .\deploy\New-Release.ps1
    It:
      1. asks for the version number (the last one plus a minor step, 1.0 -> 1.1, unless you type another),
      2. warns if there are uncommitted changes, which would go into the release,
      3. runs the tests and stops if any fail,
      4. publishes the app for 64-bit Windows with the .NET runtime included, so schools install nothing else,
      5. adds the installer, the licence, the notices and the guides (the install guide also as a PDF),
      6. checks nothing from your own install is in it - no database, keys or settings - and zips it, with a SHA-256
         checksum beside it,
      7. offers to commit the version number and tag the release in git.
    Your running copy of the helpdesk isn't touched: the build happens in releases\ and obj\, never bin\Debug.

.PARAMETER Version
    The version to make, such as 1.1 or 2.0. Asked for when left out.
.PARAMETER SkipTests
    Leave out the tests - only for trying the script itself.
.PARAMETER AllowUncommitted
    Don't stop to ask about uncommitted changes.
.PARAMETER Tag
    Whether to commit the version number and tag the release: ask (the default), yes or no.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipTests,
    [switch]$AllowUncommitted,
    [ValidateSet("ask", "yes", "no")][string]$Tag = "ask"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "EduHelpdesk.csproj"
$releases = Join-Path $root "releases"
$git = (Get-Command git -ErrorAction SilentlyContinue).Source
if (-not $git) { $git = Get-ChildItem "$env:LOCALAPPDATA\GitHubDesktop\app-*\resources\app\git\cmd\git.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName }

function Step([string]$text) { Write-Host ""; Write-Host $text -ForegroundColor Cyan }

# ---- 1. Version -----------------------------------------------------------------------------------------------------
$csproj = Get-Content $project -Raw
$current = [version]([regex]::Match($csproj, '<Version>([^<]+)</Version>').Groups[1].Value)
$released = @(Get-ChildItem $releases -Filter "EduHelpdesk-*.zip" -ErrorAction SilentlyContinue |
    ForEach-Object { [version]($_.BaseName -replace '^EduHelpdesk-', '') } | Sort-Object -Descending)
$suggested = if ($released.Count -eq 0) { "$($current.Major).$($current.Minor)" } else { "$($released[0].Major).$($released[0].Minor + 1)" }
if (-not $Version) {
    $typed = Read-Host "Version to release [$suggested]"
    $Version = if ([string]::IsNullOrWhiteSpace($typed)) { $suggested } else { $typed.Trim().TrimStart('v') }
}
if ($Version -notmatch '^\d+\.\d+(\.\d+)?$') { throw "A version is two or three numbers, such as 1.1 or 2.0." }
$semantic = [version]$(if ($Version.Split('.').Count -eq 2) { "$Version.0" } else { $Version })
$name = "EduHelpdesk-$Version"
$zip = Join-Path $releases "$name.zip"
if (Test-Path $zip) { throw "$zip already exists. Choose a new version number, or delete that zip first." }
Write-Host "Making EduHelpdesk $Version" -ForegroundColor White

# ---- 2. Uncommitted changes -----------------------------------------------------------------------------------------
if ($git) {
    $changes = & $git -C $root status --porcelain --untracked-files=no
    if ($changes -and -not $AllowUncommitted) {
        Write-Host "These changes aren't committed, and will be in the release:" -ForegroundColor Yellow
        $changes | ForEach-Object { Write-Host "  $_" }
        if ((Read-Host "Carry on anyway? (y/n) [n]") -notmatch '^(y|yes)$') { throw "Stopped. Commit (or put aside) the changes and run it again." }
    }
}

# ---- 3. Tests -------------------------------------------------------------------------------------------------------
$work = Join-Path $releases "work"
if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null
if (-not $SkipTests) {
    Step "Running the tests..."
    & dotnet test (Join-Path $root "Tests\EduHelpdesk.Tests") -o (Join-Path $work "tests") --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "The tests failed, so no release was made." }
}

# ---- 4. Publish -----------------------------------------------------------------------------------------------------
Step "Publishing for 64-bit Windows, runtime included..."
$stage = Join-Path $work $name
& dotnet publish $project -c Release -r win-x64 --self-contained true -p:Version=$semantic -o (Join-Path $stage "app") --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
# Development settings have no place on a school's server.
Remove-Item -LiteralPath (Join-Path $stage "app\appsettings.Development.json") -ErrorAction SilentlyContinue

# ---- 5. Installer, licence and guides -------------------------------------------------------------------------------
Step "Adding the installer and guides..."
Copy-Item (Join-Path $root "installer\*") $stage
Copy-Item (Join-Path $root "LICENSE.txt"), (Join-Path $root "THIRD-PARTY-NOTICES.txt") $stage
Set-Content -Path (Join-Path $stage "VERSION.txt") -Value $semantic -Encoding ASCII
# Read as UTF-8 whatever PowerShell this is: Windows PowerShell reads a file without a byte-order mark as ANSI.
$guide = [IO.File]::ReadAllText((Join-Path $root "docs\Install-Guide.md")).Replace("{{VERSION}}", $Version)
[IO.File]::WriteAllText((Join-Path $stage "INSTALL.md"), $guide)
New-Item -ItemType Directory -Force -Path (Join-Path $stage "docs") | Out-Null
Copy-Item (Join-Path $root "docs\Technical-Guide.md"), (Join-Path $root "docs\Staff-Guide.md") (Join-Path $stage "docs")
@"
EduHelpdesk $Version - an IT helpdesk for schools, by Steven Davidson.

To install, or to upgrade an existing install: unzip everything, then double-click Install.cmd.
Read INSTALL.pdf first - it takes about ten minutes.

Free to use and share under the MIT licence (LICENSE.txt), with no warranty.
"@ | Set-Content -Path (Join-Path $stage "README.txt") -Encoding UTF8
Push-Location $root
try {
    & dotnet run --file (Join-Path $PSScriptRoot "tools\MarkdownToPdf.cs") -- (Join-Path $stage "INSTALL.md") (Join-Path $stage "INSTALL.pdf") "EduHelpdesk $Version - installation guide"
    if ($LASTEXITCODE -ne 0) { throw "Making INSTALL.pdf failed." }
} finally { Pop-Location }

# ---- 6. Check, zip, checksum ----------------------------------------------------------------------------------------
Step "Checking and zipping..."
$private = Get-ChildItem $stage -Recurse -File | Where-Object {
    $_.Name -match '^(helpdesk\.db.*|appsettings\.Production\.json|install\.json|install-settings.*\.json|key-.*\.xml|.*\.log)$' -or $_.FullName -match '\\(App_Data|keys|attachments|backups)\\'
}
if ($private) { throw "Refusing to zip: these look like a real install's data or settings: $($private.FullName -join ', ')" }
if (-not (Test-Path (Join-Path $stage "app\EduHelpdesk.exe"))) { throw "The published app is missing EduHelpdesk.exe." }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256.txt" -Value "$hash  $name.zip" -Encoding ASCII
Remove-Item -LiteralPath $work -Recurse -Force

# ---- 7. Version number in git ---------------------------------------------------------------------------------------
$csproj = [regex]::Replace($csproj, '<Version>[^<]+</Version>', "<Version>$semantic</Version>")
[IO.File]::WriteAllText($project, $csproj, (New-Object Text.UTF8Encoding($false)))
$size = "{0:N1} MB" -f ((Get-Item $zip).Length / 1MB)
Step "Made $zip ($size)"
Write-Host "  SHA-256 $hash"
if ($git -and ($Tag -eq "yes" -or ($Tag -eq "ask" -and (Read-Host "Commit the version number and tag this as v$Version in git? (y/n) [y]") -notmatch '^(n|no)$'))) {
    & $git -C $root add EduHelpdesk.csproj
    & $git -C $root commit -q -m "Release $Version"
    & $git -C $root tag "v$Version"
    Write-Host "  Committed and tagged v$Version."
}
Write-Host ""
Write-Host "Share the zip (and its .sha256.txt, so a school can check it arrived intact). The guide is docs\Release-Guide.md."
