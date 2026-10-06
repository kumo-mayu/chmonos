using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 未確定の画面で押す操作：管理対象から除外する（確認の窓）・商品IDを決めて登録する（BOOTH へ問い合わせる）。
///
/// 確認の窓は出さずに、出すはずだった文と答えを <see cref="TestApp.Notices"/>・<see cref="TestApp.Answer"/> で受ける。
/// BOOTH は作り物（<see cref="FakeBooth"/>）が答える。前は、未確定のある保存先で押して、窓と一覧を撮って確かめていた。
/// </summary>
public class ResolveActionTests
{
    private static UnresolvedFile Unresolved(string path) => new()
    {
        Hash = Make.HashOf(path),
        Paths = [path],
        SizeBytes = 3,
        ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static async Task<(MainViewModel Main, ResolveViewModel Resolve)> OpenResolveAsync(TestApp app, params string[] names)
    {
        await app.Store.Unresolved.SaveAsync([.. names.Select(name => Unresolved(app.NewFile(name)))]);
        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        Assert.True(resolve.IsLoaded);
        return (main, resolve);
    }

    // ---- 件数 ----

    [Fact]
    public Task 未確定の件数は_登録する回数で言い_吹き出しでファイルの数を言う() => TestApp.Run(async app =>
    {
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip");

        Assert.Equal(2, resolve.RemainingCount);
        Assert.Equal("未確定 2 件", resolve.RemainingText);
        Assert.Equal("ファイルは 2 件です。zipやフォルダでまとまるファイルは1件と数えます。", resolve.RemainingToolTip);
        Assert.Equal(2, main.UnresolvedCount);
        Assert.NotNull(resolve.Selected);
    });

    // ---- 管理対象から除外する ----

    [Fact]
    public Task 除外は_取り返しを書いた確認を出し_やめれば何も変えない() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\first.zip");

        // 答えを決めていない確認は、やめる側で答える（TestApp の既定）
        resolve.ExcludeCommand.Execute(null);
        await app.SettleAsync();

        var asked = Assert.Single(app.Notices);
        Assert.Equal("管理対象から除外する", asked.Caption);
        Assert.Equal(
            "first.zip を管理対象から除外します。\n\nファイル自体は消しません。設定の「隠したもの」から戻せます。",
            asked.Text);
        Assert.Equal(MessageBoxButton.OKCancel, asked.Button);

        // Enter で進まないよう、既定はやめる側
        Assert.Equal(MessageBoxResult.Cancel, asked.DefaultResult);

        Assert.Single(resolve.Files);
        Assert.Empty(app.Store.Excluded.Load());
        Assert.Single(app.Store.Unresolved.Load());
    });

    [Fact]
    public Task 除外を進めると_一覧から外れて次の1件へ移り_戻す手立てが出る() => TestApp.Run(async app =>
    {
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip");
        resolve.Selected = resolve.Files.Single(row => row.FileName == "first.zip");
        app.Answer = _ => MessageBoxResult.OK;

        resolve.ExcludeCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("second.zip", Assert.Single(resolve.Files).FileName);
        Assert.Equal("second.zip", resolve.Selected!.FileName);
        Assert.Equal("未確定 1 件", resolve.RemainingText);
        Assert.True(resolve.HasUndoExclude);

        // 保存先にも書かれている（未確定から消え、除外の一覧に入る）
        Assert.Single(app.Store.Excluded.Load());
        Assert.Equal("second.zip", System.IO.Path.GetFileName(Assert.Single(app.Store.Unresolved.Load()).Paths[0]));

        // ナビの件数も、その場で減る
        await UiThread.Until(() => main.UnresolvedCount == 1, "ナビの未確定の数が減る");
    });

    // ---- 商品IDを決めて登録する ----

    [Fact]
    public Task 商品IDを確かめると_BOOTHの商品名を出し_登録すると商品になって検索に出る() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の衣装");
        var (main, resolve) = await OpenResolveAsync(app, @"a\costume.zip");
        Assert.Equal(0, main.Search.TotalCount);

        resolve.ItemIdInput = "1000001";
        Assert.True(resolve.CanPreview);
        resolve.PreviewCommand.Execute(null);
        await app.SettleAsync();

        Assert.True(resolve.HasPreview);
        Assert.Equal("作り物の衣装", resolve.Preview!.Name);

        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        // 未確定から消え、商品の記録にファイルが付く
        Assert.Empty(resolve.Files);
        Assert.Empty(app.Store.Unresolved.Load());
        var item = await app.Store.Items.LoadAsync("1000001");
        Assert.Equal("作り物の衣装", item!.Booth.Name);
        Assert.Equal("costume.zip", System.IO.Path.GetFileName(Assert.Single(item.Local.LocalFiles).Paths[0]));

        // 画面を離れなくても検索に出る（全件を読み直さずに1件を足す）
        Assert.Equal(1, main.Search.TotalCount);
        Assert.NotNull(main.Search.FindItem("1000001"));
        Assert.True(resolve.HasSettled);
    });

    [Fact]
    public Task 登録の間に別の行へ移ると_終わったとき登録した行だけが消え_見ている行はそのまま() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("9900501", "作り物の衣装");
        var (_, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip");
        resolve.Selected = resolve.Files.Single(row => row.FileName == "first.zip");
        resolve.ItemIdInput = "9900501";
        resolve.PreviewCommand.Execute(null);
        await app.SettleAsync();

        // 登録は数分かかる。答えを止めている間に、ほかの行を見に行く
        app.Booth.Hold();
        resolve.AssignCommand.Execute(null);
        resolve.Selected = resolve.Files.Single(row => row.FileName == "second.zip");
        app.Booth.Release();
        await app.SettleAsync();

        Assert.Equal("second.zip", Assert.Single(resolve.Files).FileName);
        Assert.Equal("second.zip", resolve.Selected!.FileName);
    });

    [Fact]
    public Task BOOTHに無い商品IDを確かめると_ふつうの登録ではなく_そのIDのまま登録するかを聞く() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\costume.zip");

        resolve.ItemIdInput = "1999999";
        resolve.PreviewCommand.Execute(null);
        await app.SettleAsync();

        // 作り物の BOOTH は、教えていない商品には「無い」と答える
        Assert.False(resolve.HasPreview);
        Assert.NotEmpty(app.Booth.Requests);

        // 「このIDで登録」は、見つからなかったIDのまま登録する形で押せる（ユーザ 2026-10-06）。窓で「キャンセル」なら何も起きない
        Assert.True(resolve.IsUnpublishedForm);
        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal("このIDのまま登録する", Assert.Single(app.Notices).Caption);
        Assert.Single(resolve.Files);
    });

    [Fact]
    public Task 商品IDが空なら_確かめるは押せない() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\costume.zip");

        resolve.ItemIdInput = "   ";

        Assert.False(resolve.CanPreview);
        Assert.False(resolve.PreviewCommand.CanExecute(null));
    });

    // ---- BOOTHに無い商品として登録する ----

    [Fact]
    public Task BOOTHに無い商品として登録すると_商品になって次の行へ移り_ナビの数が減る() => TestApp.Run(async app =>
    {
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip");
        resolve.Selected = resolve.Files.Single(row => row.FileName == "first.zip");
        var hash = resolve.Selected.File.Hash;
        resolve.LocalNameInput = "作り物の商品";
        app.Answer = _ => MessageBoxResult.OK;

        resolve.RegisterLocalCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("second.zip", Assert.Single(resolve.Files).FileName);
        Assert.Equal("second.zip", resolve.Selected!.FileName);
        Assert.Equal("second.zip", System.IO.Path.GetFileName(Assert.Single(app.Store.Unresolved.Load()).Paths[0]));

        var item = await app.Store.Items.LoadAsync(Core.Models.LocalItemId.For(hash));
        Assert.Equal("作り物の商品", item!.Local.DisplayName);
        Assert.Equal("first.zip", System.IO.Path.GetFileName(Assert.Single(item.Local.LocalFiles).Paths[0]));
        Assert.Empty(app.Booth.Requests);

        await UiThread.Until(() => main.UnresolvedCount == 1, "ナビの未確定の数が減る");
    });

    /// <summary>
    /// 登録の命令は記録を裏で読むので、押した直後はまだ走っている。その間に「未確定」を開き直すと、
    /// 新しい画面の均し（商品が持っている物を一覧から外す）と読み直しが重なる。
    /// どちらが先でも、登録した物は保存先の一覧へ戻らず、ほかの物は欠けない。
    /// </summary>
    [Fact]
    public Task 登録の最中に未確定を開き直しても_登録した物は戻らず_ほかの物は欠けない() => TestApp.Run(async app =>
    {
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip", @"c\third.zip");
        resolve.Selected = resolve.Files.Single(row => row.FileName == "first.zip");
        resolve.LocalNameInput = "作り物の商品";
        app.Answer = _ => MessageBoxResult.OK;

        resolve.RegisterLocalCommand.Execute(null);
        main.ShowSearchCommand.Execute(null);
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();

        string[] Names(IEnumerable<UnresolvedFile> files)
            => [.. files.Select(file => System.IO.Path.GetFileName(file.Paths[0])).Order(StringComparer.Ordinal)];

        Assert.Equal(["second.zip", "third.zip"], Names(app.Store.Unresolved.Load()));
        Assert.Equal(1, main.Search.TotalCount);

        // もう一度開けば、画面にも登録した行は出ない
        main.ShowSearchCommand.Execute(null);
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var reopened = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        Assert.Equal(["second.zip", "third.zip"], Names(reopened.Files.Select(row => row.File)));
        await UiThread.Until(() => main.UnresolvedCount == 2, "ナビの未確定の数が合う");
    });
}
