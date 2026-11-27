using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 条件「保存した検索」（ユーザ判断 2026-11-27・docs/spec/search-saved.md「条件にする・フォルダ」）：
/// 呼び出した結果と同じ商品・検索文字列を使わないときの広がり・輪を断る・名前の変更に付いていく・元が無いとき・メニュー。
/// </summary>
public class SavedSearchConditionTests
{
    private static ItemRecord Categorized(string id, string name, string category)
        => Make.Item(id, name) with { Local = Make.Item(id, name).Local with { Category = category } };

    private static async Task<SearchViewModel> StartAsync(TestApp app)
    {
        await app.AddItemAsync(Categorized("1000001", "夏のワンピース", "衣装"));
        await app.AddItemAsync(Categorized("1000002", "夏の髪", "髪型"));
        await app.AddItemAsync(Categorized("1000003", "冬のコート", "衣装"));
        await app.AddItemAsync(Categorized("1000004", "夏の帽子", "小物"));
        return (await app.StartAsync()).Search;
    }

    private static string[] Shown(SearchViewModel search) => search.ListItems.Select(card => card.Item.Id).Order(StringComparer.Ordinal).ToArray();

    private static ListModule AddCategory(SearchViewModel search, params string[] keys)
    {
        var module = (ListModule)SearchModules.Add(search, SearchModuleKind.Category);
        foreach (var key in keys)
        {
            module.AddKey(key);
        }

        return module;
    }

    private static IReadOnlyList<SearchModuleMenuEntry> SavedMenu(SearchViewModel search)
        => search.ModuleMenu.Single(heading => heading.Title == "保存した検索").Entries.OfType<SearchModuleMenuEntry>().ToList();

    private static SavedSearchModule AddSaved(SearchViewModel search, string label)
    {
        var before = search.Modules.ToList();
        SavedMenu(search).Single(entry => entry.Label == label).AddCommand.Execute(null);
        return (SavedSearchModule)search.Modules.Except(before).Single();
    }

    /// <summary>
    /// 文字「夏」・カテゴリ（衣装か小物）・除くカテゴリ（小物）・効かせていないカテゴリ（髪型）を「S」に保存する。
    /// 呼び出すと 1000001 だけ（文字を外すと 1000001・1000003）
    /// </summary>
    private static async Task SaveMixedAsync(SearchViewModel search)
    {
        search.QueryText = "夏";
        AddCategory(search, "衣装", "小物");
        AddCategory(search, "小物").IsExcluded = true;
        AddCategory(search, "髪型").IsEnabled = false;
        await search.SaveCurrentSearchAsync("S");
    }

    private static void ClearAll(SearchViewModel search)
    {
        foreach (var module in search.Modules.ToList())
        {
            module.RemoveCommand!.Execute(null);
        }

        search.QueryText = string.Empty;
    }

    [Fact]
    public Task 条件だけの結果は_呼び出した結果と同じ_除くと効かせていない条件も同じに扱う() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        await SaveMixedAsync(search);
        ClearAll(search);
        search.SavedRows.First(row => row.Name == "S").ApplyCommand.Execute(null);
        var applied = Shown(search);
        Assert.Equal(["1000001"], applied);

        ClearAll(search);
        var module = AddSaved(search, "S");

        Assert.Equal(applied, Shown(search));
        Assert.Equal("保存した検索：S", module.SummaryText);
        Assert.True(module.ShowsUseText);
        Assert.False(module.HasProblem);

        // 除くと、呼び出して当たる物以外
        module.IsExcluded = true;
        Assert.Equal(["1000002", "1000003", "1000004"], Shown(search));
    });

    [Fact]
    public Task 検索文字列も使うを外すと_文字の分だけ広がり_状態に欄が書かれる() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        await SaveMixedAsync(search);
        ClearAll(search);
        var module = AddSaved(search, "S");
        Assert.Equal(["1000001"], Shown(search));

        module.UseText = false;

        Assert.Equal(["1000001", "1000003"], Shown(search));
        var state = module.Save();
        Assert.True(state.IgnoreText);
        Assert.Equal(["S"], state.Items);

        // 既定（使う）のときは欄を書かない（v1.2.0 の形のまま）
        module.UseText = true;
        Assert.DoesNotContain("ignoreText", System.Text.Json.JsonSerializer.Serialize(module.Save(), Core.Storage.JsonStore.Options));
    });

    [Fact]
    public Task 文字の無い保存した検索では_検索文字列も使うを出さない() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        AddCategory(search, "衣装");
        await search.SaveCurrentSearchAsync("衣装だけ");
        ClearAll(search);

        Assert.False(AddSaved(search, "衣装だけ").ShowsUseText);
        Assert.Equal(["1000001", "1000003"], Shown(search));
    });

    [Fact]
    public Task 自分に戻る保存と上書きは_書かずに断る() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        await search.SaveCurrentSearchAsync("A");

        // B は A を条件に使う（輪ではない）
        ClearAll(search);
        AddSaved(search, "A");
        await search.SaveCurrentSearchAsync("B");
        Assert.Equal(["A", "B"], app.Store.SavedSearches.Load().Entries.Select(entry => entry.Name ?? "").ToArray());

        // 今の検索（条件 B）で A を上書きすると A → B → A の輪になる
        ClearAll(search);
        AddSaved(search, "B");
        app.Answer = _ => MessageBoxResult.OK;
        await search.OverwriteSavedAsync("A");

        var stored = app.Store.SavedSearches.Load().Entries;
        Assert.Equal("夏", stored[0].Text);
        Assert.Empty(stored[0].Modules);
        Assert.Contains("「A」に戻るため、保存できません", app.Notices.Last().Text);

        // 同じ名前で保存（置き換え）も同じ
        await search.SaveCurrentSearchAsync("A");
        Assert.Empty(app.Store.SavedSearches.Load().Entries[0].Modules);
    });

    [Fact]
    public Task 名前を変えると_保存した検索の中の条件と今の条件の欄が付いていく() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        await search.SaveCurrentSearchAsync("A");
        ClearAll(search);
        AddSaved(search, "A");
        await search.SaveCurrentSearchAsync("B");
        var module = Assert.IsType<SavedSearchModule>(Assert.Single(search.Modules));

        await search.RenameSavedAsync("A", "夏の物");

        var b = app.Store.SavedSearches.Load().Entries.Single(entry => entry.Name == "B");
        Assert.Equal(["夏の物"], Assert.Single(b.Modules).Items);
        Assert.Equal("夏の物", module.Name);
        Assert.False(module.HasProblem);
        Assert.Equal(["1000001", "1000002", "1000004"], Shown(search));
        Assert.Contains(SavedMenu(search), entry => entry.Label == "夏の物");
    });

    [Fact]
    public Task 元を消すと_何にも当たらず欄に言い_除くと全部が通る() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        await search.SaveCurrentSearchAsync("A");
        ClearAll(search);
        var module = AddSaved(search, "A");
        Assert.Equal(["1000001", "1000002", "1000004"], Shown(search));

        app.Answer = _ => MessageBoxResult.OK;
        await search.DeleteSavedAsync("A");

        Assert.Equal(SavedSearchProblem.Missing, module.Problem);
        Assert.Equal("この保存した検索はありません", module.ProblemText);
        Assert.False(module.ShowsUseText);
        Assert.Empty(Shown(search));

        module.IsExcluded = true;
        Assert.Equal(["1000001", "1000002", "1000003", "1000004"], Shown(search));
    });

    [Fact]
    public Task メニューは_一覧と同じ並びでフォルダを名前の頭に付け_無ければ押せない行を出す() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        var empty = Assert.Single(SavedMenu(search));
        Assert.False(empty.AddCommand.CanExecute(null));
        Assert.Equal("保存した検索はありません", empty.Label);

        search.QueryText = "夏";
        await search.SaveCurrentSearchAsync("A");
        await search.SaveCurrentSearchAsync("B");
        await search.MoveSavedToFolderAsync("A", "季節");

        Assert.Equal(["B", "季節 ／ A"], SavedMenu(search).Select(entry => entry.Label).ToArray());
        Assert.Single(search.ModuleMenu.Single(heading => heading.Title == "保存した検索").Entries.OfType<SearchModuleMenuSeparator>());
    });

    [Fact]
    public Task 保存した検索の中身を上書きすると_条件の結果も変わる() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        await search.SaveCurrentSearchAsync("A");
        ClearAll(search);
        AddSaved(search, "A");
        Assert.Equal(["1000001", "1000002", "1000004"], Shown(search));

        // 足した時の中身で固めず、今の中身で当てる
        await app.Services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeSavedSearches(
            stored => new SavedSearchList { Entries = [stored.Entries[0] with { Text = "冬" }] }));
        search.RestoreSavedSearches();

        Assert.Equal(["1000003"], Shown(search));
    });
}
