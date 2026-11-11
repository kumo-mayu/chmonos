using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>「Unityへ送る」の前に VPM との重なりを聞く窓（plan 40）。窓は出さず、聞く中身と答えごとの動きを確かめる。名前は作り物。</summary>
public sealed class UnityVpmGuardTests
{
    private static readonly VpmPackage Shader = new("com.example.shader", "作り物シェーダー", "2.0.1", "com.example.shader", true);
    private static readonly VpmPackage Tool = new("com.example.tool", "作り物の道具", "1.0.0", "com.example.tool", true);

    private static UnityPackageEntry Entry(string name) => new($@"D:\files\{name}.zip", $"{name}.unitypackage", 1) { ZipHash = "AAAA" };

    private static VpmOverlapPackage Hit(int index, string name, params VpmOverlap[] overlaps) => new(index, Entry(name), overlaps);

    /// <summary>聞かれた中身を控え、決めた順に答える。</summary>
    private sealed class Asker(params ChoiceDialogResult[] answers)
    {
        public List<ChoiceRequest> Asked { get; } = [];

        public ChoiceDialogResult Ask(ChoiceRequest request)
        {
            Asked.Add(request);
            return answers[Asked.Count - 1];
        }
    }

    [Fact]
    public void 重なりが無ければ_窓を出さずにそのまま送る()
    {
        var asker = new Asker();

        var screen = UnityVpmGuard.Decide([], 3, asker.Ask);

        Assert.Empty(asker.Asked);
        Assert.False(screen.Cancelled);
        Assert.Empty(screen.Skipped);
    }

    [Fact]
    public void 一つだけ送るときは_重なるVPMと数と外す場所を言い_送るとやめるの二択にする()
    {
        var asker = new Asker(ChoiceDialogResult.Second);

        var screen = UnityVpmGuard.Decide([Hit(0, "衣装", new VpmOverlap(Shader, 312, "Assets/lilToon"))], 1, asker.Ask);

        var asked = Assert.Single(asker.Asked);
        Assert.Equal("Unityへ送る", asked.Title);
        Assert.Equal("「衣装」の中に、VPMで入っている作り物シェーダーと同じファイルが 312 個あります。", asked.Question);
        Assert.Equal("取り込むと、VPMの作り物シェーダーが上書きされます。Unityの取り込み画面で「Assets/lilToon」のチェックを外してください。", asked.Detail);
        Assert.Equal(("送る", "やめる", (string?)null, true), (asked.First, asked.Second, asked.Third, asked.LastCancels));
        Assert.True(screen.Cancelled);
    }

    [Fact]
    public void 一つだけ送るときに送るを選べば_そのまま送る()
    {
        var screen = UnityVpmGuard.Decide([Hit(0, "衣装", new VpmOverlap(Shader, 3, "Assets/lilToon"))], 1, new Asker(ChoiceDialogResult.First).Ask);

        Assert.False(screen.Cancelled);
        Assert.Empty(screen.Skipped);
    }

    [Fact]
    public void 場所がまとまらないときは_VPMの名前で外す物を言い_二つ重なれば両方の名前を並べる()
    {
        var asker = new Asker(ChoiceDialogResult.First);

        UnityVpmGuard.Decide([Hit(0, "衣装", new VpmOverlap(Shader, 2, null), new VpmOverlap(Tool, 1, "Assets/Tools"))], 1, asker.Ask);

        var asked = Assert.Single(asker.Asked);
        Assert.Equal("「衣装」の中に、VPMで入っている作り物シェーダー・作り物の道具と同じファイルが 3 個あります。", asked.Question);
        Assert.Equal("取り込むと、VPMの作り物シェーダー・作り物の道具が上書きされます。Unityの取り込み画面で、作り物シェーダー・作り物の道具のファイルのチェックを外してください。", asked.Detail);
    }

    [Fact]
    public void 連続で送るときは_重なる包みだけ1件ずつ聞き_飛ばした物を覚える()
    {
        var asker = new Asker(ChoiceDialogResult.Second, ChoiceDialogResult.First);

        var screen = UnityVpmGuard.Decide(
            [Hit(1, "衣装A", new VpmOverlap(Shader, 5, "Assets/A")), Hit(3, "衣装B", new VpmOverlap(Shader, 7, "Assets/B"))], 4, asker.Ask);

        Assert.Equal(["Unityへ送る（1/2）", "Unityへ送る（2/2）"], asker.Asked.Select(asked => asked.Title));
        Assert.All(asker.Asked, asked => Assert.Equal(("送る", "これを飛ばす", "残りすべてを中止", true), (asked.First, asked.Second, asked.Third!, asked.LastCancels)));
        Assert.False(screen.Cancelled);
        Assert.Equal([1], screen.Skipped);
    }

    [Fact]
    public void 連続で送るときに残りすべてを中止を押すと_その先は聞かずに全部をやめる()
    {
        var asker = new Asker(ChoiceDialogResult.Third);

        var screen = UnityVpmGuard.Decide(
            [Hit(0, "衣装A", new VpmOverlap(Shader, 5, "Assets/A")), Hit(2, "衣装B", new VpmOverlap(Shader, 7, "Assets/B"))], 3, asker.Ask);

        Assert.Single(asker.Asked);
        Assert.True(screen.Cancelled);
    }

    [Fact]
    public void 一件だけ重なる連続送りでも_飛ばすを選べる()
    {
        var screen = UnityVpmGuard.Decide([Hit(1, "衣装", new VpmOverlap(Shader, 5, "Assets/A"))], 2, new Asker(ChoiceDialogResult.Second).Ask);

        Assert.False(screen.Cancelled);
        Assert.Equal([1], screen.Skipped);
    }

    [Fact]
    public void 飛ばした物とやめた物は_人が止めた扱いで_送れなかったと言わない()
    {
        Assert.True(UnityImportQueue.IsStopped(UnityVpmGuard.SkippedMessage));
        Assert.True(UnityImportQueue.IsStopped(UnityVpmGuard.DeclinedMessage));

        var sent = new UnityQueueOutcome(Entry("衣装A"), true, null);
        var skipped = new UnityQueueOutcome(Entry("衣装B"), false, UnityVpmGuard.SkippedMessage);
        Assert.Equal("1 件をUnityへ順に送りました。残り 1 件は送っていません。VPMと同じファイルがあるので、飛ばしました。", UnityQueueOutcome.Describe([sent, skipped]));
    }

    [Fact]
    public Task 送り先の場所が分からないときと_照らす物が無いときは_聞かない() => UiThread.Run(async () =>
    {
        var asked = 0;
        ChoiceQuestion.Intercept = _ =>
        {
            asked++;
            return ChoiceDialogResult.Second;
        };
        try
        {
            Assert.Same(VpmScreen.None, await UnityVpmGuard.ScreenAsync(null, [Entry("衣装")], new UnityPackageReads(), CancellationToken.None));

            // 在らないプロジェクト（Packages が無い）は VPM が無いので照らさない
            var none = await UnityVpmGuard.ScreenAsync(@"D:\作り物\在らないプロジェクト", [Entry("衣装")], new UnityPackageReads(), CancellationToken.None);
            Assert.False(none.Cancelled);
            Assert.Equal(0, asked);
        }
        finally
        {
            ChoiceQuestion.Intercept = null;
        }
    });
}
