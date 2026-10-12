using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>設定の「フォルダの置き方」（docs/spec/folder-layout.md）。名前とパスは作り物。</summary>
public sealed class SettingsFolderLayoutTests
{
    private const string Root = @"D:\作り物の置き場";

    [Fact]
    public Task 追加すると既定の段で保存し_同じフォルダは足さない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        Assert.False(settings.HasFolderLayouts);

        await settings.AddFolderLayoutAsync(Root);
        await settings.AddFolderLayoutAsync(Root + @"\");
        await app.SettleAsync();

        var rule = Assert.Single(app.Services.Settings.FolderLayouts);
        Assert.Equal(new FolderLayoutRule { Root = Root, Enabled = true, ItemNameLevel = 1, ShopNameLevel = 2 }, rule);
        Assert.True(settings.HasFolderLayouts);
    });

    [Fact]
    public Task オンオフと段を変えると_その行だけ書き換わり_外すと消える() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(current => current with
        {
            FolderLayouts = [FolderLayoutRule.DefaultFor(Root), FolderLayoutRule.DefaultFor(@"E:\別の置き場") with { ShopNameLevel = null }],
        });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);

        settings.FolderLayouts[0].Enabled = false;
        await app.SettleAsync();
        settings.FolderLayouts[0].ItemNameLevel = 2;
        await app.SettleAsync();
        settings.FolderLayouts[0].ShopNameLevel = 3;
        await app.SettleAsync();

        Assert.Equal(
            [new FolderLayoutRule { Root = Root, Enabled = false, ItemNameLevel = 2, ShopNameLevel = 3 }, FolderLayoutRule.DefaultFor(@"E:\別の置き場") with { ShopNameLevel = null }],
            app.Services.Settings.FolderLayouts);

        settings.FolderLayouts[1].RemoveCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal([Root], app.Services.Settings.FolderLayouts.Select(rule => rule.Root));
        Assert.Equal([Root], settings.FolderLayouts.Select(row => row.Root));
    });

    [Fact]
    public Task 紐付け済みのファイルで当てはまりを数えて出す() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9100001", "ふわふわ帽子", "fake-studio").WithFiles(Make.File($@"{Root}\fake-studio\【期間限定無料】ふわふわ帽子\hat.zip")));
        await app.AddItemAsync(Make.Item("9100002", "もこもこ手袋", "fake-studio").WithFiles(Make.File($@"{Root}\fake-studio\まとめ\glove.zip")));
        await app.AddItemAsync(Make.Item("9100003", "外の商品", "fake-studio").WithFiles(Make.File(@"E:\ほか\x.zip")));
        await app.ChangeSettingsAsync(current => current with { FolderLayouts = [FolderLayoutRule.DefaultFor(Root)] });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await app.SettleAsync();

        Assert.Equal("紐付け済みのファイル 2 件のうち、合っているのは商品名 1 件・ショップ名 2 件です。", settings.FolderLayouts[0].FitText);
    });

    [Fact]
    public Task 保存が終わる前に元へ戻しても_最後の操作が残る() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(current => current with { FolderLayouts = [FolderLayoutRule.DefaultFor(Root)] });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);

        // 待たずに外して入れ直す。前は2回目を「変わっていない」と見て捨て、外したままになった（点検33）
        var row = settings.FolderLayouts[0];
        row.Enabled = false;
        row.Enabled = true;
        await app.SettleAsync();

        Assert.True(Assert.Single(app.Services.Settings.FolderLayouts).Enabled);
    });

    [Fact]
    public async Task 検索の写しが空なら_保存先の記録からショップを引く()
    {
        var items = new[] { Make.Item("9100001", "別の帽子", "fake-studio").WithFiles(Make.File(@"E:\別\a.zip")) };
        var loaded = 0;
        var hint = await ResolveViewModel.LayoutHintForAsync(
            $@"{Root}\fake-studio\ふわふわ帽子\hat.zip", false, [FolderLayoutRule.DefaultFor(Root)], [],
            () => { loaded++; return Task.FromResult<IReadOnlyList<ItemRecord>>(items); });

        Assert.Equal(1, loaded);
        Assert.Equal("fake-studio", hint!.ShopSubdomain);

        // 対象がフォルダ（zip の無い展開物の根）なら、フォルダそのものを1つ上と数える
        var folder = await ResolveViewModel.LayoutHintForAsync(
            $@"{Root}\fake-studio\ふわふわ帽子", true, [FolderLayoutRule.DefaultFor(Root)], items, () => throw new InvalidOperationException());
        Assert.Equal(("ふわふわ帽子", "fake-studio"), (folder!.ItemFolder, folder.ShopFolder));
    }

    [Theory]
    [InlineData(1, null, "紐付け済みのファイル 0 件")]
    [InlineData(null, null, "商品名かショップ名のどちらかを選んでください。")]
    [InlineData(2, 2, "商品名とショップ名に同じ段は選べません。")]
    public Task 効かない指定は_先にその理由を出す(int? itemLevel, int? shopLevel, string expected) => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(current => current with
        {
            FolderLayouts = [new FolderLayoutRule { Root = Root, ItemNameLevel = itemLevel, ShopNameLevel = shopLevel }],
        });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await app.SettleAsync();

        var text = settings.FolderLayouts[0].FitText;
        Assert.Equal(expected == "紐付け済みのファイル 0 件" ? "この中に、紐付け済みのファイルはまだありません。" : expected, text);
    });
}
