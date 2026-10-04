using System.Windows;

namespace Chmonos.App.Services;

/// <summary>
/// 商品カードの大きさ（一覧の右下のスライダー・設定の <c>cardWidth</c>）。
///
/// 前は設定の「サムネイルの大きさ（小・中・大）」で3段だった（2026-09-15 に効くようにした・R2）。
/// 一覧を見ながら決めたいので、カードとリストを切り替える画面の右下にスライダーを置いて連続で変える（ユーザ判断 2026-09-29）。
/// 見た目は XAML の DynamicResource（<c>CardWidth</c>・<c>CardHeight</c>・<c>CardImageHeight</c>）、
/// 列の割りと絵を読む大きさはここを読む。
/// </summary>
public static class CardMetrics
{
    /// <summary>
    /// 幅の範囲。下は前の「小」（180）より少し小さく、札が2つ並ぶ幅。上は前の「大」（300）より少し大きい所で止める——
    /// 大きくするほど1枚の絵の画素が増え、保持の上限（32MB）に入る枚数が減る（`docs/research/memory-budget.md`）
    /// </summary>
    public const double MinWidth = 160;

    public const double MaxWidth = 360;

    public const double DefaultWidth = 228;

    /// <summary>
    /// 絵の下の文字と札の欄。大きさを変えても文字の量は変わらないので固定。
    /// 前は 136（336 − 200）で、ユーザータグの札1段だった。札をユーザータグ・属性の2段と払った額・対応の1行にした（2026-10-04・案A）
    /// ので 22 足す。値の無い商品でも同じ高さにし、一覧の段を揃える
    /// </summary>
    private const double TextAreaHeight = 158;

    /// <summary>
    /// 絵の枠の高さは幅からこれを引いた値。前の3段（180→150・228→200・300→270）がほぼこの差で、
    /// 横長の枠のまま大きさだけが変わって見える
    /// </summary>
    private const double ImageHeightInset = 28;

    /// <summary>
    /// 絵を読む大きさの刻み（DIP）。スライダーを動かすたびに読む大きさが変わると、見えているカードを毎回読み直し、
    /// 保持にも大きさ違いの写しが溜まる。刻みを越えたときだけ読み直す（160〜360 の間で 5段）
    /// </summary>
    private const int EdgeStepDip = 40;

    /// <summary>カードの右と下の間（XAML の Margin="0,0,14,14" と同じ）。</summary>
    public const double Gap = 14;

    public static double Width { get; private set; } = DefaultWidth;

    public static double ImageHeight { get; private set; } = DefaultWidth - ImageHeightInset;

    public static double Height => ImageHeight + TextAreaHeight;

    /// <summary>
    /// ショップ順の札のアイコンの一辺（メモ16-④）。幅の3割で、カードの大きさに合わせて伸び縮みする。
    /// 前は44の固定で、既定の幅（228）の札では小さかった。3割にしたのは、いちばん狭い幅（160）でもショップ名が
    /// 折り返せる高さが残る大きさのため（札の高さ290に対して、アイコン48・種類と件数の行・名前3行が入る）
    /// </summary>
    public static double ShopIconSize => ShopIconSizeFor(Width);

    internal static double ShopIconSizeFor(double width) => Math.Round(width * 0.3);

    /// <summary>列の割りに使う1枚ぶんの幅。</summary>
    public static double SlotWidth => Width + Gap;

    /// <summary>
    /// カードの絵を読む長辺（DIP）。枠の幅より少し大きく読み、刻みに切り上げる（228 → 240：前の「中」と同じ値）。
    /// 大きくすると保持する絵の画素が増える（360 のときの 400 は、240 の約2.8倍）
    /// </summary>
    public static int EdgeDip { get; private set; } = EdgeFor(DefaultWidth);

    /// <summary>
    /// カードの絵を読む長辺（画素）。<see cref="EdgeDip"/> に絵を読む倍率（画面の拡大率×表示の大きさ）を掛ける。
    /// 一覧はこれが変わったときだけ見えているカードに読み直させる（スライダーの刻みを越えたとき・
    /// 拡大率の違うモニターへ窓を移したとき・表示の大きさを変えたとき）
    /// </summary>
    public static int EdgePixels => DisplayScale.Pixels(EdgeDip);

    /// <summary>
    /// 大きさか、絵を読む倍率が変わった（一覧は列を割り直し、<see cref="EdgePixels"/> が変わっていれば読み直す）。
    /// 倍率の知らせをここへ寄せるのは、カードを並べる画面がすでにこの知らせで「割り直し・読み直し」を持っているため
    /// （倍率だけのために3つの画面へ別の知らせを足さない）
    /// </summary>
    public static event Action? Changed;

    static CardMetrics() => DisplayScale.Changed += () => Changed?.Invoke();

    public static double Clamp(double width)
        => double.IsNaN(width) ? DefaultWidth : Math.Clamp(Math.Round(width), MinWidth, MaxWidth);

    public static void Apply(double width)
    {
        var clamped = Clamp(width);
        var changed = clamped != Width;
        Width = clamped;
        ImageHeight = clamped - ImageHeightInset;
        EdgeDip = EdgeFor(clamped);

        if (Application.Current is { } app)
        {
            app.Resources["CardWidth"] = Width;
            app.Resources["CardHeight"] = Height;
            app.Resources["CardImageHeight"] = ImageHeight;
            app.Resources["CardSlotWidth"] = SlotWidth;
            app.Resources["CardShopIconSize"] = ShopIconSize;
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private static int EdgeFor(double width) => (int)(Math.Ceiling(width * 1.05 / EdgeStepDip) * EdgeStepDip);
}
