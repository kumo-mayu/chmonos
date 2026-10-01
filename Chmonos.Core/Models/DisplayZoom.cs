namespace Chmonos.Core.Models;

/// <summary>
/// アプリの中の「表示の大きさ」（設定の <c>displayZoomPercent</c>）と、絵を読む大きさの計算。
/// 画面（WPF）に依らない所だけをここに置き、試験できるようにする。
/// </summary>
public static class DisplayZoom
{
    /// <summary>
    /// 選べる段（%）。Windows の拡大率とは別に、アプリの中だけを大きく・小さくする（ユーザ指示 2026-09-29）。
    /// 下の 90 は、1366×768 のような小さい画面で一覧を1列でも多く見たい人向け。上の 150 は、
    /// 100% のまま使っている高解像度の画面で文字が小さすぎる人向け（125% の画面で 150 にすると実質 188%）。
    /// 段を細かくしすぎると Ctrl＋＋ を何度も押すことになるので、Windows の拡大率に近い刻みで5段
    /// </summary>
    public static IReadOnlyList<int> Steps { get; } = [90, 100, 110, 125, 150];

    public const int DefaultPercent = 100;

    /// <summary>
    /// 段に丸める。手で書き換えた settings.json の 120 のような値は近い段へ、範囲の外は端の段へ。
    /// 真ん中で等しく近いときは小さい方（大きすぎて画面に収まらないより、小さい方が困らない）
    /// </summary>
    public static int Normalize(int percent)
    {
        var best = Steps[0];
        foreach (var step in Steps)
        {
            if (Math.Abs(step - percent) < Math.Abs(best - percent))
            {
                best = step;
            }
        }

        return best;
    }

    /// <summary>1段大きく。いちばん上ならそのまま。</summary>
    public static int Next(int percent)
    {
        var current = Normalize(percent);
        return Steps.FirstOrDefault(step => step > current, current);
    }

    /// <summary>1段小さく。いちばん下ならそのまま。</summary>
    public static int Previous(int percent)
    {
        var current = Normalize(percent);
        return Steps.LastOrDefault(step => step < current, current);
    }

    /// <summary>画面に出す名前（"125%"）。</summary>
    public static string Label(int percent) => $"{percent}%";

    /// <summary>
    /// 表示の大きさ（DIP）を、絵を読む画素に直す。<paramref name="scale"/> は画面の拡大率×表示の大きさ（125% の画面で 110% なら 1.375）。
    /// 足りないとぼやけるので切り上げる。ただし 1.1 倍のように2進で割り切れない倍率は、掛けると
    /// 264.00000000000003 のように僅かに超え、切り上げると1画素だけ大きく読む（刻みごとに保持の鍵が増える）。
    /// その誤差だけを落としてから切り上げる
    /// </summary>
    public static int Pixels(int dip, double scale)
    {
        if (double.IsNaN(scale) || scale <= 0)
        {
            scale = 1.0;
        }

        return (int)Math.Ceiling(dip * scale - 1e-6);
    }
}
