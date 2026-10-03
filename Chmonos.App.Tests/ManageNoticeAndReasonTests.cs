using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// タグの管理・属性の管理の左の欄：「追加」を押して同じ名前があったときの「既にあります」（欄のすぐ下）と、
/// 大分類・属性の検索の当たり方（名前とメモだけ・何で当たったかを出す）。メモ21-①② 2026-10-03。
/// </summary>
public class ManageNoticeAndReasonTests
{
    private static UserTagMaster Master() => new()
    {
        Tops =
        [
            new UserTagTop { Name = "作り物の甲", Memo = "季節の物", Subs = [new UserTagSub { Name = "小の一" }, new UserTagSub { Name = "小の二", Memo = "夏向き" }] },
            new UserTagTop { Name = "作り物の乙", Subs = [new UserTagSub { Name = "小の三" }] },
        ],
    };

    private static async Task<TagManageViewModel> OpenTagsAsync(TestApp app)
    {
        // 作り物の商品に作り物の大分類を付けておく（商品の名前では当たらないことを確かめるため）
        var item = Make.Item("1000001", "ひみつの商品名");
        item = item with { Local = item.Local with { UserTags = [new UserTagAssignment { Top = "作り物の乙" }] } };
        await app.AddItemAsync(item);
        await app.Store.UserTags.SaveAsync(Master());
        var main = await app.StartAsync();
        main.ShowTagManageCommand.Execute(null);
        var tags = Assert.IsType<TagManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => tags.TopCount > 0, "タグの管理の読み込みが済む");
        return tags;
    }

    [Fact]
    public Task 大分類の追加を同じ名前で押すと_欄の下に既にありますを出し_右上の状態の文は変えない() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app);
        var before = tags.StatusText;

        tags.FilterText = "作り物の甲";
        tags.AddTopCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("「作り物の甲」は既にあります。", tags.AddNoticeText);
        Assert.Equal(before, tags.StatusText);
        Assert.Equal("作り物の甲", tags.Selected?.Name);
        Assert.Equal(2, app.Store.UserTags.Load().Tops.Count);

        // 打ち直すと消える
        tags.FilterText = "作り物の甲あ";
        Assert.Equal(string.Empty, tags.AddNoticeText);
    });

    [Fact]
    public Task 属性の追加を同じ名前で押すと_欄の下に既にありますを出し_打ち直すと消える() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "ひみつの商品名"));
        await app.Store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "作り物の甲" }] });
        var main = await app.StartAsync();
        main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => attributes.Rows.Count > 0, "属性の管理の読み込みが済む");

        attributes.FilterText = "作り物の甲";
        attributes.AddCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("「作り物の甲」は既にあります。", attributes.AddNoticeText);

        attributes.FilterText = string.Empty;
        Assert.Equal(string.Empty, attributes.AddNoticeText);
    });

    [Fact]
    public Task 小分類の追加を同じ名前で押すと_欄の下に既にありますを出し_右上の状態の文は変えない_足せたら欄を空ける() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app);
        tags.Selected = tags.Tops.First(row => row.Name == "作り物の甲");
        var before = tags.StatusText;

        tags.NewSubText = "小の一";
        tags.AddSubCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("「小の一」は既にあります。", tags.AddSubNoticeText);
        Assert.Equal(before, tags.StatusText);
        Assert.Equal(2, app.Store.UserTags.Load().Tops.First(top => top.Name == "作り物の甲").Subs.Count);

        // 打ち直すと消える
        tags.NewSubText = "小の四";
        Assert.Equal(string.Empty, tags.AddSubNoticeText);

        tags.AddSubCommand.Execute(null);
        await UiThread.Until(() => tags.NewSubText.Length == 0, "足せたら欄が空く");
        Assert.Equal(3, app.Store.UserTags.Load().Tops.First(top => top.Name == "作り物の甲").Subs.Count);
        Assert.Equal(string.Empty, tags.AddSubNoticeText);
    });

    [Fact]
    public Task 大分類の検索は_付けた商品の名前では当たらない() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app);

        tags.FilterText = "ひみつ";

        Assert.Empty(tags.Tops);
    });

    [Fact]
    public Task 大分類の検索は_名前で当たれば理由を出さず_メモと小分類で当たれば理由を出す() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app);

        tags.FilterText = "作り物の甲";
        Assert.Equal(string.Empty, Assert.Single(tags.Tops).MatchReason);

        tags.FilterText = "季節";
        Assert.Equal("メモ", Assert.Single(tags.Tops).MatchReason);
        Assert.Equal("・メモ", tags.Tops[0].MatchReasonText);

        tags.FilterText = "小の三";
        var hit = Assert.Single(tags.Tops);
        Assert.Equal("作り物の乙", hit.Name);
        Assert.Equal("小分類「小の三」", hit.MatchReason);

        // 小分類のメモに当たったときも小分類で言う
        tags.FilterText = "夏向き";
        Assert.Equal("小分類「小の二」", Assert.Single(tags.Tops).MatchReason);

        tags.FilterText = string.Empty;
        Assert.All(tags.Tops, row => Assert.Equal(string.Empty, row.MatchReason));
    });

    [Fact]
    public Task 小分類で当たった大分類を開くと_当たった小分類だけが強調される() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app);

        tags.FilterText = "小の二";
        tags.Selected = Assert.Single(tags.Tops);
        await UiThread.Until(() => tags.Subs.Count == 2, "小分類が並ぶ");

        Assert.False(tags.Subs.Single(row => row.Name == "小の一").IsFilterMatch);
        Assert.True(tags.Subs.Single(row => row.Name == "小の二").IsFilterMatch);

        // 語を消すと印も消える
        tags.FilterText = string.Empty;
        Assert.All(tags.Subs, row => Assert.False(row.IsFilterMatch));
    });

    [Fact]
    public Task 属性の検索は_付けた商品の名前では当たらず_メモで当たれば理由を出す() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "ひみつの商品名"));
        await app.Store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "作り物の甲", Memo = "季節の物" }, new AttributeDefinition { Name = "作り物の乙" }],
        });
        var main = await app.StartAsync();
        main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => attributes.Rows.Count > 0, "属性の管理の読み込みが済む");

        attributes.FilterText = "ひみつ";
        Assert.Empty(attributes.Rows);

        attributes.FilterText = "季節";
        Assert.Equal("メモ", Assert.Single(attributes.Rows).MatchReason);

        attributes.FilterText = "作り物の乙";
        Assert.Equal(string.Empty, Assert.Single(attributes.Rows).MatchReason);
    });
}
