using BoothAssetManager.App.Tests.Support;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 検索の条件の保存。条件は変えてから 0.5 秒遅らせて書く（スライダを動かす間に何十回も書かないため）ので、
/// 閉じる前の書き切りに入っていないと、変えてすぐ閉じた分だけが次の起動に残らない。
/// </summary>
public class SearchModuleSaveTests
{
    [Fact]
    public Task 条件を変えてすぐ閉じても_閉じる前の書き切りで保存される() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();

        // 遅らせた保存が走る前（0.5 秒たつ前）に、閉じる前の書き切りを呼ぶ
        main.Search.ShowOnlyBrokenZip();
        await main.FlushPendingWritesAsync();

        var saved = await app.Services.SettingsStore.UpdateUiStateAsync(state => state);
        Assert.Contains(saved.SearchModules ?? [], module => module.Kind == nameof(SearchModuleKind.BrokenZip));
    });

    [Fact]
    public Task 変えていなければ_閉じる前に書き直さない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var before = await app.Services.SettingsStore.UpdateUiStateAsync(state => state);

        await main.FlushPendingWritesAsync();

        var after = await app.Services.SettingsStore.UpdateUiStateAsync(state => state);
        Assert.Equal(before.SearchModules?.Count ?? 0, after.SearchModules?.Count ?? 0);
    });
}
