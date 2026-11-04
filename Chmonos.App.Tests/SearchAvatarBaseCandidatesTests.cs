using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

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

    /// <summary>
    /// 共通素体ごとにまとめる設定（ユーザ指示 2026-11-04・既定はオン）：群の中で同じ素体の仲間を、名前の順で最初の仲間の位置に寄せ、
    /// 素体の中は人が並べた順。まとめない設定では名前の順のまま。
    /// </summary>
    [Theory]
    [InlineData(true, "アオイ,ユウ,カイ,ソラ")]
    [InlineData(false, "アオイ,カイ,ソラ,ユウ")]
    public Task 未所持の群の中で_同じ素体の仲間を並べた順で隣にする(bool grouping, string expected) => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        static AvatarRegistryEntry Avatar(string id, string name, string? baseName) => new()
        {
            ItemId = id, BoothName = name, DisplayName = name, AvatarOverride = true, BaseName = baseName,
        };
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                Avatar("2000011", "アオイ", "作り物素体"),
                Avatar("2000012", "カイ", "作り物素体"),
                Avatar("2000013", "ユウ", "作り物素体"),
                Avatar("2000021", "ソラ", null),
            ],
            BaseGroups = [new AvatarBaseGroup { Name = "作り物素体", MemberOrder = ["2000011", "2000013"] }],
        });
        await app.ChangeSettingsAsync(settings => settings with { GroupAvatarsByBase = grouping });
        var search = (await app.StartAsync()).Search;

        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Avatar);

        var avatars = module.Suggestions
            .Where(text => !text.EndsWith("（共通素体）", StringComparison.Ordinal))
            .Select(AvatarSuggestionText.NameOf);
        Assert.Equal(expected, string.Join(",", avatars));
    });
}
