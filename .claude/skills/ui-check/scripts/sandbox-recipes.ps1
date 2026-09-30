# 写し（サンドボックス）を、作り物のデータで台本から組み立てる。New-ChmonosSandbox -Recipe が読み込んで使う
# （ui-kit.ps1 と fixtures.ps1 の関数を使う。直に読み込む必要は無い）。
#
# なぜ要るか：写しは、担当が画面を操作して作った状態を使い回していた。確かめで書き換わると元へ戻せず、
# sandboxes.md の説明と中身がずれた（2026-09-30）。台本があれば、壊しても数十秒で同じ状態を作り直せる。
#
# 台本にするのは、作り物のデータだけで作れる写し。作者の購入した物が元の写し（ui・d1check・stress-realcat など）と
# 友人のデータの写しは、台本にしない（元のデータが要る。sandboxes.md に「どの写しから、何を足したか」を書く）。
#
# 中身を書くのは tools/SandboxGen（アプリと同じ道で書く。BOOTH へは問い合わせない）。
# 作り物のファイルは %LOCALAPPDATA%\Chmonos-fixtures\<写しの名前> に置く（写しの記録がそのパスを指す）

$ChmonosRecipes = [ordered]@{
  small     = '作り物の商品 12 件（絵つき）。通信は切ってある。画面の並びや文言を、実データなしで見る'
  many      = '作り物の商品 2000 件（-Items で変える）。件数の多い一覧の見え方。速さの数字は stress-realcat と比べない（説明と絵が軽い）'
  manage    = 'many に、タグ（大分類20×小分類10）・属性20・知らせ1000・改変300 を入れた物。管理の画面の一覧'
  movecheck = '商品 10 件＋「BOOTHに無い商品」4件（3件にタグとメモ）＋未確定6件（束が2つ）。取り込み元と監視は作り物のフォルダ。ファイルを移したとき・未確定の束'
  bigcheck  = '商品 10 件（2件に大きな zip）＋未確定が多い状態（既定 約5千件、-Full で約8万件と 4.8GB の zip）。大容量の確かめ'
}

# 写しを組む道具をビルドして、実行ファイルのパスを返す。ソリューションに入れていないので、使うときにビルドする
function Build-ChmonosSandboxGen {
  $project = Join-Path $ChmonosRepo 'tools\SandboxGen'
  $out = dotnet build $project -c Release --nologo -v q 2>&1
  if ($LASTEXITCODE -ne 0) { throw "写しを組む道具がビルドできない:`n$(($out | Select-Object -Last 15) -join "`n")" }
  $exe = Join-Path $project 'bin\Release\net9.0\SandboxGen.exe'
  if (-not (Test-Path -LiteralPath $exe)) { throw "ビルドした実行ファイルが無い: $exe" }
  $exe
}

# 道具の手順を1つ走らせる。失敗したら投げる。道具の出した文を返す
function Invoke-ChmonosSandboxGen {
  param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string[]]$Step)
  # 道具は UTF-8 で書き出す。受ける側の読み方を合わせないと、日本語が化ける
  $was = [Console]::OutputEncoding
  [Console]::OutputEncoding = [Text.Encoding]::UTF8
  try { $out = & $Exe $Root @Step 2>&1 | ForEach-Object { "$_" } }
  finally { [Console]::OutputEncoding = $was }
  if ($LASTEXITCODE -ne 0) { throw "写しを組めない（$($Step[0])）: $($out -join ' / ')" }
  $out
}

# 台本を走らせる。-Root は作る写し（まだ無いこと）、-Set は作り物のファイルの組の名前
function Invoke-ChmonosRecipe {
  param([Parameter(Mandatory)][string]$Recipe, [Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Set, [int]$Items = 0, [switch]$Full)
  if (-not $ChmonosRecipes.Contains($Recipe)) { throw "台本が無い: $Recipe（在る台本: $($ChmonosRecipes.Keys -join '・')）" }
  $exe = Build-ChmonosSandboxGen
  $gen = { param([string[]]$step) Invoke-ChmonosSandboxGen -Exe $exe -Root $Root -Step $step }
  $log = [System.Collections.Generic.List[string]]::new()
  $note = { param($lines) foreach ($l in @($lines)) { $log.Add("$l") } }

  # 前の回の作り物が残っていたら消す（同じ名前の古いファイルを取り込まないように）
  $usesFixtures = $Recipe -in 'movecheck', 'bigcheck'
  if ($usesFixtures -and (Test-Path -LiteralPath (Join-Path $ChmonosFixtureHome $Set))) { & $note (Remove-ChmonosFixtures -Set $Set) }

  & $note (& $gen @('init'))
  switch ($Recipe) {
    'small' { & $note (& $gen @('items', "$(if ($Items) { $Items } else { 12 })")) }
    'many' { & $note (& $gen @('items', "$(if ($Items) { $Items } else { 2000 })")) }
    'manage' {
      & $note (& $gen @('items', "$(if ($Items) { $Items } else { 2000 })"))
      & $note (& $gen @('manage'))
    }
    'movecheck' {
      $fx = Get-ChmonosFixtureDir $Set
      # 取り込み元（監視あり）の2つと、監視だけして取り込まない所。大きい物は、移した後のハッシュの取り直しの時間を見る
      [void](New-ChmonosFixtureZip -Set $Set -Name '使った\sample-move-small.zip' -SizeMB 1)
      [void](New-ChmonosFixtureZip -Set $Set -Name '使った\sample-move-mid.zip' -SizeMB 5)
      [void](New-ChmonosFixtureZip -Set $Set -Name '使った\sample-move-big.zip' -SizeMB $(if ($Full) { 400 } else { 20 }))
      [void](New-ChmonosFixtureZip -Set $Set -Name '次使う\sample-next.zip' -SizeMB 1)
      [void](New-ChmonosFixtureZip -Set $Set -Name '外\sample-outside.zip' -SizeMB 1)
      # 束1：ばらのファイルが入ったフォルダ
      [void](New-ChmonosFixtureFolder -Set $Set -Name '束\bundle-loose' -Count 3 -PerFolder 3 -Bytes 20000)
      # 束2：zip を展開したフォルダ（Windows が付ける「展開元の zip」の記録つき）。展開元は取り込まない所に置く
      $origin = (New-ChmonosFixtureZip -Set $Set -Name 'bundle-origin\sample-outfit.zip' -SizeMB 1).Path
      $extracted = (New-ChmonosFixtureFolder -Set $Set -Name '束\bundle-extracted\SampleOutfit' -Count 3 -PerFolder 3 -Bytes 20000).Path
      foreach ($f in Get-ChildItem -LiteralPath $extracted -Recurse -File) { [void](Set-ChmonosFixtureZone -Path $f.FullName -ReferrerUrl $origin) }

      & $note (& $gen @('items', "$(if ($Items) { $Items } else { 10 })"))
      & $note (& $gen @('scan', "$fx\使った", "$fx\次使う", "$fx\束"))
      & $note (& $gen @('register', 'sample-move-small', '作り物の移動テスト小', '確かめ/移動', '移す前は「使った」にある'))
      & $note (& $gen @('register', 'sample-move-mid', '作り物の移動テスト中', '確かめ/移動', '移した後もタグとメモが残るかを見る'))
      & $note (& $gen @('register', 'sample-move-big', '作り物の移動テスト大', '確かめ', '大きいファイル'))
      & $note (& $gen @('register', 'sample-next', '作り物の次に使う物'))
      & $note (& $gen @('settings', "importFolders=$fx\使った;$fx\次使う", "watchedFolders=$fx\使った;$fx\次使う;$fx\外"))
    }
    'bigcheck' {
      $fx = Get-ChmonosFixtureDir $Set
      [void](New-ChmonosFixtureZip -Set $Set -Name 'big\sample-huge-a.zip' -SizeMB $(if ($Full) { 4800 } else { 200 }) -Parts 3)
      [void](New-ChmonosFixtureZip -Set $Set -Name 'big\sample-huge-b.zip' -SizeMB $(if ($Full) { 2300 } else { 100 }))
      [void](New-ChmonosFixtureZip -Set $Set -Name 'many\sample-manyfiles.zip' -Count $(if ($Full) { 70000 } else { 5000 }))
      [void](New-ChmonosFixtureUnityPackage -Set $Set -Name 'upkg\sample-heavy-unitypackage.zip' -InZip -Assets $(if ($Full) { 4000 } else { 200 }) -SizeMB $(if ($Full) { 1000 } else { 20 }))
      [void](New-ChmonosFixtureBrokenZip -Set $Set -Name 'broken\sample-truncated.zip' -Kind Truncated -SizeMB 5)
      [void](New-ChmonosFixtureBrokenZip -Set $Set -Name 'broken\sample-garbage.zip' -Kind Garbage -SizeMB 2)
      [void](New-ChmonosFixtureFolder -Set $Set -Name 'loose' -Count $(if ($Full) { 80000 } else { 5000 }))

      & $note (& $gen @('items', "$(if ($Items) { $Items } else { 10 })"))
      & $note (& $gen @('scan', "$fx\big", "$fx\many", "$fx\upkg", "$fx\broken", "$fx\loose"))
      & $note (& $gen @('attach', 'sample-huge-a', '1'))
      & $note (& $gen @('attach', 'sample-huge-b', '2'))
    }
  }
  & $note (& $gen @('show'))
  $log
}
