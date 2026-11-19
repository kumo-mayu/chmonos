using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>改変の画面の探す欄は、改変のメモでも当たる（ユーザ指示 2026-11-18）。作り物の名前</summary>
public sealed class ModificationHubMemoSearchTests
{
    [Fact]
    public Task メモの語で当たる() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "9900001", BoothName = "作り物のアバター", AvatarOverride = true, IsOwnedManually = true }],
        });
        var main = await app.StartAsync();
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("9900001", "夏の改変")));
        await app.Services.Commands.ExecuteAsync(new UiCommand.SetModificationMemo(created.Record.Id, "靴は最後に履かせる"));
        var hub = new ModificationHubViewModel(app.Services, main, main.Thumbnails, ModificationHubLevel.Modification);
        await UiThread.Until(() => hub.Lines.OfType<HubModLine>().Any(), "一覧が並ぶ");

        Assert.Contains("メモ", hub.QueryPlaceholder);
        hub.Query = "最後に履";
        Assert.Contains(hub.Lines.OfType<HubModLine>(), line => line.Row.Record.Name == "夏の改変");
        hub.Query = "どこにも無い語";
        Assert.DoesNotContain(hub.Lines.OfType<HubModLine>(), line => line.Row.Record.Name == "夏の改変");
    });
}
