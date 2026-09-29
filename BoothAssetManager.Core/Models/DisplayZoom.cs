namespace BoothAssetManager.Core.Models;

/// <summary>
/// 画面の拡大率と、絵を読む大きさの計算。画面（WPF）に依らない所だけをここに置き、試験できるようにする。
/// </summary>
public static class DisplayZoom
{
    /// <summary>
    /// 表示の大きさ（DIP）を、絵を読む画素に直す。<paramref name="scale"/> は画面の拡大率（125% なら 1.25）。
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
