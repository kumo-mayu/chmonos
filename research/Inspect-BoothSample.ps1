# Read-only research probe. Does not extract archives or modify source files.
# Requires a PowerShell host on .NET 9+ so ZIP UTF-8 flags are respected.
# It also uses the existing built inspector DLL.
param([switch]$IncludeUnityPackages)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot '../BoothZipInspector/bin/Debug/net10.0/BoothZipInspector.dll')
[System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)

function Read-SmallEntry($Stream, [long]$Length) {
    if ($Length -gt 2MB) { return $null }
    $buffer = [byte[]]::new([int]$Length)
    $offset = 0
    while ($offset -lt $buffer.Length) {
        $count = $Stream.Read($buffer, $offset, $buffer.Length - $offset)
        if ($count -eq 0) { throw 'Unexpected end of entry' }
        $offset += $count
    }
    return [BoothZipInspector.TextDecoder]::Decode($buffer)
}

$roots = @(Get-ChildItem -LiteralPath 'D:/storage' -Directory |
    Where-Object { $_.Name.StartsWith('VRChat_') -and -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) })
$results = foreach ($root in $roots) {
    foreach ($file in (Get-ChildItem -LiteralPath $root.FullName -Recurse -File -Filter '*.zip' |
        Where-Object { -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) })) {
        $zone = [BoothZipInspector.ZoneIdentifierReader]::Read($file.FullName)
        $inspection = [BoothZipInspector.ZipInspector]::Inspect($file.FullName)
        $deepClues = [Collections.Generic.List[object]]::new()
        $packagePaths = [Collections.Generic.List[string]]::new()
        $errors = [Collections.Generic.List[string]]::new()
        $archive = [IO.Compression.ZipFile]::Open($file.FullName, [IO.Compression.ZipArchiveMode]::Read, [Text.Encoding]::GetEncoding(932))
        try {
            foreach ($entry in $archive.Entries) {
                if ($entry.Name -match '\.pdf$' -and $entry.Length -le 2MB) {
                    $stream = $entry.Open()
                    try {
                        # Detect visible URI strings only; not a general PDF text extractor.
                        $body = Read-SmallEntry $stream $entry.Length
                        foreach ($clue in [BoothZipInspector.BoothUrlExtractor]::ExtractFromText($body, $entry.FullName)) {
                            $deepClues.Add($clue)
                        }
                    } finally { $stream.Dispose() }
                }
                if (-not $IncludeUnityPackages -or $entry.Name -notmatch '\.unitypackage$') { continue }
                $stream = $entry.Open()
                $gzip = [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionMode]::Decompress)
                $tar = [System.Formats.Tar.TarReader]::new($gzip)
                try {
                    $names = @{}
                    $hits = [Collections.Generic.List[object]]::new()
                    $count = 0
                    $total = 0L
                    $readBudget = 32MB
                    while ($null -ne ($part = $tar.GetNextEntry())) {
                        $count++
                        $total += $part.Length
                        if ($count -gt 50000 -or $total -gt 2GB) { throw 'Package scan budget exceeded' }
                        if ($null -eq $part.DataStream -or $part.Length -gt 2MB) { continue }
                        $key = ($part.Name -split '/')[0]
                        if ($part.Name -match '/pathname$') {
                            $body = Read-SmallEntry $part.DataStream $part.Length
                            $names[$key] = $body.Trim([char]0).Trim()
                        } elseif ($part.Name -match '/asset$' -and $part.Length -le $readBudget) {
                            $readBudget -= $part.Length
                            $body = Read-SmallEntry $part.DataStream $part.Length
                            foreach ($clue in [BoothZipInspector.BoothUrlExtractor]::ExtractFromText($body, $part.Name)) {
                                $hits.Add([pscustomobject]@{Key=$key; Clue=$clue})
                            }
                        }
                    }
                    foreach ($name in $names.Values) { $packagePaths.Add($entry.FullName + '::' + $name) }
                    foreach ($hit in $hits) {
                        $deepClues.Add([pscustomobject]@{Url=$hit.Clue.Url; ItemId=$hit.Clue.ItemId; SourcePath=($entry.FullName + '::' + $names[$hit.Key])})
                    }
                    if ($readBudget -le 0) { $errors.Add($entry.FullName + ': asset text budget exhausted') }
                } catch { $errors.Add($entry.FullName + ': ' + $_.Exception.Message) }
                finally { $tar.Dispose(); $gzip.Dispose(); $stream.Dispose() }
            }
        } finally { $archive.Dispose() }
        [pscustomobject]@{
            Path=$file.FullName
            Bytes=$file.Length
            ZoneFound=$zone.Found
            ZoneItemId=$zone.BoothItemId
            # Do not export signed URL query strings.
            Host=$(if($zone.HostUrl) { ([uri]$zone.HostUrl.Trim([char]0)).GetLeftPart([UriPartial]::Path) })
            Entries=$inspection.Summary.EntryCount
            TextClues=@($inspection.Clues)
            DeepClues=@($deepClues.ToArray())
            AssetPaths=@($packagePaths.ToArray() | Sort-Object -Unique)
            Errors=@($errors.ToArray())
        }
    }
}
$results | ConvertTo-Json -Depth 8
