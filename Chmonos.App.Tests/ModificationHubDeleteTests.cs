using System.Windows;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の画面で改変を消した時に、左の一覧から消えるか（feedback 2026-11-18「開き直すまで残る」）。
/// </summary>
public class ModificationHubDeleteTests
{
    private static async Task<(ModificationHubViewModel Hub, ModificationRecord Kept, ModificationRecord Doomed)> OpenAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var main = await app.StartAsync();
        var kept = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("9900001", "残す改変"))).Record;
        var doomed = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("9900001", "消す改変"))).Record;

        var hub = new ModificationHubViewModel(app.Services, main, main.Thumbnails, ModificationHubLevel.Modification);
        await UiThread.Until(() => Names(hub).Count == 2, "改変の画面の読み込みが済む");
        return (hub, kept, doomed);
    }

    private static List<string> Names(ModificationHubViewModel hub)
        => hub.Groups.OfType<HubModificationRow>().Select(row => row.Name).ToList();

    [Fact]
    public Task 窓が手前に戻った読み直しが遅れて終わっても_消した改変は左に戻らない() => TestApp.Run(async app =>
    {
        var (hub, _, doomed) = await OpenAsync(app);

        // 削除の確認の窓を閉じると窓が手前に戻り、Hub・VCC の一覧を待つ遅い読み直しが走る。それを止めておく
        using var release = new ManualResetEventSlim(false);
        app.Services.DiscoverUnityProjects = () =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return [];
        };
        app.Tools = new UnityTools(HasHub: true, HasVcc: false, HasAlcom: false, LinkOpensAlcom: false);
        hub.NoteWindowActivated();

        app.Answer = _ => MessageBoxResult.OK;
        hub.ShowModificationCommand.Execute(doomed);
        Assert.IsType<ModificationViewModel>(hub.Detail).DeleteCommand.Execute(null);
        await UiThread.Until(() => hub.Detail is null && Names(hub).Count == 1, "消した改変が左から消える");

        // 遅い読み直しを終わらせる。終わった印はツールの有無が入ること
        release.Set();
        await UiThread.Until(() => hub.Tools.HasHub, "遅い読み直しが終わる");

        Assert.Equal(["残す改変"], Names(hub));
    });
}
