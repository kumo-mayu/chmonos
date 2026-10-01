using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// 長いパスを1行に出す（公開前の点検 2026-10-01）。収まらなければ**間**を「…」にし、頭（ドライブ）と末尾（最後のフォルダ名）を残す。
/// 省いたときは吹き出しで全文を出す。
///
/// 前は末尾を「…」で切っていたので、初回の窓で深い場所を選ぶと、選んだ結果いちばん見たい最後のフォルダ名
/// （中に作る「\Chmonos」）が見えず、吹き出しも無かった。パスは末尾がいちばん要る（どこを指しているか）ので、間を省く。
///
/// **横に伸びる置き方で使う**（Grid の * の列・DockPanel の最後・Stretch）。省いた後は文が短くなるので、
/// 中身に合わせて幅が決まる置き方だと、短くなった幅のまま広がらない。
/// </summary>
public sealed class PathLine : TextBlock
{
    public static readonly DependencyProperty PathProperty = DependencyProperty.Register(
        nameof(Path), typeof(string), typeof(PathLine), new PropertyMetadata(string.Empty, (target, _) => ((PathLine)target).Fit()));

    public PathLine()
    {
        TextWrapping = TextWrapping.NoWrap;
        SizeChanged += (_, args) =>
        {
            if (args.WidthChanged)
            {
                Fit();
            }
        };
    }

    /// <summary>出すパス（全文）。<see cref="TextBlock.Text"/> は直に書かない（省いた文で上書きされる）。</summary>
    public string Path
    {
        get => (string)GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    private void Fit()
    {
        var full = Path ?? string.Empty;

        // 並べる前（幅がまだ無い）は全文を置き、並べた後の幅で省き直す
        var room = ActualWidth - Padding.Left - Padding.Right;
        var shown = room <= 0 ? full : PathEllipsis.Fit(full, candidate => Measure(candidate) <= room);

        Text = shown;
        ToolTip = shown == full ? null : full;

        // 読み上げ・自動操作には、省いた文ではなく全文を渡す
        AutomationProperties.SetName(this, full);
    }

    /// <summary>
    /// 文の幅（DIP）。字の並べ方の端数で、測った幅ちょうどだと最後の字が欠けることがあるので、1 DIP 分を余らせる
    /// </summary>
    private double Measure(string text)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace + 1;
    }
}

/// <summary>パスの間を省く決まり（幅の測り方は渡す。試験は字の数で測る）。</summary>
internal static class PathEllipsis
{
    public const string Mark = "…";

    /// <summary>
    /// 収まるならそのまま。収まらなければ、最後の区切りから後ろ（「\Chmonos」）を残して頭を詰め、間を「…」にする。
    /// 最後の名前だけでも収まらなければ、その名前の後ろを残す。区切りの無い名前は後ろを省く。
    /// </summary>
    public static string Fit(string path, Func<string, bool> fits)
    {
        if (path.Length == 0 || fits(path))
        {
            return path;
        }

        // 末尾の区切り（「D:\a\」）は名前ではないので、その前の区切りから見る
        var cut = path.TrimEnd('\\', '/').LastIndexOfAny(['\\', '/']);
        if (cut <= 0)
        {
            var kept = Longest(path.Length, count => fits(path[..count] + Mark));
            return path[..kept] + Mark;
        }

        var head = path[..cut];
        var tail = path[cut..];
        var headKept = Longest(head.Length, count => fits(head[..count] + Mark + tail));
        if (headKept > 0 || fits(Mark + tail))
        {
            return head[..headKept] + Mark + tail;
        }

        var tailKept = Longest(tail.Length, count => fits(Mark + tail[^count..]));
        return Mark + tail[^tailKept..];
    }

    /// <summary>0〜<paramref name="max"/> のうち、収まる最大の数（収まるかは数が減るほど収まりやすい）。1つも収まらなければ 0。</summary>
    private static int Longest(int max, Func<int, bool> fits)
    {
        var low = 0;
        var high = max;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (fits(middle))
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }
}
