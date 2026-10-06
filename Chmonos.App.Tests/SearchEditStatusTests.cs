using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の条件「編集状況」（2026-10-01。前の「未編集」）。三項「未入力のみ／入力済みのみ／両方」と、どの項目を見るか。
/// </summary>
public class SearchEditStatusTests
{
    private static readonly SearchModuleContext Context = new(
        () => throw new InvalidOperationException("使わない"),
        ModificationUsage.Empty,
        RecentTimes.Empty,
        null,
        DateTimeOffset.Now);

    private static ItemRecord With(string id, bool tags = false, bool memo = false)
    {
        var item = Make.Item(id, "作り物");
        return item with
        {
            Local = item.Local with
            {
                UserTags = tags ? [new UserTagAssignment { Top = "衣装" }] : [],
                Memo = memo ? "作り物のメモ" : null,
            },
        };
    }

    /// <summary>1: どちらも未入力・2: タグだけ・3: メモだけ・4: 両方・5: どちらも未入力だが③待ち。</summary>
    private static readonly ItemRecord[] Items =
        [With("1"), With("2", tags: true), With("3", memo: true), With("4", tags: true, memo: true), With("5")];

    private static UneditedModule Module(string choice, bool matchAll, params string[] fields)
    {
        var module = new UneditedModule(item => item.Id == "5");
        module.Load(new SearchModuleState { Kind = "Unedited", Choice = choice, MatchAll = matchAll, Fields = fields });
        return module;
    }

    private static string[] Passing(SearchModule module)
        => Items.Where(item => module.Passes(item, Context)).Select(item => item.Id).ToArray();

    [Fact]
    public Task 既定はユーザータグが未入力で_前の未編集と同じ() => UiThread.Run(() =>
    {
        var module = new UneditedModule(item => item.Id == "5");

        Assert.Equal("編集状況", module.Label);
        Assert.Equal([EditField.UserTags], module.SelectedFields);
        Assert.Equal(["1", "3"], Passing(module));
        Assert.Equal("編集状況：ユーザータグが未入力", module.SummaryText);
    });

    [Fact]
    public Task 項目を2つ入れると_既定はどれかが未入力_すべてにすると両方未入力() => UiThread.Run(() =>
    {
        var any = Module("unedited", matchAll: false, "userTags", "memo");
        var all = Module("unedited", matchAll: true, "userTags", "memo");

        Assert.Equal(["1", "2", "3"], Passing(any));
        Assert.Equal(["1"], Passing(all));
        Assert.Equal("編集状況：ユーザータグ・メモのどれかが未入力", any.SummaryText);
        Assert.Equal("編集状況：ユーザータグ・メモがすべて未入力", all.SummaryText);
    });

    [Fact]
    public Task 入力済みのみは未入力のみの反対で_取り込み待ちも出る() => UiThread.Run(() =>
    {
        var any = Module("edited", matchAll: false, "userTags", "memo");
        var all = Module("edited", matchAll: true, "userTags", "memo");

        Assert.Equal(["4"], Passing(any));
        Assert.Equal(["2", "3", "4"], Passing(all));
        Assert.Equal("編集状況：ユーザータグ・メモがすべて入力済み", any.SummaryText);
        Assert.Equal("編集状況：ユーザータグ・メモのどれかが入力済み", all.SummaryText);
        // つなぎ方の2つの選択肢は、選んだときに出る物を文で言う（入力済みのみは未入力のみの反対）
        Assert.Equal("すべて入力済み", any.MatchAnyLabel);
        Assert.Equal("どれかが入力済み", any.MatchAllLabel);
        Assert.True(any.MatchAny);
        Assert.False(all.MatchAny);
    });

    [Fact]
    public Task 両方は何も絞らない() => UiThread.Run(() =>
    {
        var module = Module("both", matchAll: false, "memo");

        Assert.False(module.IsActive);
        Assert.Equal(5, Passing(module).Length);
    });

    [Fact]
    public Task 両方の間は項目とつなぎ方を押せず_値は残って_戻すとそのまま効く() => UiThread.Run(() =>
    {
        // メモ2-④ 2026-10-02：両方は何も絞らないので、項目を変えても意味が無い
        var module = Module("unedited", matchAll: true, "userTags", "memo");
        module.Selected = module.Options.First(option => option.Key == "both");

        Assert.False(module.CanEditFields);
        Assert.True(module.CanChooseMatchMode);

        module.Fields.First(toggle => toggle.Field == EditField.Attributes).IsOn = true;
        module.Fields.First(toggle => toggle.Field == EditField.Memo).IsOn = false;
        module.MatchAny = true;

        Assert.Equal([EditField.UserTags, EditField.Memo], module.SelectedFields);
        Assert.True(module.MatchAll);

        module.Selected = module.Options.First(option => option.Key == "unedited");

        Assert.True(module.CanEditFields);
        Assert.Equal(["1"], Passing(module));
    });

    [Fact]
    public Task 項目の最後の1つは外せない() => UiThread.Run(() =>
    {
        var module = Module("unedited", matchAll: false, "userTags", "memo");
        var tags = module.Fields.First(toggle => toggle.Field == EditField.UserTags);
        var memo = module.Fields.First(toggle => toggle.Field == EditField.Memo);

        tags.IsOn = false;
        memo.IsOn = false;

        Assert.False(tags.IsOn);
        Assert.True(memo.IsOn);
        Assert.Equal([EditField.Memo], module.SelectedFields);
        Assert.False(module.CanChooseMatchMode);
    });

    [Fact]
    public Task 項目は状態に欄の名前で書かれ_戻すと同じ() => UiThread.Run(() =>
    {
        var module = Module("unedited", matchAll: true, "memo", "attributes");

        var state = module.Save();
        var restored = Module("both", matchAll: false);
        restored.Load(state);

        Assert.Equal(["attributes", "memo"], state.Fields);
        Assert.Equal([EditField.Attributes, EditField.Memo], restored.SelectedFields);
        Assert.True(restored.MatchAll);
        Assert.False(restored.SupportsExclude);
    });

    [Fact]
    public Task 項目と選択肢の件数は_他の条件のもとで数え_取り込み待ちは未入力に数えない() => UiThread.Run(() =>
    {
        var module = Module("unedited", matchAll: false, "userTags", "memo");

        module.RefreshCounts(Items, Context);

        Assert.Equal("ユーザータグ（2）", module.Fields.First(toggle => toggle.Field == EditField.UserTags).Display);
        Assert.Equal("メモ（2）", module.Fields.First(toggle => toggle.Field == EditField.Memo).Display);
        Assert.Equal(["未入力のみ（3）", "入力済みのみ（1）", "両方（5）"], module.Options.Select(option => option.Display));
    });

    [Fact]
    public Task 条件を足すとメニューと見出しは編集状況() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var search = (await app.StartAsync()).Search;

        var module = SearchModuleMenuTests.Add(search, SearchModuleKind.Unedited);

        Assert.IsType<UneditedModule>(module);
        Assert.Equal("編集状況", module.Label);
        Assert.Contains(search.ModuleMenu.SelectMany(heading => heading.Entries).OfType<SearchModuleMenuEntry>(), entry => entry.Label == "編集状況");
        Assert.Equal(["1000001"], search.ListItems.Select(card => card.Item.Id));
    });
}
