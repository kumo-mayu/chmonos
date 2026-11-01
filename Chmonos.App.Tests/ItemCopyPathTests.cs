using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Xunit;

namespace Chmonos.App.Tests;

/// <summary>ファイルの場所を写す（字が吹き出しにしか無く、選べないので操作を置く）。</summary>
public class ItemCopyPathTests
{
    private static async Task<ItemViewModel> OpenAsync(TestApp app)
    {
        var item = Make.Item("9900801", "作り物の商品");
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        main.ShowItem((await app.Store.Items.LoadAsync(item.Id))!);
        return Assert.IsType<ItemViewModel>(main.CurrentViewModel);
    }

    [Fact]
    public Task パスをコピーすると_その場所が写り_欄の下で言う() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app);

        page.CopyPathCommand.Execute(@"D:\作り物\a.zip");

        Assert.Equal([@"D:\作り物\a.zip"], app.Copied);
        Assert.Equal("パスをコピーしました。", page.UnityRecordNotice);
    });

    [Fact]
    public Task 写せなければ_もう一度押すよう言う() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app);
        app.Services.CopyText = _ => false;

        page.CopyPathCommand.Execute(@"D:\作り物\a.zip");

        Assert.Equal("コピーできませんでした。もう一度押してください。", page.UnityRecordNotice);
    });

    [Fact]
    public Task 場所の無い行では押せない() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app);

        Assert.False(page.CopyPathCommand.CanExecute(null));
        Assert.False(page.CopyPathCommand.CanExecute(string.Empty));
    });

    [Fact]
    public Task 省いて出すパスは_右クリックで全文を写せる() => TestApp.Run(_ =>
    {
        var line = new Chmonos.App.Controls.PathLine { Path = @"D:\作り物\とても長いパス" };

        var item = Assert.IsType<System.Windows.Controls.MenuItem>(Assert.Single(line.ContextMenu!.Items.Cast<object>()));
        Assert.Equal("パスをコピー", item.Header);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, 0, 0, true, true)]
    [InlineData(true, 0, 0, true, false)]
    [InlineData(false, 40, 0, true, false)]
    [InlineData(false, 0, 0, false, false)]
    public void 字の上を動かさずに押して_選んでいないときだけ開く(bool selected, double moveX, double moveY, bool overText, bool expected)
        => Assert.Equal(expected, Chmonos.App.Controls.SelectableLink.IsClick(selected, new System.Windows.Vector(moveX, moveY), overText));
}
