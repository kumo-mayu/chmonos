# もちふぃった～（衣装の変換ツール）とプロファイルの調べ（2026-11-06）

v1.3.0 の機能「変換で使える対応アバター」（`docs/plan.md` 8）の登録の単位を決めるための調べ。
公開のページだけを読んだ（BOOTH は7ページ）。根拠の印は CLAUDE.md の決まり。

## 分かったこと

- **本体**：おもちのびる（Nine Gates）作。https://booth.pm/ja/items/7657840 （2,500円・Windows 11・Unity 2022.3.22f1）。
  しなの・桔梗・マヌカ・ベリルのプロファイルを順逆両方向で同梱 [公式]
- **変換に要る物**：使い方ガイド https://mochifitter-guide.mochimochiomochi310.workers.dev/ の原文 [公式]
  - 「変換には、衣装元と変換先の両方のアバターの変換データ（プロファイル）が導入されている必要があります。」
  - 「衣装元のプロファイルは逆方向、変換先のプロファイルは順方向である必要があります。」
  - 「順方向は他のアバターの衣装をそのプロファイルのアバターへ合わせる方向の変換です。」「逆方向はそのプロファイルのアバターの衣装を他のアバターへ合わせる方向の変換です。」
  - 「配布されているプロファイルで特に表記が無い場合は順方向のみで逆方向は無いと判断した方が良いでしょう。」
  - 中間の素体 Template を通る2段の変換もある（「Shinano → Beryl (Template経由)」）。使う人が選ぶ物は変わらない [推測]
- **向き**：A の衣装を B へは「A の逆」＋「B の順」。B→A は別に「B の逆」＋「A の順」が要る。逆方向を作っていないプロファイルが多い（「逆変換の予定なし」https://booth.pm/ja/items/7695987 ・缶詰茶房は CUUNE の系統だけ https://booth.pm/ja/items/7691574 ）[公式：各配布者]
- **単位はアバターも共通素体もある** [公式：各配布者]
  - アバター1体：https://booth.pm/ja/items/8681879 。2体で1つ：https://booth.pm/ja/items/7662461
  - 共通素体：缶詰茶房「特定のキャラクター向けではなく、素体ごとに対応」（https://booth.pm/ja/items/7691574 ）・Cilia Base（https://booth.pm/ja/items/8760584 ）・MDoll の S/M でアバター4体（https://booth.pm/ja/items/7690877 ）
  - 素体のプロファイルが、同じ素体を使う別の店のアバターにも効くかは書いていない。プロファイルは決まった場所の FBX を参照する [推測：素体の FBX を変換先に選ぶ形]
- **ファイル**：zip の中の unitypackage。中は素体の FBX と設定の JSON の組と見られる [推測]。
  名前は揃っていない（`MD_MochiFitter_MDoll_S_1_0.unitypackage`・`MochiFitterProfile_MD_MDOLL_M_RE.unitypackage`（RE＝逆）・`MochiFitter_Profile_Ciliabase_1.01.zip`）。
  入る所の例：`Assets/OutfitRetargetingSystem/Editor`、変換の結果は `Assets/OutfitRetargetingSystem/Outputs/` [公式：MDoll の配布者]
- **BOOTH での見分け**：商品名に「もちふぃった～用」「MochiFitter Profile」「変換プロファイル」「順逆両変換対応」など。決まった書き方・タグは無い。
  アバター本体に同梱する例もある（順と逆を同梱・順方向だけ同梱。検索結果の抜き書きで読んだだけ）[主張]
- **制限**：アニメーション・複雑なギミックは動かないことがある（本体ページ）。靴・硬い物・ダボっとしたパンツ・手足の先・球体関節は崩れやすい（プロファイルの配布者たち）[公式]
- **似たツール**：プロファイルや対応表を使う同じ型の物は見つからなかった（ドレスモーフ・RRSArmatureAdjuster は骨を合わせるだけ）

## 写しで数えたこと（2026-11-06）

名前に「もちふぃった」を含む商品：友人の写し 207 件に 3 件（プロファイル 2・本体 1）、2人目の写し 782 件に 9 件（プロファイル 4・本体など）。プロファイルの商品の対応アバターは 1〜8 体。

## 分からなかったこと

本体の VCC での入れ方・プロファイルの中の詳しい作り・素体のプロファイルが別の店の同じ素体のアバターに効くか・作者の X の投稿。

## 判定の手掛かり（2回目の調べ・2026-11-06。BOOTH は1本ずつ2秒空けて25回）

- **unitypackage の中のパスが一番確か**：プロファイルは `Assets/OutfitRetargetingSystem/Editor/` の直下に、`{slug}`（アバターの短い英字の名前）の付いたファイルの組で入る。
  順方向は `posediff_template_to_{slug}.json`・`deformation_template_to_{slug}.npz`・`config_template2{slug}.json`、逆方向は `posediff_{slug}_to_template.json`・`deformation_{slug}_to_template.npz`・`config_{slug}2template.json`、共通は `avatar_data_{slug}.json`・`pose_basis_{slug}.json`
  （[他者の説明] https://github.com/Mega-Gorilla/MochiFitter-BlenderAddon-kai/blob/main/profile_data/README.md 。設定のファイルの名前は「任意ですが、一般的なパターン」）。
  **友人の2つの写しの unitypackage の控え（GUID → パス）で照らした** [実測]：この場所にファイルを持つ unitypackage は 23 個、うち 22 個がこの形のプロファイルで、向きは両方 21・順だけ 1 に分かれた（名前は伏せる）。1つの unitypackage に何体分も入ることがある（本体は4体分を同梱）ので、向きは slug ごとに見る。
- **`avatar_data_{slug}.json` の欄**：`name`・`defaultFBXPath`（例 `Assets/Nagi/FBX/Nagi.fbx`）・`meshName`・`height`・`basePose`・`blendshapes`。向きの欄は無い。slug は商品名と直には合わない（`beryl4` など）ので、アバターへ結ぶなら `defaultFBXPath` を、持っているアバターの unitypackage のパスと照らす [他者実測：公開されたファイル]
- **BOOTH の JSON**：プロファイルの商品は 13 件すべてにタグ「もちふぃった〜」（U+301C。商品名は「～」U+FF5E、半角「~」の物もある）。カテゴリは「3Dツール・システム」か「3Dモデル（その他）」で、アバター（同梱）は「3Dキャラクター」。
  バリエーション名に向きを書く例（「<X用>順変換プロファイル」「エク用(for Eku) 順方向」）。タグがあっても同梱とは限らない（タグの付いた商品 1,040 件のほとんどはアバター）[実測]
- **商品名**：検索の上位 180 件でプロファイル 31 件、別の検索と合わせて 43 件。向きの語が名前にある物 39/43（「両変換」29・「双方向」3・「順/逆変換」3・「両方向」2・「逆変換対応」1・「順方向のみ」1）。最大の配り手は「プロファイル」でなく「対応データ」と書く（28 件）[実測]
- **外の一覧**：有志の一覧（https://mochi.vr-chat.kr/ 、データ https://github.com/rnfkvkejr32/mochifitter_list の `data/profiles.json`）が動いている。1,721 件（1件＝アバター1体）、向きは順のみ 1,381・両方 312・逆のみ 9、`bodyBase`（素体）は 128 件に値あり、配り方はアバター同梱 761・無料 719・単体有料 224 [実測]。
  LICENSE が無く、使い回してよいかは書いていない。元の一覧（https://github.com/eringiriri/mochifitter_list 、MIT）は 2026-02-21 に終わった
- **SuperMochiFitter** は本体の中のフォルダの名前（変換の実行ファイル）で、別の商品ではない [推測：パスから]

## 無料のプロファイル4つの中身（2026-11-06・ユーザが取ってきた物。`DLforTest/`、中身は手元で読んだだけ）

| 物 | 向き（ユーザの把握） | 置き場 | 判定（パスから） | `defaultFBXPath` |
|---|---|---|---|---|
| Fel | 順 | `Editor/` 直下 | 順（`config_template2fel`・`posediff_template_to_fel`） | `Assets/Fel/Fel.fbx` |
| Puruwuff（slug `pw`） | 順 | `Editor/` 直下 | 順 | `Assets/Takko/Puruwuff/vrchat/fbx/pw_base.fbx` |
| Salt | 両方 | `Editor/salt/` | 両方（`config_salt2template` と `config_template2salt`） | `Assets/Amatousagi/Salt/FBX/Salt.fbx` |
| Cilia Base（共通素体） | 両方 | `Editor/GeneratedProfiles/ciliabase/` | 両方 | `Assets/Cilia Base by 23Mink/Cilia Base by 23Mink.fbx` |

[実測] 4つとも、向きの判定はユーザの把握と合った。分かった事：
- **置き場は `Editor/` の直下とは限らない**（サブフォルダ `salt/`・`GeneratedProfiles/ciliabase/` もある）。判定は「`OutfitRetargetingSystem/Editor/` の下のどこか」で見る
- slug は `avatar_data_{slug}.json` の名前から取れる。`name` の欄は大文字小文字が違うことがある（`Salt`・`Ciliabase`）
- 4つとも、アバターや素体の FBX は同梱していない（Cilia Base は約 1GB だが中身は全部プロファイルのファイル）。前の調べの「素体の FBX を同梱」は、この4つには当たらない
- 共通素体用かアバター用かは、中身からは分からない。`defaultFBXPath` が指すのは素体の FBX（Cilia Base）かアバターの FBX かの違いだけなので、FBX を持つ商品が何か（アバターか、共通素体の商品か）で見分けるか、人に決めてもらう
- アバターへのつなぎ方の見込み：2人目の友人の写しで、本体同梱の4体と template を除く slug 17 個のうち 12 個は、持っているアバターの unitypackage に、slug を名前やフォルダに含む FBX があった（slug で探した場合。`defaultFBXPath` の完全一致は、写しに zip が無く確かめていない）
