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

.PARAMETER DraftRelease
    After building, create a DRAFT GitHub release for this version and attach the
    artifacts. Nothing becomes public: a draft is visible only to people who can write
    to the repository until somebody presses Publish on GitHub. Requires the GitHub CLI
    and release notes at docs/release-notes/<version>.md. Without this switch the script
    uploads nothing anywhere.

.EXAMPLE
    pwsh build/package.ps1
#>

[CmdletBinding()]
param(
    [switch]$SkipInstaller,
    [switch]$KeepPublishDirectory,
    [switch]$DraftRelease
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

<#
    Runs gh purely to ask a question, and returns whether it succeeded.

    Not as simple as it looks. gh writes to stderr in the ordinary course of answering -
    "release not found" is how it says no - and in Windows PowerShell, merging a native
    command's stderr into the pipeline wraps each line in an ErrorRecord. With
    ErrorActionPreference set to Stop, that turns a perfectly normal "no" into a
    terminating error and kills the script on its happy path, which is exactly what
    happened the first time this ran.

    So stderr goes to nowhere rather than into the pipeline, and the exit code is the only
    thing consulted.
#>
function Invoke-Gh([string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'

    try {
        & gh @Arguments 1>$null 2>$null
        return $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $previous
    }
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
$DraftCreated  = $null
$commit        = $null

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
# Draft release
# ---------------------------------------------------------------------------
#
# Opt-in, and never otherwise. A packaging script that uploaded on every run would
# eventually upload something nobody meant to share, so this only happens when asked
# for by name.
#
# Even then it creates a *draft*: the release exists on GitHub, visible to people who
# can write to the repository and to nobody else, until a person presses Publish. That
# is the last point at which somebody looks at it and decides.

if ($DraftRelease) {
    Write-Step 'Creating a draft GitHub release'

    $gh = Get-Command 'gh' -ErrorAction SilentlyContinue
    if (-not $gh) {
        throw 'The GitHub CLI (gh) is not installed, so no draft release was created. ' +
              'The artifacts above are complete. Install it with: winget install --id GitHub.cli'
    }

    if (-not (Invoke-Gh @('auth', 'status'))) {
        throw 'The GitHub CLI is not signed in. Run: gh auth login'
    }

    $tag = "v$Version"

    $notes = Join-Path $RepoRoot "docs/release-notes/$Version.md"
    if (-not (Test-Path $notes)) {
        throw "No release notes at $notes. Write them before drafting a release - the notes " +
              'are the part a person actually reads, and generated ones say nothing.'
    }

    # An existing release for this tag is not something to overwrite silently. It may be
    # published already, in which case replacing its files changes what people have
    # downloaded under a name they were told was fixed.
    if (Invoke-Gh @('release', 'view', $tag)) {
        throw "A release already exists for $tag. Delete it deliberately, or raise the " +
              'version in Directory.Build.props, rather than replacing it in place.'
    }

    # Tag the commit these artifacts were actually built from. Left to itself, gh tags the
    # tip of the default branch, which is only the same thing by luck.
    $commit = (& git -C $RepoRoot rev-parse HEAD).Trim()

    $dirty = & git -C $RepoRoot status --porcelain
    if ($dirty) {
        Write-Host '    WARNING: the working tree has uncommitted changes.' -ForegroundColor Yellow
        Write-Host "    These artifacts were built from something that is not $($commit.Substring(0,7))," -ForegroundColor Yellow
        Write-Host '    so the tag will not describe what is in them.' -ForegroundColor Yellow
    }

    $assets = Get-ChildItem $DistDir -File | Sort-Object Name | ForEach-Object { $_.FullName }

    # --prerelease matters beyond the label. RepoDeck's own update checker skips
    # pre-releases unless the installed version is itself one, so a build marked this
    # way behaves correctly toward its own users.
    $prerelease = if ($Version -match '-') { '--prerelease' } else { '--latest' }

    & gh release create $tag @assets `
        --draft `
        $prerelease `
        --target $commit `
        --title "RepoDeck $Version" `
        --notes-file $notes

    if ($LASTEXITCODE -ne 0) { throw 'Creating the draft release failed.' }

    Write-Detail "Draft created: $tag at $($commit.Substring(0,7))"
    Write-Detail "$($assets.Count) file(s) attached"

    $DraftCreated = $tag
}

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
if ($DraftCreated) {
    Write-Host "    A DRAFT release exists at $DraftCreated. It is not public." -ForegroundColor Yellow
    Write-Host '    Nobody can download it until you press Publish on GitHub.' -ForegroundColor Yellow
}
else {
    Write-Host '    Nothing has been published anywhere. These are local files.' -ForegroundColor DarkGray
}
Write-Host ''
