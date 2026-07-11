<#
.SYNOPSIS
    Search a NavisTreeExporter items.csv for rows matching a list of
    keywords, without loading the whole file into memory.

.DESCRIPTION
    Streams the CSV line by line (Microsoft.VisualBasic.FileIO.TextFieldParser)
    and checks each row's search column (Path by default) against every
    keyword. Works fine on multi-GB / 10M+ row files since nothing is held
    in memory except the current row and the running results.

.PARAMETER CsvPath
    Path to a *_items.csv file produced by the NavisTreeExporter plugin.

.PARAMETER KeywordsFile
    Text file with one keyword per line (UTF-8).

.PARAMETER OutputPath
    Where to write the match results CSV. Defaults to
    "<CsvPath directory>\match_result.csv".

.PARAMETER SearchColumn
    Which CSV column to search: Path (default, includes the full
    hierarchy so it also covers DisplayName) or DisplayName.

.PARAMETER ExactMatch
    Require an exact match instead of "contains" (case-insensitive either way).

.EXAMPLE
    .\Find-MatchingItems.ps1 `
        -CsvPath "C:\Users\HHI\Documents\Trion_Topsides_Grating_OC_Status_260604_20260711_114402_items.csv" `
        -KeywordsFile "C:\Users\HHI\Documents\keywords.txt"
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$CsvPath,

    [Parameter(Mandatory = $true)]
    [string]$KeywordsFile,

    [string]$OutputPath,

    [ValidateSet("Path", "DisplayName")]
    [string]$SearchColumn = "Path",

    [switch]$ExactMatch
)

if (-not (Test-Path -LiteralPath $CsvPath)) {
    Write-Host "CSV file not found: $CsvPath" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path -LiteralPath $KeywordsFile)) {
    Write-Host "Keywords file not found: $KeywordsFile" -ForegroundColor Red
    exit 1
}
if (-not $OutputPath) {
    $OutputPath = Join-Path (Split-Path -LiteralPath $CsvPath -Parent) "match_result.csv"
}

$keywords = Get-Content -LiteralPath $KeywordsFile -Encoding UTF8 |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -ne "" } |
    Select-Object -Unique

if ($keywords.Count -eq 0) {
    Write-Host "Keywords file is empty: $KeywordsFile" -ForegroundColor Red
    exit 1
}

Write-Host "Loaded $($keywords.Count) keyword(s) from: $KeywordsFile"
Write-Host "Search column: $SearchColumn (mode: $(if ($ExactMatch) { 'exact match' } else { 'contains' }))"
Write-Host "Processing CSV: $CsvPath"
Write-Host ""

# Fast pre-filter: one combined regex tested per row, so most rows are
# rejected with a single check instead of looping every keyword every row.
$escapedForRegex = $keywords | ForEach-Object { [regex]::Escape($_) }
$combinedPattern = "(" + ($escapedForRegex -join "|") + ")"
$combinedRegex = [System.Text.RegularExpressions.Regex]::new(
    $combinedPattern,
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

Add-Type -AssemblyName Microsoft.VisualBasic
$parser = New-Object Microsoft.VisualBasic.FileIO.TextFieldParser($CsvPath, [System.Text.Encoding]::UTF8)
$parser.TextFieldType = [Microsoft.VisualBasic.FileIO.FieldType]::Delimited
$parser.SetDelimiters(",")
$parser.HasFieldsEnclosedInQuotes = $true

$writer = New-Object System.IO.StreamWriter($OutputPath, $false, [System.Text.Encoding]::UTF8)
$writer.WriteLine('"Keyword","Path","DisplayName","ParentPath"')

$matchedKeywords = New-Object 'System.Collections.Generic.HashSet[string]'
$rowCount = 0
$matchCount = 0
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $headers = $parser.ReadFields()
    $pathIndex = [array]::IndexOf($headers, "Path")
    $displayNameIndex = [array]::IndexOf($headers, "DisplayName")
    $parentPathIndex = [array]::IndexOf($headers, "ParentPath")
    $searchIndex = if ($SearchColumn -eq "DisplayName") { $displayNameIndex } else { $pathIndex }

    if ($searchIndex -lt 0) {
        Write-Host "CSV has no '$SearchColumn' column. Headers: $($headers -join ', ')" -ForegroundColor Red
        exit 1
    }

    while (-not $parser.EndOfData) {
        $fields = $parser.ReadFields()
        $rowCount++

        if ($rowCount % 500000 -eq 0) {
            Write-Host ("  {0:N0} rows processed... ({1:N0} matches, {2:N1}s elapsed)" -f $rowCount, $matchCount, $stopwatch.Elapsed.TotalSeconds)
        }

        if ($fields.Count -le $searchIndex) { continue }
        $target = $fields[$searchIndex]
        if ([string]::IsNullOrEmpty($target)) { continue }
        if (-not $combinedRegex.IsMatch($target)) { continue }

        $path = if ($pathIndex -ge 0 -and $fields.Count -gt $pathIndex) { $fields[$pathIndex] } else { "" }
        $displayName = if ($displayNameIndex -ge 0 -and $fields.Count -gt $displayNameIndex) { $fields[$displayNameIndex] } else { "" }
        $parentPath = if ($parentPathIndex -ge 0 -and $fields.Count -gt $parentPathIndex) { $fields[$parentPathIndex] } else { "" }

        foreach ($keyword in $keywords) {
            $isHit = if ($ExactMatch) {
                $target.Equals($keyword, [System.StringComparison]::OrdinalIgnoreCase)
            } else {
                $target.IndexOf($keyword, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            }

            if ($isHit) {
                $matchedKeywords.Add($keyword) | Out-Null
                $writer.WriteLine('"{0}","{1}","{2}","{3}"' -f `
                    ($keyword -replace '"', '""'), `
                    ($path -replace '"', '""'), `
                    ($displayName -replace '"', '""'), `
                    ($parentPath -replace '"', '""'))
                $matchCount++
            }
        }
    }
}
finally {
    $parser.Close()
    $writer.Close()
}

$stopwatch.Stop()

Write-Host ""
Write-Host ("=== Done ({0:N1}s) ===" -f $stopwatch.Elapsed.TotalSeconds) -ForegroundColor Green
Write-Host "Total rows: $($rowCount.ToString('N0'))"
Write-Host "Matches: $($matchCount.ToString('N0'))"
Write-Host "Keywords matched: $($matchedKeywords.Count) / $($keywords.Count)"
Write-Host "Result CSV: $OutputPath"

$unmatched = $keywords | Where-Object { -not $matchedKeywords.Contains($_) }
if ($unmatched.Count -gt 0) {
    $unmatchedPath = Join-Path (Split-Path -LiteralPath $OutputPath -Parent) "unmatched_keywords.txt"
    $unmatched | Set-Content -LiteralPath $unmatchedPath -Encoding UTF8
    Write-Host "$($unmatched.Count) keyword(s) had no match -> $unmatchedPath" -ForegroundColor Yellow
}
