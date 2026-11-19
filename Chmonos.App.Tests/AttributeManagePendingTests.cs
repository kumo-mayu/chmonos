using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 属性の管理の件数と削除の確かめに、付いているが値はまだの商品も数える（ユーザ判断 2026-11-20）。
/// 失う値があるのは値のある商品だけなので、値はまだだけなら「評価は戻りません」とは言わない。
/// </summary>
public class AttributeManagePendingTests
{
    private static LocalBlock Rated(string name, int value) => new() { Attributes = new Dictionary<string, int> { [name] = value } };

    private static LocalBlock Pending(string name) => new() { PendingAttributes = [name] };

    [Fact]
    public Task 件数に値はまだの商品も数え_値のある商品の数と平均は変えない() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "混在" }, new AttributeDefinition { Name = "まだだけ" }],
        });
        await app.AddItemAsync(Make.Item("9901001", "甲") with { Local = Rated("混在", 40) });
        await app.AddItemAsync(Make.Item("9901002", "乙") with { Local = Pending("混在") });
        await app.AddItemAsync(Make.Item("9901003", "丙") with { Local = Pending("まだだけ") });
        await app.AddItemAsync(Make.Item("9901004", "丁") with { Local = Pending("まだだけ") });
        var main = await app.StartAsync();
        main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        await app.SettleAsync();

        var mixed = attributes.Rows.Single(row => row.Name == "混在");
        Assert.Equal((1, 1, 2), (mixed.ItemCount, mixed.PendingCount, mixed.AttachedCount));
        Assert.Equal("2", mixed.ItemCountText);
        Assert.Equal(40, mixed.Average);

        var pendingOnly = attributes.Rows.Single(row => row.Name == "まだだけ");
        Assert.Equal((0, 2, 2), (pendingOnly.ItemCount, pendingOnly.PendingCount, pendingOnly.AttachedCount));
        Assert.Equal("2", pendingOnly.ItemCountText);
        Assert.Null(pendingOnly.Average);

        attributes.Selected = mixed;
        Assert.Contains("2 件の商品に付いています", attributes.SelectedUsageText);
        Assert.Contains("値はまだ 1 件", attributes.SelectedUsageText);
        attributes.Selected = pendingOnly;
        Assert.Contains("値はまだ 2 件", attributes.SelectedUsageText);
    });

    [Fact]
    public Task 削除の確かめは_値はまだも件数に入れ_値が無ければ評価は戻らないと言わない() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "混在" }, new AttributeDefinition { Name = "まだだけ" }],
        });
        await app.AddItemAsync(Make.Item("9901001", "甲") with { Local = Rated("混在", 40) });
        await app.AddItemAsync(Make.Item("9901002", "乙") with { Local = Pending("混在") });
        await app.AddItemAsync(Make.Item("9901003", "丙") with { Local = Pending("まだだけ") });
        var main = await app.StartAsync();
        main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        await app.SettleAsync();

        var mixed = AttributeManageViewModel.DeleteMessage(attributes.Rows.Single(row => row.Name == "混在"));
        Assert.Contains("2 件の商品から、この属性が外れます", mixed);
        Assert.Contains("うち 1 件は評価が消えます", mixed);
        Assert.Contains("1 件ぶんの評価は戻りません", mixed);

        var pendingOnly = AttributeManageViewModel.DeleteMessage(attributes.Rows.Single(row => row.Name == "まだだけ"));
        Assert.Contains("1 件の商品から、この属性が外れます", pendingOnly);
        Assert.DoesNotContain("評価は戻りません", pendingOnly);
    });
}
