using System.IO;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// アバターの候補（メモ48・メモ58）：「所持アバター／共通素体／未所持アバター」の3群と、登録簿の呼び方でも当たること、
/// 改変を選ぶ窓の「今ある改変」の行の絵。名前は作り物。
/// </summary>
public class AvatarSuggestGroupsTests
{
    private static SuggestInfo Info(int group, params string[] hints)
        => new(group, hints.Select(text => new SuggestHint(text, $"呼び方「{text}」")).ToList());

    // ----- 候補の部品：群・見出し・呼び方 -----

    [Fact]
    public void 群の番号の小さい順に並び_群が変わる行の上に区切りと見出しの印が付く()
    {
        var all = new[] { "未所持A", "素体B（共通素体）", "所持C", "未所持D" };
        var infos = new Dictionary<string, SuggestInfo>
        {
            ["所持C"] = Info(0),
            ["素体B（共通素体）"] = Info(1),
            ["未所持A"] = Info(2),
            ["未所持D"] = Info(2),
        };

        var rows = SuggestBox.Arrange(all, "", 0, text => infos.GetValueOrDefault(text));

        Assert.Equal(["所持C", "素体B（共通素体）", "未所持A", "未所持D"], rows.Select(row => row.Entry));
        Assert.Equal([0, 1, 2, 2], rows.Select(row => row.Group));
        Assert.Equal([true, true, true, false], rows.Select(row => row.GroupStart));
        // 先頭の群の上には線を引かない
        Assert.Equal([false, true, true, false], rows.Select(row => row.DividerAbove));
    }

    [Fact]
    public void 候補の残らない群は飛ばし_共通素体を出さない欄は2群のまま()
    {
        var all = new[] { "未所持A", "所持C" };
        var infos = new Dictionary<string, SuggestInfo> { ["所持C"] = Info(0), ["未所持A"] = Info(2) };

        var rows = SuggestBox.Arrange(all, "", 0, text => infos.GetValueOrDefault(text));

        Assert.Equal([true, true], rows.Select(row => row.GroupStart));
        Assert.Equal([false, true], rows.Select(row => row.DividerAbove));

        // 絞って片方の群しか残らなければ、線は引かない（見出しの印は先頭に付く）
        var narrowed = SuggestBox.Arrange(all, "未所持", 0, text => infos.GetValueOrDefault(text));
        Assert.Single(narrowed);
        Assert.True(narrowed[0].GroupStart);
        Assert.False(narrowed[0].DividerAbove);
    }

    [Fact]
    public void 呼び方で当たる_名前に入る候補より後ろで_札が付く()
    {
        var all = new[] { "ミズホ（9900001）", "キップ（9900002）" };
        var infos = new Dictionary<string, SuggestInfo>
        {
            ["ミズホ（9900001）"] = Info(0, "Kip-chan"),
            ["キップ（9900002）"] = Info(0),
        };

        var rows = SuggestBox.Arrange(all, "kip", 0, text => infos.GetValueOrDefault(text));

        // 名前に入っていなくても呼び方で当たる。札は呼び方で当たった行にだけ付く
        Assert.Equal(["ミズホ（9900001）"], rows.Select(row => row.Entry));
        Assert.Equal("呼び方「Kip-chan」", rows[0].Hit!.Label);
    }

    [Fact]
    public void 名前で当たる行には札が付かず_呼び方だけの行より先に来る()
    {
        var all = new[] { "ミズホ（9900001）", "Kipfel（9900002）" };
        var infos = new Dictionary<string, SuggestInfo> { ["ミズホ（9900001）"] = Info(0, "kip") };

        var rows = SuggestBox.Arrange(all, "kip", 0, text => infos.GetValueOrDefault(text));

        Assert.Equal(["Kipfel（9900002）", "ミズホ（9900001）"], rows.Select(row => row.Entry));
        Assert.Null(rows[0].Hit);
        Assert.NotNull(rows[1].Hit);
    }

    [Fact]
    public void 案内の無い候補は今までどおり_先頭からの件数で2群に分かれる()
    {
        var rows = SuggestBox.Arrange(["あ", "い", "う"], "", 2);

        Assert.Equal([0, 0, 1], rows.Select(row => row.Group));
        Assert.Equal([false, false, true], rows.Select(row => row.DividerAbove));
    }

    // ----- 検索の対応アバター -----

    [Fact]
    public Task 検索の対応アバターの候補は_3群に分かれ_呼び方でも当たる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = "9900001", BoothName = "作り物の持っているアバター", AvatarOverride = true, IsOwnedManually = true,
                    BaseName = "作り物の素体",
                    Aliases = [new AvatarAlias { Text = "Mzh" }],
                },
                new AvatarRegistryEntry { ItemId = "9900002", BoothName = "作り物の持っていないアバター", AvatarOverride = true },
            ],
        });
        var search = (await app.StartAsync()).Search;
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Avatar);

        var groupOf = module.Suggestions.ToDictionary(text => text, text => module.InfoSelector!(text)!.Group);

        Assert.Equal(AvatarSuggestionText.OwnedGroup, groupOf["作り物の持っているアバター（9900001）"]);
        Assert.Equal(AvatarSuggestionText.BaseGroup, groupOf["作り物の素体（共通素体）"]);
        Assert.Equal(AvatarSuggestionText.OtherGroup, groupOf["作り物の持っていないアバター（9900002）"]);
        Assert.Equal(["所持アバター", "共通素体", "未所持アバター"], module.GroupHeadings);

        var rows = SuggestBox.Arrange(module.Suggestions.ToList(), "mzh", 0, module.InfoSelector);
        Assert.Equal(["作り物の持っているアバター（9900001）"], rows.Select(row => row.Entry));
        Assert.Equal("呼び方「Mzh」", rows[0].Hit!.Label);
    });

    // ----- 商品ページの対応アバターを足す欄 -----

    [Fact]
    public Task 商品ページの対応アバターの候補は_所持_素体_未所持の群で_呼び方でも当たる() => TestApp.Run(async app =>
    {
        var self = Make.Item("1000001", "作り物の衣装");
        await app.AddItemAsync(self);
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            BaseGroups = [new AvatarBaseGroup { Name = "作り物の素体" }],
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = "9900001", BoothName = "作り物の持っているアバター", AvatarOverride = true, IsOwnedManually = true,
                    BaseName = "作り物の素体",
                    Aliases = [new AvatarAlias { Text = "Mzh" }],
                },
                new AvatarRegistryEntry { ItemId = "9900002", BoothName = "作り物の持っていないアバター", AvatarOverride = true },
            ],
        });
        var main = await app.StartAsync();
        var page = new ItemViewModel(self, app.Services, main, main.Thumbnails);

        var select = page.SupportSuggestInfoSelector;
        Assert.Equal(AvatarSuggestionText.OwnedGroup, select("作り物の持っているアバター")!.Group);
        Assert.Equal(AvatarSuggestionText.BaseGroup, select("作り物の素体（共通素体）")!.Group);
        Assert.Equal(AvatarSuggestionText.OtherGroup, select("作り物の持っていないアバター")!.Group);

        var rows = SuggestBox.Arrange(page.SupportSuggestions.ToList(), "mzh", 0, select);
        Assert.Equal(["作り物の持っているアバター"], rows.Select(row => row.Entry));
    });

    // ----- 改変を選ぶ窓 -----

    [Fact]
    public Task 改変を選ぶ窓の候補は_所持と未所持の2群で_素体の群は出ず_呼び方でも当たる() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = "9900001", BoothName = "作り物の持っていないアバター", AvatarOverride = true,
                    Aliases = [new AvatarAlias { Text = "Mzh" }],
                },
                new AvatarRegistryEntry { ItemId = "9900002", BoothName = "作り物の持っているアバター", AvatarOverride = true, IsOwnedManually = true },
            ],
        });
        await app.StartAsync();

        var model = ModificationPicking.BuildDialog(app.Services, "題", "見出し", "", [], "既存", "決定", "空");

        var rows = SuggestBox.Arrange(model.AvatarNames, "", 0, model.AvatarSuggestInfoSelector);
        Assert.Equal([AvatarSuggestionText.OwnedGroup, AvatarSuggestionText.OtherGroup], rows.Select(row => row.Group));

        var byAlias = SuggestBox.Arrange(model.AvatarNames, "mzh", 0, model.AvatarSuggestInfoSelector);
        var found = Assert.Single(byAlias);
        Assert.Contains("持っていないアバター", found.Entry);
        Assert.Equal("呼び方「Mzh」", found.Hit!.Label);
    });

    [Fact]
    public Task 改変を選ぶ窓の今ある改変の行は_写真の1枚目_無ければアバターの絵_どちらも無ければ空() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "9900001", BoothName = "作り物のアバターA", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = "9900002", BoothName = "作り物のアバターB", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = "9900003", BoothName = "作り物のアバターC", AvatarOverride = true },
            ],
        });
        await app.StartAsync();
        var paths = app.Services.Paths;

        var photo = Path.Combine(paths.ModificationImagesDir("mod-0000a001"), "p.webp");
        var avatarPicture = Path.Combine(paths.AvatarImagesDir("9900002"), "a.webp");
        Directory.CreateDirectory(Path.GetDirectoryName(photo)!);
        Directory.CreateDirectory(Path.GetDirectoryName(avatarPicture)!);
        File.WriteAllBytes(photo, [1]);
        File.WriteAllBytes(avatarPicture, [1]);

        var records = new[]
        {
            new ModificationRecord { Id = "mod-0000a001", AvatarItemId = "9900001", Name = "写真あり", Images = [new ModificationImage { FileName = "p.webp" }] },
            new ModificationRecord { Id = "mod-0000a002", AvatarItemId = "9900002", Name = "アバターの絵だけ" },
            new ModificationRecord { Id = "mod-0000a003", AvatarItemId = "9900003", Name = "絵なし" },
        };

        var model = ModificationPicking.BuildDialog(app.Services, "題", "見出し", "", records, "既存", "決定", "空");

        Assert.Equal(photo, model.Rows[0].IconPath);
        Assert.Equal(avatarPicture, model.Rows[1].IconPath);
        Assert.Null(model.Rows[2].IconPath);
        // 絵の無い行は頭文字（既定の絵）。読み込み器は作らない
        Assert.Null(model.Rows[2].Icon);
        Assert.Equal("作", model.Rows[2].Initial);
    });
}
