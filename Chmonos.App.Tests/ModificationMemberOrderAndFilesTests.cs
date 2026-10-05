using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の詳細の「使ったもの」の見せる順（メモ26-①）と、手で足すときに使ったファイルを窓で選ぶこと（メモ26-②）。
/// </summary>
public class ModificationMemberOrderAndFilesTests
{
    private const string Avatar = "1000001";
    private const string Shader = "1000002";
    private const string Costume = "1000003";
    private const string Hair = "1000004";

    /// <summary>zip の中に unitypackage を2つ持つ商品（どちらを使ったかは人にしか分からない）。</summary>
    private static ItemRecord TwoPackages(string id, string name)
        => Make.Item(id, name).WithFiles(Make.File($@"D:\files\{id}.zip") with
        {
            Contents = ["Body/Body.unitypackage", "Option/Option.unitypackage", "readme.txt"],
        });

    private static Task<(MainViewModel Main, ModificationViewModel Detail)> OpenAsync(TestApp app, params ItemRecord[] items)
        => OpenAsync(app, items, []);

    /// <param name="items">最初から使ったものに入れておく商品。</param>
    /// <param name="others">手元に置くだけの商品（名前で足す候補）。</param>
    private static async Task<(MainViewModel Main, ModificationViewModel Detail)> OpenAsync(TestApp app, ItemRecord[] items, ItemRecord[] others)
    {
        await app.AddItemAsync(Make.Item(Avatar, "作り物のアバター"));
        foreach (var item in items.Concat(others))
        {
            await app.AddItemAsync(item);
        }

        var main = await app.StartAsync();
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification(Avatar, "夏の改変")));
        foreach (var item in items)
        {
            await app.Services.Commands.ExecuteAsync(new UiCommand.AddModificationMember(
                created.Record.Id, new ModificationMember { ItemId = item.Id }));
        }

        var record = await app.Services.Modifications.LoadAsync(created.Record.Id);
        var detail = new ModificationViewModel(record!, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => detail.Members.Count == items.Length && (others.Length == 0 || detail.ItemSuggestions.Count > 0), "使ったものと候補が並ぶ");
        await app.SettleAsync();
        return (main, detail);
    }

    private static ModificationViewModel Reopen(TestApp app, MainViewModel main, ModificationViewModel detail)
        => new(detail.Record, app.Services, main, main.Thumbnails);

    // ---- メモ26-① 見せる順 ----

    [Fact]
    public Task 逆の順にすると_見せる順だけが逆になり_番号と記録の並びは入れた順のまま() => TestApp.Run(async app =>
    {
        var (_, detail) = await OpenAsync(app,
            Make.Item(Shader, "作り物のシェーダー"), Make.Item(Costume, "作り物の衣装"), Make.Item(Hair, "作り物の髪"));

        Assert.True(detail.IsInsertOrder);
        Assert.Equal([Shader, Costume, Hair], detail.Members.Select(row => row.Member.ItemId));

        detail.ShowReverseOrderCommand.Execute(null);
        await app.SettleAsync();

        Assert.True(detail.IsReversed);
        Assert.Equal([Hair, Costume, Shader], detail.Members.Select(row => row.Member.ItemId));
        // 番号は入れた順の番号のまま（上から 3・2・1）
        Assert.Equal(["3", "2", "1"], detail.Members.Select(row => row.OrderText));
        // 記録の並びは変えない
        var stored = await app.Services.Modifications.LoadAsync(detail.Record.Id);
        Assert.Equal([Shader, Costume, Hair], stored!.Members.Select(member => member.ItemId));
        Assert.Equal("入れた順の逆に並べています。Unityへは入れた順に送ります。", detail.MembersOrderText);

        detail.ShowInsertOrderCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal([Shader, Costume, Hair], detail.Members.Select(row => row.Member.ItemId));
        Assert.Equal("上から順に入れた記録です。依存するものが先に来るように並べ替えられます。", detail.MembersOrderText);
    });

    [Fact]
    public Task 逆の順で見せていても_順にUnityへ送る行は入れた順() => TestApp.Run(async app =>
    {
        var (_, detail) = await OpenAsync(app,
            Make.Item(Shader, "作り物のシェーダー"), Make.Item(Costume, "作り物の衣装"), Make.Item(Hair, "作り物の髪"));

        detail.ShowReverseOrderCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal([Shader, Costume, Hair], detail.RowsToSendAll().Select(row => row.Member.ItemId));
    });

    [Fact]
    public Task 逆の順はアプリを閉じても覚え_次に開いた改変も逆の順で始まる() => TestApp.Run(async app =>
    {
        var (main, detail) = await OpenAsync(app, Make.Item(Shader, "作り物のシェーダー"), Make.Item(Costume, "作り物の衣装"));

        detail.ShowReverseOrderCommand.Execute(null);
        await app.SettleAsync();

        Assert.True(app.Services.UiState.ModificationMembersReversed);
        var again = Reopen(app, main, detail);
        await UiThread.Until(() => again.Members.Count == 2, "使ったものが並ぶ");
        await app.SettleAsync();
        Assert.True(again.IsReversed);
        Assert.Equal([Costume, Shader], again.Members.Select(row => row.Member.ItemId));
    });

    [Fact]
    public Task 逆の順で見えている前へ動かすと_記録の上では後ろへ動く() => TestApp.Run(async app =>
    {
        var (_, detail) = await OpenAsync(app,
            Make.Item(Shader, "作り物のシェーダー"), Make.Item(Costume, "作り物の衣装"), Make.Item(Hair, "作り物の髪"));
        detail.ShowReverseOrderCommand.Execute(null);
        await app.SettleAsync();

        // 見えている並び：髪・衣装・シェーダー。いちばん上（髪＝最後に入れた物）は前へ動かせない
        var top = detail.Members[0];
        Assert.False(top.CanMoveBack);
        Assert.True(top.CanMoveForward);
        Assert.Equal("いちばん前にあります。", top.MoveBackHint);

        // 衣装を見えている前（上）へ → 記録では後ろへ（シェーダー・髪・衣装）
        var costume = detail.Members.Single(row => row.Member.ItemId == Costume);
        Assert.Equal("前へ移動します。後に送られます。", costume.MoveBackHint);
        detail.MoveMemberBackCommand.Execute(costume);
        await UiThread.Until(() => detail.Members[0].Member.ItemId == Costume, "上へ動く");

        var stored = await app.Services.Modifications.LoadAsync(detail.Record.Id);
        Assert.Equal([Shader, Hair, Costume], stored!.Members.Select(member => member.ItemId));
        Assert.Equal([Costume, Hair, Shader], detail.Members.Select(row => row.Member.ItemId));
    });

    // ---- メモ26-② 手で足すときに使ったファイルを選ぶ ----

    [Fact]
    public Task 選べるファイルが1つだけなら_窓は最初からそれを選んでおき_そのまま足すと記録に入る() => TestApp.Run(async app =>
    {
        var (_, detail) = await OpenAsync(app, [], [Make.Item(Costume, "作り物の衣装")]);

        detail.AddMemberCommand.Execute("作り物の衣装");
        await UiThread.Until(() => detail.Members.Count == 1, "足される");

        var choice = Assert.Single(Assert.Single(app.FilePicks).Choices);
        Assert.Equal("1000003.zip", choice.Selected.Label);
        var member = (await app.Services.Modifications.LoadAsync(detail.Record.Id))!.Members.Single();
        Assert.Equal(Make.HashOf(@"D:\files\1000003.zip"), member.FileHash);
        Assert.Null(member.Package);
        Assert.Equal("1000003.zip", detail.Members.Single().SourceText);
    });

    [Fact]
    public Task 選べるファイルが2つ以上なら_窓は何も選ばずに始まり_飛ばすと空のまま() => TestApp.Run(async app =>
    {
        var (_, detail) = await OpenAsync(app, [], [TwoPackages(Costume, "作り物の衣装")]);

        detail.AddMemberCommand.Execute("作り物の衣装");
        await UiThread.Until(() => detail.Members.Count == 1, "足される");

        var choice = Assert.Single(Assert.Single(app.FilePicks).Choices);
        Assert.True(choice.Selected.IsNone);
        Assert.Equal(["選ばない", "Body.unitypackage (1000003.zip)", "Option.unitypackage (1000003.zip)"], choice.Options.Select(option => option.Text));
        var member = (await app.Services.Modifications.LoadAsync(detail.Record.Id))!.Members.Single();
        Assert.Null(member.FileHash);
        Assert.Null(member.Package);
        Assert.Equal("どのファイルを使ったかは分かりません", detail.Members.Single().SourceText);
    });

    [Fact]
    public Task 窓で選んだunitypackageは_ハッシュとパッケージに入り_行にその名前を出す() => TestApp.Run(async app =>
    {
        var (_, detail) = await OpenAsync(app, [], [TwoPackages(Costume, "作り物の衣装")]);
        app.PickFiles = model =>
        {
            var choice = model.Choices.Single();
            choice.Selected = choice.Options.Single(option => option.Label == "Option.unitypackage");
            return true;
        };

        detail.AddMemberCommand.Execute("作り物の衣装");
        await UiThread.Until(() => detail.Members.Count == 1, "足される");

        var member = (await app.Services.Modifications.LoadAsync(detail.Record.Id))!.Members.Single();
        Assert.Equal(Make.HashOf(@"D:\files\1000003.zip"), member.FileHash);
        Assert.Equal("Option/Option.unitypackage", member.Package);
        Assert.Equal("Option.unitypackage (1000003.zip)", detail.Members.Single().SourceText);
    });

    [Fact]
    public Task 窓でキャンセルすると_足さない() => TestApp.Run(async app =>
    {
        var (_, detail) = await OpenAsync(app, [], [Make.Item(Costume, "作り物の衣装")]);
        app.PickFiles = _ => false;

        detail.AddMemberCommand.Execute("作り物の衣装");
        await UiThread.Until(() => app.FilePicks.Count == 1, "窓が出る");
        await app.SettleAsync();

        Assert.Empty((await app.Services.Modifications.LoadAsync(detail.Record.Id))!.Members);
        Assert.Equal(string.Empty, detail.AddNotice.Text);
    });

    [Fact]
    public void フォルダと外したファイルは選べず_選べる物が無い商品は欄を作らずに空の行になる()
    {
        // フォルダは中身のハッシュを持たないので、使ったファイルとして記録できない
        var item = Make.Item(Costume, "作り物の衣装").WithFiles(Make.File(@"D:\files\old.zip", detached: true));
        item = item with { Local = item.Local with { LocalFolders = [new LocalFolderRecord { Path = @"D:\files\costume" }] } };

        var files = new MemberFilePickViewModel([item]);

        Assert.False(files.HasChoices);
        var member = files.MemberFor(Costume, DateTimeOffset.Now);
        Assert.Null(member.FileHash);
        Assert.Null(member.Package);
    }
    [Fact]
    public Task まとめて足すときは_商品ごとに選んだ物が入り_選べるファイルの無い商品は行を出さない() => TestApp.Run(async app =>
    {
        var one = Make.Item(Shader, "作り物のシェーダー");
        var two = TwoPackages(Costume, "作り物の衣装");
        var none = Make.Item(Hair, "作り物の髪").WithFiles();
        await app.AddItemAsync(Make.Item(Avatar, "作り物のアバター"));
        await app.StartAsync();
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification(Avatar, "夏の改変")));

        var files = new MemberFilePickViewModel([one, two, none]);
        Assert.True(files.ShowsNames);
        Assert.Equal([Shader, Costume], files.Choices.Select(choice => choice.Item.Id));
        // 1つだけの商品は選んでおき、2つ以上の商品は選ばない
        Assert.False(files.Choices[0].Selected.IsNone);
        Assert.True(files.Choices[1].Selected.IsNone);
        files.Choices[1].Selected = files.Choices[1].Options[1];

        var added = await ItemSelectionActions.AddPickedAsync(app.Services, created.Record, [one, two, none], files);

        Assert.Equal(3, added);
        var members = (await app.Services.Modifications.LoadAsync(created.Record.Id))!.Members;
        Assert.Equal(Make.HashOf($@"D:\files\{Shader}.zip"), members[0].FileHash);
        Assert.Equal("Body/Body.unitypackage", members[1].Package);
        Assert.Null(members[2].FileHash);
    });

    [Fact]
    public Task 改変を選ぶ窓は_選べるファイルがあるときだけ使ったファイルの欄を持ち_前提の文を言い分ける() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        var withFiles = new MemberFilePickViewModel([Make.Item(Costume, "作り物の衣装")]);
        var withoutFiles = new MemberFilePickViewModel([Make.Item(Costume, "作り物の衣装").WithFiles()]);

        Assert.False(withFiles.ShowsNames);
        Assert.True(Build(withFiles).HasFiles);
        Assert.False(Build(withoutFiles).HasFiles);
        Assert.Equal("使ったファイルを選ぶと記録します。選ばなくても追加できます。", ModificationPicking.FilesContextText(withFiles, "送ってください。"));
        Assert.Equal("使ったファイルは記録されません。送ってください。", ModificationPicking.FilesContextText(withoutFiles, "送ってください。"));

        PickModificationDialogViewModel Build(MemberFilePickViewModel files) => ModificationPicking.BuildDialog(
            app.Services, "改変に追加", "見出し", "前提", [], "今ある改変に追加", "追加", "改変がまだありません。", files);
    });
}
