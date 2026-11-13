using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 通知の保管庫（ユーザ判断 2026-11-10）：上限を超えた古い既読・解消済みを、捨てずに月ごとの gzip へ移す。
/// 消すのはアプリを閉じるときの1回（<see cref="NotificationService.TrimArchiveAsync"/>）だけ。
/// </summary>
public sealed class NotificationArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-archive-" + Guid.NewGuid().ToString("N"));
    private readonly DataStore _store;

    public NotificationArchiveTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private NotificationArchive Archive => _store.NotificationArchive;

    private NotificationService Create(int retention = 2, int megabytes = 50)
        => new(_store, new AppSettings { NotificationRetentionCount = retention, NotificationArchiveMegabytes = megabytes });

    /// <summary>時差を決めた日時（月の分け方が、試験を回す PC の時刻帯で変わらないように）。</summary>
    private static DateTimeOffset At(int year, int month, int day) => new(year, month, day, 12, 0, 0, TimeSpan.FromHours(9));

    private static NotificationRecord Record(string id, DateTimeOffset createdAt, bool isRead = true, string? itemId = null) => new()
    {
        Id = id,
        Kind = NotificationKind.ItemUpdated,
        ItemId = itemId,
        Title = "作り物の衣装",
        Detail = "価格：500 → 800",
        CreatedAt = createdAt,
        IsRead = isRead,
    };

    private static string Json(NotificationRecord record) => JsonSerializer.Serialize(record, JsonStore.Options);

    [Fact]
    public async Task 上限を超えた古い既読は保管庫へ移り_未読は移らない()
    {
        await _store.Notifications.SaveAsync(
        [
            Record("read-old", At(2026, 9, 1)),
            Record("read-mid", At(2026, 10, 1)),
            Record("unread-old", At(2026, 8, 1), isRead: false),
            Record("unread-new", At(2026, 11, 1), isRead: false),
        ]);

        Assert.Equal(2, await Create(retention: 2).PruneAsync());

        Assert.Equal(["unread-old", "unread-new"], _store.Notifications.Load().Select(record => record.Id));
        Assert.Equal(["2026-09", "2026-10"], Archive.Months());
        Assert.Equal(["read-old"], (await Archive.ReadAsync("2026-09")).Records.Select(record => record.Id));
        Assert.Equal(["read-mid"], (await Archive.ReadAsync("2026-10")).Records.Select(record => record.Id));
        Assert.Empty((await Archive.ReadAsync("2026-08")).Records);
    }

    [Fact]
    public async Task 既読にした操作で上限を超えた分も保管庫へ移る()
    {
        await _store.Notifications.SaveAsync(
        [
            Record("a", At(2026, 9, 1), isRead: false),
            Record("b", At(2026, 9, 2), isRead: false),
            Record("c", At(2026, 9, 3), isRead: false),
        ]);

        await Create(retention: 2).SetReadAsync("a", true);

        Assert.Equal(["b", "c"], _store.Notifications.Load().Select(record => record.Id));
        Assert.Equal(["a"], (await Archive.ReadAsync("2026-09")).Records.Select(record => record.Id));
    }

    [Fact]
    public async Task 容量が0なら保管せずに捨てる()
    {
        await _store.Notifications.SaveAsync([Record("a", At(2026, 9, 1)), Record("b", At(2026, 9, 2)), Record("c", At(2026, 9, 3))]);

        Assert.Equal(1, await Create(retention: 2, megabytes: 0).PruneAsync());

        Assert.Equal(2, _store.Notifications.Load().Count);
        Assert.False(Directory.Exists(Archive.Directory));
    }

    /// <summary>保管庫に書けてから通知から外す。書けなければ外さない（逆の順だと、どこにも無くなる）。</summary>
    [Fact]
    public async Task 保管庫に書けなければ_通知から外さない()
    {
        await _store.Notifications.SaveAsync([Record("a", At(2026, 9, 1)), Record("b", At(2026, 9, 2)), Record("c", At(2026, 9, 3))]);

        // 保管庫のフォルダの場所にファイルを置いて、書けなくする
        File.WriteAllText(Archive.Directory, "邪魔なファイル");

        Assert.Equal(0, await Create(retention: 2).PruneAsync());

        Assert.Equal(["a", "b", "c"], _store.Notifications.Load().Select(record => record.Id));
    }

    [Fact]
    public async Task 読む口で_足した知らせがそのまま戻り_商品で絞れる()
    {
        var first = Record("one", At(2026, 11, 2), itemId: "9900001") with
        {
            Diffs = [new NotificationDiff { Field = "価格", Before = "500", After = "800" }],
        };
        var second = Record("two", At(2026, 11, 5), itemId: "9900002") with { IsResolved = true, IsStrong = true };
        var other = Record("three", At(2026, 12, 1), itemId: "9900001");

        await Archive.AppendAsync([second, other]);
        await Archive.AppendAsync([first]);

        var november = await Archive.ReadAsync("2026-11");
        Assert.Equal([Json(first), Json(second)], november.Records.Select(Json));
        Assert.Equal(0, november.UnreadableLines);

        Assert.Equal(["one"], (await Archive.ReadAsync("2026-11", itemId: "9900001")).Records.Select(record => record.Id));
        Assert.Equal(["three"], (await Archive.ReadAsync("2026-12", itemId: "9900001")).Records.Select(record => record.Id));
    }

    /// <summary>
    /// 足すたびに gzip のメンバーを後ろに足す。中身は1行目が版の行で、知らせは1行に1件・camelCase・種類は文字。
    /// </summary>
    [Fact]
    public async Task 中身は版の行と1行1件のJSONで_足すとメンバーが続く()
    {
        await Archive.AppendAsync([Record("a", At(2026, 11, 1))]);
        await Archive.AppendAsync([Record("b", At(2026, 11, 2))]);

        var lines = await GunzipLinesAsync(Archive.FileFor("2026-11"));

        Assert.Equal(4, lines.Count);
        Assert.Equal($"{{\"formatVersion\":{StoreFormat.Current}}}", lines[0]);
        Assert.Equal(lines[0], lines[2]);
        Assert.StartsWith("{\"id\":\"a\",\"kind\":\"itemUpdated\"", lines[1]);
    }

    /// <summary>新しい月のファイルを足す途中で止まっても、前の中身はそのまま読め、書きかけは残らない。</summary>
    [Fact]
    public async Task 書きかけで止まっても_前の中身が読める()
    {
        await Archive.AppendAsync([Record("before", At(2026, 11, 1))]);
        Archive.MidWriteForTests = () => throw new IOException("止めた");

        await Assert.ThrowsAsync<IOException>(() => Archive.AppendAsync([Record("after", At(2026, 11, 2))]));

        Archive.MidWriteForTests = null;
        var read = await Archive.ReadAsync("2026-11");
        Assert.Equal(["before"], read.Records.Select(record => record.Id));
        Assert.Equal(0, read.UnreadableLines);
        Assert.Equal(["2026-11.jsonl.gz"], Directory.EnumerateFiles(Archive.Directory).Select(Path.GetFileName));
    }

    /// <summary>保管した後、通知の保存の前に落ちると両方に残り、次にもう一度足される。読むときは1つにまとめる。</summary>
    [Fact]
    public async Task 同じ知らせが2回足されても_読むと1件()
    {
        var record = Record("a", At(2026, 11, 1));
        await Archive.AppendAsync([record]);
        await Archive.AppendAsync([record]);

        Assert.Single((await Archive.ReadAsync("2026-11")).Records);
    }

    /// <summary>
    /// 同じ商品の別の機会の更新は、同じ ID・別の作った日時で控えに入る。どちらも読める。
    /// 片方の書き直しの写しは1件にまとめる（2026-11-13 の点検 件7）。商品で絞っても同じ。
    /// </summary>
    [Fact]
    public async Task 同じIDで作った日時の違う知らせは_両方読める()
    {
        var first = Record("item-updated:9900501", At(2026, 11, 1), itemId: "9900501") with { Detail = "1回目の更新" };
        var second = Record("item-updated:9900501", At(2026, 11, 3), itemId: "9900501") with { Detail = "2回目の更新" };
        await Archive.AppendAsync([first]);
        await Archive.AppendAsync([first, second]);

        Assert.Equal(["1回目の更新", "2回目の更新"], (await Archive.ReadAsync("2026-11")).Records.Select(record => record.Detail));
        Assert.Equal(["1回目の更新", "2回目の更新"], (await Archive.ReadAsync("2026-11", "9900501")).Records.Select(record => record.Detail));
    }

    /// <summary>読めない行（新しい版・知らない種類・壊れた行）は飛ばして数える。1行のために月ごと読めなくしない。</summary>
    [Fact]
    public async Task 読めない行は飛ばして数える()
    {
        var good = Json(Record("good", At(2026, 11, 1))).Replace("\r", string.Empty).Replace("\n", string.Empty);
        var text = new StringBuilder()
            .Append("{\"formatVersion\":2}\n")
            .Append(good).Append('\n')
            .Append(good.Replace("itemUpdated", "somethingNew")).Append('\n')
            .Append("{壊れた行\n")
            .Append($"{{\"formatVersion\":{StoreFormat.Current + 1}}}\n")
            .Append(good.Replace("\"good\"", "\"newer\"")).Append('\n')
            .ToString();
        Directory.CreateDirectory(Archive.Directory);
        await using (var file = File.Create(Archive.FileFor("2026-11")))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(text));
        }

        var read = await Archive.ReadAsync("2026-11");

        Assert.Equal(["good"], read.Records.Select(record => record.Id));
        Assert.Equal(3, read.UnreadableLines);
    }

    [Fact]
    public async Task 閉じるときに容量を超えていたら_古い月から丸ごと消す()
    {
        WriteMonth("2026-08", 400_000);
        WriteMonth("2026-09", 400_000);
        WriteMonth("2026-10", 400_000);
        WriteMonth("2026-11", 400_000);
        File.WriteAllText(Path.Combine(Archive.Directory, "使う人のメモ.txt"), "消さない");

        // 1MB：合計 1.6MB から古い2つを消すと 0.8MB
        Assert.Equal(2, (await Create(megabytes: 1).TrimArchiveAsync()).DeletedFiles);

        Assert.Equal(["2026-10", "2026-11"], Archive.Months());
        Assert.True(File.Exists(Path.Combine(Archive.Directory, "使う人のメモ.txt")));
    }

    [Fact]
    public async Task 閉じるときに容量の中なら何も消さない()
    {
        WriteMonth("2026-10", 400_000);
        WriteMonth("2026-11", 400_000);

        Assert.Equal(NotificationArchiveTrim.None, await Create(megabytes: 1).TrimArchiveAsync());

        Assert.Equal(["2026-10", "2026-11"], Archive.Months());
    }

    [Fact]
    public async Task 容量が0なら_閉じるときに全部消す()
    {
        WriteMonth("2026-10", 1000);
        WriteMonth("2026-11", 1000);

        Assert.Equal(2, (await Create(megabytes: 0).TrimArchiveAsync()).DeletedFiles);

        Assert.Empty(Archive.Months());
    }

    /// <summary>
    /// 設定の JSON に手で負の数を書かれても、閉じるときに全部消さない（0 は「全部消す」なので、0 に寄せない）。
    /// 上限を超えた分は捨てずに保管する。
    /// </summary>
    [Fact]
    public async Task 手で負の数を書かれた設定では_消さずに保管する()
    {
        WriteMonth("2026-08", 1000);
        await _store.Notifications.SaveAsync([Record("a", At(2026, 9, 1)), Record("b", At(2026, 9, 2)), Record("c", At(2026, 9, 3))]);
        var service = Create(retention: 2, megabytes: -1);

        Assert.Equal(1, await service.PruneAsync());
        Assert.Equal(NotificationArchiveTrim.None, await service.TrimArchiveAsync());

        Assert.Equal(["2026-08", "2026-09"], Archive.Months());
    }

    /// <summary>消すのは閉じるときだけ。上限を超えた分を移すときは、容量を超えていても消さない（小さくしてから戻しても消えない）。</summary>
    [Fact]
    public async Task 移すときは容量を超えていても消さない()
    {
        WriteMonth("2026-08", 2_000_000);
        await _store.Notifications.SaveAsync([Record("a", At(2026, 9, 1)), Record("b", At(2026, 9, 2)), Record("c", At(2026, 9, 3))]);

        await Create(retention: 2, megabytes: 1).PruneAsync();

        Assert.Equal(["2026-08", "2026-09"], Archive.Months());
    }

    /// <summary>
    /// 最後に残った月まで消すことになるときは消さずに、新しい側の半分を残す（ユーザ判断 2026-11-10）。
    /// 古い月は今どおり丸ごと消える。
    /// </summary>
    [Fact]
    public async Task 最後の1つは消さずに新しい側の半分を残し_古い月は丸ごと消える()
    {
        await AppendMonthAsync(2026, 8, 50);
        var length = await AppendMonthAsync(2026, 9, 200);

        var trim = await Archive.TrimAsync(length * 7 / 10);

        Assert.Equal(new NotificationArchiveTrim(1, "2026-09", 100, 100, StillOver: false), trim);
        Assert.Equal(["2026-09"], Archive.Months());
        var read = await Archive.ReadAsync("2026-09");
        Assert.Equal(Enumerable.Range(100, 100).Select(i => Id(2026, 9, i)), read.Records.Select(record => record.Id));
        Assert.Equal(0, read.UnreadableLines);
        Assert.Equal(1, Archive.RewritesForTests);
        Assert.True(new FileInfo(Archive.FileFor("2026-09")).Length <= length * 7 / 10);
    }

    [Fact]
    public async Task 古い月を消して収まれば_最後の月は書き直さない()
    {
        await AppendMonthAsync(2026, 8, 50);
        var length = await AppendMonthAsync(2026, 9, 100);
        var before = await File.ReadAllBytesAsync(Archive.FileFor("2026-09"));

        var trim = await Archive.TrimAsync(length);

        Assert.Equal(new NotificationArchiveTrim(1, null, 0, 0, StillOver: false), trim);
        Assert.Equal(before, await File.ReadAllBytesAsync(Archive.FileFor("2026-09")));
        Assert.Equal(0, Archive.RewritesForTests);
    }

    /// <summary>半分で足りなければ 1/4 まで下げる。何回半分にするかは先に数えて決め、書き直しは1回。</summary>
    [Fact]
    public async Task 半分で足りなければ4分の1まで下げ_書き直しは1回()
    {
        var length = await AppendMonthAsync(2026, 9, 200);

        var trim = await Archive.TrimAsync(length * 3 / 10);

        Assert.Equal(new NotificationArchiveTrim(0, "2026-09", 150, 50, StillOver: false), trim);
        Assert.Equal(1, Archive.RewritesForTests);
        Assert.True(new FileInfo(Archive.FileFor("2026-09")).Length <= length * 3 / 10);
        Assert.Equal(Enumerable.Range(150, 50).Select(i => Id(2026, 9, i)), (await Archive.ReadAsync("2026-09")).Records.Select(record => record.Id));
    }

    [Fact]
    public async Task 容量が0なら_最後の1つも消す()
    {
        await AppendMonthAsync(2026, 9, 10);

        Assert.Equal(new NotificationArchiveTrim(1, null, 0, 0, StillOver: false), await Archive.TrimAsync(0));

        Assert.Empty(Archive.Months());
    }

    /// <summary>書き直しは一時ファイルに書いてから据える。途中で止まっても元の月は全部読め、書きかけは残らない。</summary>
    [Fact]
    public async Task 書き直しの途中で止まっても_元の月が読める()
    {
        var length = await AppendMonthAsync(2026, 9, 200);
        Archive.MidRewriteForTests = () => throw new IOException("止めた");

        await Assert.ThrowsAsync<IOException>(() => Archive.TrimAsync(length * 7 / 10));

        Archive.MidRewriteForTests = null;
        Assert.Equal(200, (await Archive.ReadAsync("2026-09")).Records.Count);
        Assert.Equal(["2026-09.jsonl.gz"], Directory.EnumerateFiles(Archive.Directory).Select(Path.GetFileName));
    }

    /// <summary>
    /// 門を待つ上限と、書き直しの上限は別（ユーザ判断 2026-11-10）。閉じるときは門を3秒・書き直しを60秒にする。
    /// 書き直しの途中で門の上限が来ても、書き直しは最後まで進む（前は1つの3秒に両方が入り、50MB の月でほぼ毎回打ち切られた）
    /// </summary>
    [Fact]
    public async Task 書き直しの途中で門の上限が来ても_書き直しは最後まで進む()
    {
        var length = await AppendMonthAsync(2026, 9, 200);
        using var gate = new CancellationTokenSource();
        using var rewrite = new CancellationTokenSource();
        Archive.MidRewriteForTests = () => { gate.Cancel(); return Task.CompletedTask; };

        var trim = await Archive.TrimAsync(length * 7 / 10, gate.Token, rewrite.Token);

        Archive.MidRewriteForTests = null;
        Assert.Equal(new NotificationArchiveTrim(0, "2026-09", 100, 100, StillOver: false), trim);
        Assert.Equal(100, (await Archive.ReadAsync("2026-09")).Records.Count);
    }

    [Fact]
    public async Task 書き直しの上限が来たら_打ち切って元の月を残す()
    {
        var length = await AppendMonthAsync(2026, 9, 200);
        using var gate = new CancellationTokenSource();
        using var rewrite = new CancellationTokenSource();
        Archive.MidRewriteForTests = () => { rewrite.Cancel(); return Task.CompletedTask; };

        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Archive.TrimAsync(length * 7 / 10, gate.Token, rewrite.Token));

        Archive.MidRewriteForTests = null;
        Assert.Equal(rewrite.Token, canceled.CancellationToken);
        Assert.Equal(200, (await Archive.ReadAsync("2026-09")).Records.Count);
        Assert.Equal(["2026-09.jsonl.gz"], Directory.EnumerateFiles(Archive.Directory).Select(Path.GetFileName));
    }

    /// <summary>中身が読めない月（gzip でない）は、最後の1つなら消さずに残す。手で取り出せたかもしれない物を消さない。</summary>
    [Fact]
    public async Task 読めない最後の月は_容量を超えていても残す()
    {
        WriteMonth("2026-09", 4000);

        var trim = await Archive.TrimAsync(1000);

        // 読めない所が1か所あったことはログに書けるよう返す
        Assert.Equal(new NotificationArchiveTrim(0, "2026-09", 0, 0, StillOver: true, UnreadableBlocks: 1), trim);
        Assert.Equal(["2026-09"], Archive.Months());
        Assert.Equal(4000, new FileInfo(Archive.FileFor("2026-09")).Length);
    }

    /// <summary>
    /// 残す側の行は解かずにそのまま写す：壊れた行・新しい版の行も残る（読む口が今どおり飛ばして数える）。
    /// 版の行は先頭に1つと、残す行の中で版が変わる所にだけ置く。
    /// </summary>
    [Fact]
    public async Task 残す側の読めない行と版の違う行は_そのまま残る()
    {
        var text = new StringBuilder().Append("{\"formatVersion\":2}\n");
        for (var i = 0; i < 10; i++)
        {
            // 説明を大きくする：書き直しは版の変わる所でメンバーを分けるので、その頭と尾の分で半分が容量を超えないように
            var detail = string.Concat(Enumerable.Range(0, 20).Select(_ => Guid.NewGuid().ToString("N")));
            text.Append(OneLine(Record(Id(2026, 9, i), At(2026, 9, 1).AddMinutes(i)) with { Detail = detail })).Append('\n');
        }

        text.Append("{壊れた行\n")
            .Append($"{{\"formatVersion\":{StoreFormat.Current + 1}}}\n")
            .Append(OneLine(Record("newer", At(2026, 9, 20)))).Append('\n');
        Directory.CreateDirectory(Archive.Directory);
        await using (var file = File.Create(Archive.FileFor("2026-09")))
        await using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(text.ToString()));
        }

        // 12行のうち新しい側の6行が残る
        var trim = await Archive.TrimAsync(new FileInfo(Archive.FileFor("2026-09")).Length - 1);

        Assert.Equal(new NotificationArchiveTrim(0, "2026-09", 6, 6, StillOver: false), trim);
        var lines = await GunzipLinesAsync(Archive.FileFor("2026-09"));
        Assert.Equal("{\"formatVersion\":2}", lines[0]);
        Assert.Equal("{壊れた行", lines[5]);
        Assert.Equal($"{{\"formatVersion\":{StoreFormat.Current + 1}}}", lines[6]);
        Assert.Equal(8, lines.Count);
        // 書き直した月は、版の変わる所でメンバーを分けてあるので、速い読みだけで読み切れる
        var precise = Archive.PreciseReadsForTests;
        var read = await Archive.ReadAsync("2026-09");
        Assert.Equal(Enumerable.Range(6, 4).Select(i => Id(2026, 9, i)), read.Records.Select(record => record.Id));
        Assert.Equal(2, read.UnreadableLines);
        Assert.Equal(0, read.UnreadableBlocks);
        Assert.Equal(precise, Archive.PreciseReadsForTests);
    }

    /// <summary>
    /// 1件ずつのかたまりの真ん中の1つが壊れても、その1件だけ欠けて前後は読める（ユーザ判断 2026-11-10「壊れたかたまりだけ飛ばす」）。
    /// 頭の1バイトが壊れたときは、GZipStream が例外を出さずに黙って読み終えるので、後ろを落としたことに気づけるかも見る。
    /// </summary>
    [Theory]
    [InlineData("content")]
    [InlineData("crc")]
    [InlineData("truncate")]
    [InlineData("header")]
    public async Task 一件ずつのかたまりの1つが壊れても_その1件だけ欠けて前後は読める(string damage)
    {
        var starts = await AppendOneByOneAsync(9);
        Damage(Archive.FileFor("2026-09"), starts[4], starts[5], damage);

        var read = await Archive.ReadAsync("2026-09");

        Assert.Equal(Enumerable.Range(0, 9).Where(i => i != 4).Select(i => Id(2026, 9, i)), read.Records.Select(record => record.Id));
        Assert.Equal(0, read.UnreadableLines);
        Assert.Equal(1, read.UnreadableBlocks);
    }

    /// <summary>
    /// 数える回と書き直しも同じ読み方で壊れたかたまりを飛ばす。前は壊れた所で数え終え、新しい側（壊れた所より後ろ）が消えた。
    /// 書き直した月は壊れていない月になる。
    /// </summary>
    [Theory]
    [InlineData("content")]
    [InlineData("crc")]
    [InlineData("truncate")]
    [InlineData("header")]
    public async Task 一件ずつのかたまりの1つが壊れても_書き直しで新しい側が残る(string damage)
    {
        var starts = await AppendOneByOneAsync(9);
        var path = Archive.FileFor("2026-09");
        Damage(path, starts[4], starts[5], damage);

        // 読める8件のうち新しい側の4件を残す
        var trim = await Archive.TrimAsync(new FileInfo(path).Length * 7 / 10);

        Assert.Equal(new NotificationArchiveTrim(0, "2026-09", 4, 4, StillOver: false, UnreadableBlocks: 1), trim);
        var precise = Archive.PreciseReadsForTests;
        var read = await Archive.ReadAsync("2026-09");
        Assert.Equal(Enumerable.Range(5, 4).Select(i => Id(2026, 9, i)), read.Records.Select(record => record.Id));
        Assert.Equal(0, read.UnreadableBlocks);
        Assert.Equal(precise, Archive.PreciseReadsForTests);
    }

    /// <summary>
    /// 書き直した1本の大きなかたまりの中が壊れていたら、壊れた所より前の行は読め、後ろは救えない。
    /// その後ろに足したかたまりは読み続ける。
    /// </summary>
    [Fact]
    public async Task 大きなかたまりの中が壊れても_前の行と後ろのかたまりは読める()
    {
        var path = Archive.FileFor("2026-09");
        var random = new Random(20261110);
        var text = new StringBuilder().Append($"{{\"formatVersion\":{StoreFormat.Current}}}\n");
        for (var i = 0; i < 3000; i++)
        {
            var detail = Convert.ToHexString(Enumerable.Range(0, 32).Select(_ => (byte)random.Next(256)).ToArray());
            text.Append(OneLine(Record(Id(2026, 9, i), At(2026, 9, 1).AddSeconds(i)) with { Detail = detail })).Append('\n');
        }

        Directory.CreateDirectory(Archive.Directory);
        await using (var file = File.Create(path))
        await using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(text.ToString()));
        }

        var bigEnd = new FileInfo(path).Length;
        for (var i = 3000; i < 3003; i++)
        {
            await Archive.AppendAsync([Record(Id(2026, 9, i), At(2026, 9, 2).AddSeconds(i))]);
        }

        Damage(path, 0, bigEnd, "content");

        var read = await Archive.ReadAsync("2026-09");

        var ids = read.Records.Select(record => record.Id).ToList();
        var before = ids.TakeWhile(id => string.CompareOrdinal(id, Id(2026, 9, 3000)) < 0).ToList();
        Assert.InRange(before.Count, 1, 2999);
        Assert.Equal(Enumerable.Range(0, before.Count).Select(i => Id(2026, 9, i)), before);
        Assert.Equal(Enumerable.Range(3000, 3).Select(i => Id(2026, 9, i)), ids.Skip(before.Count));
        Assert.Equal(1, read.UnreadableBlocks);
    }

    /// <summary>
    /// かたまりの中身に gzip の頭と同じバイト列が偶然あっても、そこを頭と取り違えない（読み直して尾が合わなければ次の候補へ）。
    /// 格納（縮めない）で書くと、中身のバイト列がそのままファイルに現れる。
    /// </summary>
    [Fact]
    public async Task 中身に頭と同じバイト列があっても_取り違えない()
    {
        var path = Archive.FileFor("2026-09");
        using (var buffer = new MemoryStream())
        {
            buffer.Write(Encoding.UTF8.GetBytes($"{{\"formatVersion\":{StoreFormat.Current}}}\n{OneLine(Record(Id(2026, 9, 0), At(2026, 9, 1)))}\n"));
            buffer.Write([0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0B, 0x01, 0x02, (byte)'\n']);
            buffer.Write(Encoding.UTF8.GetBytes($"{OneLine(Record(Id(2026, 9, 1), At(2026, 9, 1).AddMinutes(1)))}\n"));
            Directory.CreateDirectory(Archive.Directory);
            await using var file = File.Create(path);
            await using var gzip = new GZipStream(file, CompressionLevel.NoCompression);
            await gzip.WriteAsync(buffer.ToArray());
        }

        var starts = new List<long> { new FileInfo(path).Length };
        for (var i = 2; i < 5; i++)
        {
            await Archive.AppendAsync([Record(Id(2026, 9, i), At(2026, 9, 1).AddMinutes(i))]);
            starts.Add(new FileInfo(path).Length);
        }

        // 壊れていないとき：頭の数が合わないので正確な読みに回るが、結果は同じ
        var whole = await Archive.ReadAsync("2026-09");
        Assert.Equal(Enumerable.Range(0, 5).Select(i => Id(2026, 9, i)), whole.Records.Select(record => record.Id));
        Assert.Equal(1, whole.UnreadableLines);
        Assert.Equal(0, whole.UnreadableBlocks);

        // 後ろのかたまりの1つ（3件目）を壊す
        Damage(path, starts[1], starts[2], "content");
        var read = await Archive.ReadAsync("2026-09");
        Assert.Equal(new[] { 0, 1, 2, 4 }.Select(i => Id(2026, 9, i)), read.Records.Select(record => record.Id));
        Assert.Equal(1, read.UnreadableLines);
        Assert.Equal(1, read.UnreadableBlocks);
    }

    /// <summary>壊れていない月は、前と同じく1本の GZipStream で読み、壊れた所を探す読みには入らない（速さを落とさない）。</summary>
    [Fact]
    public async Task 壊れていない月は_速い読みだけで読む()
    {
        await AppendMonthAsync(2026, 9, 200);
        await AppendOneByOneAsync(5, firstIndex: 200);

        var read = await Archive.ReadAsync("2026-09");
        var trim = await Archive.TrimAsync(new FileInfo(Archive.FileFor("2026-09")).Length * 7 / 10);

        Assert.Equal(205, read.Records.Count);
        Assert.Equal(0, read.UnreadableBlocks);
        Assert.Equal(0, trim.UnreadableBlocks);
        Assert.Equal(1, Archive.RewritesForTests);
        Assert.Equal(0, Archive.PreciseReadsForTests);
    }

    /// <summary>
    /// GZipStream が元のストリームから1回に引く大きさ（8KB）より大きいかたまりでも、頭が壊れたら後ろを落としたことに気づく。
    /// 引いたバッファの中に次の頭が無いと、頭の数だけでは合ってしまうので、ファイルの終わりまで引いたかも見る。
    /// </summary>
    [Fact]
    public async Task 入力のバッファより大きいかたまりの頭が壊れても_後ろを読み続ける()
    {
        var starts = await AppendOneByOneAsync(5, detailBytes: 20_000);
        Assert.True(starts[3] - starts[2] > 16 * 1024);
        Damage(Archive.FileFor("2026-09"), starts[2], starts[3], "header");

        var read = await Archive.ReadAsync("2026-09");

        Assert.Equal(new[] { 0, 1, 3, 4 }.Select(i => Id(2026, 9, i)), read.Records.Select(record => record.Id));
        Assert.Equal(1, read.UnreadableBlocks);
    }

    /// <summary>
    /// 読む区切り（GZipStream の 8KB・行に分ける 64KB）を何度もまたぐ壊れていない月は、速い読みだけで読み切る
    /// （頭の数え方が継ぎ目で数え違えると、ふつうの月が毎回遅い読みに回る）。
    /// </summary>
    [Fact]
    public async Task 区切りをまたぐ壊れていない月は_速い読みだけで読む()
    {
        var sizes = new[] { 10, 3000, 9000, 500, 70_000, 1, 4100, 20_000, 7, 66_000, 12 };
        for (var i = 0; i < sizes.Length; i++)
        {
            await AppendOneByOneAsync(1, firstIndex: i, detailBytes: sizes[i]);
        }

        var read = await Archive.ReadAsync("2026-09");

        Assert.Equal(Enumerable.Range(0, sizes.Length).Select(i => Id(2026, 9, i)), read.Records.Select(record => record.Id));
        Assert.Equal(0, read.UnreadableBlocks);
        Assert.Equal(0, Archive.PreciseReadsForTests);
    }

    /// <summary>
    /// 知らせを1件ずつ（かたまり1つずつ）足し、各かたまりの頭の位置と、最後にファイルの大きさを返す。
    /// 説明は種を決めた乱数の16進（縮みにくく、壊し方が回ごとに変わらない）。
    /// </summary>
    private async Task<List<long>> AppendOneByOneAsync(int count, int firstIndex = 0, int detailBytes = 16)
    {
        var path = Archive.FileFor("2026-09");
        var random = new Random(firstIndex * 7919 + detailBytes);
        var starts = new List<long>();
        for (var i = firstIndex; i < firstIndex + count; i++)
        {
            starts.Add(File.Exists(path) ? new FileInfo(path).Length : 0);
            var detail = new byte[detailBytes];
            random.NextBytes(detail);
            await Archive.AppendAsync([Record(Id(2026, 9, i), At(2026, 9, 1).AddMinutes(i)) with { Detail = Convert.ToHexString(detail) }]);
        }

        starts.Add(new FileInfo(path).Length);
        return starts;
    }

    /// <summary>
    /// [start, end) のかたまりを壊す。content：中ほどの1バイトを書き換える／crc：尾の CRC を書き換える／
    /// truncate：中ほどから終わりまでを切り取る（後ろのかたまりは続く）／header：頭の1バイトを書き換える。
    /// </summary>
    internal static void Damage(string path, long start, long end, string kind)
    {
        var bytes = File.ReadAllBytes(path).ToList();
        var middle = (int)(start + (end - start) / 2);
        switch (kind)
        {
            case "content":
                bytes[middle] ^= 0x55;
                break;
            case "crc":
                bytes[(int)end - 8] ^= 0xFF;
                break;
            case "truncate":
                bytes.RemoveRange(middle, (int)end - middle);
                break;
            case "header":
                bytes[(int)start] = 0x00;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        File.WriteAllBytes(path, bytes.ToArray());
    }

    /// <summary>
    /// 見積もりは件数の比なので、新しい側の行が大きいと外れる。外れたらもう一度だけ数えて書き直す。
    /// 古い側10件は小さく、新しい側10件は大きい：1回目に10件残してもまだ超え、2回目に5件まで下がって収まる。
    /// </summary>
    [Fact]
    public async Task 見積もりが外れたら_もう一度だけ書き直して収める()
    {
        var length = await AppendSizedAsync(small: 10, large: 10, guidsPerLarge: 40);

        var trim = await Archive.TrimAsync(length * 6 / 10);

        Assert.Equal(new NotificationArchiveTrim(0, "2026-09", 15, 5, StillOver: false), trim);
        Assert.Equal(2, Archive.RewritesForTests);
        Assert.Equal(Enumerable.Range(15, 5).Select(i => Id(2026, 9, i)), (await Archive.ReadAsync("2026-09")).Records.Select(record => record.Id));
    }

    /// <summary>2回書き直しても超えるときは、それ以上は削らずに残す（呼び手がログに書く）。</summary>
    [Fact]
    public async Task 二回書き直しても超えるなら_それ以上は削らずに残す()
    {
        var length = await AppendSizedAsync(small: 30, large: 2, guidsPerLarge: 200);

        var trim = await Archive.TrimAsync(length * 6 / 10);

        Assert.Equal(new NotificationArchiveTrim(0, "2026-09", 24, 8, StillOver: true), trim);
        Assert.Equal(2, Archive.RewritesForTests);
        Assert.Equal(8, (await Archive.ReadAsync("2026-09")).Records.Count);
    }

    /// <summary>古い側に小さい知らせ、新しい側に大きい知らせ（説明に Guid を並べて縮みにくくする）を1つの束で足す。</summary>
    private async Task<long> AppendSizedAsync(int small, int large, int guidsPerLarge)
    {
        await Archive.AppendAsync(
            Enumerable.Range(0, small + large)
                .Select(i => Record(Id(2026, 9, i), At(2026, 9, 1).AddMinutes(i)) with
                {
                    Detail = string.Concat(Enumerable.Range(0, i < small ? 1 : guidsPerLarge).Select(_ => Guid.NewGuid().ToString("N"))),
                })
                .ToList());
        return new FileInfo(Archive.FileFor("2026-09")).Length;
    }

    private static string Id(int year, int month, int index) => $"{year}-{month:D2}-{index:D4}";

    private static string OneLine(NotificationRecord record) => Json(record).Replace("\r", string.Empty).Replace("\n", string.Empty);

    /// <summary>作り物の知らせを1つの月へ足し、ファイルの大きさを返す。説明に Guid を入れて、件数と大きさがほぼ比例するようにする。</summary>
    private async Task<long> AppendMonthAsync(int year, int month, int count)
    {
        await Archive.AppendAsync(
            Enumerable.Range(0, count)
                .Select(i => Record(Id(year, month, i), At(year, month, 1).AddMinutes(i)) with { Detail = Guid.NewGuid().ToString("N") })
                .ToList());
        return new FileInfo(Archive.FileFor($"{year:D4}-{month:D2}")).Length;
    }

    [Fact]
    public void 大きさと月の数を数える()
    {
        Assert.Equal((0, 0L), Archive.Usage());

        WriteMonth("2026-10", 1000);
        WriteMonth("2026-11", 2000);

        Assert.Equal((2, 3000L), Archive.Usage());
    }

    [Fact]
    public void バックアップと引越しに保管庫が入る_形式の控えには入らない()
    {
        WriteMonth("2026-11", 1000);
        File.WriteAllText(_store.Paths.SettingsFile, "{}");

        var zip = Path.Combine(_root + "-out", "backup.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        try
        {
            BackupArchive.Export(_root, zip, includeImages: false);
            using (var archive = ZipFile.OpenRead(zip))
            {
                Assert.Contains(archive.Entries, entry => entry.FullName == "notification-archive/2026-11.jsonl.gz");
            }

            var backup = StoreFormat.Backup(_root, 1, DateTimeOffset.Now);
            Assert.False(Directory.Exists(Path.Combine(backup, AppPaths.NotificationArchiveDirName)));

            var moved = Path.Combine(_root + "-out", "moved");
            Assert.True(StoreMover.Move(_root, moved).Succeeded);
            Assert.True(File.Exists(Path.Combine(moved, AppPaths.NotificationArchiveDirName, "2026-11.jsonl.gz")));
        }
        finally
        {
            Directory.Delete(_root + "-out", recursive: true);
        }
    }

    private void WriteMonth(string month, int bytes)
    {
        Directory.CreateDirectory(Archive.Directory);
        File.WriteAllBytes(Archive.FileFor(month), new byte[bytes]);
    }

    private static async Task<List<string>> GunzipLinesAsync(string path)
    {
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var lines = new List<string>();
        while (await reader.ReadLineAsync() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }
}
