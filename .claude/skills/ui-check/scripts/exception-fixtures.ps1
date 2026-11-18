# 画面で確かめが要る例外の状態を、今ある写しに作り物で足す。
#
# 使い方（アプリを閉じてから。書き込む前に控えを取る）：
#   . "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\exception-fixtures.ps1"
#   Backup-ChmonosSandbox -Store daily-ui -Name before-exceptions   # 控え（ui-kit）
#   Add-ChmonosExceptionFixtures -Store daily-ui
#   Add-ChmonosExceptionFixtures -Store mycheck -Set mycheck-exceptions -OfflineLetter T -TableLetter U -SerialLetter V
#
# 引数：
#   -Store         写しの短い名前かフルパス（本番・ユーザが普段開く保存先・friendtest は断る）
#   -Set           作り物のファイルの組の名前（既定は「<写しの名前>-exceptions」。置き場は %LOCALAPPDATA%\Chmonos-fixtures\<組>）
#   -OfflineLetter 取り外しているドライブにする文字（既定 Q）
#   -TableLetter   volumes.json の表で今の文字へ読み替える文字（既定 R）
#   -SerialLetter  記録の場所ごとの通し番号で読み替える文字（既定 S）。3つとも今つながっていない文字にする
#
# 足す物（中身は tools/SandboxGen/ExceptionFixtures.cs。sandboxes.md の daily-ui の行に一覧）：
#   商品 14 件（9010万番台の作り物 13 件＋BOOTH に無い商品 1 件）・未確定 13 件・未確定の自動検索の控え 8 件・volumes.json の2行
#
# なぜ取り込み（SandboxGen の scan）でなく、この形か：取り込みの周回は写しの全商品を見回って記録を書き直す。
# ユーザの普段のデータの写しでは、元の商品の記録に触らずに足すだけにしたい。「外した」「古い版」「無いドライブ」は取り込みでは作れない。
#
# 2回流しても増えない（作り物のファイルが既にあればそのまま使い、記録は無い物だけを足す。zip は作るたびにハッシュが変わるので作り直さない）。
# 片付け：写しは Restore-ChmonosSandbox で控えへ戻し、ファイルは Remove-ChmonosFixtures -Set <組>（読めなくしたフォルダの権限も戻して消す）。
#
# BOOTH へは行かない：作り物の番号は BOOTH に無く、予定日は 2099 年、絵の URL もショップのサブドメインも持たない。
# 足した未確定には自動検索の控え（候補0件）を付けるので、設定「未確定の候補を先に検索しておく」が入っていても検索しない。
# ただし画面で「自動検索」「情報を確認」「BOOTHから取り直す」を押すと、作り物の番号やファイル名で BOOTH へ問い合わせる。

if (-not (Get-Command Resolve-ChmonosStore -ErrorAction SilentlyContinue)) { . (Join-Path $PSScriptRoot 'ui-kit.ps1') }
if (-not (Get-Command New-ChmonosFixtureZip -ErrorAction SilentlyContinue)) { . (Join-Path $PSScriptRoot 'fixtures.ps1') }
if (-not (Get-Command Build-ChmonosSandboxGen -ErrorAction SilentlyContinue)) { . (Join-Path $PSScriptRoot 'sandbox-recipes.ps1') }

# 作り物のファイルを置く（無い組にだけ。Add-ChmonosExceptionFixtures が呼ぶ）
function New-ChmonosExceptionFiles {
  param([Parameter(Mandatory)][string]$Set)
  $fx = Get-ChmonosFixtureDir $Set
  # 商品の手元のファイル。名前ごとに中身が変わる（fixtures.ps1 の種）。同じ中身が2か所の物だけ写す
  [void](New-ChmonosFixtureUnityPackage -Set $Set -Name '手元\sample-rows-ok.zip' -InZip -Assets 20 -SizeMB 1)
  foreach ($n in 'sample-rows-dup', 'sample-rows-detached', 'sample-rows-update', 'sample-rows-dup2', 'sample-remap-table', 'sample-remap-serial',
    'sample-detached-only', 'sample-delisted', 'sample-shared', 'sample-unpublished', 'sample-local-only') {
    [void](New-ChmonosFixtureZip -Set $Set -Name "手元\$n.zip" -SizeMB 1)
  }
  [void](New-Item -ItemType Directory -Force (Join-Path $fx '写し'))
  Copy-Item -LiteralPath (Join-Path $fx '手元\sample-rows-dup.zip') -Destination (Join-Path $fx '写し\sample-rows-dup.zip')
  [void](New-ChmonosFixtureBrokenZip -Set $Set -Name '手元\sample-rows-broken.zip' -Kind Truncated -SizeMB 2)
  [void](New-ChmonosFixtureBrokenZip -Set $Set -Name '手元\sample-broken-only.zip' -Kind Truncated -SizeMB 2)
  [void](New-ChmonosFixtureZip -Set $Set -Name '読めない\sample-rows-locked.zip' -SizeMB 1)
  [void](New-ChmonosFixtureZip -Set $Set -Name '読めない\sample-locked-only.zip' -SizeMB 1)
  [void](New-ChmonosFixtureFolder -Set $Set -Name '手元\SampleRemapFolder' -Count 4 -PerFolder 4 -Bytes 20000)
  # zip の隣に同じ名前のフォルダ（展開したフォルダ）
  [void](New-ChmonosFixtureZip -Set $Set -Name '展開あり\sample-unpacked.zip' -SizeMB 1)
  [void](New-ChmonosFixtureFolder -Set $Set -Name '展開あり\sample-unpacked' -Count 3 -PerFolder 3 -Bytes 20000)
  # フォルダごと登録する物（中に unitypackage と zip）
  [void](New-ChmonosFixtureUnityPackage -Set $Set -Name 'フォルダ登録\SampleFolderItem\sample-in-folder.unitypackage' -Assets 20 -SizeMB 1)
  [void](New-ChmonosFixtureZip -Set $Set -Name 'フォルダ登録\SampleFolderItem\sample-in-folder.zip' -SizeMB 1)
  [IO.File]::WriteAllText((Join-Path $fx 'フォルダ登録\SampleFolderItem\readme.txt'), "sample folder item`n", [Text.UTF8Encoding]::new($false))

  # 未確定
  [void](New-ChmonosFixtureBrokenZip -Set $Set -Name '未確定\sample-unres-broken.zip' -Kind Garbage -SizeMB 1)
  foreach ($n in 'sample-unres-private', 'sample-bundle-a', 'sample-unres-samepath', 'sample-unres-twoplaces', 'sample-unres-candidate') {
    [void](New-ChmonosFixtureZip -Set $Set -Name "未確定\$n.zip" -SizeMB 1)
  }
  Copy-Item -LiteralPath (Join-Path $fx '未確定\sample-unres-twoplaces.zip') -Destination (Join-Path $fx '写し\sample-unres-twoplaces.zip')
  # 束：zip を展開したフォルダ（Windows が付ける展開元の記録つき）。A は元の zip が一覧にある、B は元の zip がもう無い
  $bundles = @{ 'SampleBundleA' = (Join-Path $fx '未確定\sample-bundle-a.zip'); 'SampleBundleB' = (Join-Path $fx '消えた\sample-bundle-b.zip') }
  foreach ($b in $bundles.Keys) {
    $dir = (New-ChmonosFixtureFolder -Set $Set -Name "未確定\$b" -Count 3 -PerFolder 3 -Bytes 20000).Path
    foreach ($f in Get-ChildItem -LiteralPath $dir -Recurse -File) { [void](Set-ChmonosFixtureZone -Path $f.FullName -ReferrerUrl $bundles[$b]) }
  }
}

function Add-ChmonosExceptionFixtures {
  param([Parameter(Mandatory)][string]$Store, [string]$Set,
    [string]$OfflineLetter = 'Q', [string]$TableLetter = 'R', [string]$SerialLetter = 'S')
  $root = Resolve-ChmonosStore $Store
  Assert-ChmonosSandbox $root
  if (-not (Test-Path -LiteralPath (Join-Path $root 'settings.json'))) { throw "写しが無い: $root" }
  if (-not $Set) { $Set = "$(Split-Path $root -Leaf)-exceptions" }
  foreach ($l in $OfflineLetter, $TableLetter, $SerialLetter) {
    if (Test-Path -LiteralPath "${l}:\") { throw "${l}: は今つながっている。別の文字を -OfflineLetter・-TableLetter・-SerialLetter で渡す" }
  }

  # 作り物が既にあれば作り直さずに使う。zip は中の日時が作った時刻になるので、作り直すとハッシュが変わり、
  # 写しの記録が古いハッシュのまま残って、未確定と仮IDの商品が二重にできた（2026-11-18 に2回目で踏んだ）。
  # 作り直したいときは、写しを控えへ戻し（Restore-ChmonosSandbox）、Remove-ChmonosFixtures -Set <組> で消してから流す
  $fx = Join-Path $ChmonosFixtureHome $Set
  $locked = Join-Path $fx '読めない'
  if (Test-Path -LiteralPath $fx) {
    # 読めなくしたフォルダは、ハッシュを取る間だけ戻す
    if (Test-Path -LiteralPath $locked) { [void](Restore-ChmonosFixtureRead -Path $locked) }
  }
  else {
    # 作り物が無いのに写しに記録だけ残っていると、作り直したファイルのハッシュで二重に足してしまう
    if (Test-Path -LiteralPath (Join-Path $root 'items\90100001.json')) { throw "写しに前の作り物の記録がある（作り物のファイルは無い）。写しを控えへ戻してから流す: $root" }
    New-ChmonosExceptionFiles -Set $Set
  }

  # 記録を足す（読めなくする前に、ハッシュと大きさを取る）。失敗しても、読めないフォルダは読めないまま戻す
  try {
    $exe = Build-ChmonosSandboxGen
    $out = Invoke-ChmonosSandboxGen -Exe $exe -Root $root -Step @('exceptions', $fx, $OfflineLetter, $TableLetter, $SerialLetter)
  }
  finally {
    # 確かめられない場所：読む権限を外す（片付けの Remove-ChmonosFixtures が戻す）
    [void](Deny-ChmonosFixtureRead -Path $locked)
  }
  try { [void][IO.File]::GetAttributes((Join-Path $locked 'sample-rows-locked.zip')); throw "読めなくしたのに属性が読める: $locked（アプリは「確かめられません」と見ない）" }
  catch [UnauthorizedAccessException] { }

  [pscustomobject]@{ Store = $root; Fixtures = $fx; Log = $out }
}
