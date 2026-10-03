using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>検索の対応アバターの条件の候補（メモ25 E）：素体の名前は、アバターと判定した記録から集める。</summary>
public class SearchAvatarBaseCandidatesTests
{
    [Fact]
    public Task 素体の候補は_アバターでない記録に残った素体名を拾わない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "2000001", BoothName = "作り物のアバター", AvatarOverride = true, BaseName = "作り物の甲素体" },
                // 素体を決めた後で「アバターとして扱わない」にした記録。素体名だけが残る
                new AvatarRegistryEntry { ItemId = "2000002", BoothName = "作り物のテクスチャ", AvatarOverride = false, BaseName = "作り物の乙素体" },
            ],
        });
        var search = (await app.StartAsync()).Search;

        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Avatar);

        Assert.Contains("作り物の甲素体（共通素体）", module.Suggestions);
        Assert.DoesNotContain("作り物の乙素体（共通素体）", module.Suggestions);
    });
}
