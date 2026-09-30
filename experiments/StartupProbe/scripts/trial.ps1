# 起動を1回測る（docs/research/startup-dotnet10-2026-10-01.md）。
# 見張り（StartupProbe.exe）を先に走らせてから Start-ChmonosApp で起動し、落ち着いたら閉じる。
# ほかのアプリが開いていたら測らずに 'busy' を返す（-Force で構わず測る。数字が動くので、報告に重なりを書く）
param(
  [Parameter(Mandatory)][string]$Label, [Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Out,
  [hashtable]$Env = @{}, [switch]$Force, [string]$Store = 'bootA',
  # 起動を返すまでの待ち。Start-ChmonosApp は待った後に UI Automation で窓の題を読むので、測っている最中に読まないよう長めに取る
  [int]$Settle = 5)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$probeExe = Join-Path $PSScriptRoot '..\bin\Release\net10.0-windows\StartupProbe.exe'
if (-not (Test-Path $probeExe)) { throw "見張りが無い: dotnet build experiments/StartupProbe -c Release" }
. (Join-Path $repo '.claude\skills\ui-check\scripts\ui-kit.ps1')
$others = @(Get-Process BoothAssetManager.App -ErrorAction SilentlyContinue)
if ($others.Count -and -not $Force) { return "busy: $(@(Get-ChmonosRunning | ForEach-Object { $_.Key }) -join ',') / $($others.Count) 本" }
# PowerShell は $null を空文字にして渡すので、消すときは [NullString]::Value。
# 前の回の設定が空文字で残ると、子に「空の値」として渡り、別の条件で測ってしまう（2026-10-01 に一度これで測り直した）
$knobs = 'DOTNET_TieredPGO', 'DOTNET_TC_QuickJitForLoops', 'DOTNET_TieredCompilation', 'DOTNET_TC_QuickJit', 'DOTNET_ReadyToRun', 'CHMONOS_BOOTMARKS', 'CHMONOS_BOOTJIT'
foreach ($k in $knobs) { [Environment]::SetEnvironmentVariable($k, [NullString]::Value) }
foreach ($k in $Env.Keys) { [Environment]::SetEnvironmentVariable($k, [string]$Env[$k]) }
$last = Join-Path ([IO.Path]::GetDirectoryName($Out)) 'probe-last.txt'
try {
  $probe = Start-Process $probeExe -ArgumentList "`"$Out`"", $Label -PassThru -NoNewWindow -RedirectStandardOutput $last
  Start-Sleep -Milliseconds 400
  [void](Start-ChmonosApp -Store $Store -Exe $Exe -NoTrace -IsolateTemp -SettleSeconds $Settle)
  if (-not $probe.WaitForExit(70000)) { $probe.Kill(); 'probe timeout' }
  Start-Sleep -Milliseconds 300
  [void](Stop-ChmonosApp -Store $Store)
} finally {
  foreach ($k in $Env.Keys) { [Environment]::SetEnvironmentVariable($k, [NullString]::Value) }
}
Get-Content $last
Start-Sleep -Seconds 2
