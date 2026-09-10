# Unityへの受け渡し（調査）

「unitypackageをUnityへ流し込めるか」の調査結果。**2026-09-10 に実機で測った。**
結論だけ先に書くと、**外から押し込む道は無い。Unity側に窓口を置くしかない。**

---

## 1. そもそも何を渡すことになるのか

手元の購入物12件（`DLforTest/`）を数えた。

| | 件数 |
|---|---|
| zipで来る | 12 / 12 |
| zipの中に `.unitypackage` がある | **10 / 12** |
| うち `.unitypackage` が**2つ**入っている | **2 / 10** |

**渡すものはzipの中にある。**まず取り出す手順が要る。

**1つとは限らない。**2件で2つ入っていて、どちらも片方が依存物だった。

| zip | 中身 |
|---|---|
| HeartBeatGimmick_v3.0.3 | 本体 ＋ `VRCHeartRate_Installer` |
| Kuuta_ShapekeyAddon | 本体 ＋ `BlendShare-0.0.10-User` |

**順番がある。**依存物を先に入れないと本体が通らない。
「zipを渡せば入る」とは言えない——**どれを、どの順で入れるかは人が決める。**

`.unitypackage` を含まない2件は `.psd` と `.png` だけ。Unityに入れるものではない。

---

## 2. 測ったこと

環境は Unity 2022.3.22f1（VRChat向け・Hub管理）。

### 2-1. 閉じているプロジェクトへは入る

```
Unity.exe -projectPath <proj> -importPackage <pkg> -batchmode -quit -nographics
```

| | |
|---|---|
| 結果 | **成功**（exit 0） |
| 時間 | 4秒 |
| 入った量 | 75ファイル |

**文献の「batchmodeでは入らない」は再現しなかった。**
[Unity Discussions](https://discussions.unity.com/t/command-line-importpackage-doesnt-extract-files-in-batch-mode/634281)
に不具合の報告があるが、2022.3.22f1 のこの経路では通った。
ただし試したのは**空のプロジェクト**。VRCSDKの入った実プロジェクトでは
アセット再走査とスクリプト再コンパイルが挟まるので、この4秒は当てにならない。

（空プロジェクトの作成自体は `-createProject` で5秒。）

### 2-2. 開いているプロジェクトへは入らない

同じコマンドを、そのプロジェクトをエディタで開いたまま実行した。

```
Aborting batchmode due to fatal error:
It looks like another Unity instance is running with this project open.
Multiple Unity instances cannot open the same project.
```

**exit=21。**1プロジェクトにつきエディタは1つという制約
（[Unity Support](https://support.unity.com/hc/en-us/articles/40828087523092-Resolving-the-The-project-is-currently-open-in-the-Unity-Editor-Please-close-it-in-the-Editor-to-proceed-with-this-operation-Error)）
がそのまま効く。

**これが痛い。**想定している場面は「Unityでアバターを組んでいる最中に、
管理アプリで見つけたものを送る」で、**そのときUnityは開いている。**
つまり本命の場面でCLIは使えない。

### 2-3. 割り込む口も無い

エディタが開いている名前付きパイプを見た。

```
Unity-LicenseClient-kumom            ライセンス
Unity-Upm-29644                      パッケージマネージャ（プロセス毎）
Unity-ShaderCompilerIPC-29644-40608  シェーダ
Unity-hubIPCService                  Hubとの通信
```

**「これを取り込め」と言える公開の口は無い。**`Unity-hubIPCService` は
Hub専用で、公開された仕様ではない。ここに乗るのは、Unityの更新で
黙って壊れる作りを選ぶということ。

`.unitypackage` の関連付けは `Unity.exe -openfile "%1"`。
**これが起動中のエディタへ転送されるかは未確認**——試すと
ユーザの実プロジェクトにダイアログが出る可能性があったので踏み込んでいない。
仮に転送されたとしても「**どのプロジェクトに入るか選べない**」ので、
これに頼る設計にはしない。

---

## 3. 先行例が同じ結論に着いている

同種のツールを調べたところ、**取り込みの側は例外なくUnity側の拡張**だった。

| ツール | 形 |
|---|---|
| [ゆにあせ（UniAsset）](https://booth.pm/ja/items/6748191) | Unity Editor Asset Browser（エディタ内の窓） |
| [VRC Avatar Explorer](https://booth.pm/ja/items/6372968) | 外部アプリ ＋ Unity側の Window メニュー |
| [BOOTH Package Manager](https://booth.pm/ja/items/7321369) | Unityだけで完結。データはJSON |

**外から押し込んでいるものは1つも無い。**外部アプリはJSONを書き、
Unity側の拡張がそれを読んで `AssetDatabase.ImportPackage` を呼ぶ。

VRC Avatar Explorer が「データフォルダのパスを設定させる」のも同じ理由で、
**受け渡しはファイル経由**になっている。

---

## 4. 取れる道

### 案A：Unity側に窓口を置く（先行例と同じ）

このアプリの保存先はもともと人が読めるJSONで、
`%LOCALAPPDATA%\BoothAssetManager\items\*.json` にある。
**Unity側の拡張がこれを直接読めば、受け渡しの仕組みを別に作らなくてよい。**
「JSONは人が読める形を保つ」を守ってきたことが、そのまま効く。

- できること：開いている最中に入れられる。依存の順番も画面で見せられる
- 要るもの：**別のプロジェクト**（Unity Editor拡張）。配布も別
- 手前で要ること：zipから `.unitypackage` を取り出す場所を決める

### 案B：閉じているプロジェクトにだけ入れる

`-projectPath ... -importPackage` をそのまま使う。実装は軽い。

- できること：測った通り確実に入る
- できないこと：**本命の場面で使えない**（2-2）
- 「開いているので入れられません。閉じてから押してください」と言うことになる

### 案C：Explorerで場所を開くだけ

zipから取り出して、そのフォルダをExplorerで選択状態にして開く。
人がUnityへドラッグする。

- できること：今日から作れる。壊れない。開いていても関係ない
- できないこと：**取り込みではない**。手数は減るが自動ではない

---

## 5. 判断が要るところ

**案Aは、このアプリの外にもう1つ作るという判断。**
配布も更新も別建てになる。「BOOTHで買ったものを手元で管理する」という
今の範囲を超えるので、勝手には決めない。

**案Cは案Aの手前として無駄にならない。**どちらにせよ
「zipから取り出してどこに置くか」は要るので、そこから作れば
案Aへ進んでも捨てずに済む。

---

## 6. 測っていないこと

- 実プロジェクト（VRCSDK入り）での取り込み時間
- `-openfile` が起動中のエディタへ転送されるか（3節の理由で未実施）
- VPMパッケージ形式で配布されるもの。手元の12件には無かったが、
  新しいものはこちらで来る可能性がある
