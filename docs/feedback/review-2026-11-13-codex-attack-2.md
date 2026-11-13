# Codex の点検（2026-11-13 の午後・attack 1回）

読むだけの2回と、対応の表は `review-2026-11-13-codex-2.md`。

## 指示書と走らせ方

- 指示書：harness の `external-audit` の `templates/attack.md` に `docs/dev/external-audit.md` を入れ、「今回の範囲」を先頭に足した。範囲は MCP の通訳（`Chmonos.Mcp/`）と、開いている間の監視（`LiveFolderWatch`・`FolderEvents`・`FileSettleProbe`）
- コマンド：使い捨ての作業ツリー（58fa42ac）で先に `dotnet build` してから、`codex exec -c approval_policy='"never"' -s workspace-write -C <作業ツリー> -o <作業用>/codex-attack1.md - < <作業用>/prompt-attack1.md`
- codex-cli 0.160.1・gpt-6.1-sol

## まとめ役の確かめ

- 足したファイルは3つ（`Chmonos.Core.Tests/Robustness/LiveWatchInterleavingTests.cs`・`Chmonos.Mcp.Tests/Robustness/AbnormalApiReplyTests.cs`・`TokenAndArgumentAttackTests.cs`）。禁じた部品（`Process`・`Clipboard`・`Registry`・外への通信・`StoreLocation`・`location.json`・ごみ箱へ送る部品）は無い
- 作業ツリーで走らせ直して、報告と同じ数だった：MCP の試験 75 件中 4 件が失敗、監視の試験 24 件中 1 件が失敗
- 守る場所：本番の保存先 `%LOCALAPPDATA%\Chmonos`・`location.json`・`Chmonos.Shared` は前後で同じ。使う人の保存先の2つのファイルの更新は、ユーザが開いていたアプリの物（`review-2026-11-13-codex-2.md`）
- 足した試験は失敗する物を含むので、まだリポジトリに入れていない。直すときに、その件の試験として入れる

## Codex の報告

以下、Codex の報告そのまま。

追加した27ケースは、**22成功・5失敗**でした。失敗を原因別にまとめると、次の3件です。既存のMCP試験54件・監視試験18件はすべて成功しました。

1. **［重い］隠す商品の一覧が不完全でも回答を続け、隠した商品の情報が出る。**
2. **［中］停止した旧監視の読み取り結果が再開後に混ざり、新着を自動取り込み対象から外す。**
3. **［中］MCPの8,000字上限が、制御文字の多い説明文や横に広いJSONで破れる。**

実装・既存試験は変更していません。失敗する新規試験も残しています。外部通信、アプリ本体のビルド・起動、コミット・プッシュはしていません。

### ［重い］隠す商品の一覧を取得しきれなくても、隠した商品の情報を返す

- 場所：`Chmonos.Mcp/Visibility.cs:44`、同`:55`、同`:70`
- 手順：試験用APIで、隠した商品を20,001件用意した応答を返す。200件ずつ100頁返し、続きの印を残す。通知には20,001件目の商品IDと本文 `SECRET_SYNTHETIC` を入れ、`list_notifications` を呼ぶ。別ケースでは検索応答の `items` を欠落させる。
- 起きること：隠した商品IDと通知本文が、MCPの正常な答えに残る。実際の外部送信は行っていない。
- 根拠：`Visibility.LoadAsync` は100頁で終了しても取得済みIDをそのまま採用する。欠落した `items` も空として採用する。試験 `隠すIDの取得が不完全なら_一覧の秘密を返さない` は両ケースで失敗し、出力に `Found: "SECRET_SYNTHETIC"`。
- 確かさ：**試験で確かめた**。20,001件のケースは続きのある応答、欠落ケースは異常応答。
- 直し方の方向：取得完了を確認できなければ、商品情報を含む一覧への回答を断る。頁数上限到達・必要欄欠落・続きの印の異常を、取得成功と区別する。

### ［中］停止した旧監視の控えが再開後に混ざり、新着を取りこぼす

- 場所：`Chmonos.Core/Scanning/LiveFolderWatch.cs:665`、同`:711`。影響先：`Chmonos.App/ViewModels/MainViewModel.LiveWatch.cs:157`
- 手順：旧監視の初回読み取りを保留する。停止・再開し、新監視が空の控えを確定した後、旧読み取りの結果を完了させる。その結果に含まれるパスを新監視で検出させる。
- 起きること：新監視の開始時にはなかった物が `Arrived=false` になり、自動取り込み対象から外れる。手動の取り込みでは回復できる。
- 根拠：旧セッションの `TakeBaselineAsync` が、読み取り後に停止状態を確認せず、所有者共通の控えへ追加する。試験 `停止した旧監視の遅れた控えは_再開した監視を汚さない` が失敗し、「旧監視の控えが混ざり、新着が Arrived=false になった」。
- 確かさ：**試験で確かめた**。読み取り完了と停止の競合を、制御した非同期処理で再現した。
- 直し方の方向：控えをセッションごとに持つか、反映する錠の中でセッションの世代を照合する。停止済みセッションの結果を新しい控えへ反映しない。

### ［中］答えの8,000字上限を最終出力で保証していない

- 場所：`Chmonos.Mcp/ChmonosTools.cs:321`、同`:331`、`Chmonos.Mcp/AnswerShaper.cs:97`
- 手順：説明文にU+0001を6,000字入れ、`max_chars=6000` で取得する。また、100字の値を持つ欄が1,000個あるJSONを `AnswerShaper.Fit` に渡す。
- 起きること：JSONとしては有効なまま、説明文が**36,089字**、横に広い答えが**68,891字**になる。決めた上限を超えて、使い手の文脈を消費する。
- 根拠：説明文はJSON化する前の本文字数だけを制限する。`Fit` の最後の戻り値には上限確認がない。対応する2試験が上記字数で失敗した。横に広いJSONでは、`blueprintId` の除去自体は成功した。
- 確かさ：**試験で確かめた**。
- 直し方の方向：JSON化後の長さを共通の出口で保証する。説明文は実際に返した本文の長さから `nextOffset` を計算し、収められない答えは小さな省略応答に置き換える。

### 足した試験の一覧

`Chmonos.Core.Tests/Robustness/LiveWatchInterleavingTests.cs`

| 試験名 | 結果 |
|---|---|
| 停止した旧監視の遅れた控えは_再開した監視を汚さない | **失敗**：`Arrived=false` |
| 安定確認の途中で停止すると_知らせずに終了する | 成功 |
| 同名の付け替えと成長が続く間は渡さず_最後の安定した物だけ渡す | 成功 |
| 安定確認の間に消えた物は渡さない | 成功 |
| 書き込みで掴んだ物は渡さず_解放後に出来事なしでも拾う | 成功 |
| フォルダが消えて戻れば_停止した見張りを張り直して拾う | 成功 |

`Chmonos.Mcp.Tests/Robustness/TokenAndArgumentAttackTests.cs`

| 試験名 | 結果 |
|---|---|
| 壊れた控えは無視して許しを取り直せる | 4ケース成功：null行、null配列、途中切断、照合先トークンの改行 |
| 型違いと範囲外は道具の誤りになり_次の呼び出しは動く | 6ケース成功：負数・巨大整数・配列・文字列の真偽値・オブジェクトID・null GUID |
| 存在しないIDと制御文字と巨大IDでも_次の呼び出しは動く | 3ケース成功：不存在、改行・NUL、10万字 |

`Chmonos.Mcp.Tests/Robustness/AbnormalApiReplyTests.cs`

| 試験名 | 結果 |
|---|---|
| 隠すIDの取得が不完全なら_一覧の秘密を返さない | **2ケース失敗**：秘密の本文が残る |
| APIの壊れた応答と切断は道具の誤りになり_次は回復する | 3ケース成功：途中切断JSON・型違い・接続切断 |
| 制御文字の多い説明文も_JSONにした後で8000字以内に収める | **失敗**：36,089字 |
| 横に広いAPIの答えも_秘密を落として8000字以内に収める | **失敗**：68,891字。blueprint ID除去は成功 |
| APIが応答を保留していても_呼び出しを中断して次へ進める | 成功 |

最終実行コマンドは次のとおりです。両対象を再実行しても、同じ失敗を確認しました。

```powershell
dotnet test Chmonos.Mcp.Tests/Chmonos.Mcp.Tests.csproj --no-restore --nologo -m:1 --logger "console;verbosity=normal"
```

```text
テストの実行に失敗しました。
テストの合計数: 75
     成功: 71
     失敗: 4
合計時間: 12.1142 秒
```

```powershell
dotnet test Chmonos.Core.Tests/Chmonos.Core.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~LiveFolderWatchTests|FullyQualifiedName~Robustness.LiveWatchInterleavingTests" --nologo --logger "console;verbosity=normal"
```

```text
テストの実行に失敗しました。
テストの合計数: 24
     成功: 23
     失敗: 1
合計時間: 3.1810 秒
```

実行ログは作業ツリー直下の `mcp-all.log`・`core-live-all.log` に残しました。

### 確かめていない範囲

- APIの**45秒タイムアウトそのもの**。応答保留中の明示的な中断と、その後の回復は確認済み。
- 実際のWindowsファイル監視での出来事の順序、NAS・ドライブ切断。監視試験は偽の出来事と時計を使用。
- UIへ渡した後の自動取り込み実行と二重起動。今回は渡す物の判定まで。
- 異常応答時の生のstdout全体。既存の「MCP通信だけが流れる」試験は成功。
- 今回指定された2領域以外、およびアプリを組む全体試験。本番・第三者データ・購入した実物は読んでいません。
