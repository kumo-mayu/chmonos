# Unityへの受け渡し（調査）

「unitypackageをUnityへ流し込めるか」の調査結果。**2026-09-10 に実機で測った。**

**できる。**しかもzipから取り出す手間すら要らない——
**OS側（ShellExecute）がzipの中のファイルを直接開ける。**

最初はUnityのCLIばかり見て「外から押し込む道は無い」と結論しかけたが、
**それは調べる場所を間違えていた**（ユーザ指摘）。
Explorerでzipを覗いてunitypackageをダブルクリックすると取り込みが始まる——
あれはUnityの機能ではなくWindowsの機能で、C#から同じことが叩ける。

---

## 1. 結論

```csharp
var psi = new ProcessStartInfo
{
    // zip の中を「フォルダ」として指す仮想パス。Windowsが解決する
    FileName = @"...\HeartBeatGimmick_v3.0.3.zip\なめらか心音ギミックv3.0.3\VRCHeartRate_Installer.unitypackage",
    UseShellExecute = true,   // ← これが要る。false だと素のCreateProcessで解決できない
};
Process.Start(psi);
```

これで**起動中のUnityエディタに取り込みダイアログが出る。**
zipを展開する処理も、一時ファイルを置く場所も、後始末も要らない。

---

## 2. 測ったこと

環境は Unity 2022.3.22f1（VRChat向け・Hub管理）。

### 2-1. 開いているエディタに届く

`.unitypackage` の関連付けは `Unity.exe -openfile "%1"`。
これを叩くと**新しいUnity.exeが立ち上がり、起動中のエディタへ転送して自分は終了する。**

| 見たもの | 結果 |
|---|---|
| ShellExecute直後 | 新プロセス（8116）が起動 |
| 20秒後 | 8116は消え、元のエディタ（39608）だけ |
| エディタの子ウィンドウ | **`Import Unity Package`** |
| Importを押した後 | `Assets/Sig_Ring_07/` に45ファイル |

**これが一番効く。**CLIの `-importPackage` は開いていると
`exit=21` で弾かれる（2-4）が、この経路は**開いているからこそ届く。**

### 2-2. zipの中を直接指せる

`Shell.Application` の `Item.Path` が
`<zip>\<中のフォルダ>\<ファイル>` という仮想パスを返す。
**これをそのまま `ShellExecute` に渡せる。**

| 試した形 | 結果 |
|---|---|
| zip直下のファイル | **通る**。45→120ファイル（`nHaruka/`）が着地 |
| zip内のフォルダの下 | **通る** |
| **フォルダ名が日本語** | **通る**（`なめらか心音ギミックv3.0.3`） |

日本語が通るのは必須条件だった。手元のzipは大半が日本語名。

**`Shell.Application.InvokeVerb("Open")` は使わない。**
同じことができそうに見えるが、**呼んだプロセスが終わると流れない**
（非同期で、STAで待たせても効かなかった）。`ShellExecute` の方が素直で確実。

### 2-3. Unityが開いていないと何も起きない

エディタを落とした状態で同じことをすると、
**Unity Hubの窓が開くだけで、取り込みは始まらない。**
どのプロジェクトに入れればよいか決まらないので当然ではある。

**押す前に、開いているかを見て言い分けること。**
`Unity.exe` の起動引数から `-projectPath` を読めば、
**どのプロジェクトが開いているかまで分かる。**

```
開いている: D:\work\vrchat\VRChatProjects\kip01
```

つまり画面に「**Unityの〈kip01〉に送ります**」と書ける。
「どこに入るか分からないまま送る」を避けられる。

### 2-4. 閉じているプロジェクトへはCLIで入る（別の道）

```
Unity.exe -projectPath <proj> -importPackage <pkg> -batchmode -quit -nographics
```

| | |
|---|---|
| 閉じている | **成功**（exit 0・4秒・75ファイル） |
| 開いている | **exit=21**「another Unity instance is running with this project open」 |

文献の「batchmodeでは入らない」
（[Unity Discussions](https://discussions.unity.com/t/command-line-importpackage-doesnt-extract-files-in-batch-mode/634281)）
は 2022.3.22f1 では再現しなかった。ただし試したのは空のプロジェクトで、
VRCSDK入りでは再走査と再コンパイルが挟まるのでこの4秒は当てにならない。

**この道は要らない。**2-1があれば足りるし、
「閉じているときだけ使える」機能は説明が難しい。

---

## 3. 渡すものの実態

手元の購入物12件（`DLforTest/`）を数えた。

| | 件数 |
|---|---|
| zipで来る | 12 / 12 |
| zipの中に `.unitypackage` がある | **10 / 12** |
| うち `.unitypackage` が**2つ**入っている | **2 / 10** |

**1つとは限らない。**2件で2つ入っていて、どちらも片方が依存物だった。

| zip | 中身 |
|---|---|
| HeartBeatGimmick_v3.0.3 | 本体 ＋ `VRCHeartRate_Installer` |
| Kuuta_ShapekeyAddon | 本体 ＋ `BlendShare-0.0.10-User` |

**順番がある。**依存物を先に入れないと本体が通らない。

だから「**zipを送る**」ではなく「**zipの中から選んで送る**」になる。
幸い、2節の経路は初めから1ファイル単位なので素直に乗る。
`.unitypackage` を含まない2件（`.psd` `.png` だけ）は、そもそも送る対象に出さない。

---

## 4. 決まっていないこと

### 4-1. ダイアログに出る名前に `[1]` が付く

zipから直接開くと、Unityの取り込みダイアログの見出しが
`VRCHeartRate_Installer[1]` になる。
シェルが一時展開するときに付ける名前がそのまま出ている。

**自前で取り出せば名前はこちらの指定通りになる**（試したときは `probe` と出た）。
つまり選べる。

| | zipから直接 | 自前で取り出す |
|---|---|---|
| 実装 | `ShellExecute` 1回 | 取り出す・置き場所・後始末が要る |
| 見出し | `名前[1]` | 名前そのまま |
| 一時ファイル | OSが持つ | こちらが持つ |

**`[1]` を許すなら実装はほぼ無い。**まずそれで作って、
気になるようなら自前に切り替えればよい。切り替えは呼ぶパスを変えるだけ。

### 4-2. エディタが複数開いているとどこへ行くか

未確認。プロジェクト違いで2つ開くことは普通にある。
2-3の検出で「開いているエディタが複数あります」とは言えるので、
**分かるまでは、複数あるときは送らずにその旨を出す**のが安全。

### 4-3. 一時ファイルの寿命

シェルが展開した実体を `%TEMP%` にも `INetCache` にも見つけられなかった。
それでも**Importは最後まで通った**ので実用上は問題ない。
ただし「ダイアログを長時間放置してから押す」は試していない。

---

## 5. 先行例

同種のツールは**取り込みの側をUnity Editor拡張として作っている。**

| ツール | 形 |
|---|---|
| [ゆにあせ（UniAsset）](https://booth.pm/ja/items/6748191) | Unity Editor Asset Browser |
| [VRC Avatar Explorer](https://booth.pm/ja/items/6372968) | 外部アプリ ＋ Unity側の Window メニュー |
| [BOOTH Package Manager](https://booth.pm/ja/items/7321369) | Unityだけで完結。データはJSON |

**拡張を作れば取り込み先や順番まで作り込めるが、配布物がもう1つ増える。**
2節の経路はその手前で止まる代わりに、**このアプリだけで完結する。**
まずこちらで作り、足りなければ拡張を考える。

---

## 6. 測っていないこと

- 実プロジェクト（VRCSDK入り）での取り込み時間
- エディタが複数開いているときの行き先（4-2）
- VPMパッケージ形式で配布されるもの。手元の12件には無かったが、
  新しいものはこちらで来る可能性がある
