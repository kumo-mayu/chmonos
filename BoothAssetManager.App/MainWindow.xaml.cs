using System.Windows;
using System.Linq;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App;

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
            if (e.ChangedButton == System.Windows.Input.MouseButton.XButton1
                && DataContext is MainViewModel main
                && main.RunShortcut(Services.ShortcutAction.Back))
            {
                e.Handled = true;
            }
        };

        HookFind();
    }

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

        if (!IsOnAnyScreen(placement))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;

        if (placement.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// 今の姿を保存できる形にする。
    /// 最大化中は <see cref="Window.RestoreBounds"/>（解除したときの姿）を採る。
    /// 最大化中の値を書くと、次に解除したときに画面いっぱいのまま戻らなくなる。
    /// </summary>
    public WindowPlacement CurrentPlacement()
    {
        var maximized = WindowState == WindowState.Maximized;
        var bounds = maximized || WindowState == WindowState.Minimized ? RestoreBounds : new Rect(Left, Top, Width, Height);

        return new WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = maximized,
        };
    }

    /// <summary>
    /// 今の画面の範囲に掛かっているか。
    ///
    /// 全モニタを囲む矩形（仮想画面）と重なるかで見る。WinFormsのScreenやWin32を使うと
    /// 物理ピクセルで返るので、DIPで持っているこちらの値と混ざる（複数DPIだと実際にずれる）。
    /// 判定は緩いが、狙いは「モニタを外したときに画面外へ開かない」ことなので、これで足りる。
    ///
    /// 少しでも掛かっていればよい。掴んで動かせるなら、そこから直せる。
    /// </summary>
    private static bool IsOnAnyScreen(WindowPlacement placement)
    {
        var screen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        return screen.IntersectsWith(new Rect(placement.Left, placement.Top, placement.Width, placement.Height));
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
    private static string? ReadText(IDataObject data)
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
    private static async void Handle(
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
            MessageBox.Show(
                $"受け取ったものを処理できませんでした。\n\n{exception.Message}",
                "BOOTH Asset Manager",
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

        if (System.Windows.Input.Keyboard.FocusedElement
            is System.Windows.Controls.TextBox or System.Windows.Controls.ComboBox)
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
                || gesture.Key != key
                || gesture.Modifiers != modifiers)
            {
                continue;
            }

            var typing = (modifiers & (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt)) == 0;

            // 文字の欄が使う矢印は素の矢印と Ctrl＋矢印（1語ずつ）だけ。Alt＋矢印は使わないので、
            // 欄の中でも効かせる（Alt＋← で戻れないと、入力欄の多い編集画面では戻る手段が無かった）
            var altOnly = (modifiers & System.Windows.Input.ModifierKeys.Alt) != 0
                && (modifiers & System.Windows.Input.ModifierKeys.Control) == 0;
            if (inText && (typing || (Services.Shortcuts.IsTextEditingKey(key) && !altOnly)))
            {
                return false;
            }

            // U20：検索とショップ以外の画面では、同じキーで画面の中の文字を探す（ユーザ判断）
            if (action == Services.ShortcutAction.FocusSearch && !UsesSearchBox(main.CurrentViewModel))
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
            if (action == Services.ShortcutAction.FocusSearch)
            {
                // 検索画面は差し替えた直後にはまだ組み上がっていない。組み上がってから欄へ入る
                Dispatcher.BeginInvoke(
                    () => FindDescendant<Views.SearchView>(this)?.FocusQuery(),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }

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
        var data = Clipboard.GetDataObject();
        if (data is null)
        {
            return;
        }

        var paths = data.GetDataPresent(DataFormats.FileDrop)
            ? (data.GetData(DataFormats.FileDrop) as string[])?.ToList()
            : null;

        var text = ReadText(data);

        // スクリーンショットは絵そのものとして置かれる。
        // 商品ページならこれをそのまま画像として足せる
        var hasBitmap = data.GetDataPresent(DataFormats.Bitmap);

        if (paths is { Count: > 0 } || hasBitmap || !string.IsNullOrWhiteSpace(text))
        {
            e.Handled = true;
            Handle(main, paths, text, hasBitmap);
        }
    }
}
