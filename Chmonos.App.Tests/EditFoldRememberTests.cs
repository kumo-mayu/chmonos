using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 編集画面のユーザータグ・属性の畳み（open.md 134）。既定は開き、畳んだかは次に起動した時まで覚える
/// （左の一覧のつまみと同じ ui-state.json の collapsedPanes。ユーザ判断 2026-11-19）。
/// </summary>
public sealed class EditFoldRememberTests
{
    private static async Task<EditViewModel> OpenEditAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        main.ShowEditCommand.Execute(null);
        await app.SettleAsync();
        return Assert.IsType<EditViewModel>(main.CurrentViewModel);
    }

    [Fact]
    public Task ギャラリーの小さな絵の畳みは_商品ページと改変で1つ_覚えて次の起動にも残る() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "作り物の衣装");
        await app.AddItemAsync(item);
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        Assert.False(page.GalleryTilesFolded);

        var changed = new List<string?>();
        page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        page.GalleryTilesFolded = true;
        Assert.Contains(nameof(ItemViewModel.GalleryTilesFolded), changed);

        // 別の持ち主（次の商品）も畳んだまま。書き込みも済む
        Assert.True(new ItemViewModel(item, app.Services, main, main.Thumbnails).GalleryTilesFolded);
        await app.SettleAsync();
        Assert.Equal([GalleryFoldKey.Tiles], app.Services.UiState.CollapsedPanes);

        page.GalleryTilesFolded = false;
        await app.SettleAsync();
        Assert.Empty(app.Services.UiState.CollapsedPanes ?? []);
    });

    [Fact]
    public Task 既定は開いていて_畳むと覚え_開き直した画面と書き込みの両方に残る() => TestApp.Run(async app =>
    {
        var edit = await OpenEditAsync(app);
        Assert.True(edit.IsTagsExpanded);
        Assert.True(edit.IsAttributesExpanded);

        edit.IsTagsExpanded = false;
        Assert.False(edit.IsTagsExpanded);
        Assert.True(edit.IsAttributesExpanded);

        app.Main.ShowSearchCommand.Execute(null);
        app.Main.ShowEditCommand.Execute(null);
        await app.SettleAsync();
        var next = Assert.IsType<EditViewModel>(app.Main.CurrentViewModel);
        Assert.False(next.IsTagsExpanded);
        Assert.True(next.IsAttributesExpanded);

        await app.SettleAsync();
        Assert.Equal(["edit.tags"], app.Services.UiState.CollapsedPanes);

        // 左の一覧の畳みと同じ欄に載るので、広げると消え、ほかの畳みを巻き込まない
        next.IsAttributesExpanded = false;
        next.IsTagsExpanded = true;
        await app.SettleAsync();
        Assert.Equal(["edit.attributes"], app.Services.UiState.CollapsedPanes);
    });
}
