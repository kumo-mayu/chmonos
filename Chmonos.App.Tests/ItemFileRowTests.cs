using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページの「手元のファイル」の行の札：見つかりません・取り外しているドライブ・壊れたzip の出し分け。
///
/// 札は「記録の場所に今あるか」と「取り込みが付けた印」の組み合わせで決まる。前は、ファイルを消した保存先・
/// 外付けを外した保存先・壊れた zip を置いた保存先をそれぞれ作って、商品ページを撮って確かめていた。
/// </summary>
public class ItemFileRowTests
{
    private static LocalFileRow Row(
        IReadOnlyList<string> paths, FilePresence presence = FilePresence.Present, bool broken = false) => new()
    {
        Hash = Make.HashOf("row"),
        FileName = paths.Count > 0 ? System.IO.Path.GetFileName(paths[0]) : "(見つかりません)",
        SizeText = "3 B",
        Paths = paths,
        IsBrokenArchive = broken,
        Presence = presence,
    };

    // ---- 行だけで決まる出し分け ----

    [Fact]
    public void 在るファイルは_札を出さず_開ける()
    {
        var row = Row([@"D:\files\sample.zip"]);

        Assert.False(row.IsMissing);
        Assert.False(row.IsOnDetachedDrive);
        Assert.False(row.ShowsBrokenArchive);
        Assert.True(row.CanReveal);
        Assert.Equal("D:\\files\\sample.zip\n押すと、エクスプローラでこのファイルの場所を開きます。", row.PathToolTip);
    }

    [Fact]
    public void 記録の場所に無いファイルは_見つかりませんを出し_開けない()
    {
        var row = Row([@"D:\files\sample.zip"], FilePresence.Missing);

        Assert.True(row.IsMissing);
        Assert.False(row.IsOnDetachedDrive);
        Assert.False(row.CanReveal);
        Assert.Equal("D:\\files\\sample.zip\nファイルが見つかりません。", row.PathToolTip);
    }

    [Fact]
    public void 取り外しているドライブの上のファイルは_見つかりませんと分ける()
    {
        // 無くなったのではなく今は見えないだけ。次の一手は「取り込み直す」ではなく「つなぐ」
        var row = Row([@"Q:\files\sample.zip"], FilePresence.OnDetachedDrive);

        Assert.False(row.IsMissing);
        Assert.True(row.IsOnDetachedDrive);
        Assert.False(row.CanReveal);
        Assert.Equal("Q:\\files\\sample.zip\nドライブをつなぐと開けます。", row.PathToolTip);
    }

    [Fact]
    public void 記録の場所が1つも無いファイルは_見つかりませんを出す()
    {
        var row = Row([]);

        Assert.True(row.IsMissing);
        Assert.False(row.IsOnDetachedDrive);
        Assert.False(row.CanReveal);
        Assert.Null(row.FirstPath);
        Assert.Equal("ファイルが見つかりません。", row.PathToolTip);
    }

    [Fact]
    public void 確かめられないファイルは_見つかりませんと分け_権限を確かめるよう言う()
    {
        // 権限が無い・ドライブが答えない場所は記録に「無い」と書かない（カードの印も出ない）。ここだけ「見つかりません」と出すと食い違う（MB-B）
        var row = Row([@"D:\locked\sample.zip"], FilePresence.Unverifiable);

        Assert.False(row.IsMissing);
        Assert.True(row.IsUnverifiable);
        Assert.False(row.IsOnDetachedDrive);
        Assert.False(row.CanReveal);
        Assert.Equal("D:\\locked\\sample.zip\nファイルを確かめられません。権限を確かめてください。", row.PathToolTip);
    }

    [Fact]
    public void 古い版の行は_見つかりませんと言わず_外す代わりに片付けるを出す()
    {
        var row = new LocalFileRow
        {
            Hash = Make.HashOf("old"),
            FileName = "pack.zip",
            SizeText = "3 B",
            Paths = [],
            IsOldVersion = true,
        };

        Assert.False(row.IsMissing);
        Assert.False(row.CanReveal);
        Assert.True(row.CanForgetOldVersion);
        Assert.False(row.CanDetach);
        Assert.Equal("新しい版に置き換わっています。", row.PathToolTip);
        Assert.Equal("新しい版に置き換わっています。", row.OpenMenuTip);
    }

    [Fact]
    public void 古い版でも外した行には片付けるを出さない()
    {
        var row = new LocalFileRow
        {
            Hash = Make.HashOf("old"),
            FileName = "pack.zip",
            SizeText = "3 B",
            Paths = [],
            IsOldVersion = true,
            IsDetached = true,
        };

        Assert.False(row.CanForgetOldVersion);
        Assert.False(row.CanDetach);
    }

    [Fact]
    public void 壊れたzipの札は_在る物にだけ出す()
    {
        // 無い物・取り外しているドライブの上の物に札を2つ並べると、どちらを先にするのか分からない（ユーザ判断 2026-09-30）
        Assert.True(Row([@"D:\files\sample.zip"], FilePresence.Present, broken: true).ShowsBrokenArchive);
        Assert.False(Row([@"D:\files\sample.zip"], FilePresence.Missing, broken: true).ShowsBrokenArchive);
        Assert.False(Row([@"Q:\files\sample.zip"], FilePresence.OnDetachedDrive, broken: true).ShowsBrokenArchive);
        Assert.False(Row([], broken: true).ShowsBrokenArchive);
        Assert.False(Row([@"D:\files\sample.zip"], FilePresence.Present, broken: false).ShowsBrokenArchive);
    }

    [Fact]
    public void 札は3つのうち多くても1つ()
    {
        foreach (var presence in Enum.GetValues<FilePresence>())
        {
            foreach (var broken in new[] { false, true })
            {
                var row = Row([@"D:\files\sample.zip"], presence, broken);

                var shown = new[] { row.IsMissing, row.IsOnDetachedDrive, row.IsUnverifiable, row.ShowsBrokenArchive }.Count(flag => flag);
                Assert.True(shown <= 1, $"{presence}・壊れた印 {broken} で札が {shown} つ出る");
            }
        }
    }

    [Fact]
    public void 在るかが後から分かると_札の知らせが届く()
    {
        // 行は先に出し、在るかは画面のスレッドの外で確かめて後から付ける。知らせが抜けると札が出ない
        var row = Row([@"D:\files\sample.zip"], broken: true);
        var changed = new List<string?>();
        row.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        row.Presence = FilePresence.Missing;

        Assert.Contains(nameof(LocalFileRow.IsMissing), changed);
        Assert.Contains(nameof(LocalFileRow.IsUnverifiable), changed);
        Assert.Contains(nameof(LocalFileRow.IsOnDetachedDrive), changed);
        Assert.Contains(nameof(LocalFileRow.CanReveal), changed);
        Assert.Contains(nameof(LocalFileRow.PathToolTip), changed);
        Assert.Contains(nameof(LocalFileRow.ShowsBrokenArchive), changed);
    }

    [Fact]
    public void 同じ中身が複数の場所にあれば_全部を並べて_ほかの場所を行にする()
    {
        var row = Row([@"D:\files\sample.zip", @"E:\backup\sample.zip"]);

        Assert.True(row.HasMultiplePaths);
        Assert.Equal("同じ中身が 2 箇所に", row.DuplicateNote);
        Assert.Equal([@"E:\backup\sample.zip"], row.OtherPaths);
        Assert.StartsWith("同じ中身が 2 箇所にあります：\n", row.PathToolTip);
    }

    // ---- 商品ページを通して ----

    [Fact]
    public Task 商品ページは_在る物と無い物を確かめて札を付ける() => TestApp.Run(async app =>
    {
        var present = app.NewFile("present.zip");
        var brokenPresent = app.NewFile("broken.zip");
        var missing = System.IO.Path.Combine(app.Root, "files", "moved-away.zip");
        var brokenMissing = System.IO.Path.Combine(app.Root, "files", "broken-and-deleted.zip");

        var item = Make.Item("1000001", "作り物の衣装").WithFiles(
            Make.File(present),
            Make.File(brokenPresent, archiveBroken: true),
            Make.File(missing),
            Make.File(brokenMissing, archiveBroken: true));
        await app.AddItemAsync(item);
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        LocalFileRow RowOf(string path) => page.LocalFiles.Single(row => row.FirstPath == path);

        // 在るかは裏で確かめて後から付く。4行は同じ1回で付くので、無い行に付いたら全部に付いている
        await UiThread.Until(() => RowOf(missing).IsMissing, "無いファイルに「見つかりません」が付く");

        Assert.False(RowOf(present).IsMissing);
        Assert.False(RowOf(present).ShowsBrokenArchive);

        Assert.True(RowOf(brokenPresent).ShowsBrokenArchive);
        Assert.False(RowOf(brokenPresent).IsMissing);

        Assert.True(RowOf(missing).IsMissing);

        // 壊れていて、しかも今は無い：「見つかりません」だけを言う
        Assert.True(RowOf(brokenMissing).IsMissing);
        Assert.False(RowOf(brokenMissing).ShowsBrokenArchive);
    });

    [Fact]
    public Task 商品ページは_古い版を置き換わった名前で出し_片付けると記録から消える() => TestApp.Run(async app =>
    {
        var fresh = app.NewFile("pack.zip");
        var old = Make.File(fresh) with
        {
            Hash = Make.HashOf("old-version"),
            Paths = [],
            Replaced = new ReplacedVersion(fresh, DateTimeOffset.Now),
        };
        var item = Make.Item("9900001", "作り物の衣装").WithFiles(Make.File(fresh), old);
        await app.AddItemAsync(item);
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        var row = page.LocalFiles.Single(row => row.Hash == old.Hash);

        Assert.Equal("pack.zip", row.FileName);
        Assert.True(row.IsOldVersion);
        Assert.False(row.IsMissing);
        Assert.True(row.CanForgetOldVersion);
        Assert.False(item.HasMissingFile);

        page.ForgetOldVersionCommand.Execute(row);
        await app.SettleAsync();

        var saved = await app.Services.Store.Items.LoadAsync(item.Id);
        Assert.Equal(Make.HashOf(fresh), Assert.Single(saved!.Local.LocalFiles).Hash);
    });

    [Fact]
    public Task 商品ページは_外したファイルを後ろに並べ_戻せるかを言う() => TestApp.Run(async app =>
    {
        var kept = app.NewFile("kept.zip");
        var detached = app.NewFile("detached.zip");
        var item = Make.Item("1000001", "作り物の衣装").WithFiles(
            Make.File(detached, detached: true),
            Make.File(kept));
        await app.AddItemAsync(item);
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);

        Assert.Collection(
            page.LocalFiles,
            row =>
            {
                Assert.Equal("kept.zip", row.FileName);
                Assert.True(row.IsAttached);
            },
            row =>
            {
                Assert.Equal("detached.zip", row.FileName);
                Assert.True(row.IsDetached);
                Assert.True(row.CanReattach);
                Assert.Equal("このファイルをこの商品に戻します。未確定からは消えます。", row.ReattachTip);
            });
    });

    [Fact]
    public Task 外したファイルを別の商品が持っていれば_戻せない理由にその商品の名前を言う() => TestApp.Run(async app =>
    {
        var file = app.NewFile("shared.zip");
        var item = Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(file, detached: true));
        var owner = Make.Item("1000002", "作り物の髪型").WithFiles(Make.File(file));
        await app.AddItemAsync(item);
        await app.AddItemAsync(owner);
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);

        var row = Assert.Single(page.LocalFiles);
        Assert.False(row.CanReattach);
        Assert.Equal("「作り物の髪型」に紐付けてあるので戻せません。先にそちらから外してください。", row.ReattachTip);
    });
    // ---- 「開く ▾」「Unity ▾」は出したまま、押せないときは理由を言う（ユーザ判断 2026-10-04）----

    [Theory]
    [InlineData(FilePresence.Present, true, "このファイルの開き方を選びます", "開いているUnityへ送るか、Unityのプロジェクトタブで場所を示します。")]
    [InlineData(FilePresence.Missing, false, "ファイルが見つかりません。", "ファイルが見つかりません。")]
    [InlineData(FilePresence.OnDetachedDrive, false, "ドライブをつなぐと開けます。", "ドライブをつなぐと送れます。")]
    [InlineData(FilePresence.Unverifiable, false, "ファイルを確かめられません。権限を確かめてください。", "ファイルを確かめられません。権限を確かめてください。")]
    public void 開くとUnityのボタンは_在るときだけ押せ_押せないときは理由を言う(
        FilePresence presence, bool enabled, string openTip, string unityTip)
    {
        var row = Row([@"D:\files\sample.zip"], presence);
        var package = new UnityPackageRow { Entry = new UnityPackageEntry(@"D:\files\sample.zip", "a.unitypackage", 1), FileRow = row };

        Assert.Equal(enabled, row.CanReveal);
        Assert.Equal(openTip, row.OpenMenuTip);
        Assert.Equal(enabled, package.FileRow.CanReveal);
        Assert.Equal(unityTip, package.FileRow.UnityMenuTip);
    }

    [Fact]
    public void 場所が1つも無いファイルの開くは_押せず_見つからないと言う()
    {
        var row = Row([]);

        Assert.False(row.CanReveal);
        Assert.Equal("ファイルが見つかりません。", row.OpenMenuTip);
    }

    [Fact]
    public void 在るかが後から分かると_開くの吹き出しも変わったと知らせる()
    {
        // 在るかは行を出した後で裏で確かめて付ける。知らせないと、押せないのに「開き方を選びます」が残る
        var row = Row([@"D:\files\sample.zip"]);
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.Presence = FilePresence.Missing;

        Assert.Contains(nameof(LocalFileRow.CanReveal), changed);
        Assert.Contains(nameof(LocalFileRow.OpenMenuTip), changed);
        Assert.Contains(nameof(LocalFileRow.UnityMenuTip), changed);
    }

    /// <summary>
    /// 「開く ▾」は押せないときも消さない（行の形がいつも同じ）。押せるかは IsEnabled、理由は押せなくても出る吹き出し。
    /// 部品を組んで測るには商品ページの見た目の資源が要るので、原文を読む（見た目は ViewShot の item-files-states）。
    /// </summary>
    [Theory]
    [InlineData("ItemFileOpenMenu", "{Binding CanReveal}", "{Binding OpenMenuTip}")]
    [InlineData("ItemPackageUnityMenu", "{Binding FileRow.CanReveal}", "{Binding FileRow.UnityMenuTip}")]
    public void 開くとUnityのボタンは_隠さずに押せなくし_押せなくても吹き出しを出す(string automationId, string enabled, string tip)
    {
        var panel = System.Xml.Linq.XDocument.Load(FilesPanelPath());
        var button = Assert.Single(panel.Descendants(), element =>
            element.Name.LocalName == "Button"
            && element.Attributes().Any(attribute => attribute.Name.LocalName == "AutomationProperties.AutomationId" && attribute.Value == automationId));

        Assert.Null(button.Attribute("Visibility"));
        Assert.Equal(enabled, button.Attribute("IsEnabled")?.Value);
        Assert.Equal(tip, button.Attribute("ToolTip")?.Value);
        Assert.Equal("True", button.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == "ToolTipService.ShowOnDisabled")?.Value);
    }

    private static string FilesPanelPath([System.Runtime.CompilerServices.CallerFilePath] string here = "")
        => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "..", "Chmonos.App", "Views", "ItemFilesPanel.xaml");

    // ---- 後から読んで付ける物 ----

    [Fact]
    public void 送れる物を後から付けると_件数の文も変わったと知らせる()
    {
        // zip の中は行を出した後で読んで付ける。件数を入れた文を知らせないと、画面には「0 件」のまま残る
        var row = Row([@"D:ilessample.zip"]);
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.UnityPackages =
        [
            new UnityPackageEntry(@"D:ilessample.zip", "a.unitypackage", 1),
            new UnityPackageEntry(@"D:ilessample.zip", "b.unitypackage", 1),
        ];

        Assert.Contains(nameof(LocalFileRow.UnityPackageNote), changed);
        Assert.Contains(nameof(LocalFileRow.HasManyUnityPackages), changed);
        Assert.StartsWith("Unityへ送れるもの 2 件。", row.UnityPackageNote);
    }
}
