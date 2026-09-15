using System.Windows;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.Services;

/// <summary>
/// 商品カードの大きさ（設定の「サムネイルの大きさ」小・中・大）。
///
/// 前は保存されるだけで、カードは幅228・絵の高さ200に固定だった（R2）。ユーザが触って大きさを決めるので、
/// まず効くようにした（ユーザ判断 2026-09-15）。**中が今までの大きさで、小と大は仮の値。**
/// 見た目は XAML の DynamicResource（<c>CardWidth</c>・<c>CardHeight</c>・<c>CardImageHeight</c>）、
/// 列の割りと絵を読む大きさはここを読む。
/// </summary>
public static class CardMetrics
{
    /// <summary>絵の下の文字と札の欄（今までの 336 − 200）。大きさを変えても文字の量は変わらないので固定。</summary>
    private const double TextAreaHeight = 136;

    /// <summary>カードの右と下の間（XAML の Margin="0,0,14,14" と同じ）。</summary>
    public const double Gap = 14;

    public static double Width { get; private set; } = 228;

    public static double ImageHeight { get; private set; } = 200;

    public static double Height => ImageHeight + TextAreaHeight;

    /// <summary>列の割りに使う1枚ぶんの幅。</summary>
    public static double SlotWidth => Width + Gap;

    /// <summary>
    /// カードの絵を読む長辺（DIP）。枠より少し大きく読む（中は 240：枠の幅 228 を少し超える。前の値のまま）。
    /// 大にすると保持する絵の画素が増える（320 は 240 の約1.8倍）
    /// </summary>
    public static int EdgeDip { get; private set; } = 240;

    /// <summary>大きさが変わった（一覧は列を割り直す）。</summary>
    public static event Action? Changed;

    public static void Apply(ThumbnailSize size)
    {
        (Width, ImageHeight, EdgeDip) = size switch
        {
            ThumbnailSize.Small => (180d, 150d, 190),
            ThumbnailSize.Large => (300d, 270d, 320),
            _ => (228d, 200d, 240),
        };

        if (Application.Current is { } app)
        {
            app.Resources["CardWidth"] = Width;
            app.Resources["CardHeight"] = Height;
            app.Resources["CardImageHeight"] = ImageHeight;
            app.Resources["CardSlotWidth"] = SlotWidth;
        }

        Changed?.Invoke();
    }
}
