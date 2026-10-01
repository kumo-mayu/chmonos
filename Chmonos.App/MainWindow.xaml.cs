using System.Windows;
using System.Linq;
using Chmonos.App.Services;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Unityの開き閉じはこのアプリの外で起きる。「Unityが開いていません」と
        // 出したまま、実は開いている——という嘘を防ぐため、手前に戻ったら読み直す。
        // 常時見張るほどの値ではない（プロセスを数えるだけとはいえ毎秒は無駄）。
        // Unityを開いてこちらへ戻る、が実際の流れなのでここで足りる
        Activated += (_, _) => (DataContext as MainViewModel)?.NoteWindowActivated();

        // マウスの「戻る」ボタン（U23）。ブラウザと同じ操作で画面の履歴を遡る。
        // 割り当てのショートカットと同じ道を通す（編集画面では前の1件へ）
        PreviewMouseDown += (_, e) =>
        {
            if (DataContext is not MainViewModel main)
            {
                return;
            }

            // 進むボタン（XButton2）も同じ道を通す（ユーザ指示 2026-09-20・M7）
            var action = e.ChangedButton switch
            {
                System.Windows.Input.MouseButton.XButton1 => Services.ShortcutAction.Back,
                System.Windows.Input.MouseButton.XButton2 => Services.ShortcutAction.Forward,
                _ => (Services.ShortcutAction?)null,
            };

            if (action is { } move && main.RunShortcut(move))
            {
                e.Handled = true;
            }
        };

        HookFind();
    }

    /// <summary>
    /// 窓の最小の幅（DIP）。**検索画面が横に送らずに並ぶ幅**で決めた：
    /// ナビ（既定 208）＋絞り込み欄（既定 286）＋結果の列の最小（360。`PaneGrid` が反対側に付ける最小）＋境目と窓の枠。
    /// 点検（2026-09-23）で幅 900 では編集画面だけが横に送れ、ほかの12画面は崩れずに並んだ（760 では7画面が送る）。
    /// 1366×768・125% のノートPC（幅 1093 DIP）でも、最大化せずに左右が少し余る。
    /// </summary>
    public const double MinimumWidth = 900;

    /// <summary>
    /// 窓の最小の高さ（DIP・タイトルバーを含む）。**検索画面で既定の大きさのカードが1段見える高さ**で決めた：
    /// タイトルバー約31＋上の帯78＋結果の見出し38＋一覧の下の大きさの帯約32＋通信の帯26＋カード1段（336＋間14）＝約555。
    /// 1366×768・125% でタスクバー（48px）を除いた作業領域は約576 DIP で、そこに収まる。
    /// ナビはこれより低くても縦に送れる（「設定」まで届く。`MainWindow.xaml` のナビの ScrollViewer）
    /// </summary>
    public const double MinimumHeight = 560;

    /// <summary>
    /// 前回の位置と大きさ（画素）。窓ができた所（<see cref="OnSourceInitialized"/>）で置く。
    /// WPF の Left・Top は拡大率の違うモニターの間で値が重なるので、作る前に DIP で渡すと
    /// 別のモニターに開いたり、大きさが拡大率の比だけずれたりする（<see cref="Services.WindowNative"/>）
    /// </summary>
    private WindowNative.NativeRect? _pendingPlacement;

    /// <summary>
    /// 前回の位置と大きさで開く。
    ///
    /// 保存した矩形が今あるモニタのどれとも重ならなければ捨てて中央に開く。
    /// モニタを外したり配置を変えたりすると画面の外に開いてしまい、
    /// タイトルバーが掴めず動かせなくなるため。
    /// </summary>
    public void RestorePlacement(WindowPlacement? placement)
    {
        if (placement is null || placement.Width < 320 || placement.Height < 240)
        {
            return;
        }

        var rect = new WindowNative.NativeRect
        {
            Left = (int)Math.Round(placement.Left),
            Top = (int)Math.Round(placement.Top),
            Right = (int)Math.Round(placement.Left + placement.Width),
            Bottom = (int)Math.Round(placement.Top + placement.Height),
        };

        // 少しでも掛かっていればよい。掴んで動かせるなら、そこから直せる
        if (!WindowNative.IsOnAnyMonitor(rect))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        _pendingPlacement = rect;

        if (placement.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// 今の姿を保存できる形にする（画素）。窓が無ければ null（覚え直さない）。
    /// 最大化中は「解除したときの姿」を採る。最大化中の値を書くと、次に解除したときに画面いっぱいのまま戻らなくなる。
    /// </summary>
    public WindowPlacement? CurrentPlacement()
    {
        if (WindowNative.ReadPlacement(this) is not { } current)
        {
            return null;
        }

        return new WindowPlacement
        {
            Left = current.Normal.Left,
            Top = current.Normal.Top,
            Width = current.Normal.Width,
            Height = current.Normal.Height,
            IsMaximized = current.Maximized,
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (_pendingPlacement is { } placement)
        {
            WindowNative.ApplyPlacement(this, placement);
            _pendingPlacement = null;
        }

        // 置いた後のモニターで読む（前回を別のモニターで閉じていれば、ここで初めてそちらの拡大率になる）
        Services.DisplayScale.SetMonitor(System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX);
        FitMinimumToWorkArea();
    }

    /// <summary>
    /// 拡大率の違うモニターへ移った（Per-Monitor V2）。描き直しは WPF がするので、
    /// ここでは絵を読む倍率を替え（見えているカードが読み直す）、最小の大きさを移った先の作業領域に合わせ直す。
    /// </summary>
    protected override void OnDpiChanged(System.Windows.DpiScale oldDpi, System.Windows.DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Services.DisplayScale.SetMonitor(newDpi.DpiScaleX);
        FitMinimumToWorkArea();
    }

    /// <summary>
    /// 最小の大きさを付ける。ただし**モニターの作業領域より大きくはしない**——
    /// 1280×720・150% のような画面（作業領域は約 853×450 DIP）で最小が画面より大きいと、窓の端が画面の外に出て戻せない。
    /// 収まらない分は画面の中身が送る（ナビは縦に、本文は `ViewportFitHost` が横に）。
    /// </summary>
    private void FitMinimumToWorkArea()
    {
        var work = WindowNative.WorkAreaDip(this);
        MinWidth = work is { Width: var width } ? Math.Min(MinimumWidth, width) : MinimumWidth;
        MinHeight = work is { Height: var height } ? Math.Min(MinimumHeight, height) : MinimumHeight;
    }

    /// <summary>
    /// 落ちてくるものは2種類しかない（ファイルかBOOTHのURL）ので、
    /// 画面ごとではなくウィンドウで受ける。画面ごとに受けると、
    /// 同じものを落としたのに画面によって結果が変わる。
    /// </summary>
    /// <summary>
    /// ブラウザがURLを置く形と、その中身の文字コード。**急ぐ順に並べてある。**
    ///
    /// リンクや絵をドラッグすると、素のテキストが入らないことがある。
    /// Chromium が実際に登録しているのは CFSTR_INETURLW / CFSTR_INETURLA /
    /// text/x-moz-url / CF_UNICODETEXT / CF_TEXT / HTML Format で、
    /// <c>shlobj.h</c> では CFSTR_INETURLW = "UniformResourceLocatorW"、
    /// CFSTR_INETURLA = "UniformResourceLocator"（CFSTR_SHELLURL は非推奨）。
    ///
    /// **W と A で文字コードが違う。**片方の読み方で両方を読むと文字化けする。
    /// HTML を最後にしているのは、CF_HTML の見出しに <c>SourceURL:</c>（今見ていたページ）が
    /// 入っていて、掴んだリンクより先に当たってしまうため。
    /// </summary>
    private static readonly (string Format, bool IsUnicode)[] UrlFormats =
    [
        ("UniformResourceLocatorW", true),
        ("UniformResourceLocator", false),
        ("text/x-moz-url", true),
        (DataFormats.UnicodeText, true),
        (DataFormats.Text, false),
        (DataFormats.Html, true),
    ];

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || ReadText(e.Data) is not null
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel main)
        {
            return;
        }

        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];

        // 絵そのものが落ちてくることもある（ブラウザからのドラッグ）。
        // ファイルではないので、パス経由では受け取れない
        var hasBitmap = e.Data.GetDataPresent(DataFormats.Bitmap);

        e.Handled = true;
        Handle(main, paths, ReadText(e.Data), hasBitmap);
    }

    /// <summary>
    /// 置かれている形を順に見て、最初に読めたものを返す。
    /// 中身がURLか文章かはここでは判断しない（BOOTHのURLを探すのは Core の仕事）。
    /// </summary>
    internal static string? ReadText(IDataObject data)
    {
        foreach (var (format, isUnicode) in UrlFormats)
        {
            try
            {
                if (!data.GetDataPresent(format))
                {
                    continue;
                }

                // 登録形式（CFSTR_INETURL など）は HGLOBAL の生バイト列で来る。
                // 終端の NUL まで含まれているので落とす
                var value = data.GetData(format) switch
                {
                    string text => text,
                    System.IO.MemoryStream stream => (isUnicode
                            ? System.Text.Encoding.Unicode
                            : System.Text.Encoding.Default)
                        .GetString(stream.ToArray())
                        .TrimEnd('\0'),
                    _ => null,
                };

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return format == DataFormats.Html ? StripHtmlHeader(value) : value;
                }
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or NotSupportedException)
            {
                // 置かれていると言いながら読めない形がある。次の形を見る
            }
        }

        return null;
    }

    /// <summary>
    /// CF_HTML の見出しを落として本体だけにする。
    ///
    /// 見出しには <c>SourceURL:</c>（掴んだ場所のページURL）が入っている。
    /// 落とさないと、掴んだリンクより先にそちらが当たり、**別の商品を開く**。
    /// </summary>
    private static string StripHtmlHeader(string html)
    {
        var start = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            return html[(start + "<!--StartFragment-->".Length)..];
        }

        var body = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        return body >= 0 ? html[body..] : html;
    }

    /// <summary>
    /// 落ちてきたものを振り分ける。
    ///
    /// 投げっぱなしにしないのは、**失敗すると何も起きないように見える**ため。
    /// 落としたのに無反応だと、受け付けていないのか壊れているのか分からない。
    /// </summary>
    internal static async void Handle(
        MainViewModel main,
        IReadOnlyList<string>? paths,
        string? text,
        bool hasBitmap = false)
    {
        try
        {
            await main.HandleDropAsync(paths, text, hasBitmap);
        }
        catch (Exception exception)
        {
            Core.Diagnostics.AppLog.Error("落とされた物の振り分け", exception);
            Services.Notice.Show(
                "受け取ったものを処理できませんでした。\n\n"
                + Core.Services.FailureText.Cause(exception) + "\n\n"
                + "もう一度ドロップしてください。詳しい記録は保存先のlogs\\app.logに残しました。",
                "Chmonos",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 商品ページと編集画面で左右キーを押したら、見る絵を送る。
    ///
    /// **入力欄にいるときは何もしない。**そこでの左右はカーソルの移動で、
    /// 横取りすると文字が打てなくなる（Ctrl+V と同じ理由）。
    /// </summary>
    private static bool TryMoveGallery(MainViewModel main, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (System.Windows.Input.Key.Left or System.Windows.Input.Key.Right)
            || System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None)
        {
            return false;
        }

        var (previous, next) = main.CurrentViewModel switch
        {
            ItemViewModel item => (item.PreviousImageCommand, item.NextImageCommand),
            // 編集画面でも同じ動き（ユーザ指示）。入力欄が多い画面なので、下の「入力欄では効かせない」がそのまま効く
            EditViewModel { ItemPage: { } page } => (page.PreviousImageCommand, page.NextImageCommand),
            _ => ((RelayCommand?)null, (RelayCommand?)null),
        };

        if (previous is null || next is null)
        {
            return false;
        }

        // スライダー（編集画面の属性）の左右は値を動かすキー。横取りすると、止まっても値が動かなかった（点検 2026-09-23）
        if (System.Windows.Input.Keyboard.FocusedElement
            is System.Windows.Controls.TextBox or System.Windows.Controls.ComboBox or System.Windows.Controls.Slider)
        {
            return false;
        }

        // 並び（対応アバターの札・ローカルファイルの行など）の中の左右は、並びの中を移るキー（ユーザ判断 2026-10-01）。
        // 窓がここで先に受けると、札に止まって → を押しても絵が送られるだけで、札から動けない
        if (Controls.ArrowGroup.OwnsArrows(System.Windows.Input.Keyboard.FocusedElement as DependencyObject))
        {
            return false;
        }

        var command = e.Key == System.Windows.Input.Key.Left ? previous : next;

        if (!command.CanExecute(null))
        {
            // 端では何もしないが、受け取ったことにする。
            // 他の場所へ左右が流れて画面が動くのを防ぐ
            return true;
        }

        command.Execute(null);
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// ショートカット（#43）。割り当ては設定から読む（設定画面で変えられる）。
    ///
    /// **文字の欄にいるときは、打つ・カーソルを動かす操作を横取りしない。**
    /// Ctrl も Alt も無いキーは文字を打つ操作で、矢印・Home・End はカーソルの移動なので、
    /// そういう割り当てはその欄の中では働かせない。
    /// 割り当てを打ち込んでいる欄では何もしない——Ctrl+F を割り当てようとして検索へ飛ばないため。
    /// </summary>
    private bool TryShortcut(MainViewModel main, System.Windows.Input.KeyEventArgs e)
    {
        var focused = System.Windows.Input.Keyboard.FocusedElement;
        if (focused is FrameworkElement { Tag: Views.SettingsView.ShortcutCaptureTag })
        {
            return false;
        }

        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        var inText = focused is System.Windows.Controls.TextBox or System.Windows.Controls.ComboBox;

        foreach (var action in Enum.GetValues<Services.ShortcutAction>())
        {
            if (Services.Shortcuts.Parse(Services.Shortcuts.GestureOf(main.Shortcuts, action)) is not { } gesture
                || !Services.Shortcuts.Matches(gesture, key, modifiers))
            {
                continue;
            }

            var typing = (modifiers & (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt)) == 0;

            // 文字の欄に譲る矢印は、素の矢印・Shift＋矢印（選ぶ）・Ctrl＋矢印（1語ずつ）まで。
            // Alt＋矢印（戻る・進む）と Ctrl+Shift＋矢印（スキップ）は**欄の中でも横取りする**——
            // 譲ると、入力欄だらけの編集画面では一度も効かない（Alt＋← は 2026-09-12、Ctrl+Shift+→ は B11）。
            // 代償は欄の「1語ずつ選ぶ」で、Shift＋矢印とマウスで足りる。
            // 前へ（Ctrl+Shift+←）だけは欄に譲る（ユーザ判断 2026-09-28）——スキップと両方を取ると、欄で語を選ぶ手が
            // 矢印キーから消える。前へ戻るのは入力を終えてからで足りる
            var control = (modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
            var shift = (modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
            var alt = (modifiers & System.Windows.Input.ModifierKeys.Alt) != 0;
            var appTakesArrow = (alt && !control) || (control && shift && action != Services.ShortcutAction.Previous);
            if (inText && (typing || (Services.Shortcuts.IsTextEditingKey(key) && !appTakesArrow)))
            {
                return false;
            }

            // スライダーに止まっている間の素の矢印・Home・End・PageUp・PageDown は値を動かすキー（点検 2026-09-23）
            if (focused is System.Windows.Controls.Slider && modifiers == System.Windows.Input.ModifierKeys.None
                && Services.Shortcuts.IsTextEditingKey(key))
            {
                return false;
            }

            // Ctrl+F は**どの画面でも**画面の中を探す（ユーザ判断 2026-09-20・B2）。
            // 以前は検索とショップだけ検索画面へ移していたが、同じキーで見ていた画面を失うのは食い違い
            if (action == Services.ShortcutAction.FindInPage)
            {
                e.Handled = true;
                OpenFind();
                return true;
            }

            if (!main.RunShortcut(action))
            {
                return false;
            }

            e.Handled = true;
            return true;
        }

        return false;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T found)
            {
                return found;
            }

            if (FindDescendant<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    /// <summary>
    /// 貼り付けもドロップと同じ扱い。マウスだけで完結させたいならドロップ、
    /// キーボードが使えるなら貼り付けの方が速い。
    ///
    /// 入力欄にいるときは何もしない。そこでの Ctrl+V は文字を貼る操作で、
    /// 横取りすると打てなくなる。
    /// </summary>
    /// <summary>
    /// Esc で、まとめて操作するための選択を解除する（ユーザ指示 2026-09-20・M6）。
    /// 前は解除がボタンだけで、置き場所も画面ごとに違った（検索は下の帯・未確定は一覧の上）。
    ///
    /// **文字を打っている間と、選んでいないときは横取りしない**——入力欄の Esc（候補を閉じる・打ち消す）と
    /// 画面の中の検索の帯を閉じる Esc を先に通す。
    /// </summary>
    private static bool TryClearSelection(MainViewModel main, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape
            || System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox or System.Windows.Controls.ComboBox
            || main.CurrentViewModel is not ViewModels.ISelectionScreen { HasSelection: true } screen)
        {
            return false;
        }

        screen.ClearSelection();
        e.Handled = true;
        return true;
    }

    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is MainViewModel shortcutMain && TryShortcut(shortcutMain, e))
        {
            return;
        }

        if (DataContext is MainViewModel current && TryMoveGallery(current, e))
        {
            return;
        }

        if (DataContext is MainViewModel selecting && TryClearSelection(selecting, e))
        {
            return;
        }

        if (e.Key != System.Windows.Input.Key.V
            || (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0
            || DataContext is not MainViewModel main)
        {
            return;
        }

        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox)
        {
            return;
        }

        // ドロップと同じ読み方をする。クリップボードにも、素のテキストを持たず
        // URLの形式だけが置かれることがある（ブラウザからのコピーがそう）
        List<string>? paths;
        string? text;
        bool hasBitmap;
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null)
            {
                return;
            }

            paths = data.GetDataPresent(DataFormats.FileDrop)
                ? (data.GetData(DataFormats.FileDrop) as string[])?.ToList()
                : null;

            text = ReadText(data);

            // スクリーンショットは絵そのものとして置かれる。
            // 商品ページならこれをそのまま画像として足せる
            hasBitmap = data.GetDataPresent(DataFormats.Bitmap);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // 他のアプリがクリップボードを掴んでいると読めない。黙って返し、次に押せば入る
            // （絵を読む所 MainViewModel.ReadClipboardImage と同じ守り。守りが無いと「予期しないエラー」の窓が出た。洗い出し 13）
            return;
        }

        if (paths is { Count: > 0 } || hasBitmap || !string.IsNullOrWhiteSpace(text))
        {
            e.Handled = true;
            Handle(main, paths, text, hasBitmap);
        }
    }
}
