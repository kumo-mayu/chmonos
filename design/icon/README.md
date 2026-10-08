# アプリのアイコン

2026-10-08 に作った（ユーザ判断「Aで良さそうだ」。3案のうち A：フォルダの地に、真ん中から張った蜘蛛の巣）。
名前の「クモノス（蜘蛛の巣）」と、「手元のファイル（フォルダ）を管理する」を組み合わせた。色はアプリの青 `#3767a6`（`Accent`）の地に白い巣で、明るい背景でも暗い背景でも見える。

## ファイル

| ファイル | 中身 |
|---|---|
| `icon-A.svg` | 48px 以上の絵（輪3つ・放射8本・線 8/256） |
| `icon-A-small.svg` | 32px 以下の絵。輪と放射を減らし、線を太くした（輪1つ・放射6本・線 16/256 ＝ 16px で 1px・32px で 2px） |
| `make.js` | SVG を作る台本（案 A・B・C と、見比べる見本の HTML も書く） |
| `render.html` | 8つの大きさを決まった位置に並べる、描き出し用のページ |
| `ico.js` | 大きさごとの PNG を1つの `.ico` にまとめる |

使っている所：`Chmonos.App/Assets/Chmonos.ico`（exe と主の窓。`ApplicationIcon`）、`Chmonos.App/Assets/Chmonos-64.png`（設定の「このアプリについて」）。

## 作り直し方

1. `node make.js`（SVG を書き直す。形を変えるときはここの数字を変える）
2. Edge で `render.html` を透明な背景のまま撮る：
   `msedge --headless=new --force-device-scale-factor=1 --default-background-color=00000000 --window-size=300,340 --screenshot=render.png file:///…/render.html`
3. 撮った画像から、`render.html` に書いた位置で 16・20・24・32・40・48・64・256 の正方形を切り出し、`png/icon-<大きさ>.png` に保存する（PowerShell の System.Drawing で切り出した）
4. `node ico.js png ../../Chmonos.App/Assets/Chmonos.ico`、64px を `Chmonos.App/Assets/Chmonos-64.png` に写す

Git Bash から台本に `/` で始まる文字を引数で渡さない（パスに書き換えられる）。
