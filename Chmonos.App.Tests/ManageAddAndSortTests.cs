using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// タグの管理・属性の管理の、検索と追加を1本にした左の欄（追加の行の出し分け・Enter・探す物）・メモでの検索・
/// 並べ替え（「候補の並べ替え」のときだけ動く）。メモ10-①③⑤ 2026-10-02、ユーザ指示 2026-10-02。
/// </summary>
public class ManageAddAndSortTests
{
    private static async Task<TagManageViewModel> OpenTagsAsync(TestApp app, UserTagMaster master)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        await app.Store.UserTags.SaveAsync(master);
        var main = await app.StartAsync();
        main.ShowTagManageCommand.Execute(null);
        var tags = Assert.IsType<TagManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => tags.TopCount > 0, "タグの管理の読み込みが済む");
        return tags;
    }

    private static async Task<AttributeManageViewModel> OpenAttributesAsync(TestApp app, AttributeMaster master)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        await app.Store.Attributes.SaveAsync(master);
        var main = await app.StartAsync();
        main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => attributes.Rows.Count > 0, "属性の管理の読み込みが済む");
        return attributes;
    }

    private static UserTagMaster TwoTops() => new()
    {
        Tops =
        [
            new UserTagTop { Name = "作り物の甲", Subs = [new UserTagSub { Name = "小の一", Memo = "季節の物を入れる" }, new UserTagSub { Name = "小の二" }] },
            new UserTagTop { Name = "作り物の乙" },
        ],
    };

    // ---- 検索と追加を1本にした欄 ----

    [Fact]
    public Task 大分類の欄は_打った名前が無ければ追加の行を出し_押すと足して選び欄を空ける() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app, TwoTops());

        tags.FilterText = "作り物の丙";
        Assert.True(tags.CanAddTyped);
        Assert.Equal("「作り物の丙」を大分類に追加", tags.AddTopRowText);

        tags.AddTopCommand.Execute(null);
        await UiThread.Until(() => tags.TopCount == 3, "足した大分類が並ぶ");

        Assert.Equal("「作り物の丙」を追加しました。", tags.StatusText);
        Assert.Equal(string.Empty, tags.FilterText);
        Assert.Equal("作り物の丙", tags.Selected?.Name);
        Assert.False(tags.CanAddTyped);
    });

    [Fact]
    public Task 大分類の欄は_同じ名前が有れば追加の行を出さず_その行を強調しEnterで選ぶ() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app, TwoTops());
        tags.Selected = tags.Tops.Single(row => row.Name == "作り物の甲");

        // 前後の空白は除いて照らす
        tags.FilterText = "  作り物の乙 ";

        Assert.False(tags.CanAddTyped);
        Assert.Equal(string.Empty, tags.AddTopRowText);
        Assert.True(tags.Tops.Single(row => row.Name == "作り物の乙").IsNameMatch);

        tags.SubmitFilterCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("作り物の乙", tags.Selected?.Name);
        Assert.Equal(2, app.Store.UserTags.Load().Tops.Count);
    });

    [Fact]
    public Task 大分類の欄は_Enterで足せるときは足す_空のときは何も出さない() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app, TwoTops());

        tags.FilterText = "   ";
        Assert.False(tags.CanAddTyped);

        tags.FilterText = "作り物の丙";
        tags.SubmitFilterCommand.Execute(null);
        await UiThread.Until(() => tags.TopCount == 3, "足した大分類が並ぶ");

        Assert.Equal("作り物の丙", tags.Selected?.Name);
        Assert.Equal(string.Empty, tags.FilterText);
    });

    [Fact]
    public Task 大分類の検索は_大分類と小分類の名前とメモのどれでも当たる() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app, new UserTagMaster
        {
            Tops =
            [
                new UserTagTop { Name = "作り物の甲", Memo = "大のメモ", Subs = [new UserTagSub { Name = "小の一", Memo = "小のメモ" }] },
                new UserTagTop { Name = "作り物の乙", Subs = [new UserTagSub { Name = "小の二" }] },
                new UserTagTop { Name = "作り物の丙" },
            ],
        });

        tags.FilterText = "大のメモ";
        Assert.Equal(["作り物の甲"], tags.Tops.Select(row => row.Name));

        tags.FilterText = "小のメモ";
        Assert.Equal(["作り物の甲"], tags.Tops.Select(row => row.Name));

        tags.FilterText = "小の二";
        Assert.Equal(["作り物の乙"], tags.Tops.Select(row => row.Name));

        tags.FilterText = "作り物の丙";
        Assert.Equal(["作り物の丙"], tags.Tops.Select(row => row.Name));
    });

    [Fact]
    public Task 属性の欄も_打った名前が無ければ追加の行を出し_押すと足して選び欄を空ける() => TestApp.Run(async app =>
    {
        var attributes = await OpenAttributesAsync(app, new AttributeMaster { Attributes = [new AttributeDefinition { Name = "作り物の甲" }] });

        attributes.FilterText = "作り物の乙";
        Assert.True(attributes.CanAddTyped);
        Assert.Equal("「作り物の乙」を属性に追加", attributes.AddRowText);

        attributes.AddCommand.Execute(null);
        await UiThread.Until(() => attributes.Rows.Count == 2, "足した属性が並ぶ");

        Assert.Equal("「作り物の乙」を追加しました。", attributes.StatusText);
        Assert.Equal(string.Empty, attributes.FilterText);
        Assert.Equal("作り物の乙", attributes.Selected?.Name);
    });

    [Fact]
    public Task 属性の欄も_同じ名前が有れば追加の行を出さず_その行を強調しEnterで選ぶ() => TestApp.Run(async app =>
    {
        var attributes = await OpenAttributesAsync(app, new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "作り物の甲" }, new AttributeDefinition { Name = "作り物の乙" }],
        });
        attributes.Selected = attributes.Rows.Single(row => row.Name == "作り物の甲");

        attributes.FilterText = "作り物の乙";

        Assert.False(attributes.CanAddTyped);
        Assert.True(attributes.Rows.Single(row => row.Name == "作り物の乙").IsNameMatch);

        attributes.SubmitFilterCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("作り物の乙", attributes.Selected?.Name);
        Assert.Equal(2, app.Store.Attributes.Load().Attributes.Count);
    });

    // ---- メモでも探す ----

    [Fact]
    public Task 小分類の検索は_名前に無くてもメモに当たれば当たる() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app, TwoTops());
        tags.Selected = tags.Tops.Single(row => row.Name == "作り物の甲");
        await UiThread.Until(() => tags.Subs.Count == 2, "小分類が並ぶ");

        tags.ItemFilter = "季節";

        Assert.False(tags.Subs.Single(row => row.Name == "小の一").IsHidden);
        Assert.True(tags.Subs.Single(row => row.Name == "小の二").IsHidden);
    });

    [Fact]
    public Task 属性の検索は_名前に無くてもメモに当たれば当たる() => TestApp.Run(async app =>
    {
        var attributes = await OpenAttributesAsync(app, new AttributeMaster
        {
            Attributes =
            [
                new AttributeDefinition { Name = "作り物の甲", Memo = "季節の基準" },
                new AttributeDefinition { Name = "作り物の乙" },
            ],
        });

        attributes.FilterText = "季節";

        Assert.Equal(["作り物の甲"], attributes.Rows.Select(row => row.Name));
    });

    // ---- 並べ替えは「候補の並べ替え」のときだけ ----

    [Fact]
    public Task 大分類のドラッグは_名前順では動かさず_並べ方も切り替えない() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app, TwoTops());
        tags.Sort = TagSortMode.Name;
        await app.SettleAsync();
        var before = app.Store.UserTags.Load().Tops.Select(top => top.Name).ToList();

        await tags.MoveTopAsync(tags.Tops.Single(row => row.Name == "作り物の乙"), tags.Tops.Single(row => row.Name == "作り物の甲"), after: false);

        Assert.Equal(TagSortMode.Name, tags.Sort);
        Assert.Equal(before, app.Store.UserTags.Load().Tops.Select(top => top.Name));
    });

    [Fact]
    public Task 大分類のドラッグは_候補の並べ替えのときだけ動く() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app, TwoTops());
        tags.Sort = TagSortMode.Manual;
        await app.SettleAsync();

        await tags.MoveTopAsync(tags.Tops.Single(row => row.Name == "作り物の乙"), tags.Tops.Single(row => row.Name == "作り物の甲"), after: false);

        Assert.Equal(["作り物の乙", "作り物の甲"], app.Store.UserTags.Load().Tops.Select(top => top.Name));
    });

    [Fact]
    public Task 小分類のドラッグも_名前順では動かさない() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app, TwoTops());
        tags.Sort = TagSortMode.Name;
        tags.Selected = tags.Tops.Single(row => row.Name == "作り物の甲");
        await UiThread.Until(() => tags.Subs.Count == 2, "小分類が並ぶ");

        await tags.MoveSubAsync(tags.Subs.Single(row => row.Name == "小の二"), tags.Subs.Single(row => row.Name == "小の一"), after: false);

        Assert.Equal(TagSortMode.Name, tags.Sort);
        Assert.Equal(["小の一", "小の二"], app.Store.UserTags.Load().Tops.Single(top => top.Name == "作り物の甲").Subs.Select(sub => sub.Name));
    });

    [Fact]
    public Task 属性のドラッグは_名前順では動かさず_候補の並べ替えのときだけ動く() => TestApp.Run(async app =>
    {
        var attributes = await OpenAttributesAsync(app, new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "作り物の甲" }, new AttributeDefinition { Name = "作り物の乙" }],
        });
        attributes.Sort = TagSortMode.Name;
        await app.SettleAsync();
        // 名前順で並べ直された今のファイルの並び（文字の並びは照合順なので、決め打ちしない）
        var before = app.Store.Attributes.Load().Attributes.Select(entry => entry.Name).ToList();
        var first = attributes.Rows.Single(row => row.Name == before[0]);
        var second = attributes.Rows.Single(row => row.Name == before[1]);

        await attributes.MoveAsync(second, first, after: false);
        Assert.Equal(TagSortMode.Name, attributes.Sort);
        Assert.Equal(before, app.Store.Attributes.Load().Attributes.Select(entry => entry.Name));

        attributes.Sort = TagSortMode.Manual;
        await app.SettleAsync();
        await attributes.MoveAsync(second, first, after: false);
        Assert.Equal([before[1], before[0]], app.Store.Attributes.Load().Attributes.Select(entry => entry.Name));
    });
}
