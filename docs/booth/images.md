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
dotnet run --project tools/ViewShot -- shot showcase-search,showcase-search-hover,showcase-filters,showcase-folder,showcase-item,showcase-item-confirm,showcase-modification,showcase-inbox,showcase-resolve,showcase-avatars --full --scale 2 --out <出し先>
```

描画台（`RenderTargetBitmap`）で描くので、実機の撮影より綺麗で、2倍（3200×2000）でもくっきり描ける。
メニュー（「Unity ▾」など）は別の窓に出るので描けない。Unity の Import Unity Package ウィンドウは Unity の実機で撮る。
マウスの矢印も描けない。`showcase-search-hover` は「傘」のカードを乗せて2枚目にした姿（区切りと「2 / 3」）なので、矢印は載せる画像に足す。

Unity の実機で撮るときは、空のプロジェクト `D:\work\vrchat\VRChatProjects\ChmonosShowcase`（2022.3.22f1）を使う。
いつもの確かめのプロジェクトには実在のアバターとショップのフォルダがあり、画面に写る。
「Unityで選択」はプロジェクトが Unity Hub の一覧に無いと断られるので、Hub に足しておく。

## 載せる画像の案

| # | 見出しの案 | 場面 |
|---|---|---|
| 1 | 買ったアセットを、今のフォルダのまま管理 | `showcase-search`（マウスで絵をめくる所は `showcase-search-hover`） |
| 2 | フォルダの並びはそのまま | `showcase-folder` |
| 3 | zip を入れるだけで商品を特定 | `showcase-resolve`（と取り込みの結果） |
| 4 | 条件を組み合わせて絞り込み | `showcase-filters`（対応アバター AND・価格2つ・カテゴリを除く → 3件） |
| 5 | Unity へそのままインポート | `showcase-item` ＋ Unity の実機 |
| 6 | 改変を記録 | `showcase-modification` |
| 7 | 注意点 | 文字だけ（ユーザ） |
