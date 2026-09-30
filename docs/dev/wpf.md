# WPF と .NET の落とし穴

XAML・画面の部品・一覧を書く前に読む。どれも実際に踏んで、画像で見るまで気付かなかったもの。

## 見た目

- 暗黙の `Button` スタイルは `ContentPresenter` の中央揃え・`BorderThickness="1"`・`Height=32` を持っている。
  枠の太さは上書きしても効かない（左の色の帯のような飾りは中身の側に置く）。高さを中身に合わせたいときは `Height="Auto"` を明示する
- `DockPanel` の子は既定で `Dock="Left"` に落ちる。`Dock="Bottom"` の子は**書いた順に下から積む**（ナビの帯が「設定」の下に出た。帯は「設定」のボタンより後に書く）
- `ContentControl` に見た目（`ContentTemplate`）を当てる前に中身を渡すと、型の名前を出す文字の部品が作られて見える
- 同じ要素に同じ属性を2回書くと MC3000 でビルドが落ちる（`Foreground` を Style と要素の両方に書いたとき）
- 文字の中のリンク（`Hyperlink`）に付けた `ToolTip` は出ない。**囲む `TextBlock` に付ける**（商品ページの ID の「クリックすると商品IDをコピーします」が、付いていたのに出ていなかった。2026-09-29）
- **型（ControlTemplate）を自前にしても、Windows の既定の見た目（テーマのスタイル）の指定は残る。**`Expander` は透明な 1px の枠（`BorderThickness=1`）を持つので、
  型で `TemplateBinding BorderThickness` を描くと、枠を指定していない画面まで四方に1px ずつ広がる。自前の型のスタイルで 0 を指定する（`TriangleExpander`。2026-09-30）
- **自前の型の `Border` は画素に合わせない。**見出しの高さが画素の途中で終わると、下の 1px の線が2pxにぼけて薄くなる（暗い表では束の間の線がほとんど見えなかった）。
  既定の型は `SnapsToDevicePixels="True"` を付けている。線を描く所だけ付ける（型の既定にすると、ほかの画面の丸や三角の縁の滲みまで変わった。2026-09-30）
- 添付プロパティで並べ方を変える部品（`ColumnsPanel.FullWidth`）は、`ItemsControl` の中では**項目を包む `ContentPresenter` に**付ける（`ItemContainerStyle`）。テンプレートの中の `Border` に付けても効かない

## 窓を出す瞬間

- **窓を出してから WPF が最初の1コマを画面へ出すまで、DWM は本文を白で見せる**（暗い表で起動すると2〜3コマ、約65〜150ms 白かった）。
  窓の `Background` も `HwndSource.CompositionTarget.BackgroundColor` も、WPF が描いてからしか効かない（塗っても撮り比べで白いコマの数は同じだった）。
  主の窓は `DWMWA_CLOAK` で隠して出し、`ContentRendered` の後の次の `CompositionTarget.Rendering` で見せる（`AppTheme.HideUntilFirstFrame`。3巡とも白0コマ、中身が出揃う時刻は同じ）。
  描画の回を数えて早めに見せる（2回目）と、白いコマが1つ残ることがあった。見せるのを優先度の低い仕事に回すと、起動の読み込みに押されて約0.3秒遅れた
- 起動の瞬間は目では追えない。約43msごとに画面を撮り、白に近い画素の割合で数える（`docs/research/large-files-2026-09-30.md` の白い地）

## 窓を出さずに描く（`tools/ViewShot`）

台を作ったときに踏んだ物（2026-09-30）。台の中で吸収してあるが、台を直すとき・場面を書くときに要る。

- **どこにも載せずに Measure／Arrange だけで描くと、部品は「画面に載っていない」まま**で、`Loaded` が来ず `IsVisible` も偽のまま。
  View は `Loaded` で読み込みを始めるので空の画面になる。出さない窓口（親が `HWND_MESSAGE` の `HwndSource`）に載せる（`Stage`）
- **窓は出さない限り中身を並べない。**中身を窓から外して載せ替える。外すと、窓の資源（画面ごとの `DataTemplate`）・名前の表（`ElementName`）・
  `DataContext`・地と文字の色が届かなくなるので、載せた先に持たせる（`SceneContext.Unwrap`）。`RelativeSource AncestorType=Window` と `Window.GetWindow` は届かないまま
- **色の表の差し替えは、窓の外の部品には届かない。**WPF が「資源が変わった」を配るのはアプリの窓だけで、先に作った部品は前の色のまま残った。
  載せた根にも同じ表を合わせ、差し替えのたびに入れ替える（`Stage.SyncColorTable`）
- **表示の倍率は `VisualTreeHelper.SetRootDpi` で根に入れる。**描く画像の細かさ（`RenderTargetBitmap` の dpi）だけを上げると、
  画素に合わせる丸めが実行した PC の拡大率のままになる。入れた後は並べ直しを自分で促す
- **送らないと見えない所は `BringIntoView()`** で届く（入力もフォーカスも要らない）。畳む印の開閉のように View が持つ状態は、部品の値を直に替える
- **`RenderTargetBitmap` の文字はグレースケール**（画面は ClearType）。同じ入力なら画素まで同じ画像になるので、前後の比べは 1画素の差まで見られる
- **`new App()` しただけで、起動の処理（`OnStartup`）は走る。**WPF の `Application` はコンストラクタの中で「`OnStartup` を呼ぶ仕事」を積むので、
  `Run` を呼ばなくても、メッセージを回した時点（`Dispatcher.Run`・`PushFrame`・`Dispatcher.Invoke`）で丸ごと走る。
  台は「`Run` を呼ばなければ走らない」と考えて作られ、走るたびに初回の窓か主の窓が画面に出て、サービス一式がもう1組できていた
  （保存先は台が切り離していたので本番には触れなかった）。保存先を切り離していなかった別の道具では、本番の `location.json` の指す先で起動の処理が走った。
  **`App` の側で、入口が自分の実行ファイルのときだけ起動の処理を進める**（`App.IsLaunchedAsApp`。本当の保存先を使う印 `StoreLocation.AllowsUserStore` も同じ条件）。
  道具は `InitializeComponent` で資源だけ読み、`Dispatcher.Run` を自分で回す。道具を作ったら、走っている間にプロセスが窓を持たないことを1回は見る（`EnumWindows`）
- 保存先は `AppPaths.Default` が最初に決めたら変わらないので、場面ごとにプロセスを分ける。サービス一式を組むと、前回の消し残しとして
  一時展開のフォルダ（`%TEMP%\Chmonos\unpacked`）を消す——開いているアプリの展開先を消さないよう、台は一時フォルダごと別にする（`Isolation`）

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
- **DataTemplate で出す画面は、同じ型の ViewModel に替わるとき View を作り直さない**（DataContext だけが替わる）。
  View だけが持つ状態（スクロールの位置・フォーカス・コードに持つ控え）は前の物のまま残る（フォルダビューで商品を選び直すと、流した位置 645px が次の商品に残った。2026-09-30）。
  結び付けで ViewModel から来る物は入れ替わる。入力欄の取り消しの履歴は、結び付けで文字が替わると捨てられる
- **非同期の形で書いた保存も、画面から呼ぶと画面のスレッドで走る。**錠が空いていれば最初の本当の待ちまで同期で進み、続きも画面のスレッドへ戻る
  （商品を開くたびに、履歴と足跡の読む・ディスクへ書き出す・置き換えるで約15ms）。画面を待たせたくない書き込みは `BackgroundWriteQueue` に頼む
- **UI Automation の相手がいると、配置のたびに木の更新が乗る**（商品ページを開く1回で 50〜90ms）。読み上げでなくても、常駐のソフトが相手になる。
  速さを測るときは、相手がいるかで数字が変わる（`docs/research/item-page-open-2026-09-30.md`）

## UI Automation（読み上げ・自動操作）

付け方の決まりは `docs/spec/ui-input.md`「読み上げの名前」。ここは踏んだ形（2026-09-30。どれも見えない窓に載せて、UI Automation の側から木を書き出して確かめた）。

- **素の `Border`・`TextBlock` は、押す動き（`MouseLeftButtonUp`・`MouseBinding`）と `AutomationProperties.Name` を付けても UI Automation に出ない。**
  名前だけ付いていて探せなかった（商品ページ・編集画面の星）。`Controls/PressableBorder` に替える——見た目は `Border` のままで、型が Button・Invoke を持つ部品として出る。
  文字の ✕ は、大きさを持たない `PressableBorder` で包む（余白・手の形・吹き出しは枠へ移す）。コマンドが今は実行できないときは「押せない」と出る
- **素の `ItemsControl` は行を「データの項目」として出し、名前は行の `ToString()`（行の型の名前）になる。値の等しい行は1つにまとめられ**、
  2つめ以降の中のボタンが見えない（同じ文字列3行で、見えたボタンは1つ。等しい `record` も同じ）。繰り返しの一覧は `Controls/ContentItemsControl`。
  中の `ItemsControl.ItemTemplate`・`AncestorType=ItemsControl` は書き換えなくてよい（継いだ型なので当たる）
- **暗黙の見た目（`<Style TargetType="ListView">`）は、型がぴったり同じ部品にしか当たらない。**継いだ型（`Controls/ItemListView`）は文字の色の指定が外れるので、
  型の側で `SetResourceReference(StyleProperty, typeof(ListView))` と名指しする。窓口（AutomationPeer）を足すために部品を継ぐときは、暗黙の見た目が在るかを `Themes/Controls.xaml` で確かめる
- **`ListView`（GridView）の行は「選ぶ」しか持たない。**行を押して画面を移る一覧は、行の窓口に Invoke を足す（`ItemListView`。`ListViewAutomationPeer.CreateItemAutomationPeer` を上書きし、
  列の見出しと行を作る `GridViewAutomationPeer` は自分で渡す）
- **`ListBox` の行の名前は、付けなければ行の `ToString()`（型の名前）。**`ItemContainerStyle` で `AutomationProperties.Name` を行の名前に結ぶ。
  暗黙の行の見た目が在るので、`BasedOn="{StaticResource {x:Type ListBoxItem}}"` を付ける（付けないと見た目が既定に戻る）
- **`Expander` の見出しの押す所（型の中の `ToggleButton`）は、見出しが文字の並びや札だと名前が無い。**型（`TriangleExpander`・暗黙の `Expander`）が `Expander` の名前を継ぐので、`Expander` に名前を付ける
- **隠している部品（`Collapsed`・`Hidden`）は、UI Automation の既定の見方（操作できる部品だけ）に出ない。フォーカスも受けられない。**乗せたときだけ出すボタンを `Hidden` で隠すと、乗せるまで探せず、Tab でも止まれない。
  キーボードから届かせる物は透明（`Opacity=0`）で隠し、透明な間はマウスを通す（`IsHitTestVisible=False`。見えないボタンを押させない）。`App.xaml` の `RevealOnHoverOrFocusButton`。自前の窓口で「操作できる部品」を常に真にすると、隠した枠まで出る
- **中身が文字のボタン・チェック・メニューの項目は、名前から最初の「_」が消える**（アクセスキーの印として。`AutomationProperties.Name` を付けていても消える。「__」は「_」に戻る）。
  画面の表示は、型の `ContentPresenter` が `RecognizesAccessKey` を持つときだけ消える（このアプリのボタンは持たない。メニュー・チェック・ラジオ・畳む欄の見出しは持つ）。
  名前は `Services/AutomationNames` が型に1回で掛けて守る（`AutomationProperties.NameProperty` の決まりをボタンの仲間とメニューの項目で上書きし、先に「_」を重ねる）
- **見出しでまとめた一覧（`GroupStyle`）の束は、開いている間、見出しの中の部品を木に出さない**（束の子は行だけ。畳むと見出しごと出る）。束の窓口（`GroupItemAutomationPeer`）は WPF が作る物で差し替えられない。
  一覧の窓口の側で、開いている束の後ろに見出しの中の部品を並べる（`Controls/GroupedListBox`。見出しの押す所そのものは WPF が束の窓口に結び付けているので、渡すと束が2つ出る）
- **`ToggleButton` は「オン／オフ」しか言わない。**開閉の三角は `Controls/ExpandToggle`（開閉の状態を `IExpandCollapseProvider` で渡す。値を入れるのは `SetCurrentValue`——結び付けを壊さずに、結び付けた先へも届く）
- **`ItemsControl`（と継いだ `ContentItemsControl`）は、既定で Tab で止まる。**止まっても何も起きず、フォーカスの枠だけが一覧を囲む。`ContentItemsControl` は型の側で止まらないようにしてある
- **メニューの下の段は、開くまで木に無い。**開いた後は窓の外の別の窓に出る。同じ名前の項目は AutomationId で指す
- **アプリを起動せずに確かめる**（道具は `experiments/PeerProbe`）：`HwndSource`（`WS_POPUP` だけ・`WS_VISIBLE` なし・画面の外）に部品を載せ、別のスレッドから `AutomationElement.FromHandle` で木をたどる
  （同じスレッドからは自分の窓を読めない。画面のスレッドは `Dispatcher.PushFrame` で回しておく）。画面の View は `new App().InitializeComponent()` で資源だけ読めば載る（起動の処理は、`App` が「入口が自分の実行ファイルでない」と見て進めない。上の「窓を出さずに描く」）。
  結ぶ値は `ExpandoObject` の作り物でよい（型で見た目を選ぶ所だけは本物が要る）。**メニューや窓を開く操作は押さない**（見えない窓からでも、ポップアップは画面に出る）
- **キーボードのフォーカスと Tab の順も、起動せずに確かめられる**（`experiments/PeerProbe -- focus`）。見えない窓に `SetFocus` すると、このスレッドの中だけでフォーカスが移り（ほかのアプリの前面の窓は変わらない）、
  `MoveFocus(Next)` が Tab キーと同じ決まりで次へ進む。Enter は `InputManager.ProcessInput` に渡す（実際のキーは押さない）。窓には `WS_EX_NOACTIVATE` を付けない（付けるとフォーカスを受けない）

## コマンド

- `RelayCommand` の `CanExecute` は、作った時点の状態で止まることがある（ナビの帯を押しても何も起きなかった。帯の知らせが届く前に作ったコマンドだった）。
  状態で出し入れする部品は、`CanExecute` ではなく見えるかどうか（`Visibility`）で制御する
- 画面を抜けるときに保存が要る入力は、800ms の遅れで保存し（`Debounced`）、選ぶ物が変わる瞬間は古い対象へ書き切ってから替える（メモの自動保存。`AvatarsViewModel`・`TagManageViewModel`）

## XML（辞書）

- JMdict は実体参照を大量に使う。`XmlReaderSettings.MaxCharactersFromEntities` を 0（上限なし）にしないと途中で落ちる
- `ReadElementContentAsString` は終了タグの先まで進む。そのあと `Read()` を呼ぶと1つ飛ばす

## ビルド

- アプリが開いていると実行ファイルが掴まれてビルドが落ちる。確かめ用のアプリを閉じてから（`ui-check` スキルの `Stop-ChmonosApp`）
