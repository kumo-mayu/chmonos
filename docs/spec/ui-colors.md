# 色の表と表示の色（今の決め事）

> **要点**：色は2つの表（`Themes/Light.xaml`・`Themes/Dark.xaml`）に集め、画面は鍵でしか色を指さない。2つの表は同じ鍵を持つ。
> 設定の「表示の色」（明るい／暗い／Windows に合わせる。既定は Windows に合わせる）で、その場で表を差し替える。
>
> **コード**：`App/Themes/Light.xaml`・`Dark.xaml`（色の表）・`Controls.xaml`（標準の部品の見た目）・`App/ViewModels/AppTheme.cs`（切り替え・題の帯）・
> `Core/Models/ColorThemeMode.cs`（設定の値と「Windows に合わせる」の決め方）
>
> **試験**：`Core.Tests/ThemeTableTests.cs`（鍵の揃い・直に書かない・DynamicResource で指す・暗い表のコントラスト）・`ColorThemeTests.cs`・`ColorThemePeekTests.cs`（起動の色）
>
> **経緯**：ユーザ指示 2026-09-29「後から色が簡単に変えられるように、色は一元管理する」

## 決まり

- **画面は色を直に書かない。**`#1c1f26` も `White` も書かず、表の鍵を指す。新しい色が要るときは、両方の表に同じ鍵を足してから使う
  （片方に無いと、その色の間だけ参照が空になって黙って透明になる）。試験が XAML を読んで止める
- **鍵は `DynamicResource` で指す。**`StaticResource` は読み込んだ時の色を持ち続けるので、表を差し替えても変わらない。
  鍵でないもの（スタイル・テンプレート・大きさ）は `StaticResource` のまま
- **C# で色を作らない。**要るときは鍵を指す：`element.SetResourceReference(Property, "鍵")`（リンクの色・境目の色）。
  描くたびに色を引く所（画面内検索の印）は `TryFindResource("鍵")`。変換器で色を作らない（表を差し替えても変換器は古い色を持ち続ける。ナビの選んだ項目はトリガーにした）
- **鍵は役割で名付ける**（`ChipText`・`DangerText`）。値が同じでも役割が違えば別の鍵にする（暗い表で逆向きに寄せることがあるため）
- **白い文字を載せる地は `〜Fill`、面の上の色は無印。**明るい表では同じ値（`Accent` と `AccentFill` は #3767a6）でも、
  暗い表では面の上の青は明るく、白い文字の地は濃いまま残す。白い文字は `OnAccent`、絵の上の白は `OnImage`
- **標準の部品のスタイルは `BasedOn` で既定を継ぐ。**`x:Key` 付きのスタイルを当てると `Controls.xaml` の既定の見た目が外れ、
  Windows の白い型に戻る（`SettingCheck`・`NumberBox`・メニューの項目のスタイル）
- **小窓の地**は `DialogFit.Prepare` が、書いていなければ `Surface` を指す（書かないと Windows の既定の白で出る）

## 鍵（128。ほかに Windows の色の置き換え5つ）

値は表のファイルにある。役割ごとに並べ、各行にコメントで何に使うかを書いてある。

| まとまり | 鍵 |
|---|---|
| 面 | `Bg`（画面の地）・`Surface`（カード・帯・窓）・`SurfaceAlt`（乗せた・押せない・一段下げた箱）・`Border`・`BorderStrong`・`Divider`（カードの中の行の線）・`BarTrack`（棒と進み具合の空き）・`InputBack` |
| 文字 | `Text`・`TextBody`（説明文・数の列）・`TextMuted`・`TextFaint` |
| 強調（青） | `Accent`（面の上の青：リンク・選んだ枠・線・棒）・`AccentText`（青の文字の小さな札）・`AccentSoft`・`AccentBorder`・`AccentSoftText`（AccentSoft の上の文字）・`AccentFill`・`AccentHover`（白い文字の地と、乗せたとき）・`OnAccent`・`AccentOverlay`（絵の上の青の札） |
| 状態 | `Good`／`GoodSoft`／`GoodBorder`／`GoodFill`、`Warn`／`WarnSoft`／`WarnBorder`、`Bad`／`BadSoft`／`BadBorder`／`BadFill`、`DangerText`／`DangerBorder`（取り返しのつかない操作）、`Unread`／`UnreadSoft`／`UnreadBorder`／`UnreadFill`、`NeedsWorkFill`（ナビの未確定・未:n）、`Star` |
| 札 | `ChipBack`・`ChipText`・`ChipBorder`・`ChipRemove` |
| ナビ | `Rail`・`RailActive`・`RailHover`・`RailText`・`RailTextActive`・`RailLabel`・`RailTitle`・`RailSubtitle`・`RailMuted`・`RailIntro`（初回の窓）・`RailButtonDimBorder`・`RailNoticeBack`／`Frame`／`Edge`／`Text` |
| 絵の周り | `ImageBack`（絵の枠の地）・`ThumbBack`（小さな絵の枠）・`PlaceholderGlyph`・`FolderIconFill`・`TileText`（ショップの頭文字のタイル）・`OnImage`・`ImageScrim`・`ImageScrimLight`・`ImageHoverShade`・`ImageDotIdle`／`Active`・`ImagePill`（カードの絵の上の選ぶ印）・`GlyphShadowColor`（色） |
| その他 | `DropZoneBack`／`Border`（取り込みのドロップ先）・`SurfaceFade`（札の並びの右端の消える帯）・`PopupShadowColor`（色）・`FindMatch`／`FindCurrent`（画面内検索の印） |
| 入力欄 | `InputBorder`・`InputBorderHover`・`InputBorderFocus`・`SelectionBack`（選んだ文字の地。40% で重なる）・`Caret` |
| チェック・ラジオ | `CheckBack`・`CheckBackHover`・`CheckBackPressed`・`CheckBorder`・`CheckBorderHover`・`CheckMark` |
| プルダウン | `ComboBack`・`ComboBackHover`・`ComboBorder`・`ComboBorderHover`・`ComboArrow` |
| メニュー | `MenuBack`・`MenuBorder`・`MenuHover`・`MenuHoverBorder`・`MenuText`・`MenuTextDisabled`・`MenuSeparator`・`MenuGlyph`（プルダウンの一覧も同じ地） |
| 吹き出し | `ToolTipBack`・`ToolTipBorder`・`ToolTipText` |
| 一覧の行 | `ListHoverBack`／`Border`・`ListSelectedBack`／`Border`・`ListSelectedInactiveBack`／`Border`（自前の行の見た目を持たない一覧） |
| スクロールバー | `ScrollTrack`・`ScrollThumb`・`ScrollThumbHover`・`ScrollThumbPressed`・`ScrollArrow`・`ScrollArrowHoverBack` |
| スライダー | `SliderTrack`・`SliderTrackBorder`・`SliderThumb`・`SliderThumbBorder`・`SliderThumbHover`・`SliderThumbHoverBorder`・`SliderTick` |
| 表の見出し | `ColumnHeaderBack`・`ColumnHeaderHover`・`ColumnHeaderBorder` |
| カレンダー | `CalendarToday`（「今日」の地。文字は `OnAccent`）。ほかは面・文字・一覧の行の鍵を使う（選んだ日は `ListSelectedBack`／`Border`、乗せたときは `ListHoverBack`、前後の月は `TextFaint`、曜日は `TextMuted`） |
| Windows の色 | `SystemColors` の `Window`・`WindowText`・`Control`・`ControlText`・`GrayText`（自前の見た目を持たない所：スクロールバーの角・フォーカスの点線など）。明るい表の値は Windows の既定と同じ |

### 明るい表を作ったときにまとめた色（見て差の分からない物だけ）

| 前 | 後 | 使っていた所 |
|---|---|---|
| `#eecccc` | `BadBorder`（#eec9c9） | 取り込みのエラーの枠・統計の重複の札 |
| `#eef0f2` | `ThumbBack`（#edeff3） | 商品ページの絵の帯 |
| `#edeff3`（進み具合の棒の地） | `BarTrack`（#f0f1f4） | 編集・取り込み・未確定の進み具合 |

## 暗い表の決め方

- 文字と地は WCAG の AA（本文 4.5:1・大きい文字と部品 3:1）。試験が主な組を計る（下の表）
- 面は3段で、上の段ほど明るい：`Bg` #15171c → `Surface` #1e2127 → `SurfaceAlt` #272b32。入力欄は一段沈める（`InputBack` #17191e）。ナビは地より沈める（`Rail` #101216）
- 面の上の色は明るく寄せる：`Accent` #6fa5ee（明るい表の #3767a6 のままだと暗い面で 2.9:1）・`Good` #5cc497・`Warn` #e3b36b・`Bad` #f08d8d・`Unread` #e694c3
- 白い文字の地（`〜Fill`）は濃いまま。`AccentHover` は乗せると明るくなるが、白が 4.5:1 を切らない #3f76b0 で止めた
- `TextFaint` は暗い表で AA まで上げた（#868e9b、4.9:1）
- **明るい表も AA に揃えた**（ユーザ判断 2026-09-29）。今の色を移しただけでは10組（`TextFaint`・`TextMuted`・`Good`・`Star`・`InputBorder`・`ScrollThumb`・ナビの見出し）が届かなかったので、
  色味を保ったまま届く所まで最小限だけ濃く（ナビの上は明るく）した。`TextFaint`（#6a6f78）は `TextMuted` とほぼ同じ濃さになるが、
  使う所（入力欄の中の例・つまみ）は大きさと置き場所で見分けられるので、そのままにした（ユーザ確認済み）
- 絵の上の物は絵が変わらないので同じ値。ショップの頭文字のタイル（名前から決まる淡い色）も同じで、文字 `TileText` も同じ濃さ

| 組 | 比 | | 組 | 比 |
|---|---|---|---|---|
| Text / Surface | 13.2 | | OnAccent / AccentFill | 5.7 |
| TextBody / Surface | 10.1 | | OnAccent / AccentHover | 4.7 |
| TextMuted / Surface | 6.9 | | Good / Surface | 7.5 |
| TextFaint / Surface | 4.9 | | Warn / Surface | 8.4 |
| Accent / Surface | 6.4 | | Bad / Surface | 6.8 |
| AccentText / AccentSoft | 7.5 | | Unread / Surface | 7.2 |
| MenuText / MenuHover | 8.9 | | InputBorder / InputBack（部品） | 3.4 |
| RailText / Rail | 12.6 | | ScrollThumb / ScrollTrack（部品） | 3.2 |

## 切り替え

- **その場で効く。**App.xaml の最初の合わせた辞書（色の表）を `AppTheme` が差し替え、DynamicResource が全部の色を読み直す。
  重さは、窓を出さずにカード2000枚を作って測った（2026-09-29）：作る時間の差はぶれの中、保持は参照1つあたり約120バイト
  （カード1枚に約60の参照で、2000枚すべてを作っても +14MB。一覧は見えている分しか作らない）。差し替えは2000枚の上で約0.5秒
- **Windows に合わせる**は `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` の `AppsUseLightTheme` が 0 のときだけ暗い。
  読めなければ明るい。Windows の設定を変えたとき（`UserPreferenceChanged`）も追う
- **窓の題の帯**は Windows が描くので、窓ごとに DWM へ暗い帯を頼む（`DWMWA_USE_IMMERSIVE_DARK_MODE`。20、古い版は19）。
  主の窓は `App.OnStartup`、小窓は `DialogFit.Prepare` で見張る
- 設定は `settings.json` の `colorTheme`（`system`／`light`／`dark`）。書くのは `UiCommand.ChangeSettings`。
- **起動の色**（ユーザ判断 2026-09-30）：窓を1つも出さないうちに、保存先の `settings.json` から `colorTheme` の1欄だけを読んで当てる
  （`Core/Storage/ColorThemePeek`。読むだけで、書かない・フォルダも作らない。`AppTheme.Start`）。サービス一式は保存先のフォルダを作り、
  2つ目の起動かを確かめるので、色のために先には作れない。前はサービス一式ができてから当てていて、その前に出る「既に起動しています」の窓が、
  表示の色を「明るい」にしていても Windows が暗ければ暗く出た。
  - 読めない場面は今までどおり Windows に合わせる：初回の窓（設定がまだ無い）・保存先の確かめの窓（保存先が見つからない）・壊れた設定。
  - 保存先の確かめで既定の場所へ切り替えたら、そこの設定で読み直す（`AppTheme.UseStoredMode`）。
  - サービス一式ができたら今までどおり設定から作り直す（`AppTheme.Initialize`。同じ色なら表は入れ替えない）。

## 標準の部品（`Themes/Controls.xaml`）

WPF の既定（Aero2）は色を型の中に直に持つので、色の表の鍵で描き直した。形と大きさは Aero2 に合わせ、明るい表では今までとほぼ同じ。
.NET 9 から入った Fluent（`ThemeMode`）には乗らない（部品の形も余白も全部変わる）。

- 描き直した：吹き出し・右クリックのメニューと項目（上のメニューの見出し・下の段・区切り）・入力欄・チェックボックス・ラジオボタン・
  切り替えボタン（自前の見た目の無いもの）・プルダウンと一覧の行・一覧（ListBox・ListView の地）と行・表の見出し（列の幅のつまみ付き）・
  スクロールバー・スライダー（横）・進み具合の棒の既定・畳める欄（自前の見た目の無いもの）・
  カレンダー（検索の日付の絞り込み。日付・月・年のボタンと見出し。部品の名前と日付の状態は Windows の型と同じに持つ。2026-09-29）
- 部品の中の文字は部品の `Foreground` に従う（`FollowOwnerForeground`）。アプリ全体の TextBlock の既定（色 `Text`）が勝って、
  押せない項目・危ない項目の色が出ていなかった。**テンプレートの中の文字には、外のスタイルの資源が届かない**（アプリ全体の既定を拾う）。
  文字の見た目は、そのテンプレートの中身（`ContentPresenter.Resources`）に置く（カレンダーの日付で、前後の月が薄くならず大きさも膨らんでいた）
- **知らせと確認の窓**は自前の窓（`Views/NoticeWindow`。2026-09-29）で、面・文字・印を表の鍵で描く。印は情報・質問が `AccentFill`、警告が `Warn`、エラーが `BadFill`。
  決まりは [ui-dialogs.md](ui-dialogs.md)「知らせの窓」
- **白いまま残る物**：ファイルとフォルダを選ぶ窓（Windows が描く）
