using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 未確定の画面をキーボードだけで1件ずつ片付ける（ユーザ判断 2026-10-01「8は直そう」）。
/// 欄の Enter は「情報を確認」（欄の KeyBinding が <see cref="ResolveViewModel.PreviewCommand"/> を呼ぶ）、
/// 確定は編集画面の「保存して次へ」と同じショートカット（既定 Ctrl+Enter。主の窓が <see cref="MainViewModel.RunShortcut"/> へ渡す）。
/// BOOTH は作り物（<see cref="FakeBooth"/>）が答える。
/// </summary>
public class ResolveKeyboardTests
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
        resolve.Selected = resolve.Files.Single(row => row.FileName == System.IO.Path.GetFileName(names[0]));
        return (main, resolve);
    }

    /// <summary>欄に打って Enter を押したのと同じ（欄の KeyBinding が呼ぶ命令）。</summary>
    private static async Task TypeAndEnterAsync(TestApp app, ResolveViewModel resolve, string text)
    {
        resolve.ItemIdInput = text;
        Assert.True(resolve.PreviewCommand.CanExecute(null));
        resolve.PreviewCommand.Execute(null);
        await app.SettleAsync();
    }

    [Fact]
    public Task Enterで確かめ_確定のキーで確定すると_次の行が選ばれ_欄へ戻る() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の衣装");
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip");
        var focusRequests = new List<ItemIdFocusReason>();
        resolve.ItemIdFocusRequested += focusRequests.Add;

        await TypeAndEnterAsync(app, resolve, "1000001");
        Assert.Equal("作り物の衣装", resolve.Preview!.Name);

        Assert.True(main.RunShortcut(ShortcutAction.SaveAndNext));
        await app.SettleAsync();

        var item = await app.Store.Items.LoadAsync("1000001");
        Assert.Equal("first.zip", System.IO.Path.GetFileName(Assert.Single(item!.Local.LocalFiles).Paths[0]));
        Assert.Equal("second.zip", Assert.Single(resolve.Files).FileName);

        // 次の行に止まり、欄は空で、フォーカスは欄へ（続けて次のIDを打てる）
        Assert.Equal("second.zip", resolve.Selected!.FileName);
        Assert.Equal(string.Empty, resolve.ItemIdInput);
        Assert.False(resolve.HasPreview);
        Assert.NotEmpty(focusRequests);
        Assert.All(focusRequests, reason => Assert.Equal(ItemIdFocusReason.Settled, reason));
    });

    [Fact]
    public Task 確かめていないIDでは_確定のキーを押しても登録せず_確認を促して欄へ戻す() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の衣装");
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip");
        var focusRequests = new List<ItemIdFocusReason>();
        resolve.ItemIdFocusRequested += focusRequests.Add;

        resolve.ItemIdInput = "1000001";
        Assert.True(main.RunShortcut(ShortcutAction.SaveAndNext));
        await app.SettleAsync();

        Assert.Null(await app.Store.Items.LoadAsync("1000001"));
        Assert.Single(resolve.Files);
        Assert.Equal("先に商品IDを確認してください。", resolve.StatusText);
        Assert.Equal([ItemIdFocusReason.NeedsPreview], focusRequests);

        // 確定のキーは確かめも走らせない（BOOTH へ行くのは Enter・「情報を確認」だけ）
        Assert.Empty(app.Booth.Requests);
    });

    [Fact]
    public Task 確かめた後に欄を打ち直したら_前に確かめた商品へは確定しない() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の衣装");
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip");
        await TypeAndEnterAsync(app, resolve, "1000001");
        Assert.True(resolve.HasPreview);

        resolve.ItemIdInput = "1000002";
        Assert.True(main.RunShortcut(ShortcutAction.SaveAndNext));
        await app.SettleAsync();

        Assert.Null(await app.Store.Items.LoadAsync("1000001"));
        Assert.Single(resolve.Files);
        Assert.Equal("先に商品IDを確認してください。", resolve.StatusText);
    });

    [Fact]
    public Task URLで確かめても_欄はIDに置き換わり_確定のキーで確定できる() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の衣装");
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip");
        await TypeAndEnterAsync(app, resolve, "https://sample-shop.booth.pm/items/1000001");
        Assert.Equal("1000001", resolve.ItemIdInput);

        Assert.True(main.RunShortcut(ShortcutAction.SaveAndNext));
        await app.SettleAsync();

        Assert.NotNull(await app.Store.Items.LoadAsync("1000001"));
        Assert.Empty(resolve.Files);
    });

    [Fact]
    public Task BOOTHが答えていない間は_確定のキーを押しても確定しない() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の衣装");
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip");
        app.Booth.Hold();
        try
        {
            resolve.ItemIdInput = "1000001";
            resolve.PreviewCommand.Execute(null);
            await UiThread.Until(() => app.Booth.Requests.Count > 0, "確かめの問い合わせが BOOTH に届く");
            Assert.True(resolve.IsBusy);

            // 答えの前に押した確定のキー（Enter の後に続けて押した Ctrl+Enter）は受け取るだけで、何もしない
            Assert.True(main.RunShortcut(ShortcutAction.SaveAndNext));
        }
        finally
        {
            app.Booth.Release();
        }

        await app.SettleAsync();

        // 答えが届いて商品は出るが、確定はまだ（見てから押す）
        Assert.True(resolve.HasPreview);
        Assert.Null(await app.Store.Items.LoadAsync("1000001"));
        Assert.Single(resolve.Files);
    });

    [Fact]
    public Task 下に出ている商品と同じIDで_Enterを重ねても_BOOTHへ問い合わせ直さない() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の衣装");
        var (_, resolve) = await OpenResolveAsync(app, @"a\first.zip");
        await TypeAndEnterAsync(app, resolve, "1000001");
        var asked = app.Booth.Requests.Count;
        Assert.True(asked > 0);

        await TypeAndEnterAsync(app, resolve, " 1000001 ");

        Assert.Equal(asked, app.Booth.Requests.Count);
        Assert.True(resolve.HasPreview);
    });

    [Fact]
    public Task BOOTHに無いと答えたIDで確定のキーを押しても_答えの文は残る() => TestApp.Run(async app =>
    {
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip");
        await TypeAndEnterAsync(app, resolve, "1999999");
        Assert.True(resolve.HasNotOnBoothItemId);
        var answer = resolve.StatusText;
        Assert.NotEmpty(answer);

        Assert.True(main.RunShortcut(ShortcutAction.SaveAndNext));

        Assert.Equal(answer, resolve.StatusText);
        Assert.Single(resolve.Files);
    });

    [Fact]
    public Task 読み取れなかった後に打ち直して確定のキーを押すと_前の文ではなく確認を促す() => TestApp.Run(async app =>
    {
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip");
        await TypeAndEnterAsync(app, resolve, "abc");
        Assert.StartsWith("商品IDが読み取れませんでした。", resolve.StatusText);

        resolve.ItemIdInput = "12345";
        Assert.True(main.RunShortcut(ShortcutAction.SaveAndNext));

        Assert.Equal("先に商品IDを確認してください。", resolve.StatusText);
        Assert.Empty(app.Booth.Requests);
    });

    [Fact]
    public Task BOOTHに無い商品として登録しても_除外しても_次の行の欄へ戻す知らせが出る() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip", @"c\third.zip");
        var focusRequests = new List<ItemIdFocusReason>();
        resolve.ItemIdFocusRequested += focusRequests.Add;
        app.Answer = _ => System.Windows.MessageBoxResult.OK;

        resolve.LocalNameInput = "作り物の商品";
        resolve.RegisterLocalCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(2, resolve.Files.Count);
        Assert.Contains(ItemIdFocusReason.Settled, focusRequests);

        focusRequests.Clear();
        resolve.ExcludeCommand.Execute(null);
        await app.SettleAsync();
        Assert.Single(resolve.Files);
        Assert.Contains(ItemIdFocusReason.Settled, focusRequests);
        Assert.Empty(app.Booth.Requests);
    });

    [Fact]
    public Task 最後の1件を片付けたら_欄へ戻す知らせは出ない() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\first.zip");
        var focusRequests = new List<ItemIdFocusReason>();
        resolve.ItemIdFocusRequested += focusRequests.Add;
        app.Answer = _ => System.Windows.MessageBoxResult.OK;

        resolve.ExcludeCommand.Execute(null);
        await app.SettleAsync();

        Assert.Empty(resolve.Files);
        Assert.Empty(focusRequests);
    });

    [Fact]
    public Task 確定のキーは_選んでいるファイルが無ければ受け取らない() => TestApp.Run(async app =>
    {
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip");
        resolve.Selected = null;

        Assert.False(main.RunShortcut(ShortcutAction.SaveAndNext));
    });

    [Fact]
    public Task 登録のボタンの吹き出しは_今の割り当てのキーを言い_割り当てが無ければ出さない() => TestApp.Run(async app =>
    {
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip");
        Assert.Equal("Ctrl + Enter でも登録できます。", resolve.AssignKeyHint);

        await app.ChangeSettingsAsync(settings => settings with { Shortcuts = settings.Shortcuts with { SaveAndNext = string.Empty } });
        Assert.Null(resolve.AssignKeyHint);
    });

    [Fact]
    public void 設定の行は_編集画面と未確定の両方で使うことを言う()
        => Assert.Equal("編集画面で保存して次へ・未確定で登録", Shortcuts.ActionLabel(ShortcutAction.SaveAndNext));
}
