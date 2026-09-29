# ALCOM（VCC の代わりのツール）との連携の調査

調べた日：2026-09-29（ユーザ指示「ALCOM みたいなツールとの連携もさせたいので情報を取っておいて」）。
調べただけで、アプリのコードは変えていない。ALCOM・VCC・Unity は起動していない（ALCOM は調べた時点でユーザが起動していた。触っていない）。

見た物：
- ソース：[vrc-get/vrc-get](https://github.com/vrc-get/vrc-get) のタグ `gui-v1.1.8`（コミット `5a49167`、2026-07-11）。未公開の変更は master の `CHANGELOG-gui.md`（2026-09-29 時点）
- 公式サイト：<https://vrc-get.anatawa12.com/ja/alcom/>
- VCC 2.2.0 のお知らせ：<https://vcc.docs.vrchat.com/news/release-2.2.0/>（「Important notes for tool developers」）

今の Chmonos の作り（比べる元）：`App/Services/VccLaunch.cs`（VCC を起動するだけ）、`Core/Services/UnityProjects.cs`（Unity Hub の `projects-v1.json` と VCC の `settings.json` の `userProjects` を読む）、`docs/spec/unity.md`「プロジェクトを開く・VCC」、`docs/history/unity-handoff.md` §12。

---

## 1. ALCOM と vrc-get とは（公開資料から）

| | ALCOM | vrc-get |
|---|---|---|
| 形 | 画面のあるアプリ（Tauri＝Rust＋WebView2） | コマンドライン |
| 作者 | anatawa12 ほか（コミュニティ。**VRChat の公式ではない**と README・サイト・BOOTH 用の説明に書いてある） | 同じ |
| ライセンス | MIT（リポジトリ直下の `LICENSE`。全部の crate がこれを継ぐ） | 同じ |
| 版の出方 | タグ `gui-v*`。1.1.6（2026-06-02）→ 1.1.7（07-10）→ 1.1.8（07-11）。前に beta・rc が出る | タグ `v*`。最新 1.9.2（2026-07-12） |
| 対応 | Windows（10 21H2 以降・11 23H2 以降）・macOS・Linux | 同じ |

**できること**（サイト・README・ソース）：プロジェクトの一覧・作成・追加・削除、VPM パッケージの追加・更新・削除（まとめて）、CHANGELOG の表示、
リポジトリ（VPM のカタログ）の追加・削除、Unity の起動、バックアップ、Unity 2022 への移行、テンプレート（下の §3-3）、多言語（日本語あり）。

**VCC との違い**：VCC の主な機能を持ち、速い。**VCC と同じ設定ファイルを読み書きする**ので移し替えは要らず、両方を入れたまま使える（サイトと BOOTH 用の説明文 `vrc-get-gui/booth/booth-description.md`）。
VCC に無い物：`.alcomtemplate`（VPM と unitypackage を入れたプロジェクトの型）、Unity に渡す引数をプロジェクトごとに変える、Linux・macOS。

**入れ方**：公式サイトのインストーラ（推し）、winget（`anatawa12.ALCOM`）、Scoop（`babo4d/scoop-xrtools` の `vrc-alcom`）、Homebrew・AUR・MacPorts。BOOTH にも置いてある（中身はサイトへのリンク）。
vrc-get は winget（`anatawa12.vrc-get`）・Scoop・Homebrew・cargo など。

## 2. 置き場所

### 2-1. 実行ファイル（Windows）

- インストーラは Inno Setup。`AppId={4C3D0631-AE29-4D20-A231-678D9CF8D6DB}`、既定の入れ先 `{autopf}\ALCOM`（人ごとの入れ方なら `%LOCALAPPDATA%\Programs\ALCOM\`）、実行ファイルは `ALCOM.exe`（`bundle/windows-setup.iss`）
- アンインストール情報は `…\Uninstall\{4C3D0631-…}_is1`。`DisplayName` は「ALCOM バージョン 1.1.8」のように**言語で変わる**。`Publisher` は `anatawa12`、`DisplayIcon` は `ALCOM.exe`
- **古い版（NSIS のころ）の入れ先は `%LOCALAPPDATA%\ALCOM\`**。Inno Setup に替えたとき、前の場所に上書きするようにしてあり、移さない（同 `.iss` のコメント）。つまり今も両方の場所があり得る
- 入れ先はインストーラで変えられる（`DisableDirPage=no`）。決め打ちせずアンインストール情報から引く必要がある（VCC と同じ）
- 1つだけ起動する作り（`tauri_plugin_single_instance`）。2回目に起動すると、**先に開いている窓を最小化から戻して手前に出し**、引数を渡して終わる（`src/main.rs`）。VCC は手前に出さなかった（unity-handoff §12-2）のと違う。Windows が手前に出すのを断る場合があるかは**要確認**

### 2-2. 設定とデータ

**VCC と同じフォルダ `%LOCALAPPDATA%\VRChatCreatorCompanion\` を使う**（`vrc-get-vpm/src/io/tokio.rs`）。自分だけの物はその下の `vrc-get\` に置く。

| ファイル | 誰の物 | 中身 |
|---|---|---|
| `settings.json` | VCC と共有（**ALCOM も書く**） | Unity の場所・`userProjects`（プロジェクトのパスの配列）・`userRepos`（足したリポジトリ）など。鍵の一覧は §4-2 |
| `vcc.liteDb` | VCC と共有（ALCOM も書く） | LiteDB（.NET の埋め込み DB）。コレクション `projects`（`Path` で索引）。プロジェクトごとの Unity の場所・引数・最終更新もここ |
| `Repos\*.json` | 共有 | リポジトリの中身の写し |
| `Templates\`・`VRCTemplates\` | 共有 | プロジェクトの型 |
| `vrc-get\gui-config.json` | ALCOM だけ | 画面の設定（言語・並べ方・`useAlcomForVccProtocol`・`defaultUnityArguments` など） |
| `vrc-get\vcc-settings-backup.json` | ALCOM だけ | `settings.json` の控え。**`settings.json` を書くたびに同じ物をここにも書く**（1.1.6 から。壊れたときの戻し用） |
| `vrc-get\templates\*.alcomtemplate` | ALCOM だけ | 取り込んだテンプレート |
| `vrc-get\settings.json` | vrc-get だけ | 公式・curated のリポジトリを無視する設定（「試験中」と書いてある） |
| `%LOCALAPPDATA%\com.anatawa12.vrc-get-gui\` | ALCOM だけ | WebView2 の置き場（画面の部品のキャッシュ。連携には関係ない） |

**プロジェクトの一覧は2か所ある**（VCC 2.2.0 のお知らせと `vpm_settings.rs` のコメント）：
- VRChat は 2.2.0 で **`settings.json` の `userProjects` を「非推奨」にし、`vcc.liteDb` へ移した**。当面は互換のため両方に読み書きし続けると言っている
- vrc-get は「今は `settings.json` が正、将来の VCC は `userProjects` を消して `vcc.liteDb` が正になる」と見て、`userProjects` が無ければ写さない・書くときは必ず `userProjects` を出す作りにしている
- `vcc.liteDb` を開くときは、LiteDB の共有エンジンと同じ名前の OS のミューテックス（`Global\<パスの小文字の SHA1>.Mutex`）を取る（`litedb.rs`）

**VCC が入っていないとき**（2026-09-29 にソースで確かめた。ユーザの問い：「vccが無い場合はどうなるのでしょうか？」）：
- 置き場所は **VCC の有無にかかわらず決め打ち**で、Windows ではいつも `%LOCALAPPDATA%\VRChatCreatorCompanion\`（`vrc-get-vpm/src/io/tokio.rs` の `new_default`。VCC が入っているかは見ない）
- 読む順（`environment/settings.rs` の `Settings::load`）：`settings.json` → 無ければ自前の控え `vrc-get\vcc-settings-backup.json`（戻したときは「VCC の設定が無いか壊れていたので控えから戻した」と知らせる）→ それも無ければ空の設定。`vcc.liteDb` も無ければ空から始める（`litedb.rs`）
- 保存すると、同じ場所に `settings.json` と `vcc.liteDb` を VCC と同じ形で作る
- **Chmonos への意味**：ALCOM だけを使う人でも、プロジェクトの一覧は同じ場所・同じ形に入るので、今の読み方で読める。違いは「VCCを開く」で、VCC が無いと「見つからない」になり ALCOM は開けない（案 A で解ける）

## 3. 外から呼ぶ口

### 3-1. URL（`vcc://`）

- **ALCOM は自前の URL（`alcom://` など）を持たない。**受けるのは `vcc://` だけ（`deep_link_support.rs`）
- 受ける形は **`vcc://vpm/addRepo?url=<http(s) の URL>` の1つだけ**（`headers[]=名前:値` を足せる）。リポジトリを足す窓が出る。それ以外は捨ててログに書く
- `vcc://` を引き受けるかは ALCOM の設定「vcc スキームに ALCOM を使う」（`gui-config.json` の `useAlcomForVccProtocol`）。**入れただけでは奪わない。**オンにすると `HKCU\Software\Classes\vcc` を `"ALCOM.exe" link "%1"` に書き換える。**オフにすると `HKCU\Software\Classes\vcc` を丸ごと消す**（VCC の登録に戻さない。VCC 側がいつ登録し直すかは**要確認**）
- 0.1.10-beta.4 から、アンインストールのときに `vcc:` の登録から ALCOM を外す（CHANGELOG）
- `vrc-get:` という名前の仕組みもあるが、ALCOM の画面の中だけの物（Tauri の `register_uri_scheme_protocol`）で、OS には登録されない

### 3-2. コマンドライン引数（`ALCOM.exe`）

`src/main.rs` の `process_args` が見るのは次だけ（1.1.8・master とも同じ）：
- `ALCOM.exe vcc://…` または `ALCOM.exe link vcc://…`：上の `vcc://` と同じ
- `ALCOM.exe <ファイル>.alcomtemplate`：テンプレートを取り込む（§3-3）
- それ以外は「Unknown command」とログに書いて何もしない

**プロジェクトを開く・パッケージを足す引数は無い。**外から「このプロジェクトを ALCOM で開いて」とは頼めない。

### 3-3. テンプレート（`.alcomtemplate`）

1.1.0（2025-06-19）から。インストーラが拡張子 `.alcomtemplate` を `ALCOM Project Template`（`"ALCOM.exe" "%1"`）に結び付ける。
開くと `vrc-get\templates\` に取り込み、ALCOM の「新しいプロジェクト」で選べるようになる（開いただけではプロジェクトは作られない）。形（`src/templates/alcom_template.md`。JSON、コメント不可）：

| 鍵 | 型 | 意味 |
|---|---|---|
| `$type` | 文字列 | `com.anatawa12.vrc-get.custom-template` 固定 |
| `formatVersion` | 文字列 | `"1.0"`。互換の無い変更で上が増える |
| `displayName` | 文字列 | 表示名 |
| `updateDate` | 文字列 | 省略可 |
| `id` | 文字列か null | 別のテンプレートの元にするときの ID |
| `base` | 文字列 | 元の型。`com.anatawa12.vrc-get.vrchat.avatars`（アバター）・`…vrchat.worlds`・`…blank` |
| `unityVersion` | 文字列 | 版の範囲（`2022.x.x` など） |
| `vpmDependencies` | 物 | 入れる VPM パッケージと版の範囲。省略可 |
| `unityPackages` | 文字列の配列 | **入れる unitypackage の絶対パス**。省略可 |

unitypackage は **Unity を起動せず ALCOM 自身が展開する**（`templates.rs` の `import_unitypackage`。`Library\.temp-dir.<乱数>\` に広げてから移す）。

### 3-4. vrc-get の CLI

- `vrc-get install <pkg> [版] -p <プロジェクト> -y`（`i`）・`remove`・`upgrade`・`outdated`・`search`・`repo add <url>`・`repo list` など
- JSON を出せるのは `vrc-get info project -p <プロジェクト> --json-format 1`（Unity の版と、パッケージごとの入っている版・固定された版）・`vrc-get info package <pkg> --json-format 1`・`vrc-get outdated --json-format 1`
- プロジェクトの一覧・Unity の一覧（`vrc-get vcc project list` など）は **`experimental-vcc` のときだけで、配布されている実行ファイルでは使えない**（`commands.rs`）
- ALCOM とは別に入れる必要がある（ALCOM に同梱されていない）

## 4. この PC に入っている物（2026-09-29・読んだだけ）

### 4-1. 入っている物

| | 場所 | 版 | 手掛かり |
|---|---|---|---|
| ALCOM | `%LOCALAPPDATA%\Programs\ALCOM\ALCOM.exe`（中身は `ALCOM.exe`・`unins000.exe`・`unins000.dat` の3つ） | 1.1.8（ファイルの版も 1.1.8） | `HKCU\…\Uninstall\{4C3D0631-…}_is1`。**今日 21:48 に入れて起動した**（`unins000.dat` と起動時刻） |
| VCC | `%LOCALAPPDATA%\Programs\VRChat Creator Companion\` | 2.4.5 | `HKCU\…\Uninstall\{A20FE4C3-…}_is1`（`DisplayIcon` は空。§12 と同じ） |
| vrc-get（CLI） | 無い（PATH・winget・scoop・cargo のどれにも無い） | | |
| 古い ALCOM の場所 `%LOCALAPPDATA%\ALCOM\` | 無い | | |

URL と拡張子の登録：
- `HKCU\Software\Classes\vcc` → **VCC の `CreatorCompanion.exe "%1"` のまま**（ALCOM は奪っていない。`useAlcomForVccProtocol` は false）
- `alcom:` の登録は無い
- `HKCU\Software\Classes\ALCOM Project Template` → `"ALCOM.exe" "%1"`、`.alcomtemplate` に `OpenWithProgids` として結び付け

### 4-2. 設定とデータの形（鍵と型と件数だけ。値は書かない）

`%LOCALAPPDATA%\VRChatCreatorCompanion\`：`settings.json`（3.5KB）・`vcc.liteDb`（約150KB）・`Repos\`（82ファイル）・`Logs\`・`ProjectBackups\`・`Templates\`・`VRCTemplates\`・`VRChatProjects\`・`vrc-get\`。
**ALCOM が起動して 1 分ほどで `settings.json` と `vcc.liteDb` を書き直していた**（21:49。`vrc-get\vcc-settings-backup.json` は `settings.json` と中身が同じ）。

`settings.json` の鍵：`pathToUnityExe`（文字列）・`pathToUnityHub`（文字列）・**`userProjects`（文字列の配列・12件）**・`unityEditors`（配列・0件）・`preferredUnityEditors`（物。鍵は Unity の年）・`defaultProjectPath`・`lastUIState`（数）・`skipUnityAutoFind`（真偽）・`userPackageFolders`（配列・0件）・`windowSizeData`（`width` `height` `x` `y`）・`skipRequirements`・`lastNewsUpdate`（日時）・`allowPii`・`projectBackupPath`・`showPrereleasePackages`・`trackCommunityRepos`・`selectedProviders`（数）・`lastSelectedProject`（文字列）・**`userRepos`（物の配列・7件。各 `localPath` `name` `url` `id`）**。

`vrc-get\gui-config.json` の鍵：`guiHiddenRepositories`（配列）・`hideLocalUserPackages`・`windowSize`（`width` `height`）・`fullscreen`・`language`・`theme`・`backupFormat`・`projectSorting`・`releaseChannel`・`useAlcomForVccProtocol`・`setupProcessProgress`（数）・`defaultUnityArguments`（null か文字列の配列）・`logsLevel`（配列）・`guiAnimation`・`guiCompact`・`projectViewMode`・`unityHubAccessMethod`（`ReadConfig`＝Hub の設定を読む／`CallHub`＝Hub を裏で動かす）・`recentProjectLocations`（配列）・`excludeVpmPackagesFromBackup`・`favoriteTemplates`（配列）・`lastUsedTemplate`。
個人の値でない物だけ：`useAlcomForVccProtocol`=false、`unityHubAccessMethod`=ReadConfig、`releaseChannel`=stable。`vrc-get\templates\` は無い（テンプレートは未取り込み）。

`vcc.liteDb` の中は開いていない（LiteDB の読み方を持っていないのと、ALCOM が開いている最中だったため）。

### 4-3. 今の Chmonos への影響（事実）

- **「VCCを開く」は今も VCC を開く。**`VccLaunch.FindExe` はアンインストール情報の `DisplayName` が「VRChat Creator Companion」で始まる物を先に見るので、ALCOM を見つけない。ALCOM だけが入っていて `vcc://` を奪っている PC では、3番目の手掛かり（`vcc://` の関連付け）で `ALCOM.exe` を起動する——`ALCOM.exe` は引数なしなら普通に開くので、これで動くはず（**要確認**：実際に試していない）。起動中かはプロセス名 `ALCOM` で見る（見つけた実行ファイルの名前でも見る作りなので、そのとき効く）
- **プロジェクトの一覧は、ALCOM で足した物も入る。**ALCOM も `settings.json` の `userProjects` を必ず書くため。ただし VRChat はこの欄を非推奨にしている（§2-2）。将来の VCC がこの欄を消すと、Chmonos は VCC 側の一覧を読めなくなる（Hub 側の一覧は残る）
- Chmonos は `Temp/UnityLockfile` が**在るか**で「開いているかも」を見ている。ALCOM は同じファイルが**錠を掛けられているか**（`LockFileEx` で試す）で見ている（`project.rs` の `is_unity_running`）。落ちて残ったファイルに惑わされない方法として参考になる

## 5. Unity を開くときの ALCOM の動き（ソースから）

- プロジェクトの `ProjectVersion.txt` の版と**同じ版**の Unity を、ALCOM が知っている Unity の一覧（Hub の設定を読む）から探す。1つならそれ、複数なら選ばせ、選んだ物を `vcc.liteDb` のプロジェクトに覚える
- 無ければ、中国版（`c1`）と国際版を入れ替えて探す。それも無く、版のハッシュが分かれば `unityhub://<版>/<ハッシュ>` で Hub から入れるよう案内する
- 起動は `Unity.exe -projectPath <パス>` に、プロジェクトごとの引数（`vcc.liteDb`）→ 無ければ `defaultUnityArguments` → それも無ければ何も足さない（0.1.17 で `-debugCodeOptimization` を外した）
- 開いているとき（錠が掛かっているとき）は起動せず「Unity は既に動いています」と出す。**未公開の master には「開いている Unity の窓を手前に出す」が入った**（#3109。次の版で出るはず）

Chmonos の `UnityLaunch.OpenProject` とほぼ同じ考え方（版を合わせる・開いていれば手前に出す）。

## 6. 連携の案（作るかはユーザが決める）

| 案 | 何ができるか | 手間 | 危うさ |
|---|---|---|---|
| **A. 「VCCを開く」を ALCOM にも向ける** | 入っている方を開く。両方なら `vcc://` を引き受けている方（利用者が選んだ方）→ それも決まらなければ VCC。ボタンの名前は「VCC を開く」のままか、見つけた物の名前にするか（文言は `ui-wording`） | 小（`VccLaunch` にアンインストール情報の `Publisher=anatawa12` か `AppId` の手掛かりとプロセス名 `ALCOM` を足す。古い場所 `%LOCALAPPDATA%\ALCOM\` も見る） | 小。起動するだけで中身に触らない。`DisplayName` は言語で変わるので、見分けは `AppId` か `Publisher` で |
| **B. `vcc://` で VPM のリポジトリを足す** | VPM で配られている商品（BOOTH の説明に VPM のリポジトリの URL が載っている物）に「VCC に追加」を出し、`vcc://vpm/addRepo?url=…` を開く。VCC でも ALCOM でも、引き受けている方が窓を出す | 小〜中（URL を商品のどこから拾うかが要る。今の spec は「VPM で配られる物は扱わない」） | 小。追加するかは VCC・ALCOM の窓で人が決める。URL の形は VCC の公式の物 |
| **C. `.alcomtemplate` を書き出す** | 改変（アバター＋衣装）に紐付いた unitypackage の絶対パスを `unityPackages` に、使う VPM（Modular Avatar など）を `vpmDependencies` に入れたテンプレートを作り、`ALCOM.exe` で開いて取り込ませる。ALCOM で「新しいプロジェクト」を作ると、それらが入った状態で出来る | 中（形は §3-3 の表だけ。unitypackage の場所は Chmonos が知っている） | 中。**非公式の形**（`formatVersion` 1.0。1.1.4 でも直しが入った）。ALCOM を持つ人だけ。パスが変わると作れない。unitypackage を Unity でなく ALCOM が展開する（Unity で入れたのと同じになるかは**要確認**） |
| **D. VCC の一覧を `vcc.liteDb` からも読む** | VCC が `userProjects` を消したあとも一覧が出る | 中（LiteDB の .NET 版を入れる。VCC・ALCOM と同じミューテックスを取るか、写しを読む） | 中。VCC の内部の形。読むだけに留める。**書かない** |
| **E. プロジェクトの VPM を見る（vrc-get CLI）** | `vrc-get info project --json-format 1` で、送り先のプロジェクトに入っている VPM（MA・AAO など）と版を出す。「このプロジェクトには Modular Avatar が無い」と言える | 中（CLI を別に入れてもらう。無ければ出さない）。`vpm-manifest.json`・`Packages/*/package.json` を直に読む方が軽い（**要確認**：形は VPM の公開仕様） | 小〜中。CLI の JSON は版付き（`--json-format 1`）で、変わっても番号で分かる |
| **F. vrc-get で VPM を入れる** | Chmonos から VPM を足す | 中〜大 | 大。**プロジェクトを書き換える**（Unity を開いている間は危ない）。今の方針（中身を管理しない）から外れる。推さない |

**できないこと**：ALCOM に「このプロジェクトを開いて」と頼む口が無い（§3-2）。改変に紐付けたプロジェクトは、今どおり Chmonos から Unity を直に開くのがよい。ALCOM の画面で同じプロジェクトを出したいなら、ALCOM を開くだけ（案 A）になる。

**案 A は作った（2026-09-29。ユーザ指示「ALCOMからも開けるようにしましょう。」）。**ボタンは表の案と違い、名前を見つけた物に合わせる形にした：VCC だけ「VCCを開く」、ALCOM だけ「ALCOMを開く」、両方なら2つ並べる、どちらも無ければ「VCCを開く」を押せない形で「VCCかALCOMを入れると、ここから開けます。」。
`vcc://` の先が ALCOM のときは VCC の候補にしない（§4-3 の3番目の手掛かりは ALCOM の方で使う）。ALCOM を実際に起動する確かめはまだ（ALCOM・VCC を起動しない約束で作った）。今の決め事は `docs/spec/unity.md`「プロジェクトを開く・VCC」。

**おすすめ**：A を先に（小さく、今の「起動するだけ」の方針のまま、ALCOM を使う人のボタンが生きる）。B と C は、VPM で配られる物・改変からプロジェクトを作る流れを扱うと決めたときに。D は VCC が `userProjects` を実際に消したら。

## 7. 連携するときに気を付けること

- **非公式のツール。**VRChat は ALCOM を支えていない。VCC の設定の形も公開仕様ではない（VCC 2.2.0 のお知らせくらい）。どちらの形もいつ変わってもおかしくない。Chmonos は**読むだけ・書かない**（今の作りのまま）。読めなければ候補が減るだけで済ませる（`UnityProjects` はそうなっている）
- **VCC と ALCOM は同じ `settings.json` と `vcc.liteDb` を書く。**両方を同時に開くと、片方の変更が片方に上書きされ得る（ALCOM 側に「外で書き換えられたので読み直して」と出す直しがある。`packages.rs`）。Chmonos が書き手を増やさないこと
- `vcc://` の持ち主は ALCOM の設定1つで入れ替わり、外すと登録が消える。`vcc://` の関連付けを「VCC の場所」の手掛かりにしている所（`VccLaunch.FindExe` の3番目）は、ALCOM を指すことがある——今の考え（利用者が選んだ方を開く）ならそれでよい
- 仕様が変わりやすい所：テンプレートの形（1.1 で入ったばかり）、`gui-config.json`（ALCOM だけの物で、互換の約束が無い）、Windows の入れ先（NSIS から Inno Setup に替わった）、`userProjects` の非推奨
- ALCOM を実際に動かす確かめ（案 A の起動・`.alcomtemplate` の取り込み）はまだしていない。するときはユーザに告げてから
