# Offline evidence join, not an automatic identity decision.
# Inputs are generated observations. No source ZIP is opened or modified here.
param([string]$HistoryCsv)
$ErrorActionPreference = 'Stop'
$local = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'pdf-probe-results.json') -Raw | ConvertFrom-Json).Files
$catalog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'public-catalog.json') -Raw | ConvertFrom-Json

function Normalize-Name([string]$Name) {
    # Keep version strings, punctuation and extensions: fuzzy matches need separate review.
    return $Name.Normalize([Text.NormalizationForm]::FormC).ToUpperInvariant()
}

$index = @{}
function Add-Evidence([string]$Name, [object]$Evidence) {
    if ([string]::IsNullOrWhiteSpace($Name)) { return }
    $key = Normalize-Name $Name
    if (-not $index.ContainsKey($key)) { $index[$key] = [Collections.Generic.List[object]]::new() }
    $index[$key].Add($Evidence)
}

foreach ($item in $catalog) {
    foreach ($variation in $item.Variations) {
        foreach ($file in $variation.Files) {
            if ($null -eq $file) { continue }
            Add-Evidence $file.Name ([pscustomobject]@{
                ItemId=$item.Id; VariationId=[string]$variation.Id; DownloadableId=$file.Id
                Source='public-catalog-filename'; SourceUrl=$item.Source
                DisplaySize=$file.DisplaySize; ObservedAt=$item.ObservedAt
            })
        }
    }
}

# Optional explicit export supplied by the user; never reads browser storage directly.
if ($HistoryCsv) {
    foreach ($row in (Import-Csv -LiteralPath $HistoryCsv -Encoding utf8)) {
        if ([string]$row.boothID -notmatch '^\d+$') { continue }
        Add-Evidence $row.fileName ([pscustomobject]@{
            ItemId=[string]$row.boothID; Source='assetconnect-csv-filename'
            SourceUrl=('https://booth.pm/ja/items/' + $row.boothID)
            ObservedAt=$row.timestamp
        })
    }
}

$rows = @(foreach ($file in $local) {
    $key = Normalize-Name ([IO.Path]::GetFileName($file.Path))
    $evidence = @(if ($index.ContainsKey($key)) { $index[$key].ToArray() })
    [pscustomobject]@{
        Path=$file.Path; Bytes=$file.Bytes; Sha256=$file.Sha256
        NameEvidence=$evidence; SameByteEvidence=@(); CandidateIds=@()
        Decision='unresolved'; AutomaticallyConfirmed=$false
    }
})

foreach ($group in ($rows | Group-Object Sha256)) {
    foreach ($row in $group.Group) {
        $row.SameByteEvidence = @(foreach ($peer in $group.Group) {
            if ($peer.Path -eq $row.Path -or $peer.Bytes -ne $row.Bytes) { continue }
            foreach ($evidence in $peer.NameEvidence) {
                [pscustomobject]@{ItemId=$evidence.ItemId; Source='same-sha256-as-candidate'; PeerPath=$peer.Path; InheritedSource=$evidence.Source}
            }
        })
        $row.CandidateIds = @(@($row.NameEvidence.ItemId) + @($row.SameByteEvidence.ItemId) |
            Where-Object { $_ } | Sort-Object -Unique)
        $row.Decision = if ($row.CandidateIds.Count -gt 1) { 'conflict-review' }
            elseif ($row.CandidateIds.Count -eq 1) { 'candidate-review' }
            else { 'unresolved' }
    }
}

$summary = [pscustomobject]@{
    LocalZipCount=$rows.Count
    PublicItemCount=@($catalog | Where-Object { -not $_.Error }).Count
    NameCandidateZipCount=@($rows | Where-Object { $_.NameEvidence.Count -gt 0 }).Count
    AnyCandidateZipCount=@($rows | Where-Object { $_.CandidateIds.Count -gt 0 }).Count
    ConflictZipCount=@($rows | Where-Object { $_.Decision -eq 'conflict-review' }).Count
    AutomaticallyConfirmedZipCount=0
    Note='Candidate set was selected by prior manual research; these counts are not general accuracy.'
}
$outputPath = Join-Path $PSScriptRoot 'catalog-evidence.json'
[IO.File]::WriteAllText($outputPath, (ConvertTo-Json -InputObject ([pscustomobject]@{Summary=$summary;Files=$rows}) -Depth 10))
$summary | Format-List
