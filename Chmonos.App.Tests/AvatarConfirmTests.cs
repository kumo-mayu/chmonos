using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページ（編集画面も同じ部品）の対応アバターの「確認済みにする」（ユーザ判断 2026-10-06・メモ83）。
/// 置き場は札に乗せたときの ✓・札の右クリック・見出しの「すべて確認済みにする」で、どれも同じ命令を通す。
/// </summary>
public class AvatarConfirmTests
{
    private const string ItemId = "9900861";

    private static AvatarLink Guess(string id, string name) => new()
    {
        AvatarItemId = id,
        Name = name,
        Source = AvatarLinkSource.H2Link,
        Confirmed = false,
    };

    private static ItemRecord WithAvatars(params AvatarLink[] links)
    {
        var item = Make.Item(ItemId, "作り物の衣装");
        return item with { Local = item.Local with { Avatars = links, AvatarsDetectedAt = DateTimeOffset.Now } };
    }

    private static async Task<IReadOnlyList<AvatarLink>> SavedAsync(TestApp app)
        => (await app.Store.Items.LoadAsync(ItemId))!.Local.Avatars;

    [Fact]
    public Task 札の確認済みにするで1件だけ確認済みになり_見出しの確認待ちの数が減る() => TestApp.Run(async app =>
    {
        var item = WithAvatars(Guess("9900871", "作り物のアバターA"), Guess("9900872", "作り物のアバターB"));
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        Assert.Equal("確認待ち 2", page.UnconfirmedAvatarText);

        var chip = page.Avatars.Single(row => row.ItemId == "9900871");
        Assert.True(chip.ConfirmCommand!.CanExecute(null));
        chip.ConfirmCommand.Execute(null);
        await app.SettleAsync();

        var saved = await SavedAsync(app);
        Assert.True(saved.Single(link => link.AvatarItemId == "9900871").Confirmed);
        Assert.False(saved.Single(link => link.AvatarItemId == "9900872").Confirmed);
        Assert.False(page.Avatars.Single(row => row.ItemId == "9900871").IsUnconfirmed);
        Assert.Equal("確認待ち 1", page.UnconfirmedAvatarText);
    });

    /// <summary>右クリックの項目は確認済みの札でも出したまま、押せなくする（右クリックの決まり：項目を全部出す）。</summary>
    [Fact]
    public Task 確認済みの札では確認済みにするを押せない() => TestApp.Run(async app =>
    {
        var item = WithAvatars(Guess("9900871", "作り物のアバターA") with { Confirmed = true, Source = AvatarLinkSource.SupportSection });
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);

        var chip = Assert.Single(page.Avatars);
        Assert.False(chip.IsUnconfirmed);
        Assert.False(chip.ConfirmCommand!.CanExecute(null));
        Assert.False(page.HasUnconfirmedAvatars);
        Assert.False(page.ConfirmAllAvatarsCommand.CanExecute(null));
    });

    [Fact]
    public Task すべて確認済みにするで確認待ちが全部確認済みになる() => TestApp.Run(async app =>
    {
        var item = WithAvatars(
            Guess("9900871", "作り物のアバターA"),
            Guess("9900872", "作り物のアバターB"),
            Guess("9900873", "作り物のアバターC") with { Confirmed = true, Source = AvatarLinkSource.SupportSection });
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        Assert.True(page.ConfirmAllAvatarsCommand.CanExecute(null));

        page.ConfirmAllAvatarsCommand.Execute(null);
        await app.SettleAsync();

        Assert.All(await SavedAsync(app), link => Assert.True(link.Confirmed));
        Assert.False(page.HasUnconfirmedAvatars);

        // 確かめた方の出どころは手入力（次の検出で確認待ちに戻らない）。もとから確定の行の出どころは変えない
        var saved = await SavedAsync(app);
        Assert.Equal(AvatarLinkSource.Manual, saved.Single(link => link.AvatarItemId == "9900871").Source);
        Assert.Equal(AvatarLinkSource.SupportSection, saved.Single(link => link.AvatarItemId == "9900873").Source);
    });

    /// <summary>
    /// 画面が開いた後に、ほかの書き手が対応アバターを足し、メモを書いた。確認済みにしても、
    /// 画面の古い写しで書き戻さないので、足された行もメモも残る。
    /// </summary>
    [Fact]
    public Task 開いた後に書かれた行とほかの欄を巻き戻さない() => TestApp.Run(async app =>
    {
        var item = WithAvatars(Guess("9900871", "作り物のアバターA"));
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);

        await app.Store.Items.SaveAsync(item with
        {
            Local = item.Local with
            {
                Memo = "あとから書いたメモ",
                Avatars = [.. item.Local.Avatars, Guess("9900872", "作り物のアバターB")],
            },
        });

        page.Avatars.Single().ConfirmCommand!.Execute(null);
        await app.SettleAsync();

        var saved = (await app.Store.Items.LoadAsync(ItemId))!;
        Assert.Equal("あとから書いたメモ", saved.Local.Memo);
        Assert.Equal(["9900871", "9900872"], saved.Local.Avatars.Select(link => link.AvatarItemId));
        Assert.True(saved.Local.Avatars[0].Confirmed);
        Assert.False(saved.Local.Avatars[1].Confirmed);

        // 画面も今の記録で組み直す（足された札も出る）
        Assert.Equal(2, page.Avatars.Count);
    });
}
