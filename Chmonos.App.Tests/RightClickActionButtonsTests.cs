using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 基本の操作は右クリックと画面の両方に置く（ユーザ判断 2026-10-06・メモ73）。
/// 取り込み画面の3つの一覧の「エクスプローラで開く」と、商品の画像の「この画像を削除」が、
/// 右クリックと同じ命令・同じ押せる条件につながっていることを確かめる。
/// 試験の中では画面の資源が無く View そのものは作れないので、つなぎは元の文（XAML）を読み、押せる条件は ViewModel で確かめる。
/// </summary>
public class RightClickActionButtonsTests
{
    [Theory]
    [InlineData("Folders")]
    [InlineData("Watched")]
    [InlineData("History")]
    public void 取り込みの一覧の各行は_右クリックと同じ命令で場所を開くボタンを持つ(string list)
    {
        var rowTemplate = ListOf(list);

        var menu = Assert.Single(rowTemplate.Descendants(), e => e.Name.LocalName == "MenuItem"
            && (string?)e.Attribute("Header") == "エクスプローラで開く");
        var button = Assert.Single(rowTemplate.Descendants(), e => e.Name.LocalName == "Button"
            && Id(e) == "ImportRowReveal");

        Assert.Contains("RevealFolderCommand", (string?)menu.Attribute("Command"));
        Assert.Contains("RevealFolderCommand", (string?)button.Attribute("Command"));
        Assert.Equal("{Binding}", (string?)button.Attribute("CommandParameter"));
        // 赤にしない（開くだけで何も消えない・V4）
        Assert.DoesNotContain("DangerButton", (string?)button.Attribute("Style") ?? "");
    }

    [Fact]
    public Task 場所を開く命令は_文字列の行だけで押せる_右クリックと同じ条件() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();

        // まだ確かめていない場所は押せる（見つからないと分かった行を押せなくするのは ImportRowButtonTests）。行の値が無いときだけ押せない
        Assert.True(main.Import.RevealFolderCommand.CanExecute(@"E:\作り物\外付け"));
        Assert.False(main.Import.RevealFolderCommand.CanExecute(null));
    });

    [Fact]
    public void 画像の削除ボタンは_右クリックの項目と同じ命令につながる()
    {
        var document = XDocument.Load(Path.Combine(ViewsFolder(), "ItemGalleryPanel.xaml"));
        var menu = Assert.Single(document.Descendants(), e => e.Name.LocalName == "MenuItem"
            && (string?)e.Attribute("Header") == "この画像を削除");
        var button = Assert.Single(document.Descendants(), e => e.Name.LocalName == "Button"
            && Id(e) == "GalleryRemoveImage");

        Assert.Equal("{Binding RemoveImageCommand}", (string?)menu.Attribute("Command"));
        Assert.Equal("{Binding RemoveImageCommand}", (string?)button.Attribute("Command"));
        Assert.Contains("DangerButton", (string?)button.Attribute("Style"));
    }

    [Fact]
    public Task 画像の削除ボタンは_自分で足した画像を選んでいるときだけ押せて_理由を言う() => TestApp.Run(async app =>
    {
        var item = Make.Item("9900402", "作り物の衣装");
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        page.Images.Clear();

        // 画像が無い商品には、消す相手がいないのでボタン自体を出さない
        Assert.False(page.HasCurrentImage);
        Assert.False(page.RemoveImageCommand.CanExecute(null));

        var booth = new GalleryImage { Path = @"x\booth.png", FileName = "booth.png", Number = 1 };
        var mine = new GalleryImage { Path = @"x\mine.png", FileName = "mine.png", Number = 2, IsUserAdded = true };
        page.Images.Add(booth);
        page.Images.Add(mine);

        // BOOTH の画像は消せない。出したまま押せなくして、右クリックと同じ理由を言う
        page.SelectImageCommand.Execute(booth);
        Assert.True(page.HasCurrentImage);
        Assert.False(page.RemoveImageCommand.CanExecute(null));
        Assert.Equal("自分で追加した画像だけ消せます", page.RemoveImageButtonTip);

        page.SelectImageCommand.Execute(mine);
        Assert.True(page.RemoveImageCommand.CanExecute(null));
        Assert.Equal("ファイルごと消します。元に戻せません（押すと確かめます）。", page.RemoveImageButtonTip);
    });

    /// <summary>取り込み画面の一覧（取り込み対象・監視・履歴）。ItemsSource の名前で探す。</summary>
    private static XElement ListOf(string list)
    {
        var document = XDocument.Load(Path.Combine(ViewsFolder(), "ImportView.xaml"));
        return Assert.Single(document.Descendants(), e => e.Name.LocalName == "ContentItemsControl"
            && (string?)e.Attribute("ItemsSource") == $"{{Binding {list}}}");
    }

    private static string? Id(XElement element)
        => (string?)element.Attributes().FirstOrDefault(a => a.Name.LocalName == "AutomationProperties.AutomationId");

    private static string ViewsFolder([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views");
}
