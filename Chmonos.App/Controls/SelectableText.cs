using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace Chmonos.App.Controls;

/// <summary>
/// 読み取り専用の <see cref="RichTextBox"/> に、URLをリンク化した本文を流し込む添付プロパティ。
///
/// TextBlockを使わない理由は2つある。WPFのTextBlockは文字を選択できないこと、
/// そしてBOOTHの説明文には対応アバターのURLが並ぶ文化があり、そこを踏めるようにしたいこと。
/// RichTextBoxの Document は依存関係プロパティではないのでバインドできず、ここで橋渡しする。
/// </summary>
/// <summary>
/// 本文中のリンクをアプリ内で開けるかどうかを判断し、開く役。
/// ライブラリに持っているBOOTH商品へのリンクは、ブラウザではなくアプリ内の商品ページへ送る。
/// </summary>
public interface IInAppLinkNavigator
{
    /// <summary>アプリ内に行き先があるか。false ならブラウザで開く。</summary>
    bool CanNavigate(Uri uri);

    void Navigate(Uri uri);
}

public static class SelectableText
{
    /// <summary>URLとして拾う範囲。日本語の括弧や引用符は本文側の記号なので含めない。</summary>
    private static readonly Regex UrlPattern = new(
        @"https?://[^\s　<>""'（）「」『』【】]+",
        RegexOptions.Compiled);

    /// <summary>URLの末尾に紛れ込みやすい記号。リンクからは外して本文へ戻す。</summary>
    private static readonly char[] TrailingNoise = ".,;:!?。、）)】」』>".ToCharArray();

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(SelectableText),
            new PropertyMetadata(null, OnTextChanged));

    /// <summary>アプリ内で開ける行き先の判断役。設定されていなければ全てブラウザで開く。</summary>
    public static readonly DependencyProperty NavigatorProperty =
        DependencyProperty.RegisterAttached(
            "Navigator",
            typeof(IInAppLinkNavigator),
            typeof(SelectableText),
            new PropertyMetadata(null, OnNavigatorChanged));

    /// <summary>
    /// 帯を付ける行（<see cref="ILineMarks"/>）。商品ページで、BOOTH の更新で足された行と消えた行を本文の上で示す（メモ13-②・メモ17）。
    /// 無ければ今までと同じ1つの段落で組む
    /// </summary>
    public static readonly DependencyProperty MarkedLinesProperty =
        DependencyProperty.RegisterAttached(
            "MarkedLines",
            typeof(ILineMarks),
            typeof(SelectableText),
            new PropertyMetadata(null, OnTextChanged));

    /// <summary>消えた行の段落に付ける印（<see cref="System.Windows.FrameworkContentElement.Tag"/>）。既読にするときに、画面が消える高さを測る。</summary>
    public static readonly object RemovedLineTag = new();

    public static void SetMarkedLines(DependencyObject element, ILineMarks? value) => element.SetValue(MarkedLinesProperty, value);

    public static ILineMarks? GetMarkedLines(DependencyObject element) => (ILineMarks?)element.GetValue(MarkedLinesProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetNavigator(DependencyObject element, IInAppLinkNavigator? value)
        => element.SetValue(NavigatorProperty, value);

    public static IInAppLinkNavigator? GetNavigator(DependencyObject element)
        => (IInAppLinkNavigator?)element.GetValue(NavigatorProperty);

    private static void OnTextChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
        => Refresh(element);

    // 本文とNavigatorのどちらが先に設定されるかはXAMLの書き順次第なので、どちらでも組み直す。
    private static void OnNavigatorChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
        => Refresh(element);

    private static void Refresh(DependencyObject element)
    {
        if (element is not RichTextBox box)
        {
            return;
        }

        box.IsReadOnly = true;
        box.IsDocumentEnabled = true;

        // 中身のScrollViewerに横取りされると外側のページがスクロールしなくなるので、親へ流し直す。
        box.PreviewMouseWheel -= ForwardMouseWheel;
        box.PreviewMouseWheel += ForwardMouseWheel;

        box.Document = Build(GetText(box), GetNavigator(box), GetMarkedLines(box));
    }

    private static void ForwardMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (args.Handled || sender is not RichTextBox box)
        {
            return;
        }

        args.Handled = true;
        box.RaiseEvent(new MouseWheelEventArgs(args.MouseDevice, args.Timestamp, args.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = box,
        });
    }

    /// <summary>行の種類。同じ種類の行が続く間は1つの段落にまとめる。</summary>
    private enum LineKind
    {
        Plain,
        Added,
        Removed,
    }

    internal static FlowDocument Build(string? text, IInAppLinkNavigator? navigator, ILineMarks? marks = null)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            LineHeight = 21,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };

        // 見出しごと消えた物は本文が無く、消えた行だけを並べる
        var lines = string.IsNullOrEmpty(text) ? [] : text.Replace("\r\n", "\n").Split('\n');
        var removed = marks?.RemovedLines ?? [];
        if (lines.Length == 0 && removed.Count == 0)
        {
            document.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
            return document;
        }

        Paragraph? paragraph = null;
        var paragraphKind = LineKind.Plain;
        void Append(string line, LineKind kind)
        {
            // 種類の境で段落を分ける。地と左の線は段落に付けると行の幅いっぱいに引ける（Run の地は文字の幅だけ）
            if (paragraph is null || kind != paragraphKind)
            {
                paragraph = new Paragraph { Margin = new Thickness(0) };
                document.Blocks.Add(paragraph);
                paragraphKind = kind;
                Mark(paragraph, kind);
            }
            else
            {
                paragraph.Inlines.Add(new LineBreak());
            }

            AppendLine(paragraph, line, navigator);
        }

        var next = 0;
        for (var index = 0; index <= lines.Length; index++)
        {
            // 消えた行は元の位置（その行の前）へ差し込む（メモ17）
            while (next < removed.Count && removed[next].Before <= index)
            {
                Append(removed[next++].Text, LineKind.Removed);
            }

            if (index < lines.Length)
            {
                Append(lines[index], marks?.AddedLines.Contains(index) == true ? LineKind.Added : LineKind.Plain);
            }
        }

        return document;
    }

    /// <summary>
    /// 足された行・消えた行の段落に帯（地と左の線）を付ける。色は商品ページの印と同じ系統（足した＝緑・消えた＝赤）で、
    /// 地は帯の専用の薄い色（帯が多い商品で色が強すぎないように。メモ17）。明暗で差し替わるよう鍵で指す。
    /// 色だけに頼らないよう、乗せると何の帯かを言う
    /// </summary>
    private static void Mark(Paragraph paragraph, LineKind kind)
    {
        if (kind == LineKind.Plain)
        {
            return;
        }

        var added = kind == LineKind.Added;
        paragraph.SetResourceReference(TextElement.BackgroundProperty, added ? "AddedBand" : "RemovedBand");
        paragraph.SetResourceReference(Block.BorderBrushProperty, added ? "Good" : "Bad");
        paragraph.BorderThickness = new Thickness(3, 0, 0, 0);
        paragraph.Padding = new Thickness(6, 0, 0, 0);
        paragraph.ToolTip = added ? "BOOTHで追加された行です。" : "BOOTHで削除された行です。";
        if (!added)
        {
            paragraph.Tag = RemovedLineTag;
        }
    }

    private static void AppendLine(Paragraph paragraph, string line, IInAppLinkNavigator? navigator)
    {
        var position = 0;

        foreach (Match match in UrlPattern.Matches(line))
        {
            var url = match.Value.TrimEnd(TrailingNoise);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (match.Index > position)
            {
                paragraph.Inlines.Add(new Run(line[position..match.Index]));
            }

            paragraph.Inlines.Add(CreateLink(url, uri, navigator));

            // 末尾から外した記号は本文として続ける
            position = match.Index + url.Length;
        }

        if (position < line.Length)
        {
            paragraph.Inlines.Add(new Run(line[position..]));
        }
    }

    private static Hyperlink CreateLink(string url, Uri uri, IInAppLinkNavigator? navigator)
    {
        var link = new Hyperlink(new Run(url)) { NavigateUri = uri };

        // ライブラリに持っている商品へのリンクは、色を「所持」と揃えて外部リンクと区別する。
        // 踏む前に、外に出るのか手元のページへ移るのかが分かるようにするため。
        if (navigator?.CanNavigate(uri) == true)
        {
            link.ToolTip = "ライブラリ内の商品ページを開きます";
            // 鍵で指す。色を取り出して入れると、色の表を差し替えたときに古い色のまま残る
            link.SetResourceReference(TextElement.ForegroundProperty, "Good");

            link.RequestNavigate += (_, args) =>
            {
                args.Handled = true;
                navigator.Navigate(args.Uri);
            };

            return link;
        }

        link.ToolTip = url;
        link.RequestNavigate += OpenInBrowser;
        return link;
    }

    private static void OpenInBrowser(object sender, RequestNavigateEventArgs args)
    {
        args.Handled = true;

        // 説明文は出品者が書いた物で、手で直した JSON からも来る。開くのは Shell の守り（http/https だけ）を通す
        Services.Shell.OpenUrl(args.Uri.AbsoluteUri);
    }
}
