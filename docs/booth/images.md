# BOOTH の商品画像

BOOTH のページに載せる画像の素材の作り方。作りは Paramroom の画像に倣う（白地に太い見出し、その下にアプリの画面を1〜2枚。注意は文字だけ）。
素材の撮影はこちら、正方形（2000×2000）への配置と文字入れはユーザ。

## 写すデータ

実在の商品は権利が作者にあり、友人のデータは使えないので、**架空の商品だけの写し `showcase`** で撮る（ユーザ判断 2026-10-07）。

- 商品の名前は一般名詞（うさぎ・ワンピース・ブーツ…）、ショップは「サンプル〜」、説明・対応アバター・価格は作り物。一覧は `tools/SandboxGen/showcase.json`
- 画像は CC0 の写真（StockSnap・rawpixel を Openverse で探した。人の顔・ロゴの写る物は外した）。商品ごとに2〜3枚（`image` が1枚目、`images` が2枚目から）。
  カードの絵はマウスの横の位置で替わるので、1枚だけだとその動きが伝わらない（ユーザ指摘 2026-10-07）。リポジトリには入れず、`%LOCALAPPDATA%\Chmonos-fixtures\showcase-images` に置く。出どころとライセンスは同じ所の `credits.tsv`
- 対応アバターは、説明の「対応アバター」節に名前を書いてアプリの検出に任せる（通信しない）。「傘」だけ、別の節のリンクで「確認待ち」の見本にしてある

## 作り方

```powershell
$exe = 'tools/SandboxGen/bin/Release/net10.0/SandboxGen.exe'   # dotnet build tools/SandboxGen -c Release
$root = "$env:LOCALAPPDATA\Chmonos-sandboxes\showcase"
& $exe $root init
# 最後の引数は手元の zip の置き場。画面にパスが写るので短い場所にする（作り直すと、この手順が作った印のある所だけ消す）
& $exe $root showcase tools/SandboxGen/showcase.json "$env:LOCALAPPDATA\Chmonos-fixtures\showcase-images" D:\BOOTH
dotnet run --project tools/ViewShot -- shot showcase-search,showcase-search-hover,showcase-filters,showcase-folder,showcase-item,showcase-item-confirm,showcase-modification,showcase-inbox,showcase-resolve,showcase-avatars,showcase-list,showcase-shops,showcase-stats,showcase-tags,showcase-attributes,showcase-edit,showcase-import --full --scale 2 --theme both --out <出し先>
```

描画台（`RenderTargetBitmap`）で描くので、実機の撮影より綺麗で、2倍（3200×2000）でもくっきり描ける。
メニュー（「Unity ▾」など）は別の窓に出るので描けない。Unity の Import Unity Package ウィンドウは Unity の実機で撮る。
マウスの矢印も描けない。`showcase-search-hover` は「傘」のカードを乗せて2枚目にした姿（区切りと「2 / 3」）なので、矢印は載せる画像に足す。

Unity の実機で撮るときは、空のプロジェクト `D:\work\vrchat\VRChatProjects\ChmonosShowcase`（2022.3.22f1）を使う。
いつもの確かめのプロジェクトには実在のアバターとショップのフォルダがあり、画面に写る。
「Unityで選択」はプロジェクトが Unity Hub の一覧に無いと断られるので、Hub に足しておく。

## 載せる画像の案（2026-10-07）

2000×2000 の正方形。X で共有すると横長に切られることがあるので、大事な文字は縦の中央（y = 480〜1520 あたり）に収める。
画面（3200×2000）を横幅いっぱいに置くと高さ約1250 なので、上下に文字の帯を置く形が合う。
画面を載せる物には、小さく「画面の商品は説明用の架空のものです」と入れる（原稿の「購入前の注意」にもある）。

| # | 見出し（大） | 添える文字（小） | 場面 |
|---|---|---|---|
| 1 サムネ | Chmonos（クモノス）／BOOTHで買ったアセットを、今のフォルダのまま管理 | 無料・Windows・非公式ツール | `showcase-search`（差し替えの候補は `showcase-folder`） |
| 2 | zip をドロップするだけ | 多くは自動で商品を特定。決まらない物は候補から選ぶだけ | `showcase-resolve` ＋ `showcase-import` の進み具合を切り抜く |
| 3 | 条件を組み合わせて探す | 対応アバター・価格・カテゴリ・タグ…を足して絞り込み | `showcase-filters` |
| 4 | 画像をなぞって見比べる | カードの上でマウスを動かすと、商品画像が切り替わる | `showcase-search-hover`（傘のカードにマウスの矢印を足す） |
| 5 | 商品の情報をひとまとめに | 画像・説明・対応アバター・手元のファイル | `showcase-item` |
| 6 | zip のまま Unity へ | 「Unityへ送る」でインポート、「Unityで選択」でフォルダを開く | `showcase-item` の手元のファイル ＋ Unity の実機（Import Unity Package） |
| 7 | 改変を記録 | どのアバターに何を入れたかを、写真と一緒に | `showcase-modification` |
| 8 | 更新・販売終了を通知／使った額を振り返る | ショップ・統計も | `showcase-inbox` ＋ `showcase-stats` |
| 9 | 注意点 | 文字だけ（下） | なし |

- ダークモードは、1枚を明るい色・暗い色の斜め分割にするか、5 を暗い色の版にする（1枚増やすより伝わる）
- 減らすなら 4 を 3 に、8 を 7 にまとめる（7枚）

### 注意点の画像に書くこと

原稿の「購入前の注意」（8項目）から、買う前に一番困る物に絞る。

1. Windows（64bit）専用。Mac・スマートフォンでは使えない
2. 取り込みに時間がかかる。BOOTH へ1件ずつ、1.5秒以上空けて問い合わせるため。100商品で、名前と説明まで約5分、画像まで約30分
3. 初回に Windows の警告が出ることがある（電子署名なし）。スマート アプリ コントロールにブロックされたら、設定でオフにしないと起動できない
4. 自動で決まらないファイルもある。Brave・シークレットウィンドウでダウンロードした物や展開した中身は、候補から選んで登録する
5. 非公式のツール。BOOTH・pixiv が作成・配布しているものではない

入れなかった3つ（購入履歴を読まない・対応アバターの読み取りは間違えることがある・Unity 2022.3 で確認）は本文にある。画像の下に小さく「ほかの注意は本文を見てください」と添える。
