namespace Chmonos.Core.Models;

/// <summary>
/// 流した時に上へ重ねる1行の見出し（ショップ・商品ページ・編集の画面の左）の大きさの段（設定の <c>compactHeaderPercent</c>。ユーザ判断 2026-11-20）。
/// 画面（WPF）に依らない所だけをここに置き、試験できるようにする。
/// </summary>
public static class CompactHeaderSize
{
    /// <summary>
    /// 選べる段（%）。100 は前からの大きさ（高さ 52：32 の絵と2行にならない名前（15）が余白を持って収まる）。
    /// 150 までなのは、これ以上だと見出しが画面の高さの1割を超え、流している中身を覆いすぎるため（768 の高さで 52×1.5＝78）
    /// </summary>
    public static IReadOnlyList<int> Steps { get; } = [100, 125, 150];

    public const int DefaultPercent = 100;

    /// <summary>100% のときの高さ（DIP）。</summary>
    public const double BaseHeight = 52;

    /// <summary>段に丸める。手で書き換えた JSON の値は近い段へ、範囲の外は端の段へ（真ん中で等しければ小さい方）。</summary>
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

    /// <summary>見出しの高さ（DIP）。段に丸めてから掛ける。</summary>
    public static double Height(int percent) => Math.Round(BaseHeight * Normalize(percent) / 100.0);

    /// <summary>設定画面の選択肢の名前。</summary>
    public static string Label(int percent) => Normalize(percent) switch
    {
        100 => "標準",
        125 => "大きめ",
        _ => "特大",
    };
}
