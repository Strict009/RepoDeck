<#
.SYNOPSIS
    Describes the machine RepoDeck is about to be tested on.

.DESCRIPTION
    Run this on a clean test machine before installing RepoDeck, and paste the output into
    any report. A failure described as "it didn't start" is almost impossible to act on; the
    same failure with the OS build, the architecture, whether .NET is present and whether
    RepoDeck has left anything behind is usually enough to know where to look.

    It reads. It changes nothing, installs nothing and sends nothing anywhere.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

function Section([string]$name) {
    Write-Output ''
    Write-Output "--- $name ---"
}

Write-Output 'RepoDeck clean-machine report'
Write-Output "Generated $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"

Section 'Windows'
$os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
Write-Output "Caption:      $($os.Caption)"
Write-Output "Version:      $($os.Version)  (build $($os.BuildNumber))"
Write-Output "Architecture: $env:PROCESSOR_ARCHITECTURE"
Write-Output "Locale:       $((Get-Culture).Name)"
Write-Output "PowerShell:   $($PSVersionTable.PSVersion)"

Section 'Is this actually a clean machine?'
# The whole point of the exercise is that none of these are present. If one is, the test
# still has value but is no longer testing what it claims to test.
foreach ($tool in @('dotnet', 'git', 'gh', 'devenv', 'msbuild')) {
    $found = Get-Command $tool -ErrorAction SilentlyContinue
    if ($found) {
        Write-Output "$($tool.PadRight(8)) PRESENT  $($found.Source)"
    }
    else {
        Write-Output "$($tool.PadRight(8)) absent"
    }
}

$runtimes = & dotnet --list-runtimes 2>$null
if ($LASTEXITCODE -eq 0 -and $runtimes) {
    Write-Output ''
    Write-Output 'Installed .NET runtimes (RepoDeck should not need any of these):'
    $runtimes | ForEach-Object { Write-Output "  $_" }
}

Section 'Graphics'
# Avalonia renders through SkiaSharp, and a VM with no acceleration is a genuinely
# different environment from a desktop with a GPU.
Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue |
    ForEach-Object { Write-Output "$($_.Name)  driver $($_.DriverVersion)" }

Section 'Where the profile actually is'
# A redirected or network profile changes where RepoDeck keeps everything.
Write-Output "LOCALAPPDATA: $env:LOCALAPPDATA"
Write-Output "USERPROFILE:  $env:USERPROFILE"
Write-Output "Desktop:      $([Environment]::GetFolderPath('Desktop'))"

Section 'RepoDeck'
$program = Join-Path $env:LOCALAPPDATA 'Programs\RepoDeck'
$data    = Join-Path $env:LOCALAPPDATA 'RepoDeck'

Write-Output "Program folder: $(if (Test-Path $program) { "present - $program" } else { 'absent' })"
Write-Output "Data folder:    $(if (Test-Path $data) { "present - $data" } else { 'absent' })"

$exe = Join-Path $program 'RepoDeck.exe'
if (Test-Path $exe) {
    Write-Output "Version:        $((Get-Item $exe).VersionInfo.ProductVersion)"
}

$reg = Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
       Where-Object { $_.DisplayName -like '*RepoDeck*' }
Write-Output "Registered as:  $(if ($reg) { $reg.DisplayName } else { 'not registered' })"

Section 'Logs and crash reports'
$logs = Join-Path $data 'Logs'
if (Test-Path $logs) {
    Get-ChildItem $logs -File | Sort-Object LastWriteTime -Descending | Select-Object -First 10 |
        ForEach-Object { Write-Output ("{0}  {1,8:N0} bytes  {2}" -f $_.LastWriteTime.ToString('yyyy-MM-dd HH:mm'), $_.Length, $_.Name) }

    $crashes = Get-ChildItem $logs -Filter 'crash-*.log' -File -ErrorAction SilentlyContinue
    if ($crashes) {
        Write-Output ''
        Write-Output "$($crashes.Count) crash report(s) present. Attach the newest to your report."
    }
}
else {
    Write-Output 'No log folder yet. If RepoDeck has been launched, that is itself a finding.'
}

Write-Output ''
Write-Output '--- end ---'
Write-Output 'Nothing here was changed or sent anywhere.'

# A read-only report always succeeds. Without this the exit code reflects whatever the
# last command happened to set, which makes the script look like it failed when it did not.
exit 0
