using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Controls;

/// <summary>
/// 商品ページ・編集画面の対応アバターの札1枚（<see cref="AvatarRow"/>）。**普段は枠と名前を自分で描き、乗せたとき・キーボードで止まったときに中身を作る。**
///
/// 札は部品で組むと1枚に枠・印・文字・✓・×・右クリックのメニュー・吹き出しと結び付けが二十ほど付き、
/// 対応40体の商品では、開くたびに札を組んで並べる処理が約25ms、読み上げ用の木の更新が約25ms乗っていた（2026-11-08・dotnet-trace）。
/// 欄を畳むと約70ms速く開いた。✓ と × は乗せたとき・止まったときにしか見えないので、それまで作らなくても見た目は変わらない。
/// 検索のカードの札（<see cref="CardInfoStrip"/>：自分で描く）と、カードの中身を後から作る入れ物（<see cref="DeferredCardHost"/>）と同じ考え方。
///
/// 入れ物はこの枠のまま替えない（中身を差し込むだけ）。✓・× の出し入れは「祖先の Border に乗せているか」で決まり、
/// 並び（<see cref="ArrowGroup"/>）は止まっていた物が隠れると次の札へ移るので、札を差し替える作りにすると両方が崩れる。
/// 作った中身は捨てない（札の部品は隠すだけで捨てない：ChipStrip と同じ）。
/// </summary>
public sealed class AvatarChip : PressableBorder
{
    /// <summary>作るときに当てる中身（印・名前・✓・×）。DataContext は札の <see cref="AvatarRow"/>。</summary>
    public static readonly DependencyProperty ContentTemplateProperty = DependencyProperty.Register(
        nameof(ContentTemplate), typeof(DataTemplate), typeof(AvatarChip));

    /// <summary>作るときに当てる右クリックのメニュー（根が ContextMenu のテンプレート）。</summary>
    public static readonly DependencyProperty MenuTemplateProperty = DependencyProperty.Register(
        nameof(MenuTemplate), typeof(DataTemplate), typeof(AvatarChip));

    private static readonly DependencyProperty DotBrushProperty = DependencyProperty.Register(
        "DotBrush", typeof(Brush), typeof(AvatarChip),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty NameBrushProperty = DependencyProperty.Register(
        "NameBrush", typeof(Brush), typeof(AvatarChip),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>名前の字の大きさ（作った中身の TextBlock と同じ）。</summary>
    private const double NameFontSize = 11.5;

    /// <summary>名前の字の形と色は、アプリの文字の型（App.xaml の暗黙の TextBlock の型）と同じにする。型が直に当てるので、上から受け継ぐ値ではない。</summary>
    private static readonly Typeface NameFace = new(new FontFamily("Yu Gothic UI, Meiryo, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>
    /// 名前の無い中身の大きさ（確認待ちの印があるか別）。描くときは、これに名前の幅を足すと作った中身と同じ大きさになる。
    /// × は見えない間も場所を取る（Opacity 0）ので、描くときも同じだけ空けないと、乗せた瞬間に札が伸びて並びが組み変わる。
    /// 幅を数字で決め打ちしないのは、ボタンの型（余白・字の形）を直したときに黙ってずれるため。1回だけ作って測る
    /// </summary>
    /// <remarks>テンプレートは欄（ItemAvatarsPanel）ごとの資源で、編集画面を開くたびに新しくできる。弱い参照で持ち、欄と一緒に消えるようにする</remarks>
    private static readonly ConditionalWeakTable<DataTemplate, Size[]> EmptySizes = new();

    private AvatarRow? _row;
    private bool _realized;

    public AvatarChip()
    {
        SetResourceReference(DotBrushProperty, "Warn");
        SetResourceReference(NameBrushProperty, "Text");
        DataContextChanged += (_, e) => Bind(e.NewValue as AvatarRow);
    }

    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    public DataTemplate? MenuTemplate
    {
        get => (DataTemplate?)GetValue(MenuTemplateProperty);
        set => SetValue(MenuTemplateProperty, value);
    }

    internal bool IsRealized => _realized;

    /// <summary>
    /// 札の色・名前・押したときの命令を、結び付け（Binding・Style の引き金）を使わずに入れる。
    /// 札の行の値は作った後に変わらない（init だけ。変わるときは行ごと作り直される）
    /// </summary>
    private void Bind(AvatarRow? row)
    {
        _row = row;
        if (row is null)
        {
            return;
        }

        SetResourceReference(BackgroundProperty, row.IsOwned ? "GoodSoft" : "AccentSoft");
        SetResourceReference(BorderBrushProperty, row.IsOwned ? "GoodBorder" : "AccentBorder");

        // 持っていないバリエーション向け（アバターごとに売っている衣装）。隠さず薄くする（向き先は推した値のことがある）
        Opacity = row.IsOtherVariation ? 0.45 : 1;
        Command = row.OpenCommand;
        System.Windows.Automation.AutomationProperties.SetName(this, $"{row.Name}を開く");
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>中身を作る。乗せたとき・止まったときに呼ぶ。試験からも呼ぶ。</summary>
    internal void Realize()
    {
        if (_realized || _row is null || ContentTemplate is null)
        {
            return;
        }

        _realized = true;
        Child = ContentTemplate.LoadContent() as UIElement;
        if (MenuTemplate?.LoadContent() is ContextMenu menu)
        {
            ContextMenu = menu;
        }

        ToolTip = _row.SourceTooltip;
        InvalidateVisual();

        // 札の並びは Tab で1回だけ止まる（ArrowGroup）。後からできた ✓・× を止まらない物にする。
        // 並びは見えている部品しか数えないので、先に配置を済ませる（カードの中身を後から作るときと同じ：CardBuildQueue）
        UpdateLayout();
        ArrowGroup.Adopt(this);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        Realize();
        base.OnMouseEnter(e);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        Realize();
        base.OnGotKeyboardFocus(e);
    }

    /// <summary>札を押すとそのアバターを開く（U13）。中の ✓・× はボタンが受けるので、ここまで来ない。</summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!e.Handled && Command is { } command && command.CanExecute(CommandParameter))
        {
            e.Handled = true;
            command.Execute(CommandParameter);
        }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (_realized || _row is null || ContentTemplate is null)
        {
            return base.MeasureOverride(constraint);
        }

        var empty = EmptySize(_row.IsUnconfirmed);
        var name = NameText(_row.Name);
        return new Size(
            empty.Width + name.WidthIncludingTrailingWhitespace + Frame.Width,
            Math.Max(empty.Height, name.Height) + Frame.Height);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (_realized || _row is null)
        {
            return;
        }

        // 中身の並び（作った中身と同じ）：［確認待ちの印 13］名前［× の場所］
        var left = BorderThickness.Left + Padding.Left;
        var top = BorderThickness.Top + Padding.Top;
        var contentHeight = ActualHeight - Frame.Height;
        if (_row.IsUnconfirmed)
        {
            // 印の枠は幅13で、左へ3はみ出して置く（Margin -3,0,3,0）。点は直径6で枠の真ん中
            drawing.DrawEllipse((Brush)GetValue(DotBrushProperty), null, new Point(left - 3 + 6.5, top + contentHeight / 2), 3, 3);
            left += 13;
        }

        var name = NameText(_row.Name);
        drawing.DrawText(name, new Point(left, top + (contentHeight - name.Height) / 2));
    }

    private Size Frame => new(
        BorderThickness.Left + BorderThickness.Right + Padding.Left + Padding.Right,
        BorderThickness.Top + BorderThickness.Bottom + Padding.Top + Padding.Bottom);

    private FormattedText NameText(string name) => new(
        name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        NameFace, NameFontSize, (Brush)GetValue(NameBrushProperty), null, TextFormattingMode.Ideal, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>
    /// 名前の無い中身を1回だけ作って測る（✓・× の字の形は、この札が受け継いでいる物に合わせる）。
    /// 作った直後の部品は結び付けがまだ効いていない（値は画面のスレッドが次に空いたときに入る）ので、行を渡さずに測る：
    /// 名前は空・確認待ちの印は出ている形になる。印の無い形は、中身の先頭の子（印の枠）の幅を引いて出す
    /// </summary>
    private Size EmptySize(bool unconfirmed)
    {
        var template = ContentTemplate!;
        if (EmptySizes.TryGetValue(template, out var sizes))
        {
            return sizes[unconfirmed ? 1 : 0];
        }

        if (template.LoadContent() is not Panel probe || probe.Children.Count == 0)
        {
            return default;
        }

        TextElement.SetFontFamily(probe, TextElement.GetFontFamily(this));
        TextElement.SetFontStyle(probe, TextElement.GetFontStyle(this));
        TextElement.SetFontWeight(probe, TextElement.GetFontWeight(this));
        TextElement.SetFontStretch(probe, TextElement.GetFontStretch(this));
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var withDot = probe.DesiredSize;
        sizes = [new Size(withDot.Width - probe.Children[0].DesiredSize.Width, withDot.Height), withDot];
        EmptySizes.AddOrUpdate(template, sizes);
        return sizes[unconfirmed ? 1 : 0];
    }
}
