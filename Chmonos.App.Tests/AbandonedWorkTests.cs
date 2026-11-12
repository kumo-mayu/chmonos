using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 試験の土台の「見捨てた仕事」の数の扱い（2026-11-12）。落ちた試験が裏の仕事を止めたまま残しても、
/// 後の試験がそれを待たずに済み、見捨てた仕事が終われば、また自分の仕事を待つようになること。
/// わざと止めたまま落ちる試験は一式に置けないので、数の扱いだけを new した控えで確かめる
/// </summary>
public class AbandonedWorkTests
{
    private static readonly DateTime At = new(2026, 11, 12, 10, 0, 0);

    [Fact]
    public void 何も見捨てていなければ_仕事が0件のときだけ済んだとみなす()
    {
        var abandoned = new AbandonedWork();

        Assert.True(abandoned.Settled(0));
        Assert.False(abandoned.Settled(1));
        Assert.Null(abandoned.Note);
    }

    [Fact]
    public void 見捨てた数までは待たず_それを超える仕事は待つ()
    {
        var abandoned = new AbandonedWork();

        abandoned.Abandon(2, "FooTests.落ちる", At);

        Assert.True(abandoned.Settled(2));
        Assert.False(abandoned.Settled(3));
    }

    [Fact]
    public void 印に_残した試験の名前と件数と時刻が出る()
    {
        var abandoned = new AbandonedWork();

        abandoned.Abandon(2, "FooTests.落ちる", At);

        Assert.Contains("FooTests.落ちる", abandoned.Note);
        Assert.Contains("2 件残しました", abandoned.Note);
        Assert.Contains("10:00:00", abandoned.Note);
    }

    [Fact]
    public void 続けて見捨てると_足さずに今の数にし_印は増えた分を言う()
    {
        var abandoned = new AbandonedWork();

        abandoned.Abandon(1, "FooTests.一つ目", At);

        // 2件目の試験の後片付けの時、前の1件はまだ走っていて、今の数に含まれている
        abandoned.Abandon(3, "FooTests.二つ目", At);

        Assert.Equal(3, abandoned.Count);
        Assert.Contains("FooTests.二つ目", abandoned.Note);
        Assert.Contains("2 件残しました", abandoned.Note);
    }

    [Fact]
    public void 今の数が控え以下なら_見捨てる物は増えない()
    {
        var abandoned = new AbandonedWork();
        abandoned.Abandon(2, "FooTests.一つ目", At);

        abandoned.Abandon(2, "FooTests.二つ目", At);

        Assert.Equal(2, abandoned.Count);
        Assert.Contains("FooTests.一つ目", abandoned.Note);
    }

    [Fact]
    public void 見捨てた仕事が終われば_次の試験の始めに控えを減らし_また待つ()
    {
        var abandoned = new AbandonedWork();
        abandoned.Abandon(2, "FooTests.落ちる", At);

        // 見捨てた2件のうち1件が終わった
        abandoned.Reconcile(1);

        Assert.Equal(1, abandoned.Count);
        Assert.False(abandoned.Settled(2));
    }

    [Fact]
    public void 見捨てた仕事が全部終われば_印も消える()
    {
        var abandoned = new AbandonedWork();
        abandoned.Abandon(2, "FooTests.落ちる", At);

        abandoned.Reconcile(0);

        Assert.Equal(0, abandoned.Count);
        Assert.Null(abandoned.Note);
        Assert.False(abandoned.Settled(1));
    }

    [Fact]
    public void 試験の始めの数が控えより多くても_控えは増やさない()
    {
        // 通った試験の残り（待たずに終えた UiThread.Run だけの試験など）は見捨てた物ではない
        var abandoned = new AbandonedWork();
        abandoned.Abandon(1, "FooTests.落ちる", At);

        abandoned.Reconcile(3);

        Assert.Equal(1, abandoned.Count);
    }
}
