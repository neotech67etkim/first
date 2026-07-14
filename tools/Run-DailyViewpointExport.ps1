<#
.SYNOPSIS
    Launch Navisworks against the newest matching file in a folder and run
    Export Viewpoint Images unattended (no clicks needed), for use with
    Windows Task Scheduler.

.DESCRIPTION
    Picks the most recently modified file in -SourceFolder matching any of
    -FilePatterns (*.nwf only by default - a Navisworks federated/set file
    referencing the source models; *.nwd, a published standalone file, is
    excluded by default since it's missing properties present in the .nwf),
    then starts Navisworks
    (Roamer.exe) with that file, after setting the environment variables
    the auto-mode watcher plugin reads: NAVIS_AUTO_EXPORT_IMAGES,
    NAVIS_AUTO_OUTPUT_DIR, NAVIS_AUTO_WAIT_SECONDS,
    NAVIS_AUTO_INITIAL_WAIT_SECONDS, NAVIS_AUTO_MIN_INITIAL_WAIT_SECONDS.
    The plugin waits -MinInitialWaitSeconds unconditionally right after the
    document opens (there's a real gap before Navisworks' own loading
    dialog even appears, and checking too early wrongly concludes loading
    is already done), then waits for that loading dialog to close, then
    waits -InitialWaitSeconds more (also unconditional/flat - a stability
    check was tried here too, but large models pause for several seconds
    between geometry-streaming bursts mid-load, which fooled it into
    declaring "settled" too early), then runs the whole export waiting up
    to -WaitSeconds per viewpoint for the view to settle (no interactive
    Capture prompt), writes a log plus a _COMPLETE.txt marker into a
    per-run subfolder under -OutputDir, and exits the process itself when
    done - this script just waits for that exit (or kills it after
    -TimeoutMinutes if something hangs).

    Image generation only: uploading the produced PNGs to a server is not
    handled here. A separate script/process can watch -OutputDir for new
    subfolders containing a _COMPLETE.txt marker and upload those.

.PARAMETER RoamerExe
    Full path to Navisworks Simulate's Roamer.exe.

.PARAMETER SourceFolder
    Folder to search for the file to open.

.PARAMETER FilePatterns
    Filename filters within SourceFolder (default "*.nwf" only - the
    newest file across all patterns combined is picked if you override
    this to include more than one).

.PARAMETER OutputDir
    Base folder for exported images. Each run creates its own timestamped
    subfolder under here.

.PARAMETER WaitSeconds
    Seconds to wait after moving the camera to a viewpoint before capturing
    (default 15). Increase for heavier models.

.PARAMETER InitialWaitSeconds
    Unconditional wait (seconds), always taken in full, after the loading
    dialog closes and before the per-viewpoint capture loop starts
    (default 300 = 5 minutes). Confirmed on a real .nwf that the dialog
    closing does NOT mean referenced source files have finished
    refreshing to their latest state - a plain (non-automated) open of
    the same file showed up to date geometry, but the automated capture
    showed stale/outdated geometry until this was raised to 300s.
    Increase further for very large/slow-to-refresh models.

.PARAMETER MinInitialWaitSeconds
    Unconditional minimum wait (seconds) applied right after the document
    opens, before any of the above settling detection starts (default
    300 = 5 minutes). Unlike InitialWaitSeconds this is always taken in
    full - it exists because there's a real gap between the document
    opening and Navisworks' own loading dialog actually appearing, and
    checking for that dialog too early wrongly concludes loading is
    already done. Increase if Navisworks takes a long time just to get
    the loading dialog on screen after launch.

.PARAMETER TimeoutMinutes
    If Navisworks hasn't exited on its own within this many minutes, the
    process is killed (default 45 - MinInitialWaitSeconds and
    InitialWaitSeconds alone now total 10 minutes by default, plus the
    loading-dialog wait and the per-viewpoint loop on top of that, so
    this needs real headroom).

.EXAMPLE
    .\Run-DailyViewpointExport.ps1 `
        -RoamerExe "C:\Program Files\Autodesk\Navisworks Simulate 2022\Roamer.exe" `
        -SourceFolder "C:\Incoming" `
        -OutputDir "C:\ExportedImages" `
        -WaitSeconds 10

.NOTES
    Register with Task Scheduler, e.g.:
    schtasks /Create /TN "NavisTreeExporter Daily Export" /SC DAILY /ST 02:00 /TR ^
      "powershell.exe -ExecutionPolicy Bypass -File \"C:\Tools\Run-DailyViewpointExport.ps1\" -RoamerExe \"C:\Program Files\Autodesk\Navisworks Simulate 2022\Roamer.exe\" -SourceFolder \"C:\Incoming\" -OutputDir \"C:\ExportedImages\""
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$RoamerExe,

    [Parameter(Mandatory = $true)]
    [string]$SourceFolder,

    [string[]]$FilePatterns = @("*.nwf"),

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [int]$WaitSeconds = 15,

    [int]$InitialWaitSeconds = 300,

    [int]$MinInitialWaitSeconds = 300,

    [int]$TimeoutMinutes = 45
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $RoamerExe)) {
    Write-Error "RoamerExe not found: $RoamerExe"
    exit 1
}

if (-not (Test-Path -LiteralPath $SourceFolder)) {
    Write-Error "SourceFolder not found: $SourceFolder"
    exit 1
}

$candidates = foreach ($pattern in $FilePatterns) {
    Get-ChildItem -LiteralPath $SourceFolder -Filter $pattern -File
}

$latest = $candidates | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $latest) {
    Write-Error "No file matching any of ($($FilePatterns -join ', ')) found in $SourceFolder"
    exit 1
}

Write-Host "Target file: $($latest.FullName)"
Write-Host "Output dir : $OutputDir"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$env:NAVIS_AUTO_EXPORT_IMAGES = "1"
$env:NAVIS_AUTO_OUTPUT_DIR = $OutputDir
$env:NAVIS_AUTO_WAIT_SECONDS = "$WaitSeconds"
$env:NAVIS_AUTO_INITIAL_WAIT_SECONDS = "$InitialWaitSeconds"
$env:NAVIS_AUTO_MIN_INITIAL_WAIT_SECONDS = "$MinInitialWaitSeconds"

$proc = Start-Process -FilePath $RoamerExe -ArgumentList "`"$($latest.FullName)`"" -PassThru

$exited = $proc.WaitForExit($TimeoutMinutes * 60 * 1000)

if (-not $exited) {
    Write-Warning "Navisworks did not exit within $TimeoutMinutes minute(s) - killing it."
    try { $proc.Kill() } catch {}
    exit 1
}

Write-Host "Navisworks exited with code $($proc.ExitCode)."

if ($proc.ExitCode -ne 0) {
    Write-Warning "Non-zero exit code - check $OutputDir\_auto_export_errors.log and the run subfolder's _export_log.txt."
    exit $proc.ExitCode
}

Write-Host "Done."
