<#
.SYNOPSIS
    Builds RepoDeck's distributable artifacts.

.DESCRIPTION
    Produces a self-contained win-x64 build and packages it two ways: a portable zip and,
    when the Inno Setup compiler is available, a per-user installer. Writes SHA-256
    checksums for everything it produces.

    This exists so that cutting a release is one command rather than a sequence of
    remembered ones. Nothing here publishes anything anywhere: the artifacts are left in
    dist/ for a person to look at and decide about.

    The version is read from Directory.Build.props and is never passed in. There is one
    place the version is set, and the artifacts, the installer and the running application
    all derive from it, so they cannot disagree about what they are.

.PARAMETER SkipInstaller
    Build only the portable artifact, even if the Inno Setup compiler is present.

.PARAMETER KeepPublishDirectory
    Leave the intermediate publish output in place for inspection.

.EXAMPLE
    pwsh build/package.ps1
#>

[CmdletBinding()]
param(
    [switch]$SkipInstaller,
    [switch]$KeepPublishDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot  = Split-Path -Parent $PSScriptRoot
$Project   = Join-Path $RepoRoot 'src/RepoDeck/RepoDeck.csproj'
$DistDir   = Join-Path $RepoRoot 'dist'
$Runtime   = 'win-x64'

function Write-Step([string]$text) {
    Write-Host ''
    Write-Host "==> $text" -ForegroundColor Green
}

function Write-Detail([string]$text) {
    Write-Host "    $text" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------
# Version: one source of truth
# ---------------------------------------------------------------------------

Write-Step 'Reading version'

$propsPath = Join-Path $RepoRoot 'Directory.Build.props'
if (-not (Test-Path $propsPath)) {
    throw "Cannot find Directory.Build.props at $propsPath"
}

[xml]$props = Get-Content $propsPath

# XPath rather than property access: the file has more than one PropertyGroup, and dotted
# access across an array of them is exactly the kind of thing that works until somebody
# adds a second group.
$prefixNode = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix')
$suffixNode = $props.SelectSingleNode('/Project/PropertyGroup/VersionSuffix')

$prefix = if ($prefixNode) { $prefixNode.InnerText.Trim() } else { '' }
$suffix = if ($suffixNode) { $suffixNode.InnerText.Trim() } else { '' }

if ([string]::IsNullOrWhiteSpace($prefix)) {
    throw 'Directory.Build.props does not define a VersionPrefix.'
}

$Version = if ([string]::IsNullOrWhiteSpace($suffix)) { $prefix } else { "$prefix-$suffix" }

# Windows file metadata is four numbers and cannot hold a suffix, so the installer gets
# both forms: the numeric one for the version fields, the full one for the text beside them.
$VersionNumeric = $prefix
while (($VersionNumeric -split '\.').Count -lt 4) { $VersionNumeric = "$VersionNumeric.0" }

Write-Detail "Version: $Version"
if ($Version -notmatch '-') {
    Write-Host '    NOTE: this version has no pre-release suffix, so it presents itself as a finished release.' -ForegroundColor Yellow
}

$PublishDir    = Join-Path $RepoRoot "artifacts/publish/$Runtime"
$PayloadName   = "RepoDeck-$Version-$Runtime"
$PortableName  = "RepoDeck-Portable-$Version-$Runtime.zip"
$InstallerName = "RepoDeck-Setup-$Version-$Runtime.exe"

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------

Write-Step 'Running tests'

& dotnet test (Join-Path $RepoRoot 'RepoDeck.slnx') -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw 'Tests failed. Not packaging a build that does not pass its own suite.'
}

Write-Step "Publishing $Runtime (Release, self-contained)"

if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

& dotnet publish $Project `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -o $PublishDir `
    --nologo -v q

if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$exe = Join-Path $PublishDir 'RepoDeck.exe'
if (-not (Test-Path $exe)) { throw "Publish produced no RepoDeck.exe in $PublishDir" }

$publishSize = (Get-ChildItem $PublishDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Detail ("Published {0} files, {1:N0} MB" -f `
    (Get-ChildItem $PublishDir -Recurse -File).Count, ($publishSize / 1MB))

# ---------------------------------------------------------------------------
# Refuse to package things that must never be distributed
# ---------------------------------------------------------------------------

Write-Step 'Checking the payload'

$forbidden = @(
    @{ Pattern = '*.pdb';          Why = 'debug symbols' },
    @{ Pattern = '*.cs';           Why = 'source code' },
    @{ Pattern = '*.csproj';       Why = 'project files' },
    @{ Pattern = '*Tests*';        Why = 'test assemblies' },
    @{ Pattern = 'xunit*';         Why = 'test framework' },
    @{ Pattern = 'coverlet*';      Why = 'coverage tooling' },
    @{ Pattern = '*.token';        Why = 'credentials' },
    @{ Pattern = 'secrets.json';   Why = 'credentials' },
    @{ Pattern = 'appsettings.Local.json'; Why = 'local configuration' }
)

$problems = @()
foreach ($rule in $forbidden) {
    $hits = Get-ChildItem $PublishDir -Recurse -File -Filter $rule.Pattern -ErrorAction SilentlyContinue
    foreach ($hit in $hits) {
        $problems += "$($hit.Name) ($($rule.Why))"
    }
}

# A build machine's own paths must not travel inside the shipped configuration files.
foreach ($name in @('RepoDeck.deps.json', 'RepoDeck.runtimeconfig.json')) {
    $path = Join-Path $PublishDir $name
    if (Test-Path $path) {
        $text = Get-Content $path -Raw
        if ($text -match '[A-Za-z]:\\\\') {
            $problems += "$name contains an absolute path from this machine"
        }
    }
}

if ($problems.Count -gt 0) {
    Write-Host '    Refusing to package. The publish output contains:' -ForegroundColor Red
    $problems | Select-Object -Unique | ForEach-Object { Write-Host "      - $_" -ForegroundColor Red }
    throw 'Payload check failed.'
}

Write-Detail 'No symbols, sources, tests, credentials or machine paths in the payload.'

# ---------------------------------------------------------------------------
# Portable
# ---------------------------------------------------------------------------

Write-Step 'Building the portable archive'

if (Test-Path $DistDir) {
    try {
        Remove-Item $DistDir -Recurse -Force -ErrorAction Stop
    }
    catch {
        # An artifact from a previous run being held open is a normal thing to hit -
        # an installer still running, a zip open in Explorer - and deserves a sentence
        # rather than a raw access-denied trace.
        throw "Could not clear $DistDir. Something is holding a file open in there - " +
              "a running installer, or an archive open in another window. Close it and run this again."
    }
}
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

# Stage under a named folder so extracting the zip does not spray 200-odd files into
# whatever directory somebody happened to be in.
$stage = Join-Path $RepoRoot "artifacts/stage/$PayloadName"
if (Test-Path (Split-Path $stage)) { Remove-Item (Split-Path $stage) -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item "$PublishDir/*" $stage -Recurse -Force

$portablePath = Join-Path $DistDir $PortableName
Compress-Archive -Path $stage -DestinationPath $portablePath -CompressionLevel Optimal

Write-Detail ("$PortableName  ({0:N1} MB)" -f ((Get-Item $portablePath).Length / 1MB))

# ---------------------------------------------------------------------------
# Installer
# ---------------------------------------------------------------------------

$installerPath = $null

if ($SkipInstaller) {
    Write-Step 'Skipping the installer (asked not to build it)'
}
else {
    Write-Step 'Building the installer'

    # Normalised to a plain path: Get-Command hands back a CommandInfo and Get-Item a
    # FileInfo, and they do not name the path the same way.
    $isccPath = $null

    $found = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($found) { $isccPath = $found.Source }

    if (-not $isccPath) {
        # The per-user location first: winget installs Inno Setup per-user by default, and
        # that is where it lands on a machine where nobody ran anything as administrator.
        foreach ($candidate in @(
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles}\Inno Setup 6\ISCC.exe")) {
            if ($candidate -and (Test-Path $candidate)) {
                $isccPath = $candidate
                break
            }
        }
    }

    if (-not $isccPath) {
        # A missing build tool must not cost somebody the portable artifact they already
        # have, so this is a warning rather than a failure.
        Write-Host '    Inno Setup is not installed, so no installer was built.' -ForegroundColor Yellow
        Write-Host '    The portable archive above is complete and usable.' -ForegroundColor Yellow
        Write-Host '    To build installers on this machine:' -ForegroundColor Yellow
        Write-Host '      winget install --id JRSoftware.InnoSetup --source winget' -ForegroundColor Yellow
    }
    else {
        $script = Join-Path $PSScriptRoot 'RepoDeck.iss'

        & $isccPath `
            "/DAppVersion=$Version" `
            "/DAppVersionNumeric=$VersionNumeric" `
            "/DPayloadDir=$PublishDir" `
            "/DOutputDir=$DistDir" `
            "/DOutputBaseName=$([IO.Path]::GetFileNameWithoutExtension($InstallerName))" `
            $script

        if ($LASTEXITCODE -ne 0) { throw 'The installer compiler failed.' }

        $installerPath = Join-Path $DistDir $InstallerName
        if (-not (Test-Path $installerPath)) { throw "Expected $InstallerName in $DistDir" }

        Write-Detail ("$InstallerName  ({0:N1} MB)" -f ((Get-Item $installerPath).Length / 1MB))
    }
}

# ---------------------------------------------------------------------------
# Checksums
# ---------------------------------------------------------------------------

Write-Step 'Writing SHA-256 checksums'

$checksumFile = Join-Path $DistDir "RepoDeck-$Version-$Runtime.sha256"
$lines = @()

foreach ($artifact in (Get-ChildItem $DistDir -File | Where-Object { $_.Extension -ne '.sha256' } | Sort-Object Name)) {
    $hash = (Get-FileHash $artifact.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    # The sha256sum format, so `sha256sum -c` can check it without anything being rewritten.
    $lines += "$hash *$($artifact.Name)"
    Write-Detail "$hash  $($artifact.Name)"
}

# Written with LF endings and no BOM. The sha256sum format exists so that the file can be
# checked by `sha256sum -c` on any machine, and CRLF makes every line fail to match.
[IO.File]::WriteAllText($checksumFile, ($lines -join "`n") + "`n", (New-Object Text.UTF8Encoding $false))

# ---------------------------------------------------------------------------

if (-not $KeepPublishDirectory) {
    Remove-Item (Join-Path $RepoRoot 'artifacts') -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Step "Done - RepoDeck $Version"
Write-Host ''
Get-ChildItem $DistDir -File | Sort-Object Name | ForEach-Object {
    Write-Host ("    {0,-52} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB))
}
Write-Host ''
Write-Host "    in $DistDir"
Write-Host ''
Write-Host '    Nothing has been published anywhere. These are local files.' -ForegroundColor DarkGray
Write-Host ''
