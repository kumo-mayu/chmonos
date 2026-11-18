using System.Globalization;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// 値（0〜100 %）を小さな円グラフの塗りの形にする（属性の管理の「数値｜グラフ」・open.md 164）。
///
/// 円弧1本（<c>ArcSegment</c>）では、始まりと終わりが同じ点になる 100 % を描けない（何も描かれない）。
/// 100 % は半円2つの丸、0 % は形を作らない（地の円だけが見える）。半分を超えると大きい方の弧（IsLargeArc）を使う。
/// 12時の位置から時計回りに塗る（時計と同じ向きで、値が増えるほど右回りに伸びる）。
/// </summary>
public static class PieShape
{
    /// <summary>塗りの形の文字（Path の Data）。<paramref name="percent"/> は 0〜100 に丸める。0 なら空。</summary>
    public static string Data(double percent, double size)
    {
        var ratio = Math.Clamp(percent, 0, 100) / 100;
        var r = size / 2;
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        if (ratio <= 0)
        {
            return string.Empty;
        }

        if (ratio >= 1)
        {
            return $"M {F(r)},0 A {F(r)},{F(r)} 0 1 1 {F(r)},{F(size)} A {F(r)},{F(r)} 0 1 1 {F(r)},0 Z";
        }

        var angle = ratio * 2 * Math.PI;
        var x = r + r * Math.Sin(angle);
        var y = r - r * Math.Cos(angle);
        var large = ratio > 0.5 ? 1 : 0;
        return $"M {F(r)},{F(r)} L {F(r)},0 A {F(r)},{F(r)} 0 {large} 1 {F(x)},{F(y)} Z";
    }

    /// <summary><see cref="Data"/> を固めた形。0 % なら null（Path は何も描かない）。</summary>
    public static Geometry? Geometry(double percent, double size)
    {
        var data = Data(percent, size);
        if (data.Length == 0)
        {
            return null;
        }

        var geometry = System.Windows.Media.Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
