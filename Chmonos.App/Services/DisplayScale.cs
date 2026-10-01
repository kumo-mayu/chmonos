namespace Chmonos.App.Services;

/// <summary>
/// 絵を読む倍率。**画面の拡大率（窓のあるモニターの DPI）× 表示の大きさ（アプリの設定）**。
///
/// 前は <c>ThumbnailLoader</c> が読むたびに主の窓の DPI を聞いていた。値はその時点では正しいが、
/// 拡大率の違うモニターへ窓を移しても、もう画面に出ている絵は前の倍率で読んだまま引き伸ばされていた。
/// 倍率をここ1か所に持ち、変わったら知らせる（カードの一覧は見えている分だけ読み直す。<see cref="CardMetrics"/>）。
/// </summary>
public static class DisplayScale
{
    /// <summary>主の窓があるモニターの拡大率（125% なら 1.25）。窓ができる前は等倍。</summary>
    public static double Monitor { get; private set; } = 1.0;

    /// <summary>アプリの中の「表示の大きさ」（110% なら 1.1）。</summary>
    public static double Zoom { get; private set; } = 1.0;

    /// <summary>絵を読む倍率。</summary>
    public static double Image => Monitor * Zoom;

    /// <summary>どちらかの倍率が変わった（画面の側で読み直す）。</summary>
    public static event Action? Changed;

    /// <summary>表示の大きさ（DIP）を読む画素に直す。</summary>
    public static int Pixels(int dip) => Core.Models.DisplayZoom.Pixels(dip, Image);

    public static void SetMonitor(double scale)
    {
        if (double.IsNaN(scale) || scale <= 0 || scale == Monitor)
        {
            return;
        }

        Monitor = scale;
        Changed?.Invoke();
    }

    public static void SetZoom(double zoom)
    {
        if (double.IsNaN(zoom) || zoom <= 0 || zoom == Zoom)
        {
            return;
        }

        Zoom = zoom;
        Changed?.Invoke();
    }
}
