using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// タグの管理で名前を変えた・消した後、検索のユーザータグの条件の候補が新しい名簿になること（2026-11-10）。
/// タグの管理は属性の管理と違って「候補の作り直し」（RefreshMasters）を呼ばないが、
/// 書き換えの後に必ず呼ぶライブラリの読み直し（ReloadLibraryAsync）が検索の候補も組み直すので、古い名前は残らない。
/// その道が切れたら気付けるように試験にしておく
/// </summary>
public class UserTagCandidatesAfterRenameTests
{
    [Fact]
    public Task 大分類の名前を変えて読み直すと_検索の候補が新しい名前になる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        await app.Services.Commands.ExecuteAsync(new UiCommand.AddUserTag("古い名前"));
        await main.ReloadLibraryAsync();

        var module = (UserTagModule)SearchModules.Add(main.Search, SearchModuleKind.UserTag);
        Assert.Contains("古い名前", module.Suggestions);

        await app.Services.Commands.ExecuteAsync(new UiCommand.RenameUserTag("古い名前", null, "新しい名前"));
        await main.ReloadLibraryAsync();

        Assert.Contains("新しい名前", module.Suggestions);
        Assert.DoesNotContain("古い名前", module.Suggestions);

        await app.Services.Commands.ExecuteAsync(new UiCommand.DeleteUserTag("新しい名前"));
        await main.ReloadLibraryAsync();

        Assert.DoesNotContain("新しい名前", module.Suggestions);
    });
}
