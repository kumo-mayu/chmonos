using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// アバターの管理の共通素体の欄で、素体に画像を付ける・外す（ユーザ判断 2026-11-20・152 の案A）。
/// 押したら何が書かれるか、出し分け（「画像を外す」・ボタンの名前・絵の有無）を見る
/// </summary>
public sealed class AvatarsBaseImageTests
{
    private const string Handmade = "作り物の素体";

    /// <summary>1×1 の PNG。</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static async Task<AvatarsViewModel> OpenAsync(TestApp app)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "9900301", BoothName = "作り物アバター", AvatarOverride = true, BaseName = Handmade }],
            BaseGroups = [new AvatarBaseGroup { Name = Handmade, IsManual = true }],
        });
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Bases.Count > 0, "アバターの読み込みが済む");
        avatars.ShowBaseModeCommand.Execute(null);
        avatars.SelectedBase = avatars.Bases.Single(row => row.Name == Handmade);
        return avatars;
    }

    private static AvatarBaseGroup Group(TestApp app) => app.Store.Avatars.Load().BaseGroups.Single(group => group.Name == Handmade);

    private static string WriteFile(TestApp app, string name, byte[] bytes)
    {
        var path = Path.Combine(app.Root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public Task 画像も素体の商品も無ければ_頭の字を出し_外すは出さない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        var row = avatars.SelectedBase!;

        Assert.False(row.HasIcon);
        Assert.False(row.HasOwnImage);
        Assert.Equal("画像を設定…", row.PickImageText);
        Assert.Equal("作", row.Initial);
    });

    [Fact]
    public Task 画像を設定すると_置き場に写して名前だけを書き_絵と外すが出る() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        await avatars.SetBaseImageFromFileAsync(Handmade, WriteFile(app, "base.png", TinyPng));
        await UiThread.Until(() => avatars.SelectedBase?.HasOwnImage == true, "付けた画像が行に出る");

        var image = Group(app).Image;
        Assert.NotNull(image);
        Assert.True(File.Exists(Path.Combine(app.Services.Paths.BaseImagesDir, image!)));
        var row = avatars.SelectedBase!;
        Assert.Equal(Handmade, row.Name);
        Assert.True(row.HasIcon);
        Assert.Equal("画像を変更…", row.PickImageText);
        Assert.Equal(string.Empty, row.ImageNote.Text);
    });

    [Fact]
    public Task 画像を外すと_記録とファイルが消え_欄の下で知らせる() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        await avatars.SetBaseImageFromFileAsync(Handmade, WriteFile(app, "base.png", TinyPng));
        await UiThread.Until(() => avatars.SelectedBase?.HasOwnImage == true, "付けた画像が行に出る");
        var image = Group(app).Image!;

        avatars.SelectedBase!.RemoveImageCommand!.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => avatars.SelectedBase?.HasOwnImage == false, "外した画像が行から消える");

        Assert.Null(Group(app).Image);
        Assert.False(File.Exists(Path.Combine(app.Services.Paths.BaseImagesDir, image)));
        Assert.False(avatars.SelectedBase!.HasIcon);
        Assert.Equal("画像を外しました。", avatars.SelectedBase.ImageNote.Text);
        Assert.False(avatars.SelectedBase.ImageNote.IsWarning);
    });

    [Fact]
    public Task 画像として読めないファイルは書かず_欄の下で知らせる() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        await avatars.SetBaseImageFromFileAsync(Handmade, WriteFile(app, "not-image.png", [1, 2, 3, 4]));

        Assert.Null(Group(app).Image);
        Assert.Equal("画像として読めませんでした。", avatars.SelectedBase!.ImageNote.Text);
        Assert.True(avatars.SelectedBase.ImageNote.IsWarning);
    });
}
