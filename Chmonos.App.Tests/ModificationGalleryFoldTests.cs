using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の右の欄の写真を、大きな絵ごと畳む（ユーザ判断 2026-11-20）。次に起動した時まで覚え、
/// 商品ページ・編集と共通の「小さな絵を折りたたむ」とは別に持つ。
/// </summary>
public class ModificationGalleryFoldTests
{
    [Fact]
    public Task 写真を畳むと覚え_小さな絵の畳みとは別() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var record = new ModificationRecord { Id = "mod-0000a001", AvatarItemId = "1000001", Name = "夏の改変" };
        var modification = new ModificationViewModel(record, app.Services, main, main.Thumbnails);
        Assert.False(modification.GalleryFolded);

        modification.ToggleGalleryCommand.Execute(null);

        Assert.True(modification.GalleryFolded);
        Assert.True(app.Services.PaneWidths.IsCollapsed(GalleryFoldKey.Modification));
        Assert.False(modification.GalleryTilesFolded);
        Assert.False(app.Services.PaneWidths.IsCollapsed(GalleryFoldKey.Tiles));

        // 次に開いた改変（別の改変でも）も畳んだまま
        var next = new ModificationViewModel(record with { Id = "mod-0000a002" }, app.Services, main, main.Thumbnails);
        Assert.True(next.GalleryFolded);

        next.ToggleGalleryCommand.Execute(null);
        Assert.False(modification.GalleryFolded);
    });
}
