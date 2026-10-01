# 確かめ用の作り物のファイルを作る道具。ui-kit.ps1 が無くても動く（アプリを相手にしないので）：
#   . "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\fixtures.ps1"
#
# なぜ要るか：大きな zip・件数の多い zip・壊れた zip・読めないフォルダを、担当ごとに作業用フォルダへ書き直していた
# （2026-09-30。同じ台本が6人分あった）。ここに1つ置き、引数で大きさと件数を変える。
#
# 決め事：
# - 置き場はリポジトリの外（%LOCALAPPDATA%\Chmonos-fixtures\<組の名前>）。本番・写し・リポジトリの中には作らない
# - 名前は全部作り物（sample-…）。BOOTH の商品 ID・実在の商品名・ショップ名を入れない
# - 中身は種から作る乱数。同じ引数なら同じ中身になる（前後の版を同じ物で比べられる）。
#   種はファイルの名前ごとに変える——同じ種だと別のファイルが同じ中身になり、アプリが「同じ中身の写し」として1行にまとめる
# - 権限を外す・消すのは、置き場の中のパスだけ（外のパスを渡すと断る）

# 別の置き場を使うときは、読み込んだ後でこの変数を書き換える（試すときに作業用フォルダへ向ける）
$ChmonosFixtureHome = Join-Path $env:LOCALAPPDATA 'Chmonos-fixtures'

# 件数の多い zip・2万件のばらのファイル・4000件の unitypackage を PowerShell の繰り返しで作ると分単位で掛かる。
# 作る所だけ C# にする。読み込むたびに組むと約1秒掛かるので、最初に作るときだけ組む
function Initialize-ChmonosFixtureType {
  if ('ChmonosFixture' -as [type]) { return }
  Add-Type -TypeDefinition @'
using System; using System.IO; using System.IO.Compression; using System.Formats.Tar; using System.Text;
public static class ChmonosFixture {
  // string.GetHashCode は起動ごとに変わるので、名前から種を出すのは自前で（FNV-1a）
  public static int SeedOf(string name, int seed) {
    unchecked { uint h = 2166136261; foreach (var c in name) { h ^= c; h *= 16777619; } return (int)(h & 0x7fffffff) ^ seed; }
  }
  static void WriteRandom(Stream s, long bytes, Random rng) {
    var buf = new byte[1 << 20];
    while (bytes > 0) { rng.NextBytes(buf); var n = (int)Math.Min(buf.Length, bytes); s.Write(buf, 0, n); bytes -= n; }
  }
  // 圧縮の効かない中身（乱数・無圧縮）。parts 個に割る。1個が 4GB を超えるか、全体が 4GB を超えると Zip64 になる
  public static void RandomZip(string path, string top, long totalBytes, int parts, int seed) {
    var rng = new Random(SeedOf(Path.GetFileName(path), seed));
    using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20);
    using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
    var per = totalBytes / parts;
    for (var i = 0; i < parts; i++) {
      var e = zip.CreateEntry(top + "/part" + (i + 1).ToString("D2") + ".bin", CompressionLevel.NoCompression);
      using var es = e.Open(); WriteRandom(es, i == parts - 1 ? totalBytes - per * (parts - 1) : per, rng);
    }
    var r = zip.CreateEntry(top + "/readme.txt", CompressionLevel.Fastest);
    using var w = new StreamWriter(r.Open(), new UTF8Encoding(false)); w.Write("sample file for testing\n");
  }
  // 小さなファイルが count 個。65,535 個を超えると Zip64 になる
  public static void ManyZip(string path, string top, int count, int seed) {
    var rng = new Random(SeedOf(Path.GetFileName(path), seed));
    using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20);
    using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
    var small = new byte[600];
    for (var i = 0; i < count; i++) {
      var e = zip.CreateEntry(top + "/Group_" + (i / 5000).ToString("D2") + "/Set_" + (i / 250).ToString("D3") + "/piece_" + i.ToString("D5") + ".png", CompressionLevel.Fastest);
      using var es = e.Open(); rng.NextBytes(small); es.Write(small, 0, 200 + i % 400);
    }
  }
  public static void Garbage(string path, long bytes, int seed) {
    using var fs = File.Create(path); WriteRandom(fs, bytes, new Random(SeedOf(Path.GetFileName(path), seed)));
  }
  // 小さなファイルを count 個、perFolder 個ずつのフォルダに分けて置く
  public static void Loose(string root, int count, int perFolder, int bytes, string ext, int seed) {
    var rng = new Random(SeedOf(Path.GetFileName(root), seed)); var buf = new byte[bytes];
    for (var i = 0; i < count; i++) {
      var f = i / perFolder;
      var dir = Path.Combine(root, "group_" + (f / 20).ToString("D2"), "folder_" + f.ToString("D3"));
      if (i % perFolder == 0) Directory.CreateDirectory(dir);
      rng.NextBytes(buf); File.WriteAllBytes(Path.Combine(dir, "file_" + (i % perFolder).ToString("D3") + ext), buf);
    }
  }
  // unitypackage：guid/asset・guid/asset.meta・guid/pathname の tar.gz。
  // 本体を名前より先に置く（読み手が、名前を知る前に本体を抱える向き。重い読み方を確かめられる）
  public static void UnityPackage(Stream output, int assets, long totalBytes, int seed) {
    var rng = new Random(seed);
    using var gz = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true);
    using var tar = new TarWriter(gz, TarEntryFormat.Ustar, leaveOpen: true);
    var kinds = new[] { new[] { "Textures", ".png" }, new[] { "Models", ".fbx" }, new[] { "Materials", ".mat" }, new[] { "Prefabs", ".prefab" }, new[] { "Animations", ".anim" } };
    var per = Math.Max(16, totalBytes / assets); var g = new byte[16];
    for (var i = 0; i < assets; i++) {
      rng.NextBytes(g); var guid = Convert.ToHexString(g).ToLowerInvariant();
      var kind = kinds[i % kinds.Length];
      tar.WriteEntry(new UstarTarEntry(TarEntryType.Directory, guid + "/"));
      // 絵と形は重く、ほかは軽く（実物の偏りに寄せる）。5種類で平均すると、おおよそ per になる
      var bodyBytes = (kind[1] == ".png" || kind[1] == ".fbx") ? per * 2 : per / 3;
      var body = new MemoryStream(); WriteRandom(body, bodyBytes, rng); body.Position = 0;
      tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, guid + "/asset") { DataStream = body });
      tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, guid + "/asset.meta") { DataStream = new MemoryStream(Encoding.UTF8.GetBytes("fileFormatVersion: 2\nguid: " + guid + "\n")) });
      tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, guid + "/pathname") { DataStream = new MemoryStream(Encoding.UTF8.GetBytes("Assets/SampleKit/" + kind[0] + "/Set_" + (i / 100).ToString("D2") + "/sample_" + i.ToString("D5") + kind[1])) });
    }
  }
  public static void UnityPackageFile(string path, int assets, long totalBytes, int seed) {
    using var fs = File.Create(path); UnityPackage(fs, assets, totalBytes, SeedOf(Path.GetFileName(path), seed));
  }
  // zip の中に unitypackage を1つ（買った物に多い形）
  public static void UnityPackageInZip(string path, string top, string inner, int assets, long totalBytes, int seed) {
    using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20);
    using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
    var e = zip.CreateEntry(top + "/" + inner, CompressionLevel.NoCompression);
    using (var es = e.Open()) UnityPackage(es, assets, totalBytes, SeedOf(Path.GetFileName(path), seed));
    var r = zip.CreateEntry(top + "/readme.txt", CompressionLevel.Fastest);
    using var w = new StreamWriter(r.Open(), new UTF8Encoding(false)); w.Write("unitypackage sample\n");
  }
  // 末尾に Zip64 の印（end of central directory locator）があるか。大きさや件数で Zip64 になったかを確かめる
  public static bool IsZip64(string path) {
    using var fs = File.OpenRead(path);
    var n = (int)Math.Min(fs.Length, 70000); var tail = new byte[n];
    fs.Seek(-n, SeekOrigin.End); fs.ReadExactly(tail, 0, n);
    for (var i = n - 4; i >= 0; i--) if (tail[i] == 0x50 && tail[i + 1] == 0x4b && tail[i + 2] == 0x06 && tail[i + 3] == 0x07) return true;
    return false;
  }
}
'@
}

# ---- 置き場 ----

# 組（1回の確かめで使う作り物のまとまり）のフォルダ。無ければ作る。名前に区切りの字は使えない（置き場の外へ出ないように）
function Get-ChmonosFixtureDir {
  param([Parameter(Mandatory)][string]$Set)
  if ($Set -notmatch '^[\w\-.]+$' -or $Set -match '^\.+$') { throw "組の名前に使えない字がある: $Set（英数字・_・-・. だけ）" }
  $dir = Join-Path $ChmonosFixtureHome $Set
  [IO.Directory]::CreateDirectory($dir) | Out-Null
  $dir
}

# 置き場の中のパスか。権限を外す・消す前に必ず通す（本番や写しのパスを渡されても何もしないように）
function Assert-ChmonosFixturePath([string]$Path) {
  $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
  $fixtureHome = [IO.Path]::GetFullPath($ChmonosFixtureHome).TrimEnd('\')
  if (-not $full.StartsWith($fixtureHome + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "作り物の置き場の外: $full（置き場は $fixtureHome）" }
  $full
}

function Resolve-ChmonosFixtureTarget([string]$Set, [string]$Name) {
  if ($Name -match '[:*?"<>|]' -or $Name -match '(^|[\\/])\.\.([\\/]|$)') { throw "名前に使えない字がある: $Name" }
  $path = Join-Path (Get-ChmonosFixtureDir $Set) $Name
  [IO.Directory]::CreateDirectory((Split-Path $path)) | Out-Null
  $path
}

function Get-ChmonosFixtureInfo([string]$Path) {
  $f = Get-Item -LiteralPath $Path
  [pscustomobject]@{ Path = $f.FullName; MB = [math]::Round($f.Length / 1MB, 1); Bytes = $f.Length }
}

# 置いてある組の一覧（片付け忘れを見つける）
function Get-ChmonosFixtures {
  if (-not (Test-Path -LiteralPath $ChmonosFixtureHome)) { return }
  foreach ($d in Get-ChildItem -LiteralPath $ChmonosFixtureHome -Directory) {
    # 権限を外したフォルダは数えられないので、読めた分だけ数える
    $files = @(Get-ChildItem -LiteralPath $d.FullName -Recurse -File -Force -ErrorAction SilentlyContinue)
    [pscustomobject]@{ Set = $d.Name; Files = $files.Count; MB = [math]::Round((($files | Measure-Object Length -Sum).Sum) / 1MB, 1); Path = $d.FullName }
  }
}

# ---- zip ----

# zip を作る。どちらかを指定する：
#   -SizeMB n … 圧縮の効かない中身（乱数・無圧縮）で、おおよそ n MB。-Parts で中のファイルの数
#              （Zip64 にするなら、全体が 4096 MB を超えるか、1個が 4096 MB を超える大きさにする）
#   -Count n  … 小さなファイル（200〜600 バイト）が n 個（Zip64 にするなら 65,536 個以上）
# 戻りはパス・大きさ・Zip64 になったか
function New-ChmonosFixtureZip {
  param([Parameter(Mandatory)][string]$Set, [string]$Name = 'sample-pack.zip', [double]$SizeMB, [int]$Count, [int]$Parts = 1, [int]$Seed = 20260930)
  if (($SizeMB -gt 0) -eq ($Count -gt 0)) { throw '-SizeMB（大きさ）か -Count（中身の件数）のどちらか1つを指定する' }
  Initialize-ChmonosFixtureType
  $path = Resolve-ChmonosFixtureTarget $Set $Name
  # 中のフォルダの名前は、zip の名前から作る（sample-pack.zip → SamplePack）
  $top = (([IO.Path]::GetFileNameWithoutExtension($Name) -split '[^A-Za-z0-9]+' | Where-Object { $_ } | ForEach-Object { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1) }) -join '')
  if (-not $top) { $top = 'Sample' }
  if ($SizeMB -gt 0) { [ChmonosFixture]::RandomZip($path, $top, [long]($SizeMB * 1MB), [Math]::Max(1, $Parts), $Seed) }
  else { [ChmonosFixture]::ManyZip($path, $top, $Count, $Seed) }
  Get-ChmonosFixtureInfo $path | Add-Member -NotePropertyName Zip64 -NotePropertyValue ([ChmonosFixture]::IsZip64($path)) -PassThru
}

# 読めない zip を作る。
#   -Kind Truncated … 正しい zip を作ってから途中で切る（後ろの目次が無い。ダウンロードが途中で止まった物）
#   -Kind Garbage   … 中身がでたらめ（zip の印も無い）
# -SizeMB は切る前の大きさ（Garbage はそのままの大きさ）、-KeepRatio は残す割合
function New-ChmonosFixtureBrokenZip {
  param([Parameter(Mandatory)][string]$Set, [string]$Name, [ValidateSet('Truncated', 'Garbage')][string]$Kind = 'Truncated',
    [double]$SizeMB = 5, [double]$KeepRatio = 0.6, [int]$Seed = 20260930)
  Initialize-ChmonosFixtureType
  if (-not $Name) { $Name = if ($Kind -eq 'Truncated') { 'sample-truncated.zip' } else { 'sample-garbage.zip' } }
  $path = Resolve-ChmonosFixtureTarget $Set $Name
  if ($Kind -eq 'Garbage') { [ChmonosFixture]::Garbage($path, [long]($SizeMB * 1MB), $Seed) }
  else {
    if ($KeepRatio -le 0 -or $KeepRatio -ge 1) { throw '-KeepRatio は 0 と 1 の間' }
    [ChmonosFixture]::RandomZip($path, 'SampleCut', [long]($SizeMB * 1MB), 1, $Seed)
    $fs = [IO.File]::Open($path, 'Open', 'ReadWrite')
    try { $fs.SetLength([long]($fs.Length * $KeepRatio)) } finally { $fs.Dispose() }
  }
  Get-ChmonosFixtureInfo $path | Add-Member -NotePropertyName Kind -NotePropertyValue $Kind -PassThru
}

# ---- ばらのファイル・unitypackage ----

# 小さなファイルを -Count 個置いたフォルダ（-PerFolder 個ずつ下のフォルダに分ける）。
# 同じ組に2つ作るときは -Name を変える（名前が種になるので、中身も別になる）
function New-ChmonosFixtureFolder {
  param([Parameter(Mandatory)][string]$Set, [string]$Name = 'loose', [Parameter(Mandatory)][int]$Count,
    [int]$PerFolder = 100, [int]$Bytes = 256, [string]$Extension = '.png', [int]$Seed = 20260930)
  Initialize-ChmonosFixtureType
  $root = Resolve-ChmonosFixtureTarget $Set $Name
  [ChmonosFixture]::Loose($root, $Count, [Math]::Max(1, $PerFolder), [Math]::Max(1, $Bytes), $Extension, $Seed)
  [pscustomobject]@{ Path = $root; Files = $Count; MB = [math]::Round($Count * $Bytes / 1MB, 1) }
}

# 作り物の unitypackage（-Assets 個・おおよそ -SizeMB）。-InZip なら zip の中に1つ入れる（名前は .zip にする）
function New-ChmonosFixtureUnityPackage {
  param([Parameter(Mandatory)][string]$Set, [string]$Name, [int]$Assets = 50, [double]$SizeMB = 5, [switch]$InZip, [int]$Seed = 20260930)
  Initialize-ChmonosFixtureType
  if (-not $Name) { $Name = if ($InZip) { 'sample-unitypackage.zip' } else { 'sample.unitypackage' } }
  $path = Resolve-ChmonosFixtureTarget $Set $Name
  if ($InZip) { [ChmonosFixture]::UnityPackageInZip($path, 'SamplePackage', 'sample.unitypackage', $Assets, [long]($SizeMB * 1MB), $Seed) }
  else { [ChmonosFixture]::UnityPackageFile($path, $Assets, [long]($SizeMB * 1MB), $Seed) }
  Get-ChmonosFixtureInfo $path | Add-Member -NotePropertyName Assets -NotePropertyValue $Assets -PassThru
}

# ---- ダウンロード元の記録（Zone.Identifier） ----

# ブラウザが付ける「どこから落としたか」の記録を付ける。アプリはここから商品を割り出す。
#   -ReferrerUrl … 落としたページ（作り物の番号で：https://booth.pm/ja/items/1234567）。
#                   zip を展開したフォルダの中のファイルには、Windows が展開元の zip のパスをここに書く
#   -HostUrl     … 落とした先
# 文字は BOM なしの UTF-8 で書く（日本語のパスを入れても読めるように。アプリの読み手は UTF-8 を先に試す）
function Set-ChmonosFixtureZone {
  param([Parameter(Mandatory)][string]$Path, [string]$HostUrl, [string]$ReferrerUrl, [int]$ZoneId = 3)
  $full = Assert-ChmonosFixturePath $Path
  if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "ファイルが無い: $full" }
  $text = "[ZoneTransfer]`r`nZoneId=$ZoneId`r`n"
  if ($ReferrerUrl) { $text += "ReferrerUrl=$ReferrerUrl`r`n" }
  if ($HostUrl) { $text += "HostUrl=$HostUrl`r`n" }
  [IO.File]::WriteAllBytes("${full}:Zone.Identifier", [Text.UTF8Encoding]::new($false).GetBytes($text))
  "記録を付けた: $full"
}

function Get-ChmonosFixtureZone {
  param([Parameter(Mandatory)][string]$Path)
  $ads = "$([IO.Path]::GetFullPath($Path)):Zone.Identifier"
  if (-not [IO.File]::Exists($ads)) { return '(記録なし)' }
  [IO.File]::ReadAllText($ads, [Text.Encoding]::UTF8)
}

# ---- 読む権限 ----

function Test-ChmonosFixtureReadable([string]$Path) {
  try {
    if (Test-Path -LiteralPath $Path -PathType Container) { [void][IO.Directory]::GetFileSystemEntries($Path) }
    else { $fs = [IO.File]::OpenRead($Path); $fs.Dispose() }
    $true
  } catch { $false }
}

# 読む権限を外す（今のユーザに「読み取りを拒否」を足す。フォルダは中の物にも及ぶ）。
# **終わったら必ず Restore-ChmonosFixtureRead か Remove-ChmonosFixtures**（外したままだと、片付けも中を消せない）
function Deny-ChmonosFixtureRead {
  param([Parameter(Mandatory)][string]$Path)
  $full = Assert-ChmonosFixturePath $Path
  if (-not (Test-Path -LiteralPath $full)) { throw "無い: $full" }
  $user = "$env:USERDOMAIN\$env:USERNAME"
  $rule = if (Test-Path -LiteralPath $full -PathType Container) { "${user}:(OI)(CI)(R)" } else { "${user}:(R)" }
  $out = icacls $full /deny $rule 2>&1
  if ($LASTEXITCODE -ne 0) { throw "権限を外せない: $full（$out）" }
  if (Test-ChmonosFixtureReadable $full) { throw "外したのに読める: $full" }
  "読めなくした: $full"
}

function Restore-ChmonosFixtureRead {
  param([Parameter(Mandatory)][string]$Path)
  $full = Assert-ChmonosFixturePath $Path
  if (-not (Test-Path -LiteralPath $full)) { throw "無い: $full" }
  $out = icacls $full /remove:d "$env:USERDOMAIN\$env:USERNAME" 2>&1
  if ($LASTEXITCODE -ne 0) { throw "権限を戻せない: $full（$out）" }
  if (-not (Test-ChmonosFixtureReadable $full)) { throw "戻したのに読めない: $full" }
  "読めるように戻した: $full"
}

# ---- 片付け ----

# 組を丸ごと消す。権限を外した物が残っていても消せるように、先に拒否を外す。
# 写しに作り物のパスの記録を残したまま消すと「見つからない」になるので、写しの側は Restore-ChmonosSandbox で戻す
function Remove-ChmonosFixtures {
  param([Parameter(Mandatory)][string]$Set)
  if ($Set -notmatch '^[\w\-.]+$' -or $Set -match '^\.+$') { throw "組の名前に使えない字がある: $Set" }
  $dir = Assert-ChmonosFixturePath (Join-Path $ChmonosFixtureHome $Set)
  if (-not (Test-Path -LiteralPath $dir)) { return "無い: $dir" }
  # 拒否を外す。上から順に外さないと、読めないフォルダの中へ入れない（/T は読めない所で止まる）ので、消えるまで繰り返す
  $user = "$env:USERDOMAIN\$env:USERNAME"
  for ($round = 0; $round -lt 8; $round++) {
    icacls $dir /remove:d $user /T /C /Q 2>&1 | Out-Null
    try { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction Stop; break } catch { }
  }
  if (Test-Path -LiteralPath $dir) { throw "消しきれない: $dir（開いているアプリが掴んでいる？）" }
  # 置き場が空になったら、置き場ごと消す（空のフォルダを残さない）
  if (-not @(Get-ChildItem -LiteralPath $ChmonosFixtureHome -Force).Count) { Remove-Item -LiteralPath $ChmonosFixtureHome -Force }
  "消した: $dir"
}
