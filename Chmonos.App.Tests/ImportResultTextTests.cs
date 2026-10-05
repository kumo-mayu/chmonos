using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Booth;
using Chmonos.Core.Scanning;

namespace Chmonos.App.Tests;

/// <summary>
/// 取り込みの結果の文（読めなかった物・壊れた zip・BOOTH で取れなかった物）。
///
/// どれも結果の数から決まる文で、前は「読めないファイルを置いた保存先で取り込んで、撮って読む」でしか確かめられなかった
/// （2026-09-30 に、この文の直しだけで何度も起動し直した）。
/// </summary>
public class ImportResultTextTests
{
    // ---- 見つからないファイルを探して、読めなかった物（点検の13） ----

    [Fact]
    public void 探して読めなかった物が無ければ何も言わない()
        => Assert.Equal(string.Empty, ImportViewModel.UnreadableInSearchText(files: 0, folders: 0));

    [Fact]
    public void 探して読めなかったファイルとフォルダの数を言う()
    {
        Assert.Equal("読めなかったファイルが 2 件あり、その中身は確かめられませんでした", ImportViewModel.UnreadableInSearchText(2, 0));
        Assert.Equal("読めなかったフォルダが 1 件あり、その中は探せませんでした", ImportViewModel.UnreadableInSearchText(0, 1));
        Assert.Equal("読めなかったファイルが 2 件、フォルダが 1 件あり、その中は探せませんでした", ImportViewModel.UnreadableInSearchText(2, 1));
    }

    // ---- 読めなかったファイルとフォルダ ----

    [Fact]
    public void 読めなかった物が無ければ何も言わない()
        => Assert.Equal(string.Empty, ImportViewModel.UnreadableFilesText(files: 0, folders: 0));

    [Fact]
    public void 読めなかったファイルだけなら数と次にやることを言う()
        => Assert.Equal(
            "読めなかったファイルが 3 件あり、取り込めていません。"
            + "ほかのアプリで開いていないか、エクスプローラで開けるかを確かめてから、もう一度取り込んでください。",
            ImportViewModel.UnreadableFilesText(files: 3, folders: 0));

    [Fact]
    public void 読めなかったフォルダだけなら_ほかのアプリで開いているかは言わない()
    {
        var text = ImportViewModel.UnreadableFilesText(files: 0, folders: 2);

        // フォルダの中を読めないのは権限かつながりで、「ほかのアプリで開いている」は当たらない
        Assert.Equal(
            "読めなかったフォルダが 2 件あり、取り込めていません。エクスプローラで開けるかを確かめてから、もう一度取り込んでください。",
            text);
        Assert.DoesNotContain("ほかのアプリ", text);
    }

    [Fact]
    public void ファイルとフォルダの両方なら_数を足さずに並べる()
        => Assert.StartsWith(
            "読めなかったファイルが 3 件、フォルダが 2 件あり、取り込めていません。",
            ImportViewModel.UnreadableFilesText(files: 3, folders: 2));

    [Fact]
    public void 読めなかった文は原因を並べない()
    {
        // 「別のアプリが開いている、ネットワーク越しで…、権限が無い、のいずれかです。」を外した（ユーザ判断 2026-09-30）
        foreach (var text in new[]
                 {
                     ImportViewModel.UnreadableFilesText(1, 0),
                     ImportViewModel.UnreadableFilesText(0, 1),
                     ImportViewModel.UnreadableFilesText(1, 1),
                 })
        {
            Assert.DoesNotContain("いずれか", text);
            Assert.DoesNotContain("権限", text);
            Assert.DoesNotContain("ネットワーク", text);
        }
    }

    // ---- 壊れた zip ----

    [Fact]
    public void 未確定の壊れたzipは_未確定を開くへ案内する()
    {
        Assert.Equal(string.Empty, ImportViewModel.BrokenArchiveText(0));
        Assert.Equal(
            "未確定に、壊れていて開けないzipが 2 件あります。下の「未確定を開く」で確かめて、ダウンロードし直してください。",
            ImportViewModel.BrokenArchiveText(2));
    }

    [Fact]
    public void 商品の壊れたzipは_商品が1つなら名前を言う()
        => Assert.Equal(
            "「作り物の衣装」に、壊れていて開けないzipが 2 件あります。"
            + "下の「壊れたzipがある商品を検索で開く」で確かめて、ダウンロードし直してください。",
            ImportViewModel.BrokenArchiveOnItemsText(2, ["作り物の衣装"]));

    [Fact]
    public void 商品の壊れたzipは_商品が2つ以上なら名前を言わず数を言う()
    {
        var text = ImportViewModel.BrokenArchiveOnItemsText(5, ["作り物の衣装", "作り物の髪型", "作り物の靴"]);

        Assert.Equal(
            "3 件の商品に、壊れていて開けないzipが 5 件あります。"
            + "下の「壊れたzipがある商品を検索で開く」で確かめて、ダウンロードし直してください。",
            text);
        Assert.DoesNotContain("作り物", text);
    }

    [Fact]
    public void 商品の壊れたzipは_数か名前のどちらかが無ければ何も言わない()
    {
        Assert.Equal(string.Empty, ImportViewModel.BrokenArchiveOnItemsText(0, ["作り物の衣装"]));
        Assert.Equal(string.Empty, ImportViewModel.BrokenArchiveOnItemsText(2, []));
    }

    [Fact]
    public void 長い商品名は30字で切る()
    {
        // BOOTH の商品名は100字を超える物がある。そのまま入れると一文80字の決まりを名前だけで越える
        var name = new string('あ', 29) + "いうえお";

        var text = ImportViewModel.BrokenArchiveOnItemsText(1, [name]);

        Assert.StartsWith("「" + new string('あ', 29) + "い…」に、", text);
    }

    [Fact]
    public void 商品名の切れ目は絵文字の途中に来ない()
    {
        // 30字目が2つの符号で1字になる字（絵文字）でも、半分で切らない
        var name = new string('あ', 29) + "😀" + "いうえお";

        var text = ImportViewModel.BrokenArchiveOnItemsText(1, [name]);

        Assert.StartsWith("「" + new string('あ', 29) + "😀…」に、", text);
    }

    // ---- 画面に並ぶ文（ViewModel を通して）----

    [Fact]
    public Task 結果が無い間は_どの文も出さない() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;

        Assert.False(import.HasSummary);
        Assert.Empty(import.UnreadableLines);
        Assert.False(import.HasUnreadable);
        Assert.False(import.HasNotFound);
        Assert.False(import.HasBrokenOnItemsResult);
    });

    [Fact]
    public Task 読めなかった文は_1文ずつ決まった順に並ぶ() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;

        import.Summary = new ImportSummary
        {
            FilesUnreadable = 3,
            FilesOnlineOnly = 4,
            FilesBrokenArchive = 2,
            FilesBrokenArchiveOnItems = 1,
            BrokenArchiveItemNames = ["作り物の衣装"],
        };

        // 取り込めていない物（読めない・オンラインのみ）→ 開けない物（未確定・商品）の順
        Assert.Collection(
            import.UnreadableLines,
            line => Assert.StartsWith("読めなかったファイルが 3 件あり", line.Text),
            line => Assert.StartsWith("4 件はOneDriveの「オンラインのみ」なので読めませんでした。", line.Text),
            line => Assert.StartsWith("未確定に、壊れていて開けないzipが 2 件あります。", line.Text),
            line => Assert.StartsWith("「作り物の衣装」に、壊れていて開けないzipが 1 件あります。", line.Text));
        Assert.True(import.HasUnreadable);
        Assert.Equal(string.Join("\n", import.UnreadableLines.Select(line => line.Text)), import.UnreadableText);

        // 文の種類ごとに ID が違う（確かめの道具が、読めなかった文と壊れた zip の文を ID で見分ける）
        Assert.Equal(
            ["ImportUnreadableLine", "ImportOnlineOnlyLine", "ImportBrokenZipLine", "ImportBrokenZipOnItemsLine"],
            import.UnreadableLines.Select(line => line.Id));
    });

    [Fact]
    public Task 壊れたzipが未確定と商品の両方にあると_2つの文が続けて並ぶ() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;

        import.Summary = new ImportSummary
        {
            FilesBrokenArchive = 2,
            FilesBrokenArchiveOnItems = 3,
            BrokenArchiveItemNames = ["作り物の衣装", "作り物の髪型"],
        };

        // 2行が並んでも合計と読まれないよう、どこにあるかを文の頭で言う
        Assert.Collection(
            import.UnreadableLines,
            line =>
            {
                Assert.StartsWith("未確定に、", line.Text);
                Assert.Equal(ImportResultLine.BrokenZip, line.Id);
            },
            line =>
            {
                Assert.StartsWith("2 件の商品に、", line.Text);
                Assert.Equal(ImportResultLine.BrokenZipOnItems, line.Id);
            });
    });

    [Fact]
    public Task 出す文が無い行は_空の行として残さない() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;

        import.Summary = new ImportSummary { FilesScanned = 10, ItemsAdded = 2, FilesOnlineOnly = 1 };

        var line = Assert.Single(import.UnreadableLines);
        Assert.Contains("常にこのデバイスに保持する", line.Text);
        Assert.Equal(ImportResultLine.OnlineOnly, line.Id);
    });

    [Fact]
    public Task 商品の壊れたzipがあるときだけ_検索で開くボタンを出す() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;

        import.Summary = new ImportSummary { FilesBrokenArchive = 2 };
        Assert.False(import.HasBrokenOnItemsResult);

        import.Summary = new ImportSummary { FilesBrokenArchiveOnItems = 1, BrokenArchiveItemNames = ["作り物の衣装"] };
        Assert.True(import.HasBrokenOnItemsResult);
    });

    [Fact]
    public Task 結果が変わると_文の知らせが届く() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;
        var changed = new List<string?>();
        import.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        import.Summary = new ImportSummary { FilesUnreadable = 1 };

        // 画面は1文ずつの一覧（UnreadableLines）を並べる。知らせが抜けると、前の結果の文が残る
        Assert.Contains(nameof(ImportViewModel.UnreadableLines), changed);
        Assert.Contains(nameof(ImportViewModel.HasUnreadable), changed);
        Assert.Contains(nameof(ImportViewModel.NotFoundText), changed);
        Assert.Contains(nameof(ImportViewModel.HasBrokenOnItemsResult), changed);
    });

    // ---- BOOTH で取れなかった物 ----

    [Fact]
    public Task BOOTHで見つからなかった物は_未確定を開くへ案内する() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;

        import.Summary = new ImportSummary { NotFound = 2 };

        Assert.Equal("BOOTHで見つからなかったものが 2 件あります。下の「未確定を開く」から確かめてください。", import.NotFoundText);
        Assert.True(import.HasNotFound);
        Assert.True(import.HasUnresolvedResult);
    });

    [Fact]
    public Task つながっていなくて止めた回は_待つのではなく_つないでから押すと言う() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;

        import.Summary = new ImportSummary { Stopped = BoothOutageKind.Offline, TemporaryFailures = 5 };

        Assert.Equal(
            "途中で止めました。ネットにつながっていないようです。つながってから、下の帯の「続きから進む」を押してください。",
            import.NotFoundText);

        // 件数は付けない（②で止めた回は①が済んでいて、取れなかった数に入らない）
        Assert.DoesNotContain("5 件", import.NotFoundText);
    });

    [Fact]
    public Task BOOTHが不調で止めた回は_時間をおくと言う() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;

        import.Summary = new ImportSummary { Stopped = BoothOutageKind.ServerDown };

        Assert.Equal(
            "途中で止めました。BOOTHが不調のようです。時間をおいて、下の帯の「続きから進む」を押してください。",
            import.NotFoundText);
    });

    [Fact]
    public Task 止めずに取れなかった分は_数と続きからを言う() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;

        import.Summary = new ImportSummary { NotFound = 1, TemporaryFailures = 3 };

        Assert.Equal(
            "BOOTHで見つからなかったものが 1 件あります。下の「未確定を開く」から確かめてください。"
            + "3 件はBOOTHの不調で取れませんでした。少し待ってから、下の帯の「続きから進む」で取り直せます。",
            import.NotFoundText);
    });
}
