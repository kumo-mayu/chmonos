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
}
