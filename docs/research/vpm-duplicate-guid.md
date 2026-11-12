# VPM で入っている物と同じ GUID を unitypackage で入れたとき（2026-11-11）

> **要点**：VPM で入っている lilToon と同じ GUID の物を unitypackage で取り込むと、Unity は `Assets/` に新しく作らず、**`Packages/` の中の VPM のファイルを unitypackage の中身で黙って上書きする**（中身が違うとき）。中身が同じなら何もしない。どちらもログに警告は出ない [実測]。
> きっかけはユーザ案（2026-11-11「VPMパッケージの記録とそれに伴うlilToonの二重導入の防止くらいは入れても良いかも」）。作るかは plan に書く。

## 確かめ方 [実測]

- 場所：確かめ用のプロジェクト `cleanTest - コピー`（Unity 2022.3.22f1。VPM に `jp.lilxyzw.liltoon` 2.3.3 ほか7本）。ユーザが「確かめからやりましょう」と頼んだうえで、画面を出さないバッチ（`-batchmode -nographics -importPackage … -quit`）で取り込んだ。
- 取り込んだ物：手元の商品の控え（unitypackage の中身の一覧 34本）に `Assets/lilToon` を同梱した物は無かった（ユーザ「最近のものは入ってないと思う」）。そこで、プロジェクトの `Packages/jp.lilxyzw.liltoon` から3つ（シェーダー2つと LICENSE）を、**同じ GUID のまま `Assets/lilToon/…` の置き場で**詰めた作り物の unitypackage を作った（購入した物は使っていない）。

| 回 | 中身 | 結果 |
|---|---|---|
| 1 | VPM の物と同じ中身 | `Assets/lilToon` は作られない。`Packages/` も変わらない。ログは「Import package from …」だけ |
| 2 | 中身の末尾に1行足した（別の版のつもり） | `Assets/lilToon` は作られない。**`Packages/jp.lilxyzw.liltoon/Shader/ltspass_dummy.shader` などの3つが、unitypackage の中身に置き換わった**。ログは置き換わったファイルを取り込み直した行（改行の混ざりの注意）だけで、置き換えたことは言わない |

- 2回目の後、置き換わった3つは足した1行を取り除いて元に戻した（版の表記 `package.json` は 2.3.3 のまま）。

## 分かったこと

- Unity は unitypackage の中の物を **GUID で今ある物に当て、置き場（pathname）より GUID を優先する**。VPM の物は `Packages/` にあっても書き換えられる。
- 害が出るのは「同じ GUID・違う中身」＝ **VPM と違う版の lilToon を同梱した unitypackage**。VPM の版とファイルが混ざり、VPM の側は自分の版のままだと思っている（VCC・ALCOM は気付かない [推測]）。
- 画面を出す取り込み（2026-11-12 に確かめた [実測]）：同じプロジェクト（Unity 2022.3.22f1）へ、上の2回目と同じ形の作り物（同じ GUID の3つを `Assets/lilToon/…` の置き場で詰め、中身は VPM の物と1行違う）を、Chmonos の「Unityへ送る」と同じ道（Assets > Import Package > Custom Package...）で渡した。
  取り込み画面には、重なる物が **unitypackage の中の置き場ではなく、プロジェクトの今の置き場**で並んだ：`Packages` > `jp.lilxyzw.liltoon` > `LICENSE`、`Shader` > `ltspass_bakeramp.shader`・`ltspass_dummy.shader`。どれにも「変わる」の印（↻）が付き、`Assets/lilToon` は出なかった。
  そこで Chmonos の窓は、外す場所を送り先のプロジェクトの側の置き場（`Packages/jp.lilxyzw.liltoon`）で言う（[unity-vpm.md](../spec/unity-vpm.md)）。重ならない新しいファイルと混ざる包みでの並びは見ていない。

## 作るとしたら（案）

- Unity プロジェクトの `Packages/vpm-manifest.json` と `Packages/*/package.json` を読み、入っている VPM の名前と版を控える（読むだけ。入れる・消すはしない）。
- 送る unitypackage の GUID（Chmonos は控えに持っている）と、プロジェクトの `Packages/` の `.meta` の GUID を照らし、重なりがあれば送る前に窓で言う。lilToon に限らず、同梱した Modular Avatar なども拾える。
- 重なる中身を抜いた unitypackage を作り直して送る案は、商品の中身を書き換えるので後回し。
