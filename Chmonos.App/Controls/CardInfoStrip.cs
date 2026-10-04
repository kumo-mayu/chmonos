using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Controls;

/// <summary>
/// カードの名前の下の札と1行（ユーザ判断 2026-10-04・案A）：ユーザータグの札2枚＋残りの数・属性の札2枚・払った額と対応の数。
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
        var x = 0.0;
        foreach (var tag in new[] { info.Tag1, info.Tag2 })
        {
            if (tag is not null)
            {
                x = DrawChip(drawing, x, 0, Text(tag, 10, Get(TagTextProperty), TagTextMax, dpi), 7, 9, Get(TagBackProperty), Get(TagBorderProperty));
            }
        }

        if (info.HasTagMore)
        {
            DrawChip(drawing, x, 0, Text(info.TagMore, 10, Get(MoreTextProperty), double.PositiveInfinity, dpi), 6, 9, Get(MoreBackProperty), Get(MoreBorderProperty));
        }

        // 2段目：属性（線だけの角の丸い札。名前は薄く、値は太く）
        x = 0;
        foreach (var chip in new[] { info.Attribute1, info.Attribute2 })
        {
            if (chip is not null)
            {
                var text = Text($"{chip.Name} {chip.ValueText}", 10, Get(AttributeNameProperty), AttributeTextMax, dpi);
                var nameLength = chip.Name.Length + 1;
                text.SetForegroundBrush(Get(AttributeValueProperty), nameLength, chip.ValueText.Length);
                text.SetFontWeight(FontWeights.Bold, nameLength, chip.ValueText.Length);
                x = DrawChip(drawing, x, ChipHeight + RowGap, text, 5, 4, null, Get(AttributeBorderProperty));
            }
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

    /// <summary>札を1枚描き、次の札の左端を返す。</summary>
    private static double DrawChip(
        DrawingContext drawing, double x, double y, FormattedText text, double padding, double radius, Brush? back, Brush border)
    {
        var chipWidth = Math.Ceiling(text.Width + padding * 2);
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
