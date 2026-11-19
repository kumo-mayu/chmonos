using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変を選ぶ窓（「改変に追加して送る」「改変に追加」）の行の「複製…」（ユーザ判断 2026-11-20）。
/// 名前を聞いて複製し、複製の行を元の行のすぐ下に入れて、複製を選んだ状態にする（元の選びは外す）。
/// </summary>
public class PickModificationDuplicateTests
{
    private static async Task<(PickModificationDialogViewModel Model, ModificationRecord Source)> BuildAsync(TestApp app)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "2000001", BoothName = "作り物のアバター", AvatarOverride = true }],
        });
        await app.StartAsync();

        var first = (CommandResult.ModificationCreated)await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("2000001", "普段着"));
        var second = (CommandResult.ModificationCreated)await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("2000001", "制服"));
        var model = ModificationPicking.BuildDialog(
            app.Services, "改変に追加して送る", "見出し", "", [first.Record, second.Record], "既存", "追加して送る", "空");
        return (model, first.Record);
    }

    [Fact]
    public Task 複製すると_元の下に複製の行が入り_複製だけが選ばれる() => TestApp.Run(async app =>
    {
        var (model, source) = await BuildAsync(app);
        Assert.True(model.CanDuplicate);
        model.Rows[0].IsChecked = true;
        model.Rows[1].IsChecked = true;

        app.NameModification = dialog =>
        {
            Assert.Equal("普段着", dialog.Name); // 元の名前を入れて開く（改変の画面の複製と同じ）
            dialog.Name = "普段着の別案";
            return true;
        };
        await model.DuplicateAsync(model.Rows[0]);

        Assert.Equal(["普段着", "普段着の別案", "制服"], model.Rows.Select(row => row.Name));
        Assert.Equal(["普段着の別案", "制服"], model.PickedRecords.Select(record => record.Name));
        Assert.False(model.MakingNew);

        var saved = (await app.Services.Modifications.LoadForAvatarAsync("2000001")).Select(record => record.Name).Order().ToList();
        Assert.Equal(["制服", "普段着", "普段着の別案"], saved);
        Assert.NotEqual(source.Id, model.PickedRecords[0].Id);
    });

    [Fact]
    public Task 名前の窓をやめると_何も作らず選びも変えない() => TestApp.Run(async app =>
    {
        var (model, _) = await BuildAsync(app);
        model.Rows[0].IsChecked = true;

        app.NameModification = _ => false;
        await model.DuplicateAsync(model.Rows[0]);

        Assert.Equal(["普段着", "制服"], model.Rows.Select(row => row.Name));
        Assert.Equal(["普段着"], model.PickedRecords.Select(record => record.Name));
        Assert.Equal(2, (await app.Services.Modifications.LoadForAvatarAsync("2000001")).Count);
    });

    [Fact]
    public Task 新しく作る側を選んでいても_複製すると今ある改変を選ぶ側に戻る() => TestApp.Run(async app =>
    {
        var (model, _) = await BuildAsync(app);
        model.MakingNew = true;

        app.NameModification = _ => true;
        await model.DuplicateAsync(model.Rows[1]);

        Assert.False(model.MakingNew);
        Assert.Single(model.PickedRecords);
        Assert.Equal(model.Rows[2].Record.Id, model.PickedRecords[0].Id);
        Assert.True(model.CanCommit);
    });
}
