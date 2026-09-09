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

        e.Handled = true;
        Handle(main, paths, ReadText(e.Data));
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
    private static async void Handle(MainViewModel main, IReadOnlyList<string>? paths, string? text)
    {
        try
        {
            await main.HandleDropAsync(paths, text);
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
    /// 貼り付けもドロップと同じ扱い。マウスだけで完結させたいならドロップ、
    /// キーボードが使えるなら貼り付けの方が速い。
    ///
    /// 入力欄にいるときは何もしない。そこでの Ctrl+V は文字を貼る操作で、
    /// 横取りすると打てなくなる。
    /// </summary>
    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
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

        if (paths is { Count: > 0 } || !string.IsNullOrWhiteSpace(text))
        {
            e.Handled = true;
            Handle(main, paths, text);
        }
    }
}
