using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// Unity へ順に送った後の知らせの文（<see cref="UnityQueueOutcome.Describe"/>）。
///
/// 前は Unity を開いて実際に送り、止めて、出た知らせを読むしかなかった（Unity を動かす確かめは、頼まれたときだけ）。
/// 結果の一覧から文を作るだけの所なので、結果を作り物で渡して確かめる。
/// </summary>
public class UnityQueueTextTests
{
    private static UnityPackageEntry Package(string name)
        => new($@"D:\files\{name}.zip", $"{name}.unitypackage", 100);

    private static UnityQueueOutcome Sent(string name) => new(Package(name), Opened: true, Problem: null);

    private static UnityQueueOutcome NotSent(string name, string problem) => new(Package(name), Opened: false, problem);

    [Fact]
    public void 全部送れたら_送った数だけを言う()
        => Assert.Equal(
            "3 件をUnityへ順に送りました。",
            UnityQueueOutcome.Describe([Sent("a"), Sent("b"), Sent("c")]));

    [Fact]
    public void 取り込み画面を出した後で止めたら_残りの数と_Unityに残った画面の閉じ方を言う()
    {
        var text = UnityQueueOutcome.Describe(
        [
            Sent("a"),
            NotSent("b", UnityImportQueue.StoppedMessage),
            NotSent("c", UnityImportQueue.StoppedMessage),
        ]);

        Assert.Equal(
            "1 件をUnityへ順に送りました。残り 2 件は送っていません。"
            + "送るのを中止しました。残った取り込み画面は、Unityで「Cancel」を押して閉じてください。"
            + "「Import」を押すと、このアプリの記録には残りません。",
            text);
    }

    [Fact]
    public void Unityに窓を出す前に止めたら_無い画面を閉じてとは言わない()
    {
        // zip から取り出している途中で止めたとき。閉じてもらう取り込み画面がまだ無い（ユーザ判断 2026-09-30）
        var text = UnityQueueOutcome.Describe(
        [
            NotSent("a", UnityImportQueue.StoppedBeforeWindowMessage),
            NotSent("b", UnityImportQueue.StoppedBeforeWindowMessage),
        ]);

        Assert.Equal("0 件をUnityへ順に送りました。残り 2 件は送っていません。送るのを中止しました。", text);
        Assert.DoesNotContain("Cancel", text);
    }

    [Fact]
    public void 止めたときの文は_括弧に入れない()
    {
        // 止めた理由は文が長く、「n 件は送れませんでした（理由）」に入れると入れ子の括弧になって読めなかった
        var text = UnityQueueOutcome.Describe([Sent("a"), NotSent("b", UnityImportQueue.StoppedMessage)]);

        Assert.DoesNotContain("送れませんでした", text);
        Assert.DoesNotContain("（", text);
    }

    [Fact]
    public void 失敗したら_数と1件目の理由を言う()
        => Assert.Equal(
            "1 件をUnityへ順に送りました。2 件は送れませんでした（Unityの窓が見つかりませんでした）。",
            UnityQueueOutcome.Describe(
            [
                Sent("a"),
                NotSent("b", "Unityの窓が見つかりませんでした"),
                NotSent("c", "別の理由"),
            ]));

    [Fact]
    public void 失敗と止めたのが混ざったら_失敗として言う()
    {
        // 全部が「人が止めた」のときだけ、止めた言い方にする。1つでも失敗があれば、失敗を隠さない
        var text = UnityQueueOutcome.Describe(
        [
            NotSent("a", "Unityの窓が見つかりませんでした"),
            NotSent("b", UnityImportQueue.StoppedMessage),
        ]);

        Assert.Equal("0 件をUnityへ順に送りました。2 件は送れませんでした（Unityの窓が見つかりませんでした）。", text);
    }

    [Fact]
    public void 既に入っていた物とCancelされた物は_送った数の内訳として言う()
    {
        var text = UnityQueueOutcome.DescribeShown(
        [
            Sent("a"),
            new UnityQueueOutcome(Package("b"), true, null, AlreadyPresent: true),
            new UnityQueueOutcome(Package("c"), true, null, Cancelled: true),
        ]);

        Assert.Equal("3 件をUnityへ順に送りました（うち 1 件は既にすべて入っていました、1 件はCancelされたので入っていません）。", text);
    }

    [Fact]
    public void 同じ物を2回出しても_送った数は1と数える()
    {
        // 取り込みが終わるのを待ちきれなかった物は、同じ物の結果が2つ並ぶことがある
        var text = UnityQueueOutcome.DescribeShown([Sent("a"), Sent("a")]);

        Assert.Equal("1 件をUnityへ順に送りました。", text);
    }

    [Fact]
    public void 人が止めた結果だけを_止めたと見分ける()
    {
        Assert.True(UnityImportQueue.IsStopped(UnityImportQueue.StoppedMessage));
        Assert.True(UnityImportQueue.IsStopped(UnityImportQueue.StoppedBeforeWindowMessage));
        Assert.False(UnityImportQueue.IsStopped("途中でやめました"));
        Assert.False(UnityImportQueue.IsStopped(null));
    }
}
