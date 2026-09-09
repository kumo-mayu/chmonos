# Fetch only the finite set of public candidates already identified in the report.
# No login, browser profile, cookies, or asset downloads.
$ErrorActionPreference = 'Stop'
$report = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'BOOTH_ZIP_LINKING_RESEARCH.md') -Raw
$ids = @([regex]::Matches($report, '(?m)^\| VRChat_[^\r\n]+\| \[(\d+)\]') |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
# Include known dependency/related products to expose possible false matches.
$ids = @($ids + @('3087170', '4792153') | Sort-Object -Unique)
$catalog = foreach ($itemId in $ids) {
    $source = 'https://booth.pm/ja/items/' + $itemId + '.json'
    try {
        $item = Invoke-RestMethod -Uri $source -TimeoutSec 25
        $variations = foreach ($variation in $item.variations) {
            $files = @(foreach ($download in @($variation.downloadable.no_musics) + @($variation.downloadable.musics)) {
                if ($null -eq $download) { continue }
                $downloadId = [regex]::Match([string]$download.url, '/downloadables/(\d+)').Groups[1].Value
                [pscustomobject]@{Id=$downloadId; Name=$download.name; DisplaySize=$download.file_size; Url=$download.url}
            })
            [pscustomobject]@{Id=$variation.id; Name=$variation.name; Status=$variation.status; DownloadablePresent=($null -ne $variation.downloadable); Files=@($files)}
        }
        $descriptionLinks = @([regex]::Matches([string]$item.description, 'https?://[^\s<>"\[\]()]+') |
            ForEach-Object { $_.Value.TrimEnd('.', ',', '。', '、') } | Sort-Object -Unique)
        [pscustomobject]@{Id=[string]$item.id; Name=$item.name; Shop=$item.shop; Source=$source; ObservedAt=[DateTimeOffset]::UtcNow.ToString('o'); Tags=$item.tags; DescriptionLinks=$descriptionLinks; Variations=@($variations)}
        Write-Host ('Fetched ' + $itemId + ': ' + $item.name)
    } catch {
        [pscustomobject]@{Id=$itemId; Source=$source; Error=$_.Exception.Message}
    }
    Start-Sleep -Milliseconds 700
}
$outputPath = Join-Path $PSScriptRoot 'public-catalog.json'
[IO.File]::WriteAllText($outputPath, (ConvertTo-Json -InputObject @($catalog) -Depth 12))
Write-Host ('Saved ' + $catalog.Count + ' public observations to ' + $outputPath)
