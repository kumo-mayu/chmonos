using System.Windows;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 使ったものの「更新 ▾」の「新しい版を送る…」（feedback 2026-11-18・ユーザ判断）。
/// 送る前に上書きの確認を出し、**Unity に入った時だけ**記録の使ったファイルを新しい版に替える。
/// Unity は動かさない：送り先と送った結果は <see cref="MemberNewVersionSend"/> の差し替えで決める。
/// </summary>
public class ModificationNewVersionSendTests
{
    private const string Avatar = "1000001";
    private const string Costume = "1000003";

    private static async Task<(ModificationViewModel Detail, string OldHash, string NewHash)> OpenAsync(TestApp app)
    {
        // 送る物を組む所は zip が在るかを見るので、本物のファイルを置く
        var oldFile = Make.File(app.NewFile("costume-v1.zip")) with { Contents = ["v1/Costume.unitypackage"] };
        var newFile = Make.File(app.NewFile("costume-v2.zip")) with { Contents = ["v2/Costume.unitypackage"] };
        var costume = Make.Item(Costume, "作り物の衣装").WithFiles(oldFile, newFile);
        costume = costume with
        {
            Local = costume.Local with { ContentUpdate = new ContentUpdate { FoundAt = DateTimeOffset.Now, FilesBefore = [oldFile.Hash] } },
        };

        await app.AddItemAsync(Make.Item(Avatar, "作り物のアバター"));
        await app.AddItemAsync(costume);
        var main = await app.StartAsync();
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification(Avatar, "夏の改変")));
        await app.Services.Commands.ExecuteAsync(new UiCommand.AddModificationMember(created.Record.Id, new ModificationMember
        {
            ItemId = Costume,
            FileHash = oldFile.Hash,
            Package = "v1/Costume.unitypackage",
            AddedAt = DateTimeOffset.Now,
        }));

        var record = await app.Services.Modifications.LoadAsync(created.Record.Id);
        var detail = new ModificationViewModel(record!, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => detail.Members.Count == 1, "使ったものが並ぶ");
        await app.SettleAsync();
        Assert.True(detail.Members[0].IsBehind);
        return (detail, oldFile.Hash, newFile.Hash);
    }

    private static async Task<ModificationMember> RecordedAsync(TestApp app, ModificationViewModel detail)
        => Assert.Single((await app.Services.Modifications.LoadAsync(detail.Record.Id))!.Members);

    /// <summary>送り先と送った結果を決めて走らせる。送った包みを返す。</summary>
    private static async Task<List<UnityPackageEntry>> SendAsync(
        ModificationViewModel detail, OpenUnityEditor? target, Func<UnityPackageEntry, UnityQueueOutcome> outcome)
    {
        var sent = new List<UnityPackageEntry>();
        MemberNewVersionSend.PickTargetIntercept = (_, _) => target;
        MemberNewVersionSend.SendIntercept = packages =>
        {
            sent.AddRange(packages);
            return [.. packages.Select(outcome)];
        };
        try
        {
            await detail.SendNewVersionAsync(detail.Members[0]);
        }
        finally
        {
            MemberNewVersionSend.PickTargetIntercept = null;
            MemberNewVersionSend.SendIntercept = null;
        }

        return sent;
    }

    private static readonly OpenUnityEditor Editor = new(1234, "作り物のプロジェクト");

    [Fact]
    public Task 確認してUnityに入れば_新しい版を送って記録を新しい版に替え_札が消える() => TestApp.Run(async app =>
    {
        var (detail, _, newHash) = await OpenAsync(app);
        app.Answer = _ => MessageBoxResult.OK;

        var sent = await SendAsync(detail, Editor, package => new UnityQueueOutcome(package, Opened: true, Problem: null));

        // 送ったのは新しい版の同じ名前の包み
        var package = Assert.Single(sent);
        Assert.Equal("v2/Costume.unitypackage", package.EntryPath);

        // 確認は上書きで設定が消えうる事とバックアップを言う。既定は「キャンセル」
        var asked = Assert.Single(app.Notices, notice => notice.Caption == MemberNewVersionSend.Title);
        Assert.Contains("設定が消えることがあります", asked.Text);
        Assert.Contains("バックアップ", asked.Text);
        Assert.Equal(MessageBoxResult.Cancel, asked.DefaultResult);

        var recorded = await RecordedAsync(app, detail);
        Assert.Equal(newHash, recorded.FileHash);
        Assert.Equal("v2/Costume.unitypackage", recorded.Package);
        await UiThread.Until(() => !detail.Members[0].IsBehind, "札が消える");
        Assert.Equal("「作り物の衣装」の新しい版を送り、使ったファイルを新しい版に替えました。", detail.MembersNotice.Text);
    });

    [Fact]
    public Task Unityの取り込み画面で取り消されたら_記録を変えない() => TestApp.Run(async app =>
    {
        var (detail, oldHash, _) = await OpenAsync(app);
        app.Answer = _ => MessageBoxResult.OK;

        var sent = await SendAsync(detail, Editor, package => new UnityQueueOutcome(package, Opened: true, Problem: null, Cancelled: true));

        Assert.Single(sent);
        var recorded = await RecordedAsync(app, detail);
        Assert.Equal(oldHash, recorded.FileHash);
        Assert.Equal("v1/Costume.unitypackage", recorded.Package);
        Assert.True(detail.Members[0].IsBehind);
        Assert.StartsWith("Unityに入らなかったので、使ったファイルの記録は変えていません。", detail.MembersNotice.Text);
    });

    [Fact]
    public Task 送れなかったら_記録を変えない() => TestApp.Run(async app =>
    {
        var (detail, oldHash, _) = await OpenAsync(app);
        app.Answer = _ => MessageBoxResult.OK;

        await SendAsync(detail, Editor, package => new UnityQueueOutcome(package, Opened: false, Problem: "Unityが応答しません"));

        Assert.Equal(oldHash, (await RecordedAsync(app, detail)).FileHash);
        Assert.True(detail.Members[0].IsBehind);
    });

    [Fact]
    public Task 確認でキャンセルしたら_送らず記録を変えない() => TestApp.Run(async app =>
    {
        var (detail, oldHash, _) = await OpenAsync(app);

        // 既定の答えは「キャンセル」
        var sent = await SendAsync(detail, Editor, package => new UnityQueueOutcome(package, Opened: true, Problem: null));

        Assert.Empty(sent);
        Assert.Equal(oldHash, (await RecordedAsync(app, detail)).FileHash);
        Assert.True(detail.Members[0].IsBehind);
    });

    [Fact]
    public Task 送り先のUnityを選ばなかったら_確認も出さず記録を変えない() => TestApp.Run(async app =>
    {
        var (detail, oldHash, _) = await OpenAsync(app);
        app.Answer = _ => MessageBoxResult.OK;

        var sent = await SendAsync(detail, target: null, package => new UnityQueueOutcome(package, Opened: true, Problem: null));

        Assert.Empty(sent);
        Assert.DoesNotContain(app.Notices, notice => notice.Caption == MemberNewVersionSend.Title);
        Assert.Equal(oldHash, (await RecordedAsync(app, detail)).FileHash);
    });
}
