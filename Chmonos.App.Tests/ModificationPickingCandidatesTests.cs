using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変を選ぶ窓の「どのアバターの改変か」の候補（メモ25 A）。
/// 登録簿にはアバターでないと分かった物も残るので、候補はアバターだけに絞る。
/// </summary>
public class ModificationPickingCandidatesTests
{
    [Fact]
    public Task 候補はアバターだけ_名前から引くときもアバターでない同名の物を拾わない() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "2000001", BoothName = "作り物のアバター", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = "2000002", BoothName = "作り物のテクスチャ", AvatarOverride = false },
            ],
        });
        await app.StartAsync();

        var model = ModificationPicking.BuildDialog(
            app.Services, "題", "見出し", "", [], "既存", "決定", "空");

        var candidate = Assert.Single(model.AvatarNames);
        Assert.Contains("作り物のアバター", candidate);

        model.PickAvatarCommand.Execute(candidate);
        Assert.Equal("2000001", model.NewAvatarItemId);
        model.PickAvatarCommand.Execute("作り物のテクスチャ");
        Assert.Equal("2000001", model.NewAvatarItemId); // 引けない名前では変わらない
    });

    private static ModificationRecord Mod(string id, string avatarId, string name, string? project = null)
        => new() { Id = id, AvatarItemId = avatarId, Name = name, UnityProject = project };

    private static async Task<PickModificationDialogViewModel> BuildWithModsAsync(TestApp app)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "2000001", BoothName = "作り物のアバターA", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = "2000002", BoothName = "作り物のアバターB", AvatarOverride = true },
            ],
        });
        await app.StartAsync();

        return ModificationPicking.BuildDialog(
            app.Services, "題", "見出し", "",
            [
                Mod("mod-1", "2000001", "普段着", @"D:\Unity\作り物プロジェクト"),
                Mod("mod-2", "2000001", "制服"),
                Mod("mod-3", "2000002", "水着A", @"D:\Unity\Sandbox"),
            ],
            "既存", "決定", "空");
    }

    [Fact]
    public Task 探す欄は_改変の名前_アバターの名前_プロジェクトの名前で絞り_大文字小文字と全角半角を見ない() => TestApp.Run(async app =>
    {
        var model = await BuildWithModsAsync(app);
        Assert.Equal(3, model.Rows.Count);

        model.FilterText = "制服";
        Assert.Equal(["制服"], model.Rows.Select(row => row.Name));

        model.FilterText = "アバターA";
        Assert.Equal(["普段着", "制服"], model.Rows.Select(row => row.Name));

        model.FilterText = "ｓａｎｄｂｏｘ"; // 全角・小文字でも、プロジェクト名の Sandbox に当たる
        Assert.Equal(["水着A"], model.Rows.Select(row => row.Name));

        model.FilterText = "作り物 普段"; // 空白で区切った語は全部が当たる（プロジェクト名と改変名にまたがってよい）
        Assert.Equal(["普段着"], model.Rows.Select(row => row.Name));

        model.FilterText = "存在しない";
        Assert.Empty(model.Rows);
        Assert.True(model.HasNoMatch);
        Assert.True(model.HasRows); // 欄と見出しは消さない

        model.ClearFilterCommand.Execute(null);
        Assert.Equal(3, model.Rows.Count);
        Assert.False(model.HasNoMatch);
    });

    [Fact]
    public Task 当たった行には_名前以外で何に当たったかを出す() => TestApp.Run(async app =>
    {
        var model = await BuildWithModsAsync(app);

        model.FilterText = "普段着"; // 名前は行に出ているので理由は言わない
        Assert.Equal([""], model.Rows.Select(row => row.MatchNote));

        model.FilterText = "アバターA";
        Assert.Equal(["アバター：作り物のアバターA", "アバター：作り物のアバターA"], model.Rows.Select(row => row.MatchNote));

        model.FilterText = "作り物 A"; // 「作り物」はアバターにもプロジェクトにも入っている
        Assert.Equal("アバター：作り物のアバターA　プロジェクト：作り物プロジェクト", model.Rows.Single(row => row.Name == "普段着").MatchNote);

        model.FilterText = string.Empty;
        Assert.All(model.Rows, row => Assert.False(row.HasMatchNote));
    });

    [Fact]
    public Task 絞って選んでいた改変が外れたら_選びを解いて追加できなくする() => TestApp.Run(async app =>
    {
        var model = await BuildWithModsAsync(app);
        model.PickCommand.Execute(model.Rows.Single(row => row.Name == "水着A"));
        Assert.True(model.CanCommit);

        model.FilterText = "制服";

        Assert.Null(model.Picked);
        Assert.False(model.CanCommit);
    });

    [Fact]
    public Task アバターの候補には絵を引く口が付き_絵が無い名前では絵を返さない() => TestApp.Run(async app =>
    {
        var model = await BuildWithModsAsync(app);

        // 絵の無いアバターは null（候補の部品が頭文字を出す）。口そのものが無いと、候補にアイコンの欄が出ない
        Assert.NotNull(model.AvatarIconSelector);
        Assert.Null(model.AvatarIconSelector!("作り物のアバターA"));
        Assert.Null(model.AvatarIconSelector("存在しない名前"));
    });
}
