using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページと編集ページの「アバター」の欄（ユーザ判断 2026-10-07）。
/// 作者がカテゴリを3Dキャラクターにしていないアバターを、手でアバターとして登録簿に載せる。2つの画面は同じ部品を使う
/// </summary>
public sealed class ItemAvatarRegistrationTests
{
    [Fact]
    public Task 商品ページで登録すると_手で登録したアバターになり_外すと戻る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9901401", "作り物のアバター"));
        var main = await app.StartAsync();
        main.ShowItem((await app.Store.Items.LoadAsync("9901401"))!);
        var avatar = Assert.IsType<ItemViewModel>(main.CurrentViewModel).AvatarRegistration;

        Assert.False(avatar.IsAvatar);
        Assert.True(avatar.RegisterCommand.CanExecute(null));
        Assert.Equal(string.Empty, avatar.StateText);

        avatar.RegisterCommand.Execute(null);
        await app.SettleAsync();

        Assert.True(avatar.IsManual);
        Assert.Equal("手で登録したアバターです。", avatar.StateText);
        Assert.False(avatar.RegisterCommand.CanExecute(null));
        Assert.True(Assert.Single(app.Store.Avatars.Load().Entries).AvatarOverride);

        avatar.UnregisterCommand.Execute(null);
        await app.SettleAsync();

        Assert.False(avatar.IsAvatar);
        Assert.Null(Assert.Single(app.Store.Avatars.Load().Entries).AvatarOverride);
    });

    [Fact]
    public Task アバターの管理で開くと_その商品を選んだアバターの画面へ移る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9901402", "作り物のアバター"));
        var main = await app.StartAsync();
        main.ShowItem((await app.Store.Items.LoadAsync("9901402"))!);
        var avatar = Assert.IsType<ItemViewModel>(main.CurrentViewModel).AvatarRegistration;
        avatar.RegisterCommand.Execute(null);
        await app.SettleAsync();

        avatar.OpenInAvatarsCommand.Execute(null);
        await app.SettleAsync();

        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        Assert.Equal("9901402", avatars.Selected?.ItemId);
    });

    /// <summary>編集ページで次の商品へ進むと、欄も次の商品の状態に替わる（前の商品の状態を持ち越さない）。</summary>
    [Fact]
    public Task 編集ページで次へ進むと_欄が次の商品の状態に替わる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9901403", "作り物のアバター甲"));
        await app.AddItemAsync(Make.Item("9901404", "作り物の衣装乙"));
        var main = await app.StartAsync();
        main.ShowEditCommand.Execute(null);
        await app.SettleAsync();
        var edit = Assert.IsType<EditViewModel>(main.CurrentViewModel);
        var first = edit.CurrentItemId!;

        edit.AvatarRegistration.RegisterCommand.Execute(null);
        await app.SettleAsync();
        Assert.True(edit.AvatarRegistration.IsManual);

        edit.SkipCommand.Execute(null);
        await app.SettleAsync();

        Assert.NotEqual(first, edit.CurrentItemId);
        Assert.False(edit.AvatarRegistration.IsAvatar);
        Assert.True(edit.AvatarRegistration.RegisterCommand.CanExecute(null));
    });

    /// <summary>登録簿に書けないときは、欄の下にそう言う（外部の点検 2026-10-07。前はログに残るだけで、押しても何も起きないように見えた）。</summary>
    [Fact]
    public Task 登録簿に書けないときは_欄の下にそう言う() => TestApp.Run(async app =>
    {
        app.AllowLoggedFailures = true;
        await app.AddItemAsync(Make.Item("9901405", "作り物のアバター"));
        var main = await app.StartAsync();
        main.ShowItem((await app.Store.Items.LoadAsync("9901405"))!);
        var avatar = Assert.IsType<ItemViewModel>(main.CurrentViewModel).AvatarRegistration;
        var registry = app.Services.Paths.AvatarRegistryFile;
        await app.Store.Avatars.UpdateAsync(current => current);
        System.IO.File.SetAttributes(registry, System.IO.FileAttributes.ReadOnly);
        try
        {
            avatar.RegisterCommand.Execute(null);
            await app.SettleAsync();
        }
        finally
        {
            System.IO.File.SetAttributes(registry, System.IO.FileAttributes.Normal);
        }

        Assert.StartsWith("登録簿に書けませんでした。", avatar.Notice);
        Assert.False(avatar.IsAvatar);
    });

    /// <summary>アバターの画面の「見つかった場所」は、無いときも「なし」と言う（ユーザ指摘 2026-10-07。空だと1行ぶん間延びして見えた）。</summary>
    [Fact]
    public Task 見つかった場所が無いときは_なしと言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9901406", "作り物のアバター"));
        var main = await app.StartAsync();
        main.ShowItem((await app.Store.Items.LoadAsync("9901406"))!);
        var avatar = Assert.IsType<ItemViewModel>(main.CurrentViewModel).AvatarRegistration;
        avatar.RegisterCommand.Execute(null);
        await app.SettleAsync();

        avatar.OpenInAvatarsCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("見つかった場所：なし", Assert.IsType<AvatarsViewModel>(main.CurrentViewModel).SelectedSeenAsText);
    });
}
