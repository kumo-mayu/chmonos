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

        var screen = UnityVpmGuard.Decide([Hit(0, "衣装", new VpmOverlap(Shader, 312, "Packages/com.example.shader"))], 1, asker.Ask);

        var asked = Assert.Single(asker.Asked);
        Assert.Equal("Unityへ送る", asked.Title);
        Assert.Equal("「衣装」の中に、VPMで入っている作り物シェーダーと同じファイルが 312 個あります。", asked.Question);
        Assert.Equal("取り込むと、VPMの作り物シェーダーが上書きされます。Unityの取り込み画面で「Packages/com.example.shader」のチェックを外してください。", asked.Detail);
        Assert.Equal(("送る", "やめる", (string?)null, true), (asked.First, asked.Second, asked.Third, asked.LastCancels));
        Assert.True(screen.Cancelled);
    }

    [Fact]
    public void 一つだけ送るときに送るを選べば_そのまま送る()
    {
        var screen = UnityVpmGuard.Decide([Hit(0, "衣装", new VpmOverlap(Shader, 3, "Packages/com.example.shader"))], 1, new Asker(ChoiceDialogResult.First).Ask);

        Assert.False(screen.Cancelled);
        Assert.Empty(screen.Skipped);
    }

    [Fact]
    public void 二つのVPMに重なれば_両方の名前を並べ_外す場所は1行に1つずつ並べる()
    {
        var asker = new Asker(ChoiceDialogResult.First);

        // 実物の長さの場所（27字・25字）。「と」でつなぐと外す文だけで80字を超える
        var lil = Shader with { Folder = "jp.lilxyzw.liltoon" };
        var avatar = Tool with { Folder = "nadena.dev.modular-avatar" };
        UnityVpmGuard.Decide(
            [Hit(0, "衣装", new VpmOverlap(lil, 2, "Packages/jp.lilxyzw.liltoon"), new VpmOverlap(avatar, 1, "Packages/nadena.dev.modular-avatar/Runtime"))], 1, asker.Ask);

        var asked = Assert.Single(asker.Asked);
        Assert.Equal("「衣装」の中に、VPMで入っている作り物シェーダー・作り物の道具と同じファイルが 3 個あります。", asked.Question);
        Assert.Equal(
            "取り込むと、VPMの作り物シェーダー・作り物の道具が上書きされます。Unityの取り込み画面で、次の場所のチェックを外してください。\n"
            + "Packages/jp.lilxyzw.liltoon\nPackages/nadena.dev.modular-avatar/Runtime",
            asked.Detail);
        Assert.All(asked.Detail!.Split('。', '\n'), sentence => Assert.True(sentence.Length <= 80, sentence));
    }

    [Fact]
    public void 連続で送るときは_重なる包みだけ1件ずつ聞き_飛ばした物を覚える()
    {
        var asker = new Asker(ChoiceDialogResult.Second, ChoiceDialogResult.First);

        var screen = UnityVpmGuard.Decide(
            [Hit(1, "衣装A", new VpmOverlap(Shader, 5, "Packages/com.example.shader")), Hit(3, "衣装B", new VpmOverlap(Shader, 7, "Packages/com.example.shader"))], 4, asker.Ask);

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
            [Hit(0, "衣装A", new VpmOverlap(Shader, 5, "Packages/com.example.shader")), Hit(2, "衣装B", new VpmOverlap(Shader, 7, "Packages/com.example.shader"))], 3, asker.Ask);

        Assert.Single(asker.Asked);
        Assert.True(screen.Cancelled);
    }

    [Fact]
    public void 一件だけ重なる連続送りでも_飛ばすを選べる()
    {
        var screen = UnityVpmGuard.Decide([Hit(1, "衣装", new VpmOverlap(Shader, 5, "Packages/com.example.shader"))], 2, new Asker(ChoiceDialogResult.Second).Ask);

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
        Assert.Equal("1 件をUnityへ順に送りました。1 件はVPMと同じファイルがあるので飛ばしました。", UnityQueueOutcome.Describe([sent, skipped]));
        Assert.Empty(UnityQueueOutcome.FailedNotChosen([sent, skipped]));
    }

    [Fact]
    public void 飛ばした後に中止したときは_残った取り込み画面の閉じ方を隠さない()
    {
        var skipped = new UnityQueueOutcome(Entry("衣装A"), false, UnityVpmGuard.SkippedMessage);
        var stopped = new UnityQueueOutcome(Entry("衣装B"), false, UnityImportQueue.StoppedMessage);

        Assert.Equal(
            $"0 件をUnityへ順に送りました。1 件はVPMと同じファイルがあるので飛ばしました。残り 1 件は送っていません。{UnityImportQueue.StoppedMessage}",
            UnityQueueOutcome.Describe([skipped, stopped]));
    }

    [Fact]
    public void 何も送らずにやめたときは_やめたことだけを持ち_呼び手は黙れる()
    {
        IReadOnlyList<UnityQueueOutcome> declined =
            [new(Entry("衣装A"), false, UnityVpmGuard.DeclinedMessage), new(Entry("衣装B"), false, UnityVpmGuard.DeclinedMessage)];

        Assert.True(UnityQueueOutcome.AllDeclined(declined));
        Assert.Equal(UnityVpmGuard.DeclinedMessage, UnityQueueOutcome.Describe(declined));
        Assert.False(UnityQueueOutcome.AllDeclined([new(Entry("衣装A"), false, UnityImportQueue.StoppedBeforeWindowMessage)]));
        Assert.False(UnityQueueOutcome.AllDeclined([]));
    }

    [Fact]
    public void 飛ばした物と本当の失敗が混ざっても_失敗の一覧には失敗だけを並べる()
    {
        var skipped = new UnityQueueOutcome(Entry("衣装A"), false, UnityVpmGuard.SkippedMessage);
        var broken = new UnityQueueOutcome(Entry("衣装B"), false, "zipから取り出せませんでした。");

        Assert.Equal([broken], UnityQueueOutcome.FailedNotChosen([skipped, broken]));
        Assert.Equal("0 件をUnityへ順に送りました。1 件はVPMと同じファイルがあるので飛ばしました。1 件は送れませんでした（zipから取り出せませんでした。）。", UnityQueueOutcome.Describe([skipped, broken]));
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
