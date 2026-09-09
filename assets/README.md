# assets/

アプリに同梱する第三者のデータ。

## JMdict_e.gz

日英辞書 **JMdict**（英語版・Electronic Dictionary Research and Development Group）。
`http://ftp.edrdg.org/pub/Nihongo/JMdict_e.gz` から取得したものを**無改変で**置いている。

- **用途**：検索で表記をまたいで引くため。`shark` → サメ、`ゆびわ` → 指輪 のように、
  英語と読みから日本語の語を引く。索引は初回に手元で組み、
  ライブラリと同じ場所へ置く（配布物には含めない）
- **EDICT2ではなくJMdictを使う理由**：JMdictは頻度の階級（`nf01`〜`nf48`）と
  「ふつうかなで書く語」の印（`uk`）を持っている。EDICT2の `(P)` は入/切だけで、
  裏返した索引の並び順を決めきれない。実測でも
  `shark`→鮫（EDICT2）／**サメ**（JMdict）と分かれ、手元のライブラリに当たるのは後者だった
- **ライセンス**：Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0)
  <https://www.edrdg.org/edrdg/licence.html>
- **出典表示**：設定画面に1行出している。ライセンスの求めに従い、
  この辞書を使った機能の画面から辿れるようにしてある

**加工した辞書は配らない。**同梱するのはこの無改変のファイルだけで、
索引は各自の手元で組む。そのため継承条項（加工物を同じライセンスで配る）に
触れる配布物が存在しない。

更新するときは上のURLから落とし直して置き換えるだけでよい。
形式が変わらない限り、コード側の変更は要らない。
