using BoothAssetManager.App.Tests.Support;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.App.Views;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 商品ページの中身が替わったとき、流した位置をどうするか（保つ・先頭へ戻す・覚えた位置へ戻す）。
///
/// 同じ型の ViewModel に替わるとき画面は作り直されないので、何もしないと前の商品で流した位置が次の商品に残る。
/// どうするかは「どこに出ているか」と「どう来たか（進んだ・戻る／進む・同じ商品の開き直し）」で決まり、
/// 覚えた位置は画面の履歴の1件ごとに持つ。ここでは画面（View）の代わりに、位置を答える手順を試験が預ける。
/// 実際に位置が合った絵は、tools/ViewShot の item-page-back-after-scroll・item-page-next-after-scroll・
/// folder-item-reselect・modification-item-reselect などの場面で見る
/// </summary>
public class ItemViewScrollTests
{
    private static readonly ItemRecord First = Make.Item("1000001", "作り物の衣装");
    private static readonly ItemRecord Second = Make.Item("1000002", "作り物の髪型");

    private static async Task<MainViewModel> StartAsync(TestApp app)
    {
        await app.AddItemAsync(First);
        await app.AddItemAsync(Second);
        return await app.StartAsync();
    }

    private static ItemViewModel Page(MainViewModel main) => Assert.IsType<ItemViewModel>(main.CurrentViewModel);

    /// <summary>画面（View）が DataContext の付け替えでやること：前の画面と今の画面から、扱いを決める。</summary>
    private static ItemView.ScrollOnOpen Arrive(ItemViewModel? previous, MainViewModel main)
        => ItemView.ScrollFor(previous, Page(main));

    /// <summary>戻る・進むの開き直しは保存先を読むので、その商品のページに替わるまで待つ。</summary>
    private static async Task UntilPageAsync(MainViewModel main, TestApp app, string itemId)
    {
        await UiThread.Until(
            () => main.CurrentViewModel is ItemViewModel page && page.Item.Id == itemId,
            "戻る・進むで商品ページが開く");
        await app.SettleAsync();
    }

    [Fact]
    public Task 組み込んだ商品ページは_別の商品へ替わったら先頭へ戻す() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);

        ItemViewModel Embedded(ItemRecord item) => new(item, app.Services, main, main.Thumbnails) { IsEmbedded = true };

        // フォルダビュー・改変の画面で選び直した
        Assert.Equal(ItemView.ScrollOnOpen.Top, ItemView.ScrollFor(Embedded(First), Embedded(Second)));

        // 同じ商品を開き直した（取り直した後・ファイルを外した後）。見ていた所を保つ
        Assert.Equal(ItemView.ScrollOnOpen.Keep, ItemView.ScrollFor(Embedded(First), Embedded(First)));

        // 作りたての画面は先頭から始まるので、戻す物が無い
        Assert.Equal(ItemView.ScrollOnOpen.Keep, ItemView.ScrollFor(null, Embedded(First)));
    });

    [Fact]
    public Task 主の窓で別の商品へ進んだら_先頭から出す() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);

        // 検索から開いた。画面は作りたてで先頭にいるので、戻す物が無い
        main.ShowItem(First);
        var first = Page(main);
        Assert.Equal(ItemView.ScrollOnOpen.Keep, Arrive(null, main));

        // 商品から商品へ（説明の中のリンク・対応アバター）。画面は使い回されるので、先頭へ戻す
        first.ScrollReader = () => 1500;
        main.ShowItem(Second);
        Assert.Equal(ItemView.ScrollOnOpen.Top, Arrive(first, main));

        // 同じ商品へ進んだとき（説明に自分へのリンクがある）も、進んだのだから先頭から
        var second = Page(main);
        main.ShowItem(Second);
        Assert.Equal(ItemView.ScrollOnOpen.Top, Arrive(second, main));
    });

    [Fact]
    public Task 主の窓で戻る_進むで来たら_その商品を離れたときの位置へ戻す() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        main.ShowItem(First);
        var first = Page(main);
        first.ScrollReader = () => 1500;

        main.ShowItem(Second);
        var second = Page(main);
        second.ScrollReader = () => 600;

        // 戻る：1つ目を離れたときの位置（2つ目で流した位置ではない）
        main.GoBack();
        await UntilPageAsync(main, app, First.Id);
        Assert.Equal(ItemView.ScrollOnOpen.Restore, Arrive(second, main));
        Assert.Equal(1500, Page(main).TakeRestoreScrollOffset());

        // 戻った先で流し直してから進む：2つ目を離れたときの位置
        var back = Page(main);
        back.ScrollReader = () => 200;
        main.GoForward();
        await UntilPageAsync(main, app, Second.Id);
        Assert.Equal(ItemView.ScrollOnOpen.Restore, Arrive(back, main));
        Assert.Equal(600, Page(main).TakeRestoreScrollOffset());

        // もう一度戻る：流し直した位置。履歴の1件ごとに、離れるたびに控え直す
        Page(main).ScrollReader = () => 600;
        main.GoBack();
        await UntilPageAsync(main, app, First.Id);
        Assert.Equal(200, Page(main).TakeRestoreScrollOffset());
    });

    [Fact]
    public Task 別の種類の画面を挟んで戻っても_離れたときの位置へ戻す() => TestApp.Run(async app =>
    {
        // 統計の画面へ移ると商品ページの画面は捨てられ、戻ると作りたての画面へ差し込まれる（前の画面は無い）
        var main = await StartAsync(app);
        main.ShowItem(First);
        Page(main).ScrollReader = () => 1500;
        main.ShowStatsCommand.Execute(null);
        await app.SettleAsync();

        main.GoBack();
        await UntilPageAsync(main, app, First.Id);

        Assert.Equal(ItemView.ScrollOnOpen.Restore, Arrive(null, main));
        Assert.Equal(1500, Page(main).TakeRestoreScrollOffset());
    });

    [Fact]
    public Task 先頭で離れた商品へ戻ったら_先頭へ戻す() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        main.ShowItem(First);
        var first = Page(main);
        first.ScrollReader = () => 0;
        main.ShowItem(Second);
        var second = Page(main);
        second.ScrollReader = () => 600;

        main.GoBack();
        await UntilPageAsync(main, app, First.Id);

        // 「戻す」で位置が 0。画面は先頭へ戻す（2つ目の位置を残さない）
        Assert.Equal(ItemView.ScrollOnOpen.Restore, Arrive(second, main));
        Assert.Equal(0, Page(main).TakeRestoreScrollOffset());
    });

    [Fact]
    public Task 同じ商品の開き直しは_今の位置を保つ() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        main.ShowItem(First);
        var first = Page(main);
        first.ScrollReader = () => 1500;

        // 取り直した後・ファイルを外した後の道
        main.ReplaceItem(First);
        Assert.NotSame(first, Page(main));
        Assert.Equal(ItemView.ScrollOnOpen.Keep, Arrive(first, main));

        // ID を変えた後の開き直しは、同じ商品でも ID が違う。ID では見分けない
        var reopened = Page(main);
        main.ReplaceItem(Second);
        Assert.Equal(ItemView.ScrollOnOpen.Keep, Arrive(reopened, main));
    });

    [Fact]
    public Task 覚えた位置は1回だけ渡す() => TestApp.Run(async app =>
    {
        // 画面が作り直されて同じ商品ページがもう一度差し込まれても、人がその後に流した位置から引き戻さない
        var main = await StartAsync(app);
        main.ShowItem(First);
        Page(main).ScrollReader = () => 1500;
        main.ShowItem(Second);

        main.GoBack();
        await UntilPageAsync(main, app, First.Id);
        var page = Page(main);

        Assert.Equal(1500, page.TakeRestoreScrollOffset());
        Assert.Null(page.TakeRestoreScrollOffset());
        Assert.Equal(ItemView.ScrollOnOpen.Keep, ItemView.ScrollFor(null, page));
    });

    [Fact]
    public Task 画面が付いていない商品ページは_先頭として控える() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        main.ShowItem(First);

        Assert.Equal(0, Page(main).CaptureScrollOffset());
    });
}
