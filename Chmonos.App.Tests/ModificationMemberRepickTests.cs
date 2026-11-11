using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 使ったものの行の「使ったファイルを選び直す…」（ユーザ指示 2026-11-11：改変画面で unitypackage の版を人が変えられない）。
/// 窓は手で足すときと同じ選ぶ欄で、書くのは <see cref="UiCommand.ChangeModificationMemberFiles"/> 1本。
/// 錠の中で今の行に当てること・ほかの欄を保つことは Core の試験（ModificationServiceTests）で押さえる。
/// </summary>
public class ModificationMemberRepickTests
{
    private const string Avatar = "1000001";
    private const string Costume = "1000003";
    private const string OldZip = @"D:\files\costume-v1.zip";
    private const string NewZip = @"D:\files\costume-v2.zip";

    /// <summary>zip の中に unitypackage を2つ持つ（依存物と本体）。</summary>
    private static LocalFileRecord Zip(string path, string version)
        => Make.File(path) with { Contents = [$"{version}/Dependency.unitypackage", $"{version}/Costume.unitypackage"] };

    private static ItemRecord Costumes(params LocalFileRecord[] files) => Make.Item(Costume, "作り物の衣装").WithFiles(files);

    private static ModificationMember Recorded(string zip, string package) => new()
    {
        ItemId = Costume,
        FileHash = Make.HashOf(zip),
        Package = package,
        AddedAt = DateTimeOffset.Now,
    };

    // ---- 開いたときに選ばれている物 ----

    [Fact]
    public void 開くと今の記録が選ばれていて_確定しても何も変わらない()
    {
        var member = Recorded(OldZip, "v1/Costume.unitypackage");
        var model = MemberFilePickViewModel.ForRepick(member, "作り物の衣装", Costumes(Zip(OldZip, "v1"), Zip(NewZip, "v2")));

        var choice = Assert.Single(model.Choices);
        Assert.Equal("Costume.unitypackage (costume-v1.zip)", choice.Selected.Text);
        Assert.True(choice.Selected.Matches(member));
        Assert.True(choice.Options[0].IsNone);
        Assert.False(model.HasNotice);

        // 2つ目の欄は「選ばない」から（依存物と本体を両方入れたときだけ選ぶ）
        Assert.True(choice.AllowsSecond);
        Assert.True(choice.Second.IsNone);
    }

    [Fact]
    public void 記録の無い行は選ばないで開く()
    {
        var member = new ModificationMember { ItemId = Costume, AddedAt = DateTimeOffset.Now };
        var model = MemberFilePickViewModel.ForRepick(member, "作り物の衣装", Costumes(Zip(OldZip, "v1")));

        Assert.True(model.Choices[0].Selected.IsNone);
        Assert.False(model.HasNotice);
    }

    [Fact]
    public void 記録したファイルが手元に無ければ_今の記録のままを選んで開き_一覧の上で言う()
    {
        // 外した・消えた版の zip。確定を押しただけで記録が消えないよう、「選ばない」では開かない
        var member = Recorded(OldZip, "v1/Costume.unitypackage");
        var model = MemberFilePickViewModel.ForRepick(member, "作り物の衣装", Costumes(Make.File(OldZip, detached: true), Zip(NewZip, "v2")));

        var choice = model.Choices[0];
        Assert.True(choice.Selected.IsKeepCurrent);
        Assert.Equal("今の記録のまま：Costume.unitypackage (costume-v1.zip)", choice.Selected.Text);
        Assert.Equal("記録しているファイルは手元にありません。", model.Notice);
        Assert.Equal(["選ばない", "今の記録のまま：Costume.unitypackage (costume-v1.zip)", "Dependency.unitypackage (costume-v2.zip)", "Costume.unitypackage (costume-v2.zip)"],
            choice.Options.Select(option => option.Text));
    }

    [Fact]
    public void 登録したフォルダの中の物の記録は_手元に無いとは言わない()
    {
        var member = new ModificationMember { ItemId = Costume, Package = @"D:\folders\costume\Costume.unitypackage", AddedAt = DateTimeOffset.Now };
        var model = MemberFilePickViewModel.ForRepick(member, "作り物の衣装", Costumes(Zip(NewZip, "v2")));

        Assert.True(model.Choices[0].Selected.IsKeepCurrent);
        Assert.Equal("今の記録は登録したフォルダの中のファイルで、一覧からは選べません。", model.Notice);
    }

    [Fact]
    public void 新しい版を選ぶから開くと_新しい版の同じ名前のunitypackageを選ぶ()
    {
        var member = Recorded(OldZip, "v1/Costume.unitypackage");
        var item = Costumes(Zip(OldZip, "v1"), Zip(NewZip, "v2"));
        item = item with { Local = item.Local with { ContentUpdate = new ContentUpdate { FoundAt = DateTimeOffset.Now, FilesBefore = [Make.HashOf(OldZip)] } } };

        var picked = MemberFilePickViewModel.ForRepick(member, "作り物の衣装", item, preferNewVersion: true).Choices[0].Selected;
        Assert.Equal("Costume.unitypackage (costume-v2.zip)", picked.Text);

        // 新しい版を選んで書けば、その行の新しい版の札は消える
        Assert.True(Core.Services.UsedUpdates.IsMemberBehind(item, member));
        Assert.False(Core.Services.UsedUpdates.IsMemberBehind(item, member with { FileHash = picked.FileHash, Package = picked.Package }));

        // 新しい版の中で1つに決まらなければ、今の記録のまま開く（推定で入れない）
        var unknown = Recorded(OldZip, "v1/Other.unitypackage");
        Assert.True(MemberFilePickViewModel.ForRepick(unknown, "作り物の衣装", item, preferNewVersion: true).Choices[0].Selected.Matches(unknown));
    }

    // ---- 選び直すと記録が替わる ----

    private static async Task<ModificationViewModel> OpenAsync(TestApp app, ItemRecord item, ModificationMember member)
    {
        await app.AddItemAsync(Make.Item(Avatar, "作り物のアバター"));
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification(Avatar, "夏の改変")));
        await app.Services.Commands.ExecuteAsync(new UiCommand.AddModificationMember(created.Record.Id, member));

        var record = await app.Services.Modifications.LoadAsync(created.Record.Id);
        var detail = new ModificationViewModel(record!, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => detail.Members.Count == 1, "使ったものが並ぶ");
        await app.SettleAsync();
        return detail;
    }

    private static async Task<IReadOnlyList<ModificationMember>> RepickAsync(TestApp app, ModificationViewModel detail, Action<MemberFileChoice> pick)
    {

        app.PickFiles = model =>
        {
            pick(model.Choices[0]);
            return true;
        };

        detail.RepickMemberFilesCommand.Execute(detail.Members[0]);
        await UiThread.Until(() => detail.MembersNotice.Text.Length > 0, "選び直しの結果が出る");
        await app.SettleAsync();
        return (await app.Services.Modifications.LoadAsync(detail.Record.Id))!.Members;
    }

    [Fact]
    public Task 選び直すと記録が替わる() => TestApp.Run(async app =>
    {
        var detail = await OpenAsync(app, Costumes(Zip(OldZip, "v1"), Zip(NewZip, "v2")), Recorded(OldZip, "v1/Costume.unitypackage"));

        var members = await RepickAsync(app, detail, choice => choice.Selected = choice.Options.Single(option => option.Text == "Costume.unitypackage (costume-v2.zip)"));

        var member = Assert.Single(members);
        Assert.Equal(Make.HashOf(NewZip), member.FileHash);
        Assert.Equal("v2/Costume.unitypackage", member.Package);
        Assert.Equal("「作り物の衣装」の使ったファイルを変更しました。", detail.MembersNotice.Text);
        Assert.Equal("Costume.unitypackage (costume-v2.zip)", detail.Members[0].SourceText);
    });

    [Fact]
    public Task 選ばないで確定すると記録が空に戻る() => TestApp.Run(async app =>
    {
        var detail = await OpenAsync(app, Costumes(Zip(OldZip, "v1")), Recorded(OldZip, "v1/Costume.unitypackage"));

        var members = await RepickAsync(app, detail, choice => choice.Selected = choice.Options[0]);

        Assert.False(Assert.Single(members).HasFile);
        Assert.Equal("「作り物の衣装」の使ったファイルの記録を削除しました。", detail.MembersNotice.Text);
        Assert.Equal("どのファイルを使ったかは分かりません", detail.Members[0].SourceText);
    });

    [Fact]
    public Task 二つ選ぶとその行が2行になり_外した印は残る() => TestApp.Run(async app =>
    {
        var detail = await OpenAsync(app, Costumes(Zip(NewZip, "v2")), Recorded(OldZip, "v1/Costume.unitypackage"));
        await app.Services.Commands.ExecuteAsync(new UiCommand.SetModificationMemberDetached(detail.Record.Id, detail.Members[0].Member, true));
        detail = new ModificationViewModel((await app.Services.Modifications.LoadAsync(detail.Record.Id))!, app.Services, app.Main, app.Main.Thumbnails);
        await UiThread.Until(() => detail.Members.Count == 1 && detail.Members[0].IsDetached, "外した行が並ぶ");

        var members = await RepickAsync(app, detail, choice =>
        {
            choice.Selected = choice.Options.Single(option => option.Text == "Dependency.unitypackage (costume-v2.zip)");
            choice.Second = choice.Options.Single(option => option.Text == "Costume.unitypackage (costume-v2.zip)");
        });

        Assert.Equal(["v2/Dependency.unitypackage", "v2/Costume.unitypackage"], members.Select(member => member.Package));
        Assert.All(members, member => Assert.True(member.Detached));
        Assert.Equal(members[0].AddedAt, members[1].AddedAt);
    });

    [Fact]
    public Task 選びを変えずに確定したり_キャンセルしたりすると書かない() => TestApp.Run(async app =>
    {
        var detail = await OpenAsync(app, Costumes(Zip(OldZip, "v1")), Recorded(OldZip, "v1/Costume.unitypackage"));
        var before = (await app.Services.Modifications.LoadAsync(detail.Record.Id))!.UpdatedAt;

        app.PickFiles = _ => true;
        detail.RepickMemberFilesCommand.Execute(detail.Members[0]);
        await app.SettleAsync();
        app.PickFiles = model =>
        {
            model.Choices[0].Selected = model.Choices[0].Options[0];
            return false;
        };
        detail.RepickMemberFilesCommand.Execute(detail.Members[0]);
        await app.SettleAsync();

        Assert.Equal(2, app.FilePicks.Count);
        Assert.Equal(before, (await app.Services.Modifications.LoadAsync(detail.Record.Id))!.UpdatedAt);
        Assert.Equal(string.Empty, detail.MembersNotice.Text);
    });

    [Fact]
    public Task 改変の画面の左の一覧から選び直すと_行の下で言い_右の詳細の行も今の記録になる() => TestApp.Run(async app =>
    {
        // 行は中身（ファイルの欄を含む）で指すので、右の詳細が古い行のままだと、そこでの外す・送るが当たらない
        var detail = await OpenAsync(app, Costumes(Zip(OldZip, "v1"), Zip(NewZip, "v2")), Recorded(OldZip, "v1/Costume.unitypackage"));
        var hub = new ModificationHubViewModel(app.Services, app.Main, app.Main.Thumbnails, ModificationHubLevel.Modification);
        await UiThread.Until(() => hub.EmptyText != "読み込んでいます…", "改変の画面の読み込みが済む");
        hub.ExpandAllCommand.Execute(null);
        await UiThread.Until(() => hub.Lines.OfType<HubMemberLine>().Any(), "使ったものの行が並ぶ");
        hub.ShowModificationCommand.Execute(detail.Record);
        var right = Assert.IsType<ModificationViewModel>(hub.Detail);
        await UiThread.Until(() => right.Members.Count == 1, "右に使ったものが並ぶ");

        app.PickFiles = model =>
        {
            model.Choices[0].Selected = model.Choices[0].Options.Single(option => option.Text == "Costume.unitypackage (costume-v2.zip)");
            return true;
        };
        var row = hub.Lines.OfType<HubMemberLine>().Single().Row;
        hub.RepickMemberFilesCommand.Execute(row);
        await UiThread.Until(() => right.Members.Count == 1 && right.Members[0].Member.FileHash == Make.HashOf(NewZip), "右の行が今の記録になる");
        await app.SettleAsync();

        var line = hub.Lines.OfType<HubMemberLine>().Single().Row;
        Assert.Equal("Costume.unitypackage (costume-v2.zip)", line.FileText);
        Assert.Equal("「作り物の衣装」の使ったファイルを変更しました。", line.Notice.Text);
        Assert.Equal("v2/Costume.unitypackage", Assert.Single((await app.Services.Modifications.LoadAsync(detail.Record.Id))!.Members).Package);
    });

    [Fact]
    public Task 窓を開いている間に消された行には書かずに言う() => TestApp.Run(async app =>
    {
        var detail = await OpenAsync(app, Costumes(Zip(OldZip, "v1"), Zip(NewZip, "v2")), Recorded(OldZip, "v1/Costume.unitypackage"));
        var row = detail.Members[0];
        await app.Services.Commands.ExecuteAsync(new UiCommand.RemoveModificationMember(detail.Record.Id, row.Member));

        app.PickFiles = model =>
        {
            model.Choices[0].Selected = model.Choices[0].Options.Last();
            return true;
        };
        detail.RepickMemberFilesCommand.Execute(row);
        await UiThread.Until(() => detail.MembersNotice.Text.Length > 0, "失敗が出る");

        Assert.Equal("選んでいる間にこの行が変わったため、記録を変えませんでした。", detail.MembersNotice.Text);
        Assert.True(detail.MembersNotice.IsWarning);
        Assert.Empty((await app.Services.Modifications.LoadAsync(detail.Record.Id))!.Members);
    });
}
