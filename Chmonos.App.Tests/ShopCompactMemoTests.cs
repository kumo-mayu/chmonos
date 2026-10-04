using System.ComponentModel;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>ショップの1行の見出しに出すメモの先頭1行（メモ28）。メモがあるときだけ出し、無いときは何も出さない。</summary>
public class ShopCompactMemoTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   \n  \n", "")]
    [InlineData("利用規約：改変可", "利用規約：改変可")]
    // 2行以上あるときは、1行に収まって切れていなくても「…」を付ける（メモ40）。空行だけの続きは数えない
    [InlineData("1行目\n2行目", "1行目…")]
    [InlineData("1行目\r\n2行目", "1行目…")]
    [InlineData("1行目\n\n  \n", "1行目")]
    // 空行から書き始めたメモでも、見出しが空に見えない
    [InlineData("\n\n  最初の字のある行  \n次", "最初の字のある行…")]
    public void メモの先頭の1行は_字のある最初の行を前後の空白なしで返す(string? memo, string expected)
    {
        Assert.Equal(expected, ShopViewModel.FirstLineOf(memo));
    }

    [Fact]
    public Task メモを書くと_見出しのメモの行と出す印が変わり_消すと出さない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        await main.ShowShopAsync("sample-shop");
        var shop = Assert.IsType<ShopViewModel>(main.CurrentViewModel);
        await app.SettleAsync();

        Assert.False(shop.HasMemoLine);

        var changed = new List<string?>();
        ((INotifyPropertyChanged)shop).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        shop.Memo = "作り物のメモ\n2行目";

        Assert.True(shop.HasMemoLine);
        Assert.Equal("作り物のメモ…", shop.MemoFirstLine);
        Assert.Contains(nameof(ShopViewModel.MemoFirstLine), changed);
        Assert.Contains(nameof(ShopViewModel.HasMemoLine), changed);

        shop.Memo = string.Empty;
        Assert.False(shop.HasMemoLine);
        await shop.FlushPendingWritesAsync();
    });
}
