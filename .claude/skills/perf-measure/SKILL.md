---
name: perf-measure
description: Chmonos の画面の速さ・固まり・メモリを測って、前後の版を比べる。「重い」「カクつく」「メモリ」と言われたとき、一覧・絵・読み込みの作りを変えたとき、変更の効き目を数字で示すときに使う。
---

# 速さとメモリを測る

このプロジェクトは数字で決める。見立て（「絵の保持を増やせば軽くなる」）は測ると外れることが多い
（固まりは絵ではなくカードを作る数で決まっていた。U12）。

## 台

- 保存先は `stress-realcat`（作った2000件・分類5種類）。起動は `ui-check` スキルの `Start-ChmonosApp -Store stress-realcat`
- 前にあった `-stress` は分類の名前に通し番号が付いて2000種類あり、分類の選択肢を並べるだけで起動が約5.5秒遅れた（実際の分類は多くても数十）。
  分類が偏った台で測ると、測りたい物でない所が遅く出る

## 道具

`scripts/perf-kit.ps1`（`ui-check` の `ui-kit.ps1` を読んだ後にドットで読み込む）。前は作業用フォルダに置いていて、担当ごとに書き直していた。

```powershell
. "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\ui-kit.ps1"
. "D:\work\ClaudeCode\chmonos\.claude\skills\perf-measure\scripts\perf-kit.ps1"
```

| 関数 | 何をするか |
|---|---|
| `Measure-ChmonosStep -Label x -Action {…} [-QuietMs 1500]` | 操作を1つ測る（1回の呼び出しの中で）。落ち着くまでの時間・固まりの回数と合計と最長・メモリの最大と後 |
| `Start-ChmonosProbe -Run r1` → `Add-ChmonosProbeMark -Run r1 -Label a` → `Stop-ChmonosProbe -Run r1` | 呼び出しをまたいで長く測る（見張りを別のプロセスで走らせる）。取り込みの間・画面を何周もする間 |
| `Get-ChmonosProbeSummary -Run r1 [-From a] [-To b]` / `Get-ChmonosProbeStalls -Run r1 [-MinMs 200]` | 印の間の固まりとメモリ／長い固まりの一覧 |
| `Get-ChmonosProbeConnections -Run r1` | 見張っている間にアプリが通信した相手（BOOTH へ問い合わせたかを見る） |
| `Get-ChmonosMem` / `Wait-ChmonosResponsive` | 今のメモリ／画面のスレッドが返事をするまで待つ（固まっている間に撮らない） |
| `Compare-ChmonosShots -A a -B b [-Region]` / `Show-ChmonosDiff a b` | 撮った2枚を画素で比べる／違いが字の縁の揺れか、位置のずれかを見分ける |
| `Find-ChmonosOne -Name` / `Invoke-ChmonosEl $el` | 計測の外で探し、計測の中では押すだけにする |

前後の版は `Start-ChmonosApp -Store stress-realcat -Exe <その版の実行ファイル>` で替える。
**ほかのアプリ（別の担当の確かめ）が動いていると数字が動く。測るのは1本だけのとき**（`Get-ChmonosRunning` で見る）。

## 測り方

1. 画面の速さは、検索画面を端から端まで同じ歩みで流して測る（毎回同じ回数・同じ間隔でスクロールする）
2. 固まりは、流している間ずっと窓へ空のメッセージ（`WM_NULL`、`SendMessageTimeout`）を送り続け、
   返りが 33ms（2コマ）を超えた回数と合計で見る。1歩の直後に1回聞くだけだと、後回しにした作業の固まりを取りこぼす
3. メモリは作業セットとプライベートの最大と、落ち着いた後の値
4. 前後の版は交互に測る（前→後）。PC の使われ方で数字が動くので、別の日の数字と比べない。
   差がぶれの2倍より大きければ1巡で止める（ユーザ判断 2026-09-24）。ぶれは同じ版を続けて測ったときの揺れで、
   2026-09-24 の実測では、スクロールの固まりが 3・3・5 回、検索への切り替えが周回ごとに 1.6→1.8→2.3 秒と動いた。
   固まりが消える・メモリが数十MB以上減る、のような差はこれに埋もれないので1巡で足りる。
   差が小さく、どちらとも言えないときだけ、2巡目（前→後）を測る
5. **Core の処理（全件の読み込み・検索の照合・辞書・取り込みの走査）は、画面越しに測らない。**
   Release の Core.dll を参照する小さな計測用のプログラム（作業用フォルダ）で、写しを読むだけにして、時間と割り当て
   （`Stopwatch`・`GC.GetTotalAllocatedBytes`・残るメモリ）を直接測る。画面を通すと読み取りの道具の手間とぶれが乗る
6. 前の版は `git worktree` か別のフォルダへのビルド（`-o`）で並べて置く
7. 1つのプロセスで続けて測ると、前の操作の余熱（片付いていないメモリ）が後の数字に乗る。メモリの最大と落ち着いた値は、場面ごとに起動し直して測る

## 目標

- メモリは多い時でも 300〜400MB（サムネイルの保持は 132MB）
- 詳しい上限・分かったこと・打った手は `docs/research/memory-budget.md`

## 報告

表で出す：版・巡・固まりの回数と合計・最長・メモリの最大と落ち着いた値。
差が巡ごとのぶれより小さいときは「差は測れなかった」と書く。
