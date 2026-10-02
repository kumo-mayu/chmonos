using System.ComponentModel;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// アバターの画面を、検索が商品を読み終える前に開いたとき（2026-10-02。描く台で見つけた。アプリでも起動直後に開くと起き得る）。
///
/// 行は開いた時点の検索の写しで商品を引いていて、引けなかった行は、持っているアバターでも
/// 「この名前の商品は手元にありません」の札になり、右の欄も「所有していない」と出ていた。
/// ここでは「主画面を作った後に商品を置き、検索の読み直しをまだしていない」状態で開いて、写しが古い場面を作る。
/// </summary>
public sealed class AvatarsSearchLoadTests
{
    private const string AvatarId = "2000001";

    /// <summary>持っているアバター（ファイルを1つ持つ商品）を、検索がまだ知らない状態で置いて、アバターの画面を開く。</summary>
    private static async Task<(MainViewModel Main, AvatarsViewModel Avatars, AvatarRowViewModel Row)> OpenBeforeSearchKnowsAsync(TestApp app)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = AvatarId, BoothName = "作り物のアバター", AvatarOverride = true }],
        });
        var main = await app.StartAsync();
        await app.AddItemAsync(Make.Item(AvatarId, "作り物のアバター").WithFiles(Make.File(app.NewFile("avatar.zip"))));

        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        var row = avatars.Rows.Single(row => row.ItemId == AvatarId);
        avatars.Selected = row;

        // 持っていること自体は保存先から数えるので、見出しは「所有しているアバター」に入っている。商品だけが引けていない
        Assert.True(row.IsOwned);
        Assert.False(row.HasCard);
        return (main, avatars, row);
    }

    [Fact]
    public Task 検索の読み込みが済んだら_行の商品のカードと右の欄の所有を引き直す() => TestApp.Run(async app =>
    {
        var (main, avatars, row) = await OpenBeforeSearchKnowsAsync(app);

        await main.Search.ReloadAsync();
        await app.SettleAsync();

        // 前は行を作り直さないので、名前だけの札と「所有していない」のまま残った
        Assert.True(row.HasCard);
        Assert.Equal(AvatarId, Assert.IsType<ItemCardViewModel>(row.Card).Item.Id);
        Assert.True(avatars.IsOwnedByFile);
        Assert.Equal("所有している（ファイルあり）", avatars.SelectedOwnedText);
        Assert.False(avatars.ShowsOwnedToggle);
    });

    [Fact]
    public Task 検索が最初の読み込みをしている間は_手元にありませんも所有していないも言い切らない() => TestApp.Run(async app =>
    {
        var (main, avatars, row) = await OpenBeforeSearchKnowsAsync(app);

        // 検索がまだ1件も読んでいない状態で読み始めた瞬間を見る（起動直後と同じ）。
        // アバターの画面の方が先に知らせを受けるので、受けた時点の表示を控える
        string? ownedWhileLoading = null;
        bool? missingNoteWhileLoading = null;
        bool? toggleWhileLoading = null;
        PropertyChangedEventHandler onChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(SearchViewModel.IsLoading) && main.Search.IsLoading && ownedWhileLoading is null)
            {
                ownedWhileLoading = avatars.SelectedOwnedText;
                missingNoteWhileLoading = row.ShowsMissingNote;
                toggleWhileLoading = avatars.ShowsOwnedToggle;
            }
        };
        Assert.Equal(0, main.Search.TotalCount);
        main.Search.PropertyChanged += onChanged;
        try
        {
            await main.Search.ReloadAsync();
            await app.SettleAsync();
        }
        finally
        {
            main.Search.PropertyChanged -= onChanged;
        }

        Assert.Equal("所有しているか確かめています…", ownedWhileLoading);
        Assert.False(missingNoteWhileLoading);
        Assert.False(toggleWhileLoading);

        // 読み終えたら言い切る
        Assert.Equal("所有している（ファイルあり）", avatars.SelectedOwnedText);
    });

    [Fact]
    public Task 読み終えても手元に無いアバターには_手元にありませんを添え_所有していないと言う() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = AvatarId, BoothName = "作り物のアバター", AvatarOverride = true }],
        });
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        var row = avatars.Rows.Single(row => row.ItemId == AvatarId);
        avatars.Selected = row;

        Assert.False(row.HasCard);
        Assert.True(row.ShowsMissingNote);
        Assert.Equal("所有していない", avatars.SelectedOwnedText);
        Assert.True(avatars.ShowsOwnedToggle);
    });
}
