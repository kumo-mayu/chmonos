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

    [Theory]
    // 流した量と同じだけ詰める
    [InlineData(0, 30, 2000, 400, 30)]
    // 一番上まで戻したら元に戻る（端数は一番上）
    [InlineData(200, 0.4, 1800, 400, 0)]
    // 途中で少し戻しても詰めた量は減らさない（戻した分だけ開くと一覧がずれる）
    [InlineData(200, 120, 1800, 400, 200)]
    // 詰め切る高さを越えては詰めない
    [InlineData(0, 900, 2000, 400, 400)]
    // 詰めた後に流せる量が1残る所まで（短い一覧で一番上へ押し戻されない）。元の量は 今の流せる量＋詰めた量
    [InlineData(100, 260, 150, 400, 249)]
    [InlineData(0, 240, 250, 400, 240)]
    [InlineData(0, 300, 250, 400, 249)]
    public void 流した量に合わせて少しずつ詰める(double current, double offset, double scrollable, double max, double expected)
    {
        Assert.Equal(expected, ShopViewModel.NextHeaderShrink(current, offset, scrollable, max), 3);
    }

    [Fact]
    public Task 少しずつ詰め_詰め切ったときだけ1行になり_戻すと元に戻る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        await main.ShowShopAsync("sample-shop");
        var shop = Assert.IsType<ShopViewModel>(main.CurrentViewModel);
        await app.SettleAsync();

        shop.NoteListScrolled(offset: 40, scrollable: 2000, maxShrink: 300);
        Assert.Equal(40, shop.HeaderShrink);
        Assert.False(shop.IsHeaderCompact);
        Assert.True(shop.ShowFullHeader);

        shop.NoteListScrolled(offset: 299.8, scrollable: 1700, maxShrink: 300);
        Assert.True(shop.IsHeaderCompact);

        shop.NoteListScrolled(offset: 0, scrollable: 2000, maxShrink: 300);
        Assert.False(shop.IsHeaderCompact);
        Assert.Equal(0, shop.HeaderShrink);
    });

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

        shop.NoteListScrolled(offset: 120, scrollable: 2000, maxShrink: 100);
        Assert.True(shop.IsHeaderCompact);
        Assert.False(shop.ShowFullHeader);
        Assert.False(shop.ShowMemo);
        Assert.Equal("ショップのメモを開く", shop.MemoToggleName);

        shop.ToggleMemoCommand.Execute(null);
        Assert.True(shop.ShowMemo);
        Assert.Equal("ショップのメモを折りたたむ", shop.MemoToggleName);

        // メモを開いて一覧が狭まり、流せる量が減っても縮めたまま
        shop.NoteListScrolled(offset: 120, scrollable: 150, maxShrink: 100);
        Assert.True(shop.IsHeaderCompact);

        shop.NoteListScrolled(offset: 0, scrollable: 2000, maxShrink: 100);
        Assert.False(shop.IsHeaderCompact);
        Assert.True(shop.ShowFullHeader);
        Assert.True(shop.ShowMemo);

        // 縮めた行で開いたメモは、元に戻したら閉じた扱い。次に縮めたときは閉じた形から
        Assert.False(shop.IsMemoOpen);
        shop.NoteListScrolled(offset: 120, scrollable: 2000, maxShrink: 100);
        Assert.False(shop.ShowMemo);
    });
}
