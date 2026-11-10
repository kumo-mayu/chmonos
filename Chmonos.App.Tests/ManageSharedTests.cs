using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// タグの管理と属性の管理が同じ土台で持つ動き（一覧に無い名前の直し・開き直したときの選び直し・並べ方の既定・
/// 中の商品の段）。2つの画面を1つの土台に寄せる前に、両方の今の動きを書き留めておく（R3 2026-11-10）。
/// </summary>
public class ManageSharedTests
{
    private static ItemRecord TaggedItem(string top, params string[] subs)
    {
        var item = Make.Item("1000001", "作り物の衣装");
        return item with { Local = item.Local with { UserTags = [new UserTagAssignment { Top = top, Subs = subs }] } };
    }

    private static ItemRecord RatedItem(string attribute, int value)
    {
        var item = Make.Item("1000001", "作り物の衣装");
        return item with { Local = item.Local with { Attributes = new Dictionary<string, int> { [attribute] = value } } };
    }

    private static async Task<TagManageViewModel> OpenTagsAsync(TestApp app, ItemRecord item, UserTagMaster master)
    {
        await app.AddItemAsync(item);
        await app.Store.UserTags.SaveAsync(master);
        var main = await app.StartAsync();
        main.ShowTagManageCommand.Execute(null);
        var tags = Assert.IsType<TagManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => tags.TopCount > 0, "タグの管理の読み込みが済む");
        return tags;
    }

    private static async Task<AttributeManageViewModel> OpenAttributesAsync(TestApp app, ItemRecord item, AttributeMaster master)
    {
        await app.AddItemAsync(item);
        await app.Store.Attributes.SaveAsync(master);
        var main = await app.StartAsync();
        main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => attributes.Rows.Count > 0, "属性の管理の読み込みが済む");
        return attributes;
    }

    private static UserTagMaster OneTop() => new() { Tops = [new UserTagTop { Name = "作り物の甲" }] };

    private static AttributeMaster OneAttribute() => new() { Attributes = [new AttributeDefinition { Name = "作り物の質感" }] };

    // ---- 一覧に無い名前の直し ----

    [Fact]
    public Task タグの一覧に無い大分類は_一覧に追加すると並び_一覧の見出しの近くに知らせる() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app, TaggedItem("外の大分類"), OneTop());
        await UiThread.Until(() => tags.HasOrphans, "一覧に無い大分類が出る");

        tags.Orphans.Single().AddToMasterCommand!.Execute(null);
        await UiThread.Until(() => tags.TopCount == 2 && !tags.HasOrphans, "一覧に入り、一覧に無い名前が消える");

        Assert.Equal("「外の大分類」を一覧に追加しました。1 件の商品が絞り込みに表示されるようになります。", tags.ListNotice.Text);
    });

    [Fact]
    public Task タグの一覧に無い大分類は_商品から外すと消え_一覧の見出しの近くに知らせる() => TestApp.Run(async app =>
    {
        app.Answer = _ => System.Windows.MessageBoxResult.OK;
        var tags = await OpenTagsAsync(app, TaggedItem("外の大分類"), OneTop());
        await UiThread.Until(() => tags.HasOrphans, "一覧に無い大分類が出る");

        tags.Orphans.Single().RemoveCommand!.Execute(null);
        await UiThread.Until(() => !tags.HasOrphans, "一覧に無い名前が消える");

        Assert.Equal(1, tags.TopCount);
        Assert.Equal("「外の大分類」を 1 件の商品から外しました。1 件はユーザータグが空になり、未編集に戻りました。", tags.ListNotice.Text);
    });

    /// <summary>
    /// 一覧に無い名前の操作も、できなかったときは一覧の見出しの近くに失敗を出す（ユーザ判断 2026-11-10。前は何も出なかった）。
    /// 商品の JSON を読み取り専用にして書き換えを失敗させる
    /// </summary>
    [Fact]
    public Task タグの一覧に無い大分類を外せなかったら_一覧の見出しの近くに失敗を出す() => TestApp.Run(async app =>
    {
        app.Answer = _ => System.Windows.MessageBoxResult.OK;
        app.AllowLoggedFailures = true;
        var tags = await OpenTagsAsync(app, TaggedItem("外の大分類"), OneTop());
        await UiThread.Until(() => tags.HasOrphans, "一覧に無い大分類が出る");

        var itemFile = app.Services.Paths.ItemFile("1000001");
        File.SetAttributes(itemFile, FileAttributes.ReadOnly);
        try
        {
            tags.Orphans.Single().RemoveCommand!.Execute(null);
            await UiThread.Until(() => tags.ListNotice.Text.Length > 0, "失敗が出る");
        }
        finally
        {
            File.SetAttributes(itemFile, FileAttributes.Normal);
        }

        Assert.True(tags.ListNotice.IsWarning);
        Assert.StartsWith("外せませんでした。", tags.ListNotice.Text, StringComparison.Ordinal);
    });

    [Fact]
    public Task 属性の一覧に無い属性を外せなかったら_一覧の見出しの近くに失敗を出す() => TestApp.Run(async app =>
    {
        app.Answer = _ => System.Windows.MessageBoxResult.OK;
        app.AllowLoggedFailures = true;
        var attributes = await OpenAttributesAsync(app, RatedItem("外の属性", 40), OneAttribute());
        await UiThread.Until(() => attributes.HasOrphans, "一覧に無い属性が出る");

        var itemFile = app.Services.Paths.ItemFile("1000001");
        File.SetAttributes(itemFile, FileAttributes.ReadOnly);
        try
        {
            attributes.Orphans.Single().RemoveCommand!.Execute(null);
            await UiThread.Until(() => attributes.ListNotice.Text.Length > 0, "失敗が出る");
        }
        finally
        {
            File.SetAttributes(itemFile, FileAttributes.Normal);
        }

        Assert.True(attributes.ListNotice.IsWarning);
        Assert.StartsWith("外せませんでした。", attributes.ListNotice.Text, StringComparison.Ordinal);
    });

    [Fact]
    public Task 属性の一覧に無い属性は_一覧に追加すると並び_一覧の見出しの近くに知らせる() => TestApp.Run(async app =>
    {
        var attributes = await OpenAttributesAsync(app, RatedItem("外の属性", 40), OneAttribute());
        await UiThread.Until(() => attributes.HasOrphans, "一覧に無い属性が出る");

        attributes.Orphans.Single().AddToMasterCommand!.Execute(null);
        await UiThread.Until(() => attributes.Rows.Count == 2 && !attributes.HasOrphans, "一覧に入り、一覧に無い名前が消える");

        Assert.Equal("「外の属性」を一覧に追加しました。1 件の商品が絞り込みに表示されるようになります。", attributes.ListNotice.Text);
    });

    [Fact]
    public Task 属性の一覧に無い属性は_商品から外すと消え_一覧の見出しの近くに知らせる() => TestApp.Run(async app =>
    {
        app.Answer = _ => System.Windows.MessageBoxResult.OK;
        var attributes = await OpenAttributesAsync(app, RatedItem("外の属性", 40), OneAttribute());
        await UiThread.Until(() => attributes.HasOrphans, "一覧に無い属性が出る");

        attributes.Orphans.Single().RemoveCommand!.Execute(null);
        await UiThread.Until(() => !attributes.HasOrphans, "一覧に無い名前が消える");

        Assert.Single(attributes.Rows);
        Assert.Equal("「外の属性」を 1 件の商品から外しました。", attributes.ListNotice.Text);
    });

    // ---- 開き直したときの選び直し・並べ方の既定 ----

    [Fact]
    public Task タグの管理は_開き直すと前に選んでいた大分類に戻る() => TestApp.Run(async app =>
    {
        var master = new UserTagMaster { Tops = [new UserTagTop { Name = "作り物の甲" }, new UserTagTop { Name = "作り物の乙" }] };
        var tags = await OpenTagsAsync(app, TaggedItem("作り物の甲"), master);
        tags.Selected = tags.Tops.Single(row => row.Name == "作り物の乙");

        tags.Main.ShowAttributeManageCommand.Execute(null);
        tags.Main.ShowTagManageCommand.Execute(null);
        var reopened = Assert.IsType<TagManageViewModel>(tags.Main.CurrentViewModel);
        await UiThread.Until(() => reopened.Selected is not null, "開き直した画面が選ぶ");

        Assert.NotSame(tags, reopened);
        Assert.Equal("作り物の乙", reopened.Selected!.Name);
    });

    [Fact]
    public Task 属性の管理は_開き直すと前に選んでいた属性に戻る() => TestApp.Run(async app =>
    {
        var master = new AttributeMaster { Attributes = [new AttributeDefinition { Name = "作り物の質感" }, new AttributeDefinition { Name = "作り物の色味" }] };
        var attributes = await OpenAttributesAsync(app, RatedItem("作り物の質感", 40), master);
        attributes.Selected = attributes.Rows.Single(row => row.Name == "作り物の色味");

        attributes.Main.ShowTagManageCommand.Execute(null);
        attributes.Main.ShowAttributeManageCommand.Execute(null);
        var reopened = Assert.IsType<AttributeManageViewModel>(attributes.Main.CurrentViewModel);
        await UiThread.Until(() => reopened.Selected is not null, "開き直した画面が選ぶ");

        Assert.NotSame(attributes, reopened);
        Assert.Equal("作り物の色味", reopened.Selected!.Name);
    });

    [Fact]
    public Task 並べ方の既定は_タグが名前順_属性が候補の並べ替え() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app, TaggedItem("作り物の甲"), OneTop());
        Assert.True(tags.SortsByName);

        await app.Store.Attributes.SaveAsync(OneAttribute());
        tags.Main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(tags.Main.CurrentViewModel);
        await UiThread.Until(() => attributes.Rows.Count > 0, "属性の管理の読み込みが済む");
        Assert.True(attributes.SortsManually);
    });

    // ---- 中の商品の段 ----

    [Fact]
    public Task タグの管理は_小分類を開いて幅が決まると_中の商品が段に並ぶ() => TestApp.Run(async app =>
    {
        var master = new UserTagMaster { Tops = [new UserTagTop { Name = "作り物の甲", Subs = [new UserTagSub { Name = "小の一" }] }] };
        var tags = await OpenTagsAsync(app, TaggedItem("作り物の甲", "小の一"), master);
        await UiThread.Until(() => tags.Subs.Count == 1, "小分類が並ぶ");

        tags.Subs[0].IsExpanded = true;
        tags.SetItemsWidth(800);
        await UiThread.Until(() => tags.Lines.OfType<ManageItemLine>().Any(), "中の商品の段が並ぶ");

        Assert.Same(tags, tags.Lines[0]);
        Assert.Same(tags.Subs[0], tags.Lines[1]);
        Assert.IsType<TagSubFootLine>(tags.Lines[^2]);
        Assert.IsType<ManageFoot>(tags.Lines[^1]);
    });

    [Fact]
    public Task 属性の管理は_商品を開いて幅が決まると_中の商品が段に並ぶ() => TestApp.Run(async app =>
    {
        var attributes = await OpenAttributesAsync(app, RatedItem("作り物の質感", 40), OneAttribute());

        attributes.IsItemsExpanded = true;
        attributes.SetItemsWidth(800);
        await UiThread.Until(() => attributes.Lines.OfType<ManageItemLine>().Any(), "中の商品の段が並ぶ");

        Assert.Same(attributes, attributes.Lines[0]);
        Assert.Equal("40 %", Assert.Single(attributes.Items).ValueText);
        Assert.IsType<ManageFoot>(attributes.Lines[^1]);
    });
}
