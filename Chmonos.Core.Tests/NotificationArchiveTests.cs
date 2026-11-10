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
        Assert.Equal(2, await Create(megabytes: 1).TrimArchiveAsync());

        Assert.Equal(["2026-10", "2026-11"], Archive.Months());
        Assert.True(File.Exists(Path.Combine(Archive.Directory, "使う人のメモ.txt")));
    }

    [Fact]
    public async Task 閉じるときに容量の中なら何も消さない()
    {
        WriteMonth("2026-10", 400_000);
        WriteMonth("2026-11", 400_000);

        Assert.Equal(0, await Create(megabytes: 1).TrimArchiveAsync());

        Assert.Equal(["2026-10", "2026-11"], Archive.Months());
    }

    [Fact]
    public async Task 容量が0なら_閉じるときに全部消す()
    {
        WriteMonth("2026-10", 1000);
        WriteMonth("2026-11", 1000);

        Assert.Equal(2, await Create(megabytes: 0).TrimArchiveAsync());

        Assert.Empty(Archive.Months());
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
