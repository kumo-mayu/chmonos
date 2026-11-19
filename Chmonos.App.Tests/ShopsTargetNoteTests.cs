using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// ショップ一覧の「対象 ▾」（open.md 31・案C）。ボタンは検索画面と同じ字にし、今の対象は横の小さな字で常に見せる
/// （ユーザ指示 2026-09-16「対象がどちらかが常にわかるようにする」）。対象を変えたら、その字も読み上げの名前も追従する。
/// </summary>
public sealed class ShopsTargetNoteTests
{
    private static ShopCardViewModel Card(string? memo) => new()
    {
        Shop = new Core.Services.ShopSummary { Subdomain = "sample", Name = "作り物のショップ", KnownCount = 1, OwnedCount = 1, SpentYen = 0 },
        Memo = memo,
    };

    /// <summary>open.md「カードに乗せるとショップのメモを出す」：字がある時だけ。長いメモは先頭の数行で切る。</summary>
    [Fact]
    public void カードの吹き出しは_メモに字がある時だけ出し_長いメモは先頭の数行で切る()
    {
        Assert.False(Card(null).HasMemoTip);
        Assert.False(Card("").HasMemoTip);
        Assert.False(Card(" \r\n \n").HasMemoTip);

        var short_ = Card("問い合わせはDMで\r\n返信は遅め");
        Assert.True(short_.HasMemoTip);
        Assert.Equal("問い合わせはDMで\n返信は遅め", short_.MemoTip);

        var lines = Enumerable.Range(1, ShopCardViewModel.MemoTipMaxLines).Select(n => $"{n}行目");
        Assert.Equal(string.Join('\n', lines), Card(string.Join('\n', lines)).MemoTip);

        var longer = string.Join('\n', Enumerable.Range(1, ShopCardViewModel.MemoTipMaxLines + 3).Select(n => $"{n}行目"));
        var tip = Card(longer).MemoTip;
        Assert.EndsWith("\n…", tip);
        Assert.DoesNotContain($"{ShopCardViewModel.MemoTipMaxLines + 1}行目", tip);
        Assert.Contains($"{ShopCardViewModel.MemoTipMaxLines}行目", tip);
    }

    [Fact]
    public Task 今の対象は横の字に出て_対象を切り替えると追従する() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        main.ShowShopsCommand.Execute(null);
        var shops = Assert.IsType<ShopsViewModel>(main.CurrentViewModel);

        var changed = new List<string?>();
        shops.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal("ショップ名・メモ", shops.TargetsNote);
        Assert.Equal("対象：ショップ名・メモ", shops.TargetsText);

        shops.SearchMemos = false;
        Assert.Equal("ショップ名", shops.TargetsNote);
        Assert.Contains(nameof(ShopsViewModel.TargetsNote), changed);

        shops.SearchMemos = true;
        shops.SearchNames = false;
        Assert.Equal("メモ", shops.TargetsNote);

        // 最後の1つは外せない（対象が空にならない）
        shops.SearchMemos = false;
        Assert.Equal("メモ", shops.TargetsNote);
    });
}
