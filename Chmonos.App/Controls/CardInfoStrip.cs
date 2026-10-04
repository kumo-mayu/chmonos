using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Controls;

/// <summary>
/// カードの名前の下の札と1行（ユーザ判断 2026-10-04・案A）：ユーザータグの札＋残りの数・属性の札（どちらもカードの幅に入るだけ）・払った額と対応の数。
///
/// **部品を並べずに、この1つが自分で描く。**札を枠と文字の部品で組むと、カード1枚につき部品が十数個・結び付けが二十ほど増え、
/// 作り物の2000件を同じ歩みで流す時間が 1.37秒 → 2.12秒（33ms を超えた歩みが 60 歩中 2 → 44）になった。
/// 札は押さない飾りなので、部品である必要が無い。高さは値の有無によらず決め打ち（一覧の段を揃える）
/// </summary>
public sealed class CardInfoStrip : FrameworkElement
{
    // 縦の割り付け（今までのカードの札と同じ大きさ）。札の段 18・間 4・札の段 18・間 4・1行 16
    private const double ChipHeight = 18;
    private const double RowGap = 4;
    private const double MetaHeight = 16;
    private const double ChipGap = 4;
    private const double TagTextMax = 84;
    private const double AttributeTextMax = 96;


    /// <summary>1行の右端は右下の星に掛けない。</summary>
    private const double StarInset = 22;

    public const double StripHeight = ChipHeight + RowGap + ChipHeight + RowGap + MetaHeight;

    private static readonly FontFamily Family = new("Yu Gothic UI, Meiryo, Segoe UI");
    private static readonly Typeface Regular = new(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static readonly DependencyProperty InfoProperty = DependencyProperty.Register(
        nameof(Info), typeof(CardInfo), typeof(CardInfoStrip),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public CardInfo? Info
    {
        get => (CardInfo?)GetValue(InfoProperty);
        set => SetValue(InfoProperty, value);
    }

    // 色は色の表の鍵で受ける（明るい・暗いの切り替えで描き直す）
    private static readonly DependencyProperty TagBackProperty = BrushProperty("TagBack");
    private static readonly DependencyProperty TagBorderProperty = BrushProperty("TagBorder");
    private static readonly DependencyProperty TagTextProperty = BrushProperty("TagText");
    private static readonly DependencyProperty MoreBackProperty = BrushProperty("MoreBack");
    private static readonly DependencyProperty MoreBorderProperty = BrushProperty("MoreBorder");
    private static readonly DependencyProperty MoreTextProperty = BrushProperty("MoreText");
    private static readonly DependencyProperty AttributeBorderProperty = BrushProperty("AttributeBorder");
    private static readonly DependencyProperty AttributeNameProperty = BrushProperty("AttributeName");
    private static readonly DependencyProperty AttributeValueProperty = BrushProperty("AttributeValue");
    private static readonly DependencyProperty MetaTextProperty = BrushProperty("MetaText");

    private static DependencyProperty BrushProperty(string name)
        => DependencyProperty.Register(name, typeof(Brush), typeof(CardInfoStrip),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public CardInfoStrip()
    {
        SetResourceReference(TagBackProperty, "AccentSoft");
        SetResourceReference(TagBorderProperty, "AccentBorder");
        SetResourceReference(TagTextProperty, "AccentText");
        SetResourceReference(MoreBackProperty, "ChipBack");
        SetResourceReference(MoreBorderProperty, "ChipBorder");
        SetResourceReference(MoreTextProperty, "ChipText");
        SetResourceReference(AttributeBorderProperty, "BorderStrong");
        SetResourceReference(AttributeNameProperty, "TextMuted");
        SetResourceReference(AttributeValueProperty, "TextBody");
        SetResourceReference(MetaTextProperty, "TextMuted");
        SnapsToDevicePixels = true;
    }

    private Brush Get(DependencyProperty property) => (Brush)GetValue(property);

    protected override Size MeasureOverride(Size availableSize)
        => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, StripHeight);

    protected override void OnRender(DrawingContext drawing)
    {
        if (Info is not { } info)
        {
            return;
        }

        var width = ActualWidth;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, width, StripHeight)));

        // 1段目：ユーザータグ（丸い札）と残りの数
        // 札の数はここで、カードの幅に入るだけ決める（メモ37-③。前は2枚・幅200未満は1枚の決め打ちだった）。高さは変えない
        var tagTexts = info.Tags.Select(tag => Text(tag, 10, Get(TagTextProperty), TagTextMax, dpi)).ToList();
        var tagCount = FitCount(tagTexts.Select(text => ChipWidth(text, 7)).ToList(), info.TagTotal, width, ChipGap,
            hidden => ChipWidth(Text($"+{hidden}", 10, Get(MoreTextProperty), double.PositiveInfinity, dpi), 6));
        var x = 0.0;
        for (var i = 0; i < tagCount; i++)
        {
            x = DrawChip(drawing, x, 0, tagTexts[i], 7, 9, Get(TagBackProperty), Get(TagBorderProperty));
        }

        var tagsRight = x;
        if (info.TagTotal > tagCount)
        {
            tagsRight = DrawChip(drawing, x, 0, Text($"+{info.TagTotal - tagCount}", 10, Get(MoreTextProperty), double.PositiveInfinity, dpi), 6, 9,
                Get(MoreBackProperty), Get(MoreBorderProperty));
        }

        // 2段目：属性（線だけの角の丸い札。名前は薄く、値は太く）。右下の星の上に掛からない幅まで。入らない分は出さない（全部は乗せたときの重ねで見える）
        var attributeTexts = info.Attributes.Select(chip =>
        {
            var text = Text($"{chip.Name} {chip.ValueText}", 10, Get(AttributeNameProperty), AttributeTextMax, dpi);
            var nameLength = chip.Name.Length + 1;
            text.SetForegroundBrush(Get(AttributeValueProperty), nameLength, chip.ValueText.Length);
            text.SetFontWeight(FontWeights.Bold, nameLength, chip.ValueText.Length);
            return text;
        }).ToList();
        var attributeCount = FitCount(attributeTexts.Select(text => ChipWidth(text, 5)).ToList(), attributeTexts.Count, width - StarInset, ChipGap, null);
        x = 0;
        for (var i = 0; i < attributeCount; i++)
        {
            x = DrawChip(drawing, x, ChipHeight + RowGap, attributeTexts[i], 5, 4, null, Get(AttributeBorderProperty));
        }

        // 札の段の、札のある幅だけを「乗せている」と数える（メモ37-②：重ねは札に乗せて少し待ってから出す）。
        // 何も描いていない所は当たらないので、透明で塗っておく。札の間の隙間で離れた扱いにならないよう、段ごと1枚で覆う
        var chipsRight = Math.Max(tagsRight, x) - ChipGap;
        if (chipsRight > 0 && (tagCount > 0 || attributeCount > 0))
        {
            drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Math.Min(width, chipsRight), ChipHeight + RowGap + ChipHeight));
        }

        // 3段目：払った額・対応の数
        if (info.MetaLine.Length > 0)
        {
            // 未所持の襷（カードの左下）に掛からないよう、そのとき1行を右へ寄せる（CardInfo.MetaMargin）
            var left = info.MetaMargin.Left;
            var meta = Text(info.MetaLine, 11, Get(MetaTextProperty), Math.Max(1, width - left - StarInset), dpi);
            drawing.DrawText(meta, new Point(left, ChipHeight + RowGap + ChipHeight + RowGap + (MetaHeight - meta.Height) / 2));
        }

        drawing.Pop();
    }

    private static double ChipWidth(FormattedText text, double padding) => Math.Ceiling(text.Width + padding * 2);

    /// <summary>
    /// 幅 <paramref name="available"/> に札を何枚並べられるか。入らない札があるときは「+n」の札のぶんも空ける。
    /// 1枚も入らなくても1枚は出す（切れていても、何かが付いていると分かる方がよい）
    /// </summary>
    /// <param name="widths">札ごとの幅（付いている順）。</param>
    /// <param name="total">付いている札の数（<paramref name="widths"/> は測る上限までなので、それ以上に付いていることがある）。</param>
    /// <param name="moreWidthOf">隠れる数から「+n」の札の幅を出す。出さない段（属性）は null。</param>
    internal static int FitCount(IReadOnlyList<double> widths, int total, double available, double gap, Func<int, double>? moreWidthOf)
    {
        for (var count = widths.Count; count >= 1; count--)
        {
            var used = widths.Take(count).Sum() + gap * (count - 1);
            var hidden = total - count;
            if (hidden > 0 && moreWidthOf is not null)
            {
                used += gap + moreWidthOf(hidden);
            }

            if (used <= available)
            {
                return count;
            }
        }

        return Math.Min(1, widths.Count);
    }

    /// <summary>札を1枚描き、次の札の左端を返す。</summary>
    private static double DrawChip(
        DrawingContext drawing, double x, double y, FormattedText text, double padding, double radius, Brush? back, Brush border)
    {
        var chipWidth = ChipWidth(text, padding);
        var rect = new Rect(x + 0.5, y + 0.5, chipWidth - 1, ChipHeight - 1);
        drawing.DrawRoundedRectangle(back, new Pen(border, 1), rect, radius, radius);
        drawing.DrawText(text, new Point(x + padding, y + (ChipHeight - text.Height) / 2));
        return x + chipWidth + ChipGap;
    }

    private static FormattedText Text(string text, double size, Brush brush, double maxWidth, double dpi)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Regular, size, brush, null,
            TextFormattingMode.Ideal, dpi)
        {
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        if (!double.IsInfinity(maxWidth))
        {
            formatted.MaxTextWidth = maxWidth;
        }

        return formatted;
    }

    /// <summary>読み上げには、描いた札の文字を1つの文として渡す（部品が無いので、言わないと札が無いのと同じになる）。</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new StripPeer(this);

    private sealed class StripPeer(CardInfoStrip owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(CardInfoStrip);

        protected override bool IsControlElementCore() => !string.IsNullOrEmpty(GetNameCore());

        protected override string GetNameCore()
        {
            if (((CardInfoStrip)Owner).Info is not { } info)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (info.TagsLine.Length > 0)
            {
                parts.Add(info.TagsLine);
            }

            if (info.AttributesLine.Length > 0)
            {
                parts.Add(info.AttributesLine);
            }

            if (info.MetaLine.Length > 0)
            {
                parts.Add(info.MetaLine);
            }

            return string.Join("、", parts);
        }
    }
}
