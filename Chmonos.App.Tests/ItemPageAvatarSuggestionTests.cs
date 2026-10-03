using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>商品ページの対応アバターを足す欄の候補（メモ25 D）：その商品自身は、自分の対応アバターにならないので出さない。</summary>
public class ItemPageAvatarSuggestionTests
{
    [Fact]
    public Task 対応アバターの候補は_その商品自身を出さず_足しても入らない() => TestApp.Run(async app =>
    {
        var self = Make.Item("1000001", "作り物のアバター");
        await app.AddItemAsync(self);
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "1000001", BoothName = "作り物のアバター", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = "2000002", BoothName = "もう1体のアバター", AvatarOverride = true },
            ],
        });
        var main = await app.StartAsync();
        var page = new ItemViewModel(self, app.Services, main, main.Thumbnails);

        Assert.Equal(["もう1体のアバター"], page.SupportSuggestions);

        // 打って足そうとしても足されない
        page.AddAvatarCommand.Execute("作り物のアバター");
        await app.SettleAsync();
        Assert.Empty((await app.Store.Items.LoadAsync("1000001"))!.Local.Avatars);
    });
}
