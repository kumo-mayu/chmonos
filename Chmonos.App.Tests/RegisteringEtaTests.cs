using Chmonos.App.ViewModels;
using Xunit;

namespace Chmonos.App.Tests;

/// <summary>
/// 未確定で登録するときの「BOOTHへあと n 件・約 m 分」（メモ34）。
/// 目安は残りの数 × 問い合わせの間隔。時計には頼らず、数と間隔だけで決まる。
/// </summary>
public class RegisteringEtaTests
{
    [Theory]
    [InlineData(2, 1500, "BOOTHへあと 2 件・1分以内")]
    [InlineData(39, 1500, "BOOTHへあと 39 件・1分以内")]
    [InlineData(41, 1500, "BOOTHへあと 41 件・約 2 分")]
    [InlineData(40, 3000, "BOOTHへあと 40 件・約 2 分")]
    [InlineData(2300, 1500, "BOOTHへあと 2300 件・約 58 分")]
    [InlineData(2401, 1500, "BOOTHへあと 2401 件・約 1 時間 1 分")]
    public void 残りの数と間隔から目安の時間を出す(int left, int intervalMs, string expected)
        => Assert.Equal(expected, ResolveViewModel.RequestsLeftText(left, intervalMs));

    [Fact]
    public void 分からないときと残りが無いときは何も出さない()
    {
        Assert.Equal(string.Empty, ResolveViewModel.RequestsLeftText(null, 1500));
        Assert.Equal(string.Empty, ResolveViewModel.RequestsLeftText(0, 1500));
    }

    [Fact]
    public void 残りが減ると目安も減る()
    {
        Assert.Equal("BOOTHへあと 100 件・約 3 分", ResolveViewModel.RequestsLeftText(100, 1500));
        Assert.Equal("BOOTHへあと 10 件・1分以内", ResolveViewModel.RequestsLeftText(10, 1500));
    }
}
