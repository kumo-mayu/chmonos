# 衣装の変換ツール（もちふぃった～以外）

2026-11-20 に Web で調べた（ユーザ依頼「検索でどのような変換ソフトがあるか調べてきてください」）。
きっかけは open.md のメモ「変換は自動で検出されるしか経路が無いので、検出できないソフトや新しいソフトを使っている人に対応できない」。
もちふぃった～の事実は `mochifitter.md`。BOOTH のページは私（Claude）は Web の検索と読み取りで見ただけで、ツールは使っていない。

## プロファイルを使うツール

| ツール | 動く所 | プロファイル | 根拠 |
|---|---|---|---|
| もちふぃった～ | Unity | 変換元に逆方向・変換先に順方向。アバターの販売元や有志が BOOTH で配る | `mochifitter.md` |
| きせった (Kisetter)（旧名 Eden Auto Morpher。edenlabs。v3.6.7・2026-11-13） | Unity | **変換元の代わりだけ**。本体に17体分が入る | [公式] 下の節 |
| OutFitter（v2.4・2,500円） | Unity | アバターごと（JSON・NPZ）。作る道具 Profile Maker（800円）は別売り。両方向の変換の形と書いてある | [主張] [BOOTH](https://booth.pm/ja/items/8869290)・[Profile Maker](https://booth.pm/ja/items/8869293) |

### きせった (Kisetter)

[公式] [BOOTH](https://booth.pm/ja/items/7721082)・[衣装自動対応ガイド](https://kisetter.edenlabs.workers.dev/ja/docs/auto-fitting/)・[Q&A](https://kisetter.edenlabs.workers.dev/ja/docs/faq/)（3.5.0 基準）

- 入れる物は「変換元アバター」か「変換元プロファイル」のどちらか＋衣装＋変換先アバター（シーンに置いた手元のアバター）。もちふぃった～の順方向に当たる物は無い
- 変換先は Unity の Humanoid なら何でもよい（T ポーズ。A ポーズは切り替えで対応。BOOTH 以外・改変済みも可）
- 両方のアバターを持っていれば、プロファイル無しで変換できる
- プロファイルは `Assets/@Eden_Tools/Kisetter/Profiles/<名前>/<名前>.json` と `<名前>.eb` の組（前の版の手引きでは `Eden_AutoMorpher/Profiles`）。本体は今も `Assets/@Eden_Tools/Eden_AutoMorpher` に入ると書いてある
- 本体に17体分（配布を作者が許した物）。足の形（Foot_Heel・HighHeel）で別のプロファイルがあるらしい
- 自分で作る道具（Profile Saver）がある。手順のページは読めなかった（Notion の旧手引き・新しい手引きとも取れず）
- BOOTH で、きせった用のプロファイルを別に売る例は、検索では見つからなかった
- 分からない事：`.json` の中身（アバター名が入るか）・`.eb` が何か

## プロファイルの要らないツール

衣装と体のメッシュから直に合わせる。Chmonos の変換の判定（プロファイルの有無）には乗らない。[主張] はどれも BOOTH の説明。

- Unity：[Body Fit](https://booth.pm/ja/items/9062815)（無料・NDMF/MA）・[ClothTransformApplier](https://booth.pm/ja/items/6291387)・Alterith（3,000円。記事に名前があるだけで BOOTH のページは見つからず）
- Blender：[Outfit Refit](https://booth.pm/zh-cn/items/9078766)・[ドレスフィット](https://booth.pm/ja/items/8930466)（無料）・[D9speed's Avatar Interchange](https://booth.pm/zh-cn/items/8647065)
- 変換の後の補助：[DecoFit](https://booth.pm/ja/items/9043331)・[ELeFitBone](https://booth.pm/en/items/7895100)（販売終了）

## Chmonos への意味［推測］

- ツールごとに「何があれば A 用の衣装を B に着せられるか」が違う。きせったでは、手元の A と B の組で決まり、プロファイルの商品を探す話にならない
- プロファイルを自動で見分けられそうなのは、もちふぃった～ときせった（置き場の形がある）だけ。ほかは人が足す道しか無い
- そのため、プロファイルを介さず「A 用の衣装は B でも使える」を人が足せる形（avatar2avatar）が要る。案は open.md の変換の行
