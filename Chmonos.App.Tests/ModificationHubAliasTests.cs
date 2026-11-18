using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の画面の探す欄は、アバターを呼び方でも引くが、人が消した呼び方では引かない
/// （ユーザ判断 2026-11-18：消した呼び方は探す欄・候補・照合のどこでも使わない。`AvatarAliasList.LiveTexts`）。作り物の名前
/// </summary>
public sealed class ModificationHubAliasTests
{
    [Fact]
    public Task 消した呼び方では当たらず_残した呼び方では当たる() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = "9900001", BoothName = "作り物のアバター", AvatarOverride = true, IsOwnedManually = true,
                    Aliases = [new AvatarAlias { Text = "Kip" }, new AvatarAlias { Text = "Mzh", Rejected = true }],
                },
            ],
        });
        var main = await app.StartAsync();
        await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("9900001", "夏の改変"));
        var hub = new ModificationHubViewModel(app.Services, main, main.Thumbnails, ModificationHubLevel.Avatar);
        await UiThread.Until(() => hub.Lines.OfType<HubModLine>().Any(), "一覧が並ぶ");

        hub.Query = "kip";
        Assert.Contains(hub.Lines.OfType<HubModLine>(), line => line.Row.Record.Name == "夏の改変");

        hub.Query = "mzh";
        Assert.DoesNotContain(hub.Lines.OfType<HubModLine>(), line => line.Row.Record.Name == "夏の改変");
    });
}
