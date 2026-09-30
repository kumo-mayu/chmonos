# 変種を交互に何巡も測る。-Variants は 'ラベル=実行ファイル' か 'ラベル=実行ファイル|変数:値;変数:値' の並び。
# -Marks を付けた変種（ラベルが印付きの版）は、区切りの印の出力先（CHMONOS_BOOTMARKS）を渡し、書き出し（開始から約10秒）まで待つ。
# ほかのアプリが開いていたら1分おきに待つ（最大 -MaxWaitMin 分。過ぎたら重なったまま測り、結果の others に本数が残る）
param([Parameter(Mandatory)][string[]]$Variants, [int]$Rounds = 5, [Parameter(Mandatory)][string]$Out,
  [int]$MaxWaitMin = 40, [string[]]$Marks = @(), [string]$Store = 'bootA')
$marksDir = Join-Path ([IO.Path]::GetDirectoryName($Out)) 'marks-out'
$waited = 0
for ($r = 1; $r -le $Rounds; $r++) {
  foreach ($v in $Variants) {
    $label, $exe = $v -split '=', 2
    $venv = @{}
    if ($exe -like '*|*') { $exe, $kv = $exe -split '\|', 2; foreach ($p in $kv -split ';') { $kk, $vv = $p -split ':', 2; $venv[$kk] = $vv } }
    $settle = 5
    if ($Marks -contains $label) { New-Item -ItemType Directory -Force $marksDir | Out-Null; $venv['CHMONOS_BOOTMARKS'] = Join-Path $marksDir "$label-$r.tsv"; $settle = 11 }
    while ($true) {
      $res = & (Join-Path $PSScriptRoot 'trial.ps1') -Label "$label#$r" -Exe $exe -Out $Out -Env $venv -Settle $settle -Store $Store -Force:($waited -ge $MaxWaitMin)
      if ("$res" -like 'busy*') { "$(Get-Date -f HH:mm:ss) 待つ: $res"; Start-Sleep -Seconds 60; $waited++; continue }
      $res; break
    }
  }
}
"待った分: $waited"
