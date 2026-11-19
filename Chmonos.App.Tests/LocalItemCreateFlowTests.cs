using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の件数の帯の「BOOTHに無い商品を登録…」（2026-11-20 のメモ「ファイルの無いlocalItemが作成できない気がする」）。
/// 名前を窓で聞き、ファイルを持たない仮IDの商品を作って、その商品ページを開く。BOOTH へは問い合わせない。
/// </summary>
public class LocalItemCreateFlowTests
{
    [Fact]
    public Task 名前を入れると_ファイルの無い商品ができて商品ページが開き_検索にも並ぶ() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "BOOTHの商品"));
        var main = await app.StartAsync();
        app.NameLocalItem = model =>
        {
            model.Name = "  手で作った衣装  ";
            return true;
        };

        Assert.True(main.CreateLocalItemCommand.CanExecute(null));
        await main.CreateLocalItemAsync();

        Assert.Single(app.LocalItemNameDialogs);
        var page = Assert.IsType<ItemViewModel>(main.CurrentViewModel);
        Assert.True(page.Item.IsLocalOnly);
        Assert.False(page.Item.IsOwned);
        Assert.Equal("手で作った衣装", page.Item.Local.DisplayName);

        // 作った商品はディスクにもある（命令を通って書かれた）
        Assert.NotNull(await app.Store.Items.LoadAsync(page.Item.Id));

        // 全件を読み直さず、検索の写しへ1件足している
        Assert.Contains(main.Search.ListItems, card => card.Item.Id == page.Item.Id);

        // 仮IDの商品なので、BOOTH へは一度も問い合わせない
        Assert.Empty(app.Booth.Requests);

        // 後から付けられるよう、ファイルの欄の「追加…」は押せる
        Assert.True(page.AttachFilesCommand.CanExecute(null));
    });

    [Fact]
    public Task 窓を閉じたら何も作らない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var before = main.CurrentViewModel;

        await main.CreateLocalItemAsync();

        Assert.Single(app.LocalItemNameDialogs);
        Assert.Empty(app.Store.Items.EnumerateItemIds());
        Assert.Same(before, main.CurrentViewModel);
    });

    /// <summary>空白だけの名前では窓の「登録」を押せない（作ると画面に local-… しか出ない）。</summary>
    [Fact]
    public void 空白だけの名前では登録を押せない()
    {
        var model = new LocalItemNameDialogViewModel();
        Assert.False(model.CanCommit);

        model.Name = "   ";
        Assert.False(model.CanCommit);

        model.Name = "衣装";
        Assert.True(model.CanCommit);
    }
}
