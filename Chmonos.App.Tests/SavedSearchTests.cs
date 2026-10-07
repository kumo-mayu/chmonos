using System.IO;
using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 保存した検索（ユーザ判断 2026-10-03・10-05）：保存・呼び出しで置き換わる・上書き・名前の変更・削除・並べ替え・
/// 今の検索と同じ行の印・JSON の読み書き。
/// </summary>
public class SavedSearchTests
{
    private static ItemRecord Categorized(string id, string name, string category)
        => Make.Item(id, name) with { Local = Make.Item(id, name).Local with { Category = category } };

    private static async Task<SearchViewModel> StartAsync(TestApp app)
    {
        await app.AddItemAsync(Categorized("1000001", "夏のワンピース", "衣装"));
        await app.AddItemAsync(Categorized("1000002", "夏の髪", "髪型"));
        await app.AddItemAsync(Categorized("1000003", "冬のコート", "衣装"));
        return (await app.StartAsync()).Search;
    }

    private static string[] Shown(SearchViewModel search) => search.ListItems.Select(card => card.Item.Id).Order(StringComparer.Ordinal).ToArray();

    private static string[] Names(SearchViewModel search) => search.SavedRows.Select(row => row.Name).ToArray();

    private static string[] StoredNames(TestApp app) => app.Store.SavedSearches.Load().Entries.Select(entry => entry.Name ?? "").ToArray();

    private static void UseCategory(SearchViewModel search, string category)
    {
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Category);
        module.AddKey(category);
    }

    private static void SortByName(SearchViewModel search)
        => search.SortField = search.SortFields.First(field => field.Kind == SortKind.Name);

    private static SavedSearchRow Row(SearchViewModel search, string name) => search.SavedRows.First(row => row.Name == name);

    [Fact]
    public Task 保存すると_文字と条件と表示順とリストかが名前付きで書かれ_節に行が出る() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        UseCategory(search, "衣装");
        SortByName(search);
        search.IsListMode = true;

        await search.SaveCurrentSearchAsync("  夏の衣装  ");

        var stored = Assert.Single(app.Store.SavedSearches.Load().Entries);
        Assert.Equal("夏の衣装", stored.Name);
        Assert.Equal("夏", stored.Text);
        Assert.Equal("Category", Assert.Single(stored.Modules).Kind);
        Assert.Equal(["衣装"], stored.Modules[0].Items);
        Assert.Equal("名前順", stored.Sort);
        Assert.Equal(ResultView.List, stored.View);
        Assert.Equal(["夏の衣装"], Names(search));
        Assert.True(Row(search, "夏の衣装").IsCurrent);
        Assert.Equal("夏の衣装", search.CurrentSavedName);
    });

    [Fact]
    public Task 呼び出すと_条件と文字と表示順とカードかリストかが置き換わる() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        UseCategory(search, "衣装");
        SortByName(search);
        search.IsListMode = true;
        await search.SaveCurrentSearchAsync("夏の衣装");

        // 別の検索にしてから呼び出す。今の値の上に重ねず、置き換える
        search.ClearFiltersCommand.Execute(null);
        search.QueryText = "冬";
        search.SortField = search.SortFields.First(field => field.Kind == SortKind.AcquiredAt);
        search.IsListMode = false;
        Assert.Null(search.CurrentSavedName);

        Row(search, "夏の衣装").ApplyCommand.Execute(null);

        Assert.Equal("夏", search.QueryText);
        Assert.Equal(["1000001"], Shown(search));
        Assert.Equal(SortKind.Name, search.SortField.Kind);
        Assert.True(search.IsListMode);
        Assert.Equal("夏の衣装", search.CurrentSavedName);
    });

    [Fact]
    public Task 既定の表示順で保存した物を呼び出すと_今の表示順も既定に戻る() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        await search.SaveCurrentSearchAsync("夏");

        SortByName(search);
        Row(search, "夏").ApplyCommand.Execute(null);

        Assert.Equal(SearchViewModel.DefaultSort.Label, search.Sort.Label);
        Assert.False(search.IsListMode);
    });

    [Fact]
    public Task 今は無い表示順で保存した物を呼び出すと_既定の順で並べて窓で言い_今は無いタグの値は条件に残す() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);

        search.ApplySaved(new SearchHistoryEntry
        {
            Name = "消えた属性",
            Sort = "消した属性 が高い順",
            Modules = [new SearchModuleState { Kind = "Category", Items = ["消したカテゴリ"] }],
        });

        Assert.Equal(SearchViewModel.DefaultSort.Label, search.Sort.Label);
        var notice = Assert.Single(app.Notices);
        Assert.Contains("消した属性 が高い順", notice.Text);
        Assert.Equal(MessageBoxImage.Warning, notice.Icon);

        // 黙って落とすと保存したときより広い結果が出る。値は条件に見えたまま、0件になる
        Assert.Equal(["消したカテゴリ"], ((ListModule)search.Modules.Single(module => module.Kind == SearchModuleKind.Category)).Chips.Select(chip => chip.Key));
        Assert.Empty(Shown(search));
    });

    [Fact]
    public Task 上書きすると_名前と場所はそのままで中身が今の検索になる_同じ検索のときは押せない() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        await search.SaveCurrentSearchAsync("A");
        search.QueryText = "冬";
        await search.SaveCurrentSearchAsync("B");
        Assert.False(Row(search, "B").OverwriteCommand.CanExecute(null));

        search.QueryText = "コート";
        Assert.True(Row(search, "A").OverwriteCommand.CanExecute(null));
        app.Answer = _ => MessageBoxResult.OK;
        await search.OverwriteSavedAsync("A");

        Assert.Equal(["A", "B"], StoredNames(app));
        Assert.Equal("コート", app.Store.SavedSearches.Load().Entries[0].Text);
        Assert.Equal("A", search.CurrentSavedName);
        Assert.Contains("戻せません", app.Notices.Last().Text);
    });

    [Fact]
    public Task 上書きと削除は_確認でキャンセルすると何も書かない() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        await search.SaveCurrentSearchAsync("A");
        search.QueryText = "冬";

        await search.OverwriteSavedAsync("A");
        await search.DeleteSavedAsync("A");

        Assert.Equal(2, app.Notices.Count);
        Assert.All(app.Notices, notice => Assert.Equal(MessageBoxResult.Cancel, notice.DefaultResult));
        var stored = Assert.Single(app.Store.SavedSearches.Load().Entries);
        Assert.Equal("夏", stored.Text);
    });

    [Fact]
    public Task 名前を変えると_書き換わり_ほかの行と同じ名前には変えない() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        await search.SaveCurrentSearchAsync("A");
        search.QueryText = "冬";
        await search.SaveCurrentSearchAsync("B");

        await search.RenameSavedAsync("A", " 夏の物 ");
        await search.RenameSavedAsync("B", "夏の物");

        Assert.Equal(["夏の物", "B"], StoredNames(app));
        Assert.Equal(["夏の物", "B"], Names(search));
    });

    [Fact]
    public Task 削除すると行が消える() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        await search.SaveCurrentSearchAsync("A");
        search.QueryText = "冬";
        await search.SaveCurrentSearchAsync("B");
        app.Answer = _ => MessageBoxResult.OK;

        await search.DeleteSavedAsync("A");

        Assert.Equal(["B"], StoredNames(app));
        Assert.Equal(["B"], Names(search));
        Assert.Contains("元に戻せません", app.Notices.Single().Text);
    });

    [Fact]
    public Task 上へ下へで並べ替え_端の行はその向きに動かせない() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        foreach (var name in new[] { "A", "B", "C" })
        {
            search.QueryText = name;
            await search.SaveCurrentSearchAsync(name);
        }

        Assert.False(Row(search, "A").MoveUpCommand.CanExecute(null));
        Assert.False(Row(search, "C").MoveDownCommand.CanExecute(null));

        await search.MoveSavedAsync("C", -1);
        await search.MoveSavedAsync("A", +1);

        Assert.Equal(["C", "A", "B"], StoredNames(app));
        Assert.Equal(["C", "A", "B"], Names(search));
    });

    [Fact]
    public Task 今の検索と同じ行だけに印が付き_カードとリストが違えば同じではない() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        await search.SaveCurrentSearchAsync("夏");
        search.QueryText = "冬";
        await search.SaveCurrentSearchAsync("冬");

        Assert.Equal([false, true], search.SavedRows.Select(row => row.IsCurrent));
        Assert.Equal("冬", search.CurrentSavedName);

        search.IsListMode = true;
        Assert.All(search.SavedRows, row => Assert.False(row.IsCurrent));
        Assert.Null(search.CurrentSavedName);

        search.IsListMode = false;
        search.QueryText = "夏";
        Assert.Equal("夏", search.CurrentSavedName);
    });

    [Fact]
    public Task 保存の小窓は_今の条件の要約を名前に入れて始まり_同じ名前は既にありますで止める() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        Assert.Equal(string.Empty, search.SavedNamePrefill());

        search.QueryText = "夏";
        UseCategory(search, "衣装");
        Assert.Equal("夏 / カテゴリ：衣装", search.SavedNamePrefill());

        var dialog = new SavedSearchNameDialogViewModel(SavedSearchNameDialogViewModel.Purpose.Save, search.SavedNamePrefill(), ["夏の衣装"]);
        Assert.Equal("条件、検索文字列、表示順などが保存されます。", dialog.Note);
        Assert.True(dialog.CanCommit);
        dialog.Name = " 夏の衣装 ";
        Assert.True(dialog.IsTaken);
        Assert.False(dialog.CanCommit);
        dialog.Name = "  ";
        Assert.False(dialog.IsTaken);
        Assert.False(dialog.CanCommit);
    });

    [Fact]
    public Task 多いときだけ探す欄を出し_名前か要約に含む行だけを並べる() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        for (var index = 1; index <= SearchViewModel.SavedSearchVisibleRows; index++)
        {
            search.QueryText = $"語{index}";
            await search.SaveCurrentSearchAsync($"検索{index}");
        }

        Assert.False(search.ShowsSavedFilter);

        search.QueryText = "コート";
        await search.SaveCurrentSearchAsync("冬物");
        Assert.True(search.ShowsSavedFilter);

        search.SavedFilter = "こーと";
        Assert.Equal(["冬物"], Names(search));
        search.SavedFilter = "無い語";
        Assert.Empty(search.SavedRows);
        Assert.True(search.ShowsSavedFilterEmpty);
    });

    [Fact]
    public Task 今の検索と同じ物があるとき_ボタンの印が出て_呼び出すと一覧を閉じる合図が出る() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        Assert.False(search.ShowsCurrentSaved);
        await search.SaveCurrentSearchAsync("夏");
        Assert.True(search.ShowsCurrentSaved);

        search.QueryText = "冬";
        Assert.False(search.ShowsCurrentSaved);

        var closed = 0;
        search.SavedApplied += () => closed++;
        Row(search, "夏").ApplyCommand.Execute(null);
        Assert.Equal(1, closed);
        Assert.True(search.ShowsCurrentSaved);
    });

    [Fact]
    public Task 上書きの確認の窓は_条件を上書きと言う() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        await search.SaveCurrentSearchAsync("A");
        search.QueryText = "冬";
        await search.OverwriteSavedAsync("A");

        var notice = Assert.Single(app.Notices);
        Assert.Equal("条件を上書き", notice.Caption);
        Assert.Contains("条件を上書きします", notice.Text);
    });

    [Fact]
    public Task 手で直したJSONの欠けた配列とnullの行を読める() => TestApp.Run(async app =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(app.Store.Paths.SavedSearchesFile)!);
        await File.WriteAllTextAsync(app.Store.Paths.SavedSearchesFile, """
            {
              "entries": [
                { "name": "配列なし", "text": "夏", "modules": null, "targets": null, "view": "list" },
                null,
                { "name": "条件だけ", "modules": [ { "kind": "Category", "enabled": true, "items": null } ] }
              ]
            }
            """);

        var search = await StartAsync(app);

        Assert.Equal(["配列なし", "条件だけ"], Names(search));
        Row(search, "配列なし").ApplyCommand.Execute(null);
        Assert.Equal("夏", search.QueryText);
        Assert.True(search.IsListMode);
    });

    [Fact]
    public Task 保存したファイルは_計算で出せる値を書かず_カードかリストかを語で書く() => TestApp.Run(async app =>
    {
        var search = await StartAsync(app);
        search.QueryText = "夏";
        search.IsListMode = true;
        await search.SaveCurrentSearchAsync("夏");

        var json = await File.ReadAllTextAsync(app.Store.Paths.SavedSearchesFile);
        Assert.Contains("\"view\": \"list\"", json);
        Assert.Contains("\"name\": \"夏\"", json);
        Assert.DoesNotContain("fingerprint", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isNamed", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"summary\": \"夏\"", json);
    });

    /// <summary>
    /// 編集で新しいカテゴリを入れたら、置いてある条件の候補にも出る（外部の点検 2026-10-07。前は全件を読み直すまで古いままだった）。
    /// </summary>
    [Fact]
    public Task 編集で新しいカテゴリを入れたら_置いてある条件の候補に出る() => TestApp.Run(async app =>
    {
        var item = Make.Item("9901101", "作り物の衣装") with { Local = new LocalBlock { Category = "前のカテゴリ" } };
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        var search = main.Search;
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Category);

        search.NoteItemChanged(item with { Local = item.Local with { Category = "新しいカテゴリ" } });
        await app.SettleAsync();
        module.AddCommand.Execute("新しいカテゴリ");

        Assert.Contains(module.Chips, chip => chip.Text == "新しいカテゴリ");
    });
}
