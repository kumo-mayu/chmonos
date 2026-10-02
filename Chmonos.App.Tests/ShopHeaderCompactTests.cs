using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// ショップの中の上の段（バナー・見出しと集計・メモ）は、商品の一覧を流し始めたら1行に縮め、一番上まで戻すと元に戻す（2026-10-02 のメモ7-⑤）。
/// </summary>
public class ShopHeaderCompactTests
{
    [Theory]
    // 一番上では縮めない（端数は一番上として扱う）
    [InlineData(false, 0, 2000, 400, false)]
    [InlineData(false, 0.4, 2000, 400, false)]
    // 流し始めたら縮める
    [InlineData(false, 48, 2000, 400, true)]
    // 縮めると流せる量が残らない短い一覧では縮めない（一番上へ押し戻され、開いたり閉じたりを繰り返すので）
    [InlineData(false, 48, 300, 400, false)]
    [InlineData(false, 48, 400, 400, false)]
    // 縮めた後は、一番上へ戻すまで縮めたまま（縮めて流せる量が減っても戻さない）
    [InlineData(true, 10, 100, 400, true)]
    [InlineData(true, 0, 2000, 400, false)]
    public void 流れの位置で縮めるかを決める(bool compact, double offset, double scrollable, double gain, bool expected)
    {
        Assert.Equal(expected, ShopViewModel.NextHeaderCompact(compact, offset, scrollable, gain));
    }

    [Fact]
    public Task 縮めるとバナーとメモを隠し_メモは縮めた行から開け_一番上へ戻すと元に戻る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        await main.ShowShopAsync("sample-shop");
        var shop = Assert.IsType<ShopViewModel>(main.CurrentViewModel);
        await app.SettleAsync();

        Assert.False(shop.IsHeaderCompact);
        Assert.True(shop.ShowFullHeader);
        Assert.True(shop.ShowMemo);

        shop.NoteListScrolled(offset: 120, scrollable: 2000, collapseGain: 400);
        Assert.True(shop.IsHeaderCompact);
        Assert.False(shop.ShowFullHeader);
        Assert.False(shop.ShowMemo);
        Assert.Equal("ショップのメモを開く", shop.MemoToggleName);

        shop.ToggleMemoCommand.Execute(null);
        Assert.True(shop.ShowMemo);
        Assert.Equal("ショップのメモを折りたたむ", shop.MemoToggleName);

        // メモを開いて一覧が狭まり、流せる量が減っても縮めたまま
        shop.NoteListScrolled(offset: 120, scrollable: 150, collapseGain: 400);
        Assert.True(shop.IsHeaderCompact);

        shop.NoteListScrolled(offset: 0, scrollable: 2000, collapseGain: 400);
        Assert.False(shop.IsHeaderCompact);
        Assert.True(shop.ShowFullHeader);
        Assert.True(shop.ShowMemo);

        // 縮めた行で開いたメモは、元に戻したら閉じた扱い。次に縮めたときは閉じた形から
        Assert.False(shop.IsMemoOpen);
        shop.NoteListScrolled(offset: 120, scrollable: 2000, collapseGain: 400);
        Assert.False(shop.ShowMemo);
    });
}
