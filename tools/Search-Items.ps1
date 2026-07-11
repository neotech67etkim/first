<#
.SYNOPSIS
    Interactive keyword search over a NavisTreeExporter items.csv.

.DESCRIPTION
    Loads the CSV into memory once (this is the only slow step), then
    repeatedly prompts for a keyword and searches instantly against the
    in-memory data. Meant for quick ad-hoc lookups instead of maintaining a
    keywords file (see Find-MatchingItems.ps1 for batch matching a whole list
    at once).

.PARAMETER CsvPath
    Path to a *_items.csv file produced by the NavisTreeExporter plugin.

.PARAMETER SearchColumn
    Which CSV column to search: Path (default, includes the full hierarchy)
    or DisplayName.

.PARAMETER MaxResults
    How many matches to print per search (default 50). The total match
    count is always shown even if it's larger.

.EXAMPLE
    .\Search-Items.ps1 -CsvPath "C:\Users\HHI\Documents\..._items.csv"
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$CsvPath,

    [ValidateSet("Path", "DisplayName")]
    [string]$SearchColumn = "Path",

    [int]$MaxResults = 50
)

if (-not (Test-Path -LiteralPath $CsvPath)) {
    Write-Host "CSV file not found: $CsvPath" -ForegroundColor Red
    exit 1
}

Write-Host "Loading CSV into memory (one-time cost - searches after this are instant): $CsvPath"

Add-Type -AssemblyName Microsoft.VisualBasic
$parser = New-Object Microsoft.VisualBasic.FileIO.TextFieldParser($CsvPath, [System.Text.Encoding]::UTF8)
$parser.TextFieldType = [Microsoft.VisualBasic.FileIO.FieldType]::Delimited
$parser.SetDelimiters(",")
$parser.HasFieldsEnclosedInQuotes = $true

$paths = New-Object System.Collections.Generic.List[string]
$displayNames = New-Object System.Collections.Generic.List[string]
$parentPaths = New-Object System.Collections.Generic.List[string]

$rowCount = 0
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $headers = $parser.ReadFields()
    $pathIndex = [array]::IndexOf($headers, "Path")
    $displayNameIndex = [array]::IndexOf($headers, "DisplayName")
    $parentPathIndex = [array]::IndexOf($headers, "ParentPath")

    if ($pathIndex -lt 0) {
        Write-Host "CSV has no 'Path' column. Headers: $($headers -join ', ')" -ForegroundColor Red
        exit 1
    }

    while (-not $parser.EndOfData) {
        $fields = $parser.ReadFields()
        $rowCount++

        if ($rowCount % 1000000 -eq 0) {
            Write-Host ("  {0:N0} rows loaded... ({1:N1}s elapsed)" -f $rowCount, $stopwatch.Elapsed.TotalSeconds)
        }

        $paths.Add($(if ($fields.Count -gt $pathIndex) { $fields[$pathIndex] } else { "" }))
        $displayNames.Add($(if ($displayNameIndex -ge 0 -and $fields.Count -gt $displayNameIndex) { $fields[$displayNameIndex] } else { "" }))
        $parentPaths.Add($(if ($parentPathIndex -ge 0 -and $fields.Count -gt $parentPathIndex) { $fields[$parentPathIndex] } else { "" }))
    }
}
finally {
    $parser.Close()
}

$stopwatch.Stop()
Write-Host ("Loaded {0:N0} rows in {1:N1}s." -f $rowCount, $stopwatch.Elapsed.TotalSeconds) -ForegroundColor Green
Write-Host "Search column: $SearchColumn"
Write-Host "Type a keyword and press Enter. Type 'exit' to quit, 'save' to save the last results to CSV."
Write-Host ""

$searchList = if ($SearchColumn -eq "DisplayName") { $displayNames } else { $paths }
$lastMatchIndexes = New-Object System.Collections.Generic.List[int]
$lastKeyword = $null

while ($true) {
    $keyword = Read-Host "keyword"

    if ([string]::IsNullOrWhiteSpace($keyword)) { continue }
    if ($keyword -eq "exit" -or $keyword -eq "quit") { break }

    if ($keyword -eq "save") {
        if ($lastMatchIndexes.Count -eq 0) {
            Write-Host "No results to save yet - run a search first." -ForegroundColor Yellow
            continue
        }
        # Non-ASCII keyword characters become underscores here (filename only -
        # the saved CSV content itself still has the original text, written
        # via UTF8 below).
        $safeKeyword = ($lastKeyword -replace '[^a-zA-Z0-9_-]', '_')
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $outPath = Join-Path (Split-Path -LiteralPath $CsvPath -Parent) "search_${safeKeyword}_${timestamp}.csv"
        $writer = New-Object System.IO.StreamWriter($outPath, $false, [System.Text.Encoding]::UTF8)
        $writer.WriteLine('"Path","DisplayName","ParentPath"')
        foreach ($i in $lastMatchIndexes) {
            $writer.WriteLine('"{0}","{1}","{2}"' -f `
                ($paths[$i] -replace '"', '""'), `
                ($displayNames[$i] -replace '"', '""'), `
                ($parentPaths[$i] -replace '"', '""'))
        }
        $writer.Close()
        Write-Host "Saved $($lastMatchIndexes.Count) result(s) to: $outPath" -ForegroundColor Green
        continue
    }

    $searchStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $lastMatchIndexes = New-Object System.Collections.Generic.List[int]
    $lastKeyword = $keyword

    for ($i = 0; $i -lt $searchList.Count; $i++) {
        if ($searchList[$i].IndexOf($keyword, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $lastMatchIndexes.Add($i)
        }
    }
    $searchStopwatch.Stop()

    Write-Host ("Found {0:N0} match(es) in {1:N2}s" -f $lastMatchIndexes.Count, $searchStopwatch.Elapsed.TotalSeconds) -ForegroundColor Cyan

    $shown = 0
    foreach ($i in $lastMatchIndexes) {
        if ($shown -ge $MaxResults) { break }
        Write-Host "  $($paths[$i])"
        $shown++
    }
    if ($lastMatchIndexes.Count -gt $MaxResults) {
        Write-Host "  ... and $($lastMatchIndexes.Count - $MaxResults) more. Type 'save' to write all of them to a CSV."
    }
    Write-Host ""
}

Write-Host "Bye."
