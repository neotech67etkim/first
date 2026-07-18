<#
.SYNOPSIS
    Launch Navisworks against the newest NWD in a source folder, import a
    fixed viewpoint set from an XML file, and capture each one - for
    unattended daily/hourly progress-photo generation.

.DESCRIPTION
    Picks the most recently modified file in -SourceFolder matching any of
    -FilePatterns (*.nwd only by default, unlike Run-DailyViewpointExport.ps1
    which defaults to *.nwf), then starts Navisworks (Roamer.exe) with that
    file, after setting NAVIS_AUTO_EXPORT_IMAGES, NAVIS_AUTO_OUTPUT_DIR,
    NAVIS_AUTO_WAIT_SECONDS, NAVIS_AUTO_INITIAL_WAIT_SECONDS,
    NAVIS_AUTO_MIN_INITIAL_WAIT_SECONDS, and (the key difference from
    Run-DailyViewpointExport.ps1) NAVIS_AUTO_VIEWPOINTS_XML.

    With NAVIS_AUTO_VIEWPOINTS_XML set, the plugin does NOT use whatever
    viewpoints happen to already be saved in the opened document - instead
    it reads -ViewpointsXmlPath (a Navisworks "Export Viewpoints..." XML
    file) itself and builds the camera position/rotation/projection for
    each view entry directly via the API (there is no supported API to
    trigger Navisworks' own XML import, so this reads the file itself).
    Each viewpoint is then captured the same way as the normal flow (wait
    for the document to load, wait for the view to settle, wait for each
    viewpoint's render to settle), but into a run folder named "yyMMdd_HH"
    with files named plainly "<viewpoint name>_yyMMdd.png" (no diagnostic
    timing tag - see Run-DailyViewpointExport.ps1 if you want that instead).

    KNOWN LIMITATION: clip planes/sections defined in the XML are not
    reconstructed yet - a viewpoint that relies on a clip box/section in
    Navisworks will currently capture the model unclipped.

    NOTE: -SourceFolder, -OutputDir, and -ViewpointsXmlPath are required
    (no default) rather than baked into this file, since this project has
    hit real mojibake problems from non-ASCII characters saved directly in
    .ps1 source before - pass your actual (e.g. Korean-named) folder paths
    as command-line arguments instead, typed directly at the prompt.

.PARAMETER RoamerExe
    Full path to Navisworks Simulate's Roamer.exe.

.PARAMETER SourceFolder
    Folder to search for the NWD to open.

.PARAMETER FilePatterns
    Filename filters within SourceFolder (default "*.nwd" only).

.PARAMETER OutputDir
    Base folder for exported images. Each run creates its own "yyMMdd_HH"
    subfolder under here.

.PARAMETER ViewpointsXmlPath
    Path to the Navisworks viewpoints XML export to import and capture.

.PARAMETER WaitSeconds
    Seconds to wait for each viewpoint's render to settle before capturing
    (default 15). Increase for heavier models.

.PARAMETER InitialWaitSeconds
    Unconditional wait (seconds), always taken in full, after the loading
    dialog closes and before the per-viewpoint capture loop starts
    (default 300 = 5 minutes - confirmed necessary on a real file since
    referenced source data can keep refreshing in the background for
    several minutes after the dialog closes).

.PARAMETER MinInitialWaitSeconds
    Unconditional minimum wait (seconds) applied right after the document
    opens, before watching for the loading dialog at all (default 300 = 5
    minutes - there's a real gap before the dialog even appears).

.PARAMETER TimeoutMinutes
    If Navisworks hasn't exited on its own within this many minutes, the
    process is killed (default 45).

.EXAMPLE
    .\Run-ProgressViewpointExport.ps1 `
        -RoamerExe "C:\Program Files\Autodesk\Navisworks Simulate 2022\Roamer.exe" `
        -SourceFolder "M:\...\Export_NWD\..." `
        -OutputDir "M:\...\Export_NWD\...\Captured_Progress" `
        -ViewpointsXmlPath "M:\...\Captured_Progress\....xml"

.NOTES
    Register with Task Scheduler, e.g. daily at 6am (put the actual paths
    in place of the ... below):
    schtasks /Create /TN "NavisTreeExporter Progress Export" /SC DAILY /ST 06:00 /TR ^
      "powershell.exe -ExecutionPolicy Bypass -File \"C:\Tools\Run-ProgressViewpointExport.ps1\" -RoamerExe \"C:\Program Files\Autodesk\Navisworks Simulate 2022\Roamer.exe\" -SourceFolder \"...\" -OutputDir \"...\" -ViewpointsXmlPath \"...\""
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$RoamerExe,

    [Parameter(Mandatory = $true)]
    [string]$SourceFolder,

    [string[]]$FilePatterns = @("*.nwd"),

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [Parameter(Mandatory = $true)]
    [string]$ViewpointsXmlPath,

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

if (-not (Test-Path -LiteralPath $ViewpointsXmlPath)) {
    Write-Error "ViewpointsXmlPath not found: $ViewpointsXmlPath"
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
Write-Host "Viewpoints  : $ViewpointsXmlPath"
Write-Host "Output dir  : $OutputDir"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$env:NAVIS_AUTO_EXPORT_IMAGES = "1"
$env:NAVIS_AUTO_OUTPUT_DIR = $OutputDir
$env:NAVIS_AUTO_WAIT_SECONDS = "$WaitSeconds"
$env:NAVIS_AUTO_INITIAL_WAIT_SECONDS = "$InitialWaitSeconds"
$env:NAVIS_AUTO_MIN_INITIAL_WAIT_SECONDS = "$MinInitialWaitSeconds"
$env:NAVIS_AUTO_VIEWPOINTS_XML = $ViewpointsXmlPath

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
