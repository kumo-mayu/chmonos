# WPF と .NET の落とし穴

XAML・画面の部品・一覧を書く前に読む。どれも実際に踏んで、画像で見るまで気付かなかったもの。

## 見た目

- 暗黙の `Button` スタイルは `ContentPresenter` の中央揃え・`BorderThickness="1"`・`Height=32` を持っている。
  枠の太さは上書きしても効かない（左の色の帯のような飾りは中身の側に置く）。高さを中身に合わせたいときは `Height="Auto"` を明示する
- `DockPanel` の子は既定で `Dock="Left"` に落ちる。`Dock="Bottom"` の子は**書いた順に下から積む**（ナビの帯が「設定」の下に出た。帯は「設定」のボタンより後に書く）
- `ContentControl` に見た目（`ContentTemplate`）を当てる前に中身を渡すと、型の名前を出す文字の部品が作られて見える
- 同じ要素に同じ属性を2回書くと MC3000 でビルドが落ちる（`Foreground` を Style と要素の両方に書いたとき）
- 文字の中のリンク（`Hyperlink`）に付けた `ToolTip` は出ない。**囲む `TextBlock` に付ける**（商品ページの ID の「クリックすると商品IDをコピーします」が、付いていたのに出ていなかった。2026-09-29）
- 添付プロパティで並べ方を変える部品（`ColumnsPanel.FullWidth`）は、`ItemsControl` の中では**項目を包む `ContentPresenter` に**付ける（`ItemContainerStyle`）。テンプレートの中の `Border` に付けても効かない

## 一覧と速さ

- **仮想化しない `ItemsControl` は、`ItemsSource` を差し替えると部品を全部作り直す**（札244枚で約550ms）。
  `ObservableCollection` を足し引きし、減らすときは隠す（`ChipStrip`）
- **WPF の一覧は、画面外の行（既定で上下1画面）を1回の処理でまとめて作る。**部品の多い項目は1回が重くなる。
  検索のカードは白い枠だけ先に並べ、中身を4枚ずつ作る（`DeferredCardHost`）
- **見出しでまとめた（グループ化した）一覧は、既定では全行を作る**（仮想化されない）
- **WPF の絵は一度画面に出すと、描画用の写しをもう1枚持つ。**絵のまま保持すると保持1MBにつき全体で約2.2MBになる。
  保持は画素で持ち、WPF の絵は画面に出す分だけ作る（`ThumbnailLoader`）
- **絵は画面のスレッドで復号しない。**`ThumbnailLoader` の `Peek〜`（カード・一覧・頭の絵・原寸・指定の大きさ）で裏で読み、届いたら知らせ直す。
  同期の `Load〜` を使うのは、なぞって絵を送る所だけ（裏へ回すと送るたびに灰色がちらつく）。
  頭の小さな絵（切り抜いて出す物）は短い辺で縮める（`PeekForIcon`・`PeekForFill`）。長い辺で縮めると横長の絵がぼやける
- 仮想化しない一覧を組み直すときは、丸ごと差し替えずに差分で寄せる（`CollectionSync.Apply`）。
  一度に全部入れ替えるなら `RangeObservableCollection.ReplaceAll`（見出しでまとめた一覧は1件ずつ足すと1件ごとに振り分け直す）
- **入れ子の一覧（束の中の行・見出しの中の改変）と `ScrollViewer` の中の `WrapPanel` は、仮想化されない。**平らな1本に並べ直し、
  `VirtualizingStackPanel`（`Recycling`・`ScrollUnit="Pixel"`）に載せる。束の枠・字下げ・縦線は行ごとの余白と線で描き分ける
  （要確認・改変の左の一覧・タグと属性の管理。2026-09-24：知らせ1000件で7秒、改変300件で8秒、商品2000件で7秒固まっていた）。
  WrapPanel は前が1段に置いていた数で段に切る（`ManageItemLines`。幅は View が一覧の `ViewportWidth` から枠の分を引いて渡す）。
  仮想化するとスクロールバーのつまみは見積もりになり、流すと少し長さが変わる
- テンプレートの中の `ScrollViewer` は UI Automation に部品として出ないので、流す操作が消える。一覧は `Controls/ContentItemsControl` にする（窓口から流す操作を渡す）
- `ContentPresenter` は中身を自分の DataContext にする。`<ContentPresenter Content="{Binding Row}" Margin="{Binding InnerMargin}"/>` の Margin は
  行ではなく中身を見て黙って効かない。余白は外の `Border` に付ける（改変の左の一覧で、行の間が4pxずつ詰まっていた）
- 裏から細かく届く進み具合は、最新だけを間引いて出す（`Services/LatestProgress`。`Progress<T>` は全部を画面のスレッドへ積む）
- 検索の View は1つを持ち回す（`Views/SearchViewHost`）。View に状態を足すときは、離れる（Unloaded）・戻る（Loaded）が何度も来る前提で書く

## コマンド

- `RelayCommand` の `CanExecute` は、作った時点の状態で止まることがある（ナビの帯を押しても何も起きなかった。帯の知らせが届く前に作ったコマンドだった）。
  状態で出し入れする部品は、`CanExecute` ではなく見えるかどうか（`Visibility`）で制御する
- 画面を抜けるときに保存が要る入力は、800ms の遅れで保存し（`Debounced`）、選ぶ物が変わる瞬間は古い対象へ書き切ってから替える（メモの自動保存。`AvatarsViewModel`・`TagManageViewModel`）

## XML（辞書）

- JMdict は実体参照を大量に使う。`XmlReaderSettings.MaxCharactersFromEntities` を 0（上限なし）にしないと途中で落ちる
- `ReadElementContentAsString` は終了タグの先まで進む。そのあと `Read()` を呼ぶと1つ飛ばす

## ビルド

- アプリが開いていると実行ファイルが掴まれてビルドが落ちる。確かめ用のアプリを閉じてから（`ui-check` スキルの `Stop-ChmonosApp`）
