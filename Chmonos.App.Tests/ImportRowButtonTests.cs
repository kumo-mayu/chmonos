using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 取り込み画面の3つの一覧の行のボタン（ユーザ判断 2026-10-06・メモ75）。
/// 赤いボタンは1語（外す・消す）に短くして同じ幅にそろえ、何を外す・消すかは読み上げの名前で言う。
/// 場所が見つからない行は「エクスプローラで開く」を押せなくして理由を言う。
/// </summary>
public class ImportRowButtonTests
{
    [Theory]
    [InlineData("Folders", "ImportFolderRemove", "外す", "を取り込み対象から外す")]
    [InlineData("Watched", "ImportWatchedRemove", "外す", "を監視から外す")]
    [InlineData("History", "ImportHistoryForget", "消す", "を履歴から消す")]
    public void 赤いボタンは1語で同じ幅_何を外す消すかは読み上げの名前で言う(string list, string id, string label, string nameEnd)
    {
        var button = Assert.Single(ListOf(list).Descendants(), e => e.Name.LocalName == "Button" && Id(e) == id);

        Assert.Equal(label, (string?)button.Attribute("Content"));
        Assert.Equal("60", (string?)button.Attribute("Width"));
        Assert.Null(button.Attribute("MinWidth"));
        Assert.EndsWith(nameEnd + "}", (string?)button.Attributes().First(a => a.Name.LocalName == "AutomationProperties.Name"));
    }

    [Fact]
    public Task 取り込みを始めるボタンの文は_開始() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;

        Assert.Equal("開始", import.StartText);
    });

    [Theory]
    [InlineData("Folders")]
    [InlineData("Watched")]
    [InlineData("History")]
    public void 場所を開くボタンと右クリックは_押せない理由を吹き出しで言う(string list)
    {
        var rowTemplate = ListOf(list);
        var menu = Assert.Single(rowTemplate.Descendants(), e => e.Name.LocalName == "MenuItem"
            && (string?)e.Attribute("Header") == "エクスプローラで開く");
        var button = Assert.Single(rowTemplate.Descendants(), e => e.Name.LocalName == "Button" && Id(e) == "ImportRowReveal");

        Assert.Equal("True", Attr(menu, "ToolTipService.ShowOnDisabled"));
        Assert.Equal("True", Attr(button, "ToolTipService.ShowOnDisabled"));
        Assert.Contains(menu.Descendants(), e => e.Name.LocalName == "MultiBinding"
            && ((string?)e.Attribute("Converter") ?? "").Contains("PlaceTipConverter"));
        Assert.Contains(button.Descendants(), e => e.Name.LocalName == "MultiBinding"
            && ((string?)e.Attribute("Converter") ?? "").Contains("PlaceTipConverter"));
    }

    [Fact]
    public Task 見つからない場所は押せず理由を言い_在る場所とファイルは押せる() => TestApp.Run(async app =>
    {
        var folder = Path.GetDirectoryName(app.NewFile(@"downloads\costume.zip"))!;
        var zip = Path.Combine(folder, "costume.zip");
        var missing = Path.Combine(folder, "移した後");
        var detached = Path.Combine(UnusedDrive(), "外付け", "素材");
        var import = (await app.StartAsync()).Import;

        import.Folders.Add(folder);
        import.Folders.Add(missing);
        import.History.Add(zip);
        import.History.Add(detached);
        await import.PlaceCheckTask;
        await app.SettleAsync();

        Assert.True(import.RevealFolderCommand.CanExecute(folder));
        Assert.Null(import.RevealBlockedReason(folder));

        // フォルダにもファイルにも無い
        Assert.False(import.RevealFolderCommand.CanExecute(missing));
        Assert.Equal("見つかりません", import.RevealBlockedReason(missing));

        // 履歴にはファイルも並ぶ。ファイルとして在れば開ける
        Assert.True(import.RevealFolderCommand.CanExecute(zip));

        // つながっていないドライブの上は、見つからないとは言わない
        Assert.False(import.RevealFolderCommand.CanExecute(detached));
        Assert.Equal("今つながっていません", import.RevealBlockedReason(detached));

        // 押せるかを何度聞いても、確かめの版は進まない（聞くたびにディスクを見ていない）
        var version = import.PlacesVersion;
        for (var i = 0; i < 5; i++)
        {
            import.RevealFolderCommand.CanExecute(missing);
        }

        Assert.Equal(version, import.PlacesVersion);
    });

    [Fact]
    public Task まだ確かめていない行は押せる() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;

        Assert.True(import.RevealFolderCommand.CanExecute(@"D:\まだ一覧に無い"));
        Assert.False(import.RevealFolderCommand.CanExecute(null));
    });

    [Fact]
    public Task 吹き出しは_押せないときだけ理由_押せるボタンは開くと言い_右クリックは黙る() => TestApp.Run(async app =>
    {
        var folder = Path.GetDirectoryName(app.NewFile(@"downloads\costume.zip"))!;
        var missing = Path.Combine(folder, "移した後");
        var import = (await app.StartAsync()).Import;
        import.Folders.Add(folder);
        import.Folders.Add(missing);
        await import.PlaceCheckTask;
        await app.SettleAsync();
        var converter = PlaceTipConverter.Instance;
        object[] missingRow = [missing, import, import.PlacesVersion];
        object[] openRow = [folder, import, import.PlacesVersion];

        Assert.Equal("見つかりません", converter.Convert(missingRow, typeof(object), null, null!));
        Assert.Equal("見つかりません", converter.Convert(missingRow, typeof(object), "menu", null!));
        Assert.Equal("エクスプローラで開きます。", converter.Convert(openRow, typeof(object), null, null!));
        Assert.Null(converter.Convert(openRow, typeof(object), "menu", null!));
    });

    private static string UnusedDrive()
    {
        var used = DriveInfo.GetDrives().Select(drive => drive.Name[0]).ToHashSet();
        var letter = "ZYXWVUTSRQPONMLKJIHGFED".First(c => !used.Contains(c));
        return $@"{letter}:\";
    }

    private static string? Attr(XElement element, string name)
        => (string?)element.Attributes().FirstOrDefault(a => a.Name.LocalName == name);

    private static XElement ListOf(string list)
    {
        var document = XDocument.Load(Path.Combine(ViewsFolder(), "ImportView.xaml"));
        return Assert.Single(document.Descendants(), e => e.Name.LocalName == "ContentItemsControl"
            && (string?)e.Attribute("ItemsSource") == $"{{Binding {list}}}");
    }

    private static string? Id(XElement element) => Attr(element, "AutomationProperties.AutomationId");

    private static string ViewsFolder([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views");
}
