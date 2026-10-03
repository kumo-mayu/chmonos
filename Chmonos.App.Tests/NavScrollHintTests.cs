namespace Chmonos.App.Tests;

/// <summary>
/// 狭い窓でナビの項目が入り切らないとき、上下に続きがあることを知らせる帯（ユーザ指示 2026-10-03）。
/// どの向きに出すかは送りの位置から決まる計算なので、ここで確かめる。見た目（重ねて出す・項目が動かない）は ViewShot の場面で見る。
/// </summary>
public class NavScrollHintTests
{
    [Theory]
    // 入り切るときはどちらも出さない（端数の流せる量も無いものとして扱う）
    [InlineData(0, 0, false, false)]
    [InlineData(0, 0.4, false, false)]
    // 一番上：下にだけ続きがある
    [InlineData(0, 120, false, true)]
    // 途中：両方
    [InlineData(40, 120, true, true)]
    // 一番下まで送ったら下の印が消える（端数は着いたとみなす）
    [InlineData(120, 120, true, false)]
    [InlineData(119.4, 120, true, false)]
    public void 続きがある向きに印を出す(double offset, double scrollable, bool above, bool below)
    {
        Assert.Equal((above, below), MainWindow.NavScrollHint(offset, scrollable));
    }
}
