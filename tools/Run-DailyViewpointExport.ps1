<#
.SYNOPSIS
    Launch Navisworks against the newest matching file in a folder and run
    Export Viewpoint Images unattended (no clicks needed), for use with
    Windows Task Scheduler.

.DESCRIPTION
    Picks the most recently modified file in -NwdFolder matching -NwdPattern,
    then starts Navisworks (Roamer.exe) with that file, after setting the
    environment variables the ExportViewpointImagesAddin plugin's auto mode
    reads: NAVIS_AUTO_EXPORT_IMAGES, NAVIS_AUTO_OUTPUT_DIR,
    NAVIS_AUTO_WAIT_SECONDS. The plugin then runs the whole export with a
    fixed wait per viewpoint (no interactive Capture prompt), writes a log
    plus a _COMPLETE.txt marker into a per-run subfolder under -OutputDir,
    and exits the process itself when done - this script just waits for
    that exit (or kills it after -TimeoutMinutes if something hangs).

    Image generation only: uploading the produced PNGs to a server is not
    handled here. A separate script/process can watch -OutputDir for new
    subfolders containing a _COMPLETE.txt marker and upload those.

.PARAMETER RoamerExe
    Full path to Navisworks Simulate's Roamer.exe.

.PARAMETER NwdFolder
    Folder to search for the file to open.

.PARAMETER NwdPattern
    Filename filter within NwdFolder (default "*.nwd").

.PARAMETER OutputDir
    Base folder for exported images. Each run creates its own timestamped
    subfolder under here.

.PARAMETER WaitSeconds
    Seconds to wait after moving the camera to a viewpoint before capturing
    (default 8). Increase for heavier models.

.PARAMETER TimeoutMinutes
    If Navisworks hasn't exited on its own within this many minutes, the
    process is killed (default 20).

.EXAMPLE
    .\Run-DailyViewpointExport.ps1 `
        -RoamerExe "C:\Program Files\Autodesk\Navisworks Simulate 2022\Roamer.exe" `
        -NwdFolder "C:\Incoming" `
        -OutputDir "C:\ExportedImages" `
        -WaitSeconds 10

.NOTES
    Register with Task Scheduler, e.g.:
    schtasks /Create /TN "NavisTreeExporter Daily Export" /SC DAILY /ST 02:00 /TR ^
      "powershell.exe -ExecutionPolicy Bypass -File \"C:\Tools\Run-DailyViewpointExport.ps1\" -RoamerExe \"C:\Program Files\Autodesk\Navisworks Simulate 2022\Roamer.exe\" -NwdFolder \"C:\Incoming\" -OutputDir \"C:\ExportedImages\""
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$RoamerExe,

    [Parameter(Mandatory = $true)]
    [string]$NwdFolder,

    [string]$NwdPattern = "*.nwd",

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [int]$WaitSeconds = 8,

    [int]$TimeoutMinutes = 20
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $RoamerExe)) {
    Write-Error "RoamerExe not found: $RoamerExe"
    exit 1
}

if (-not (Test-Path -LiteralPath $NwdFolder)) {
    Write-Error "NwdFolder not found: $NwdFolder"
    exit 1
}

$latest = Get-ChildItem -LiteralPath $NwdFolder -Filter $NwdPattern -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $latest) {
    Write-Error "No file matching '$NwdPattern' found in $NwdFolder"
    exit 1
}

Write-Host "Target file: $($latest.FullName)"
Write-Host "Output dir : $OutputDir"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$env:NAVIS_AUTO_EXPORT_IMAGES = "1"
$env:NAVIS_AUTO_OUTPUT_DIR = $OutputDir
$env:NAVIS_AUTO_WAIT_SECONDS = "$WaitSeconds"

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
