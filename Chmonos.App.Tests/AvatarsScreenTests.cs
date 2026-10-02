using Chmonos.App.ViewModels;
using Chmonos.App.Tests.Support;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>アバターの管理の画面（メモ9 の直し。2026-10-02）。</summary>
public sealed class AvatarsScreenTests
{
    private const string AvatarId = "2000001";

    private static Task<AvatarsViewModel> OpenAsync(TestApp app, params AvatarRegistryEntry[] entries)
        => OpenAsync(app, [], entries);

    private static async Task<AvatarsViewModel> OpenAsync(TestApp app, IReadOnlyList<AvatarBaseGroup> bases, params AvatarRegistryEntry[] entries)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = entries.Length > 0
                ? entries
                : [new AvatarRegistryEntry { ItemId = AvatarId, BoothName = "作り物のアバター", AvatarOverride = true }],
            BaseGroups = bases,
        });
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        return avatars;
    }

    [Fact]
    public Task BOOTHの名前に戻すは1回で戻り_名前の欄は開かない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app, new AvatarRegistryEntry
        {
            ItemId = AvatarId,
            BoothName = "作り物のアバター",
            DisplayName = "付けた名前",
            AvatarOverride = true,
        });
        avatars.Selected = avatars.Rows.Single(row => row.ItemId == AvatarId);
        Assert.True(avatars.HasManualName);
        Assert.False(avatars.IsEditingName);

        avatars.ResetNameCommand.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => !avatars.IsLoading, "読み直しが済む");

        Assert.False(avatars.HasManualName);
        Assert.NotEqual("付けた名前", avatars.SelectedName);
        // 前は、読み直して選び直すときに「付けた名前」が打ちかけとして控えられ、欄が開いたまま残った
        Assert.False(avatars.IsEditingName);
        Assert.Equal(avatars.SelectedName, avatars.NameInput);
        Assert.Null(app.Store.Avatars.Load().Entries.Single().DisplayName);
    });

    [Fact]
    public Task 右の欄のIDを押すと_IDだけをコピーして知らせる() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.Selected = avatars.Rows.Single(row => row.ItemId == AvatarId);

        avatars.CopyIdCommand.Execute(null);

        Assert.Equal([AvatarId], app.Copied);
        Assert.Equal($"{AvatarId} をコピーしました。", avatars.Status);
        Assert.Equal("クリックすると商品IDをコピーします", avatars.IdCopyTip);
    });

    [Fact]
    public Task 持っているアバターの行は_右クリックのお気に入りでカードと同じく星が付く() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(AvatarId, "作り物のアバター"));
        var avatars = await OpenAsync(app);
        var row = avatars.Rows.Single(row => row.ItemId == AvatarId);
        var card = Assert.IsType<ItemCardViewModel>(row.Card);
        Assert.Equal("お気に入りに入れる", card.FavoriteTip);

        // メニューの「お気に入りに入れる」は、行のカードを入れ物の画面の ToggleFavoriteAsync へ渡す（カードの星と同じ道）
        await avatars.ToggleFavoriteAsync(card);
        await app.SettleAsync();

        Assert.Equal("お気に入りから外す", card.FavoriteTip);
        Assert.True((await app.Store.Items.LoadAsync(AvatarId))!.Local.IsFavorite);
    });

    [Fact]
    public Task アバターのカードには選ぶ箱を出さず_検索のカードには出す() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(AvatarId, "作り物のアバター"));
        var avatars = await OpenAsync(app);

        // アバターの一覧は1つだけ選ぶ（右に詳細を出す）。カードの選ぶ箱で何枚でも印が付いていた
        Assert.False(avatars.Rows.Single(row => row.ItemId == AvatarId).Card!.CanSelect);
        Assert.True(app.Main.Search.CardFor(AvatarId)!.CanSelect);
    });

    [Fact]
    public Task 名前が挙がっただけのアバターの行にはカードが無く_お気に入りは出ない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        Assert.Null(avatars.Rows.Single(row => row.ItemId == AvatarId).Card);
    });

    [Fact]
    public Task 共通素体を足す欄は_新しい名前をEnterか追加で足し_欄を空ける() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app, [new AvatarBaseGroup { Name = "作り物の素体A" }]);
        avatars.ShowBaseModeCommand.Execute(null);

        avatars.NewBaseName = "作り物の素体B";
        avatars.AddBaseCommand.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => avatars.Bases.Count == 2, "足した素体が並ぶ");

        Assert.Equal("共通素体「作り物の素体B」を追加しました。", avatars.Status);
        Assert.Equal(string.Empty, avatars.NewBaseName);
        Assert.Equal("作り物の素体B", avatars.SelectedBase?.Name);
    });

    [Fact]
    public Task 共通素体を足す欄に今ある名前を入れると_書かずに既にありますと言う() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app, [new AvatarBaseGroup { Name = "作り物の素体A" }, new AvatarBaseGroup { Name = "作り物の素体B" }]);
        avatars.ShowBaseModeCommand.Execute(null);
        avatars.SelectedBase = avatars.Bases.Single(row => row.Name == "作り物の素体B");
        var writes = app.Store.Avatars.WriteCount;

        // 綴りの大小が違っても同じ素体
        avatars.NewBaseName = "作り物の素体a";
        avatars.AddBaseCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("共通素体「作り物の素体A」は既にあります。", avatars.Status);
        Assert.Equal("作り物の素体A", avatars.SelectedBase?.Name);
        Assert.Equal("作り物の素体a", avatars.NewBaseName);
        Assert.Equal(writes, app.Store.Avatars.WriteCount);
        Assert.Equal(2, app.Store.Avatars.Load().BaseGroups.Count);
    });

    [Fact]
    public Task 共通素体の行の削除は_元に戻せないと確かめてから消す() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app, [new AvatarBaseGroup { Name = "作り物の素体A" }, new AvatarBaseGroup { Name = "作り物の素体B" }]);
        avatars.ShowBaseModeCommand.Execute(null);

        // 行の右クリックの「削除」は、行の DeleteCommand（右の欄の「削除」と同じ）。やめると何も消えない
        avatars.Bases.Single(row => row.Name == "作り物の素体A").DeleteCommand!.Execute(null);
        await app.SettleAsync();
        var asked = Assert.Single(app.Notices);
        Assert.Equal("共通素体を削除", asked.Caption);
        Assert.Contains("この操作は元に戻せません。", asked.Text, StringComparison.Ordinal);
        Assert.Equal(2, avatars.Bases.Count);

        app.Answer = _ => System.Windows.MessageBoxResult.OK;
        avatars.Bases.Single(row => row.Name == "作り物の素体A").DeleteCommand!.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => avatars.Bases.Count == 1, "消した素体が一覧から消える");
        Assert.Equal("作り物の素体B", avatars.Bases.Single().Name);
    });

    [Fact]
    public void 絵の無い候補は頭文字を出し_飾り記号は飛ばす()
    {
        // 既定の共通素体は名前だけの種で絵を持たない（メモ3-①）。候補の頭が空の四角にならないように頭文字を出す
        var suggestion = new Chmonos.App.Controls.Suggestion { Value = "【作り物】素体", Display = "【作り物】素体", IconFactory = _ => null };

        Assert.Null(suggestion.Icon);
        Assert.Equal(System.Windows.Visibility.Visible, suggestion.IconVisibility);
        Assert.Equal("作", suggestion.Initial);
    }

    [Fact]
    public Task 打ちかけの名前は別のアバターを選んで戻っても残る() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(
            app,
            new AvatarRegistryEntry { ItemId = AvatarId, BoothName = "作り物のアバター", AvatarOverride = true },
            new AvatarRegistryEntry { ItemId = "2000002", BoothName = "もう1体のアバター", AvatarOverride = true });
        var first = avatars.Rows.Single(row => row.ItemId == AvatarId);
        avatars.Selected = first;
        avatars.StartRenameCommand.Execute(null);
        avatars.NameInput = "打ちかけ";

        avatars.Selected = avatars.Rows.Single(row => row.ItemId == "2000002");
        Assert.False(avatars.IsEditingName);
        avatars.Selected = first;

        Assert.True(avatars.IsEditingName);
        Assert.Equal("打ちかけ", avatars.NameInput);
    });
}
