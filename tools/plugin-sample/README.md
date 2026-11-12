# Chmonos のプラグインの見本

外のソフトから Chmonos の右クリックに項目を足す仕組み（[docs/spec/plugins.md](../../docs/spec/plugins.md)・[plugins-menu.md](../../docs/spec/plugins-menu.md)）を、いちばん小さく使った PowerShell 7 の台本です。プラグインを作るときの出発点にしてください。

## 動かし方

1. Chmonos を起動し、設定の「API」で「APIを使う」をオンにする。
2. `pwsh -File tools/plugin-sample/ChmonosPluginSample.ps1` を動かす（Chmonos を2つ以上動かしているときは `-Store <保存先のフォルダ>`）。
3. Chmonos に「APIの利用の許可」と「メニューの項目の許可」の窓が順に出るので、どちらも［許可］を押す。
4. 商品の右クリック・商品ページ・複数選択の帯の「ほかのソフト ▸ Chmonos の見本」に、次の2つが出る。
   - **商品名をコピー**：選んだ商品の名前を1行ずつクリップボードへ入れる
   - **IDを表示**：商品 ID を台本の窓に出す
5. 押すと、Chmonos の下の帯に台本からの返事（「2 件の商品名をコピーしました」など）が出る。

止めるときは Ctrl+C です。止めて60秒たつと、項目は灰色になり「応答していません」と出ます。

## 中でしていること

| 段 | API |
|---|---|
| 動いている Chmonos を探す | `%LOCALAPPDATA%\Chmonos.Shared\running\*.json`（[running-instances.md](../../docs/spec/running-instances.md)） |
| 初めてなら許可を求め、トークンを受け取る | `POST /v1/clients/request` → `POST /v1/clients/request/result` |
| 項目を宣言する（毎回送る。同じ内容なら聞かれない） | `PUT /v1/plugin/menu` |
| 押されるのを待つ | `GET /v1/plugin/events?after=&wait=30&epoch=` を繰り返す |
| 結果を返す | `POST /v1/plugin/events/{seq}/result` |

- トークンは `%LOCALAPPDATA%\ChmonosPluginSample\tokens.json` に保存先ごとに控えます。要らなくなったらフォルダごと消してください（Chmonos の設定の「つないでいるソフト」で取り消すこともできます）。
- Chmonos が起動し直した（`reset`）ときは項目を宣言し直し、許可を取り消された（401）ときは求め直し、Chmonos が閉じたときは探し直します。

## 確かめたこと

2026-11-12、写しの保存先で、許可の窓2つ・カードの右クリックの「IDを表示」・2件選んだ帯の「商品名をコピー」（クリップボードに2行）・下の帯の返事まで、実際のアプリで通した。
