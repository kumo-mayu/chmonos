namespace Chmonos.Core.Services;

/// <summary>
/// 桁違いに高い数（外れ値）の境を決める（検索の価格の「外れ値を無視」・ユーザ判断 2026-09-16）。
///
/// **95%の位置の数の5倍以上**を外れ値とする。
///
/// BOOTH には、支援用の種類（0円・150円の商品に 99,999円の種類がある）や、販売を止めるために
/// あり得ない高値にした商品（購入者がダウンロードできる権利は残したい）がある。これらは値段ではなく目印なので、
/// 目盛の幅と照合から外したい。一方で、5,000〜8,500円のアバターのような「高いけれど普通の商品」は残したい。
///
/// 比べた定義（友人の写し205件・種類ごとの価格628個。`docs/feedback/done-2026-09.md` 2026-09-16）：
/// - 統計（Tukey・そのまま：1,925円以上の43個／対数：3,007円以上の12個）と MAD（対数・3倍：4,044円以上の8個）は、
///   高い側に長く伸びた普通の商品まで外れ値にする。
/// - 上位5%を切る（2,000円以上の24個）は、外れ値が無くても必ず削る。
/// - 隣と5倍以上離れた切れ目（99,999円の1個）は、止め値が近い値で何段もある（3万・5万・99,999円）と、隣との差が5倍に届かず取りこぼす。
/// - **95%の位置の5倍**（1万円以上＝99,999円の1個）は、止め値が何段あってもまとめて取れ、普通の高い商品は残る。
///   止め値が全体の5%を超えるほど多いと95%の位置が引き上げられて効かなくなるが、そこまで多いライブラリは考えにくい。
/// </summary>
public static class Outliers
{
    /// <summary>基準にする位置（95%）。</summary>
    public const double Percentile = 0.95;

    /// <summary>基準の何倍から外れ値とするか。上位の普通の刻みは1.2倍前後、止め値は10倍以上離れていた。</summary>
    public const int Factor = 5;

    /// <summary>
    /// 外れ値の境（この数以上が外れ値）。数が無い・基準が0以下（ほとんど無料）なら null（外れ値なし）。
    /// </summary>
    public static long? UpperFence(IEnumerable<long> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0)
        {
            return null;
        }

        var reference = PercentileOf(sorted, Percentile);
        if (reference <= 0)
        {
            return null;
        }

        var fence = reference * Factor;
        // 境は払った額の合計（32bit を超え得る）と同じ幅で持つ。long に収まらない境は「外れ値なし」
        return fence >= long.MaxValue ? null : (long)Math.Ceiling(fence);
    }

    /// <summary>並べた数の、その位置の値（隣り合う2つの間は直線で埋める）。</summary>
    private static double PercentileOf(IReadOnlyList<long> sorted, double percentile)
    {
        var at = (sorted.Count - 1) * percentile;
        var low = (int)Math.Floor(at);
        var high = (int)Math.Ceiling(at);
        // 差は double で取る（long の引き算は両端が離れていると桁あふれする）
        return sorted[low] + (((double)sorted[high] - sorted[low]) * (at - low));
    }
}
