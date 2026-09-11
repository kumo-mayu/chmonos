using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace BoothAssetManager.App.Views;

/// <summary>画面内検索の一致1つ。どの部品の、どこから何文字か。</summary>
public sealed record FindMatch(FrameworkElement Owner, TextPointer Start, int Length);

/// <summary>
/// 画面の中の文字を探して印を付ける（U20・Ctrl+F）。
///
/// 探すのは**見えている文字**（TextBlock と、説明文の RichTextBox）だけ。入力欄は探さない
/// （打っている最中の文字が一致に数えられると、どこを探しているのか分からなくなる）。
/// 一覧のうち画面の外の行は作られていない（仮想化）ので探せない——検索とショップの画面は
/// Ctrl+F で検索欄へ入る（ユーザ判断）ので、ここに来るのはそれ以外の画面。
/// 畳んだ見出しの中は、画面の側が開いてから探す（<see cref="ViewModels.ItemViewModel.RevealMatches"/>）。
/// </summary>
public static class FindInPage
{
    /// <summary>ひらがな・カタカナ、全角・半角、大文字・小文字は区別しない。アバター名の絞り込みと揃える。</summary>
    public const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

    public static List<FindMatch> Find(DependencyObject root, string needle)
    {
        var matches = new List<FindMatch>();
        if (needle.Length == 0)
        {
            return matches;
        }

        var compare = CultureInfo.CurrentCulture.CompareInfo;
        Walk(root);
        return matches;

        void Walk(DependencyObject node)
        {
            if (node is UIElement { IsVisible: false })
            {
                return;
            }

            switch (node)
            {
                case TextBlock block:
                    Collect(block, block.ContentStart, block.ContentEnd);
                    return;
                case RichTextBox rich:
                    Collect(rich, rich.Document.ContentStart, rich.Document.ContentEnd);
                    return;
                case TextBox:
                    return;
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            {
                Walk(VisualTreeHelper.GetChild(node, index));
            }
        }

        // 文字の並び（Run）ごとに探す。リンクで区切られた所をまたぐ一致は拾えないが、まれなので割り切る
        void Collect(FrameworkElement owner, TextPointer start, TextPointer end)
        {
            for (var pointer = start;
                 pointer is not null && pointer.CompareTo(end) < 0;
                 pointer = pointer.GetNextContextPosition(LogicalDirection.Forward))
            {
                if (pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
                {
                    continue;
                }

                var text = pointer.GetTextInRun(LogicalDirection.Forward);
                var from = 0;
                while (from < text.Length)
                {
                    var index = compare.IndexOf(text.AsSpan(from), needle.AsSpan(), Options, out var length);
                    if (index < 0 || pointer.GetPositionAtOffset(from + index) is not { } found)
                    {
                        break;
                    }

                    matches.Add(new FindMatch(owner, found, length));
                    from += index + Math.Max(1, length);
                }
            }
        }
    }

    /// <summary>
    /// 印の四角（<paramref name="host"/> の座標）。行をまたぐ一致は行ごとに分ける。
    /// 部品が作り直されて一致が古くなっていたら、何も返さない。
    /// </summary>
    public static List<Rect> RectsOf(FindMatch match, Visual host)
    {
        var rects = new List<Rect>();
        try
        {
            if (!match.Owner.IsVisible || !host.IsAncestorOf(match.Owner))
            {
                return rects;
            }

            var transform = match.Owner.TransformToAncestor(host);
            Rect? line = null;

            for (var offset = 0; offset < match.Length; offset++)
            {
                if (match.Start.GetPositionAtOffset(offset) is not { } left
                    || match.Start.GetPositionAtOffset(offset + 1) is not { } right)
                {
                    break;
                }

                var a = left.GetCharacterRect(LogicalDirection.Forward);
                var b = right.GetCharacterRect(LogicalDirection.Backward);
                if (a.IsEmpty || b.IsEmpty)
                {
                    continue;
                }

                var glyph = new Rect(new Point(Math.Min(a.Left, b.Left), a.Top), new Point(Math.Max(a.Left, b.Left), a.Bottom));
                if (line is { } current && Math.Abs(current.Top - glyph.Top) < 1)
                {
                    line = Rect.Union(current, glyph);
                }
                else
                {
                    if (line is { } done)
                    {
                        rects.Add(transform.TransformBounds(done));
                    }

                    line = glyph;
                }
            }

            if (line is { } last)
            {
                rects.Add(transform.TransformBounds(last));
            }
        }
        catch (InvalidOperationException)
        {
            // 部品が画面から外れた（読み直しで作り直された）。印を描かないだけにする
        }

        return rects;
    }

    /// <summary>一致の場所まで流す。</summary>
    public static void BringIntoView(FindMatch match)
    {
        try
        {
            var rect = match.Start.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty)
            {
                match.Owner.BringIntoView();
                return;
            }

            // 上下に少し余白を付ける。ちょうど端に来ると、帯の下に隠れたり見落としたりする
            rect.Inflate(0, 40);
            match.Owner.BringIntoView(rect);
        }
        catch (InvalidOperationException)
        {
        }
    }
}

/// <summary>画面内検索の印。今の一致だけ濃くする。押す操作は下の画面へ通す。</summary>
public sealed class FindHighlightAdorner : Adorner
{
    private static readonly Brush MatchBrush = Frozen(Color.FromArgb(0x70, 0xFF, 0xE0, 0x66));
    private static readonly Brush CurrentBrush = Frozen(Color.FromArgb(0xA0, 0xFF, 0x9F, 0x2E));

    public FindHighlightAdorner(UIElement host)
        : base(host)
    {
        IsHitTestVisible = false;
    }

    public IReadOnlyList<FindMatch> Matches { get; set; } = [];

    public int Current { get; set; } = -1;

    protected override void OnRender(DrawingContext drawingContext)
    {
        for (var index = 0; index < Matches.Count; index++)
        {
            foreach (var rect in FindInPage.RectsOf(Matches[index], AdornedElement))
            {
                drawingContext.DrawRectangle(index == Current ? CurrentBrush : MatchBrush, null, rect);
            }
        }
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
