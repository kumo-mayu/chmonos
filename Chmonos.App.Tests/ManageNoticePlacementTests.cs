using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// タグの管理・属性の管理：操作の結果の知らせが、押した所の近くに出ること（2026-10-04・notice-placement-2026-10-03.md）。
/// 前は右の欄の先頭の1行（StatusText）にまとめて出していた。ここでは「どの出し先に入り、ほかの出し先は空のまま」を確かめる
/// </summary>
public class ManageNoticePlacementTests
{
    private static UserTagMaster Master() => new()
    {
        Tops =
        [
            new UserTagTop { Name = "作り物の甲", Subs = [new UserTagSub { Name = "小の一" }, new UserTagSub { Name = "小の二" }] },
            new UserTagTop { Name = "作り物の乙" },
        ],
    };

    private static async Task<TagManageViewModel> OpenTagsAsync(TestApp app, bool withItem = false)
    {
        if (withItem)
        {
            var item = Make.Item("1000001", "ひみつの商品名");
            item = item with { Local = item.Local with { UserTags = [new UserTagAssignment { Top = "作り物の乙" }] } };
            await app.AddItemAsync(item);
        }

        await app.Store.UserTags.SaveAsync(Master());
        var main = await app.StartAsync();
        main.ShowTagManageCommand.Execute(null);
        var tags = Assert.IsType<TagManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => tags.TopCount > 0, "タグの管理の読み込みが済む");
        return tags;
    }

    private static async Task<AttributeManageViewModel> OpenAttributesAsync(TestApp app)
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "作り物の甲" }, new AttributeDefinition { Name = "作り物の乙" }],
        });
        var main = await app.StartAsync();
        main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => attributes.Rows.Count == 2, "属性の管理の読み込みが済む");
        return attributes;
    }

    private static void AssertOnly(
        string place, TagManageViewModel tags, string? add = null, string? addSub = null, string? name = null, string? sub = null, string? list = null)
    {
        Assert.True(
            (add ?? string.Empty) == tags.AddNoticeText
            && (addSub ?? string.Empty) == tags.AddSubNoticeText
            && (name ?? string.Empty) == tags.NameNotice.Text
            && (sub ?? string.Empty) == tags.SubNotice.Text
            && (list ?? string.Empty) == tags.ListNotice.Text,
            $"{place}：欄の下[{tags.AddNoticeText}] 小分類の欄の下[{tags.AddSubNoticeText}] 名前[{tags.NameNotice.Text}] 小分類の見出し[{tags.SubNotice.Text}] 一覧の見出し[{tags.ListNotice.Text}]");
    }

    [Fact]
    public Task 大分類の名前が空のまま追加を押すと_欄の下に警告の色で出る() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app);

        tags.AddTopCommand.Execute(null);
        await app.SettleAsync();

        AssertOnly("空の名前", tags, add: "大分類の名前を入れてから押してください。");
        Assert.True(tags.AddNoticeIsWarning);
    });

    [Fact]
    public Task 大分類を削除すると_一覧の見出しの近くに出て_名前の近くと欄の下は空のまま() => TestApp.Run(async app =>
    {
        app.Answer = _ => System.Windows.MessageBoxResult.OK;
        var tags = await OpenTagsAsync(app);
        tags.Selected = tags.Tops.First(row => row.Name == "作り物の乙");

        tags.DeleteTopCommand.Execute(null);
        await UiThread.Until(() => tags.ListNotice.Text.Length > 0, "削除の結果が出る");

        AssertOnly("大分類の削除", tags, list: "削除し、0 件の商品から外しました。");
        Assert.False(tags.ListNotice.IsWarning);
    });

    [Fact]
    public Task 小分類を削除すると_小分類の見出しの近くに出る() => TestApp.Run(async app =>
    {
        app.Answer = _ => System.Windows.MessageBoxResult.OK;
        var tags = await OpenTagsAsync(app);
        tags.Selected = tags.Tops.First(row => row.Name == "作り物の甲");

        tags.Subs.First(row => row.Name == "小の一").DeleteCommand!.Execute(null);
        await UiThread.Until(() => tags.SubNotice.Text.Length > 0, "小分類の削除の結果が出る");

        AssertOnly("小分類の削除", tags, sub: "「小の一」を削除し、0 件の商品から外しました。");

        // 別の大分類へ移ると、前の大分類の話なので消える
        tags.Selected = tags.Tops.First(row => row.Name == "作り物の乙");
        AssertOnly("選び直し", tags);
    });

    [Fact]
    public Task メモの自動保存は成功しても何も出さない() => TestApp.Run(async app =>
    {
        var tags = await OpenTagsAsync(app);
        tags.Selected = tags.Tops.First(row => row.Name == "作り物の甲");

        tags.MemoDraft = "作り物のメモ";
        await tags.ReloadAsync();
        await app.SettleAsync();

        Assert.Equal("作り物のメモ", app.Store.UserTags.Load().Tops.First(top => top.Name == "作り物の甲").Memo);
        AssertOnly("メモの保存", tags);
    });

    [Fact]
    public Task 書き込みの失敗は_呼び手の渡した出し先に出る() => TestApp.Run(async app =>
    {
        app.Answer = _ => System.Windows.MessageBoxResult.OK;
        app.AllowLoggedFailures = true;
        var tags = await OpenTagsAsync(app, withItem: true);
        tags.Selected = tags.Tops.First(row => row.Name == "作り物の乙");

        // 商品の JSON を読み取り専用にして、書き換えを失敗させる
        var itemFile = app.Services.Paths.ItemFile("1000001");
        File.SetAttributes(itemFile, FileAttributes.ReadOnly);
        try
        {
            tags.DeleteTopCommand.Execute(null);
            await UiThread.Until(() => tags.ListNotice.Text.Length > 0, "失敗が出る");
        }
        finally
        {
            File.SetAttributes(itemFile, FileAttributes.Normal);
        }

        Assert.True(tags.ListNotice.IsWarning);
        Assert.StartsWith("削除できませんでした。", tags.ListNotice.Text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, tags.NameNotice.Text);
        Assert.Equal(string.Empty, tags.SubNotice.Text);
    });

    [Fact]
    public Task 属性を削除すると_一覧の見出しの近くに出る_メモの保存は出さない() => TestApp.Run(async app =>
    {
        app.Answer = _ => System.Windows.MessageBoxResult.OK;
        var attributes = await OpenAttributesAsync(app);
        attributes.Selected = attributes.Rows.First(row => row.Name == "作り物の乙");

        attributes.MemoDraft = "作り物のメモ";
        await attributes.ReloadAsync();
        await app.SettleAsync();
        Assert.Equal(string.Empty, attributes.ListNotice.Text);
        Assert.Equal(string.Empty, attributes.NameNotice.Text);
        Assert.Equal(string.Empty, attributes.DefaultNotice.Text);

        attributes.DeleteCommand.Execute(null);
        await UiThread.Until(() => attributes.ListNotice.Text.Length > 0, "削除の結果が出る");

        Assert.Equal("削除し、0 件の商品から評価を外しました。", attributes.ListNotice.Text);
        Assert.False(attributes.ListNotice.IsWarning);
        Assert.Equal(string.Empty, attributes.NameNotice.Text);
        Assert.Equal(string.Empty, attributes.AddNoticeText);
    });

    [Fact]
    public Task 最初から並べるの切り替えは_そのボタンの下に出る() => TestApp.Run(async app =>
    {
        var attributes = await OpenAttributesAsync(app);
        attributes.Selected = attributes.Rows.First(row => row.Name == "作り物の甲");

        attributes.ToggleDefaultCommand.Execute(null);
        await UiThread.Until(() => attributes.DefaultNotice.Text.Length > 0, "切り替えの結果が出る");

        Assert.Equal("「作り物の甲」を編集画面に最初から並べます。値は動かしたときだけ付きます。", attributes.DefaultNotice.Text);
        Assert.Equal(string.Empty, attributes.ListNotice.Text);

        // 別の属性へ移ると、前の属性の話なので消える
        attributes.Selected = attributes.Rows.First(row => row.Name == "作り物の乙");
        Assert.Equal(string.Empty, attributes.DefaultNotice.Text);
    });

    [Fact]
    public Task 属性の欄の下の知らせは_既にありますは警告_足せたことはふつうの色() => TestApp.Run(async app =>
    {
        var attributes = await OpenAttributesAsync(app);

        attributes.FilterText = "作り物の甲";
        attributes.AddCommand.Execute(null);
        await app.SettleAsync();
        Assert.True(attributes.AddNoticeIsWarning);

        attributes.FilterText = "作り物の丙";
        attributes.AddCommand.Execute(null);
        await UiThread.Until(() => attributes.Rows.Count == 3 && attributes.AddNoticeText.Length > 0, "足せる");
        Assert.False(attributes.AddNoticeIsWarning);
    });
}
