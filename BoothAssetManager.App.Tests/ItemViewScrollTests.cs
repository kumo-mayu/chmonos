using BoothAssetManager.App.Tests.Support;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.App.Views;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 商品ページの中身が替わったとき、流した位置を先頭へ戻すか。
///
/// 同じ型の ViewModel に替わるとき画面は作り直されないので、前の商品で流した位置が次の商品に残る。
/// 戻すかどうかは「どこに出ているか」と「同じ商品か」で決まる。実際に先頭へ戻った絵は、
/// tools/ViewShot の folder-item-reselect・modification-item-reselect の場面で見る
/// </summary>
public class ItemViewScrollTests
{
    [Fact]
    public Task 組み込んだ商品ページは_別の商品へ替わったら先頭へ戻す() => TestApp.Run(async app =>
    {
        var first = Make.Item("1000001", "作り物の衣装");
        var second = Make.Item("1000002", "作り物の髪型");
        await app.AddItemAsync(first);
        await app.AddItemAsync(second);
        var main = await app.StartAsync();

        ItemViewModel Page(ItemRecord item, bool embedded)
            => new(item, app.Services, main, main.Thumbnails) { IsEmbedded = embedded };

        // フォルダビュー・改変の画面で選び直した
        Assert.True(ItemView.ShouldReturnToTop(Page(first, embedded: true), Page(second, embedded: true)));

        // 同じ商品を開き直した（取り直した後・ファイルを外した後）。見ていた所を保つ
        Assert.False(ItemView.ShouldReturnToTop(Page(first, embedded: true), Page(first, embedded: true)));

        // 主の窓で商品から商品へ移った。今の動き（位置が残る）のまま
        Assert.False(ItemView.ShouldReturnToTop(Page(first, embedded: false), Page(second, embedded: false)));

        // 作りたての画面は先頭から始まるので、戻す物が無い
        Assert.False(ItemView.ShouldReturnToTop(null, Page(first, embedded: true)));
    });
}
