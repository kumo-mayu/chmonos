using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページの「記録していること」の「更新通知」のチェック（ユーザ判断 2026-10-04・メモ27-④）。
/// 前は見せるだけで押せず、変えるには編集画面を開く必要があった。
/// </summary>
public class ItemPageNotifyTests
{
    [Fact]
    public Task 更新通知のチェックを押すと_その項目だけを書き_知らせは出さない() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "作り物の衣装");
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        Assert.True(page.NotifyOnUpdate);

        // 開いた後で別の所がメモを書いた。持ち主を宣言して書くので、古い写しで消さない
        var stored = await app.Store.Items.LoadAsync(item.Id);
        await app.Store.Items.SaveAsync(stored! with { Local = stored.Local with { Memo = "後から書いたメモ" } });

        page.ToggleNotifyCommand.Execute(null);
        await app.SettleAsync();

        var saved = await app.Store.Items.LoadAsync(item.Id);
        Assert.False(saved!.Local.NotifyOnUpdate);
        Assert.Equal("後から書いたメモ", saved.Local.Memo);
        Assert.False(page.NotifyOnUpdate);
        Assert.Equal(string.Empty, page.RefreshStatus);
        Assert.Empty(app.Notices);

        page.ToggleNotifyCommand.Execute(null);
        await app.SettleAsync();

        Assert.True((await app.Store.Items.LoadAsync(item.Id))!.Local.NotifyOnUpdate);
        Assert.True(page.NotifyOnUpdate);
    });
}
