using System.Diagnostics;
using System.IO.Compression;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 引越し・置き換え・書き出し・戻すの、元のデータと人が置いた物を消さないための守り（外部の点検 2026-10-06）。
///
/// - 別名（ジャンクション）で同じ実体を指す場所を「別の場所」と見て、置き換えの失敗の片付けが元を消していた
/// - 運んでいる間・突き合わせの後にほかの書き手が書いた入力を、大きさだけの突き合わせが見逃し、元を消していた
/// - 書き出しの途中で読めなくなったファイルを「飛ばした」と数え、作りかけの zip で前の zip を上書きしていた
/// - 戻すの片付けが展開先を丸ごと消し、戻している間に人が置いた物まで消していた
///
/// ジャンクションは試験の中で一時フォルダに作る（<c>mklink /J</c> は管理者の権限が要らない）。
/// </summary>
public sealed class StoreTransferSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-transfer-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<string> _links = [];

    private string Source => Path.Combine(_root, "src");

    private string Destination => Path.Combine(_root, "dst");

    public StoreTransferSafetyTests()
    {
        Directory.CreateDirectory(Path.Combine(Source, "items"));
        Directory.CreateDirectory(Path.Combine(Source, "images", "123"));
        File.WriteAllText(Path.Combine(Source, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(Source, "items", "123.json"), "{ \"id\": \"123\" }");
        File.WriteAllBytes(Path.Combine(Source, "images", "123", "a.webp"), new byte[64]);
    }

    public void Dispose()
    {
        // 先にリンクだけを外す（中身を辿って消さない）
        foreach (var link in _links.Where(Directory.Exists))
        {
            Directory.Delete(link);
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Junction(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit();
        Assert.True(Directory.Exists(link), "ジャンクションを作れませんでした");
        _links.Add(link);
        return link;
    }

    private sealed class SyncProgress(Action<StoreMoveProgress> report) : IProgress<StoreMoveProgress>
    {
        public void Report(StoreMoveProgress value) => report(value);
    }

    private sealed class SyncBackupProgress(Action<BackupProgress> report) : IProgress<BackupProgress>
    {
        public void Report(BackupProgress value) => report(value);
    }

    private Dictionary<string, string> SourceContents()
        => Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Source, path), File.ReadAllText);

    // ---- 1. 実体で比べる ----

    [Fact]
    public void ジャンクション越しの場所は_文字では別でも_実体で同じか内側と見る()
    {
        var link = Junction(Path.Combine(_root, "alias"), Source);

        Assert.True(FolderIdentity.IsSame(link, Source));
        Assert.True(FolderIdentity.IsSameOrInside(Path.Combine(link, "items"), Source));
        Assert.True(FolderIdentity.IsSameOrInside(Path.Combine(link, "まだ無い", "下"), Source));
        Assert.True(FolderIdentity.IsSameOrInside(Source, link));
        Assert.False(FolderIdentity.IsSameOrInside(Destination, Source));
        Assert.False(FolderIdentity.IsSame(_root, Source));
    }

    /// <summary>
    /// 別名で同じ実体を指す場所を置き換えで選ぶと、始める前に断り、元に触れない。
    /// 前は選んだ先（＝元）の中身を退け、運ぶ物が無いまま進むか、失敗の片付けで元の物を消していた。
    /// </summary>
    [Fact]
    public void 同じ実体を別名で選んだ置き換えは断り_元のファイルは全部そのまま()
    {
        var before = SourceContents();
        var link = Junction(Path.Combine(_root, "alias"), Source);

        var result = StoreMover.Replace(Source, link);

        Assert.False(result.Succeeded);
        Assert.Null(result.ParkedAt);
        Assert.Equal(before, SourceContents());
    }

    [Fact]
    public void 今の保存先の中を別名で選んだ引越しは断る()
    {
        var link = Junction(Path.Combine(_root, "alias"), Source);

        var result = StoreMover.Move(Source, Path.Combine(link, "next"));

        // 写し始めずに断る（写すと、写した物がまた運ぶ元に数えられる）
        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Copied);
        Assert.Contains("今の保存先の中にあります", result.Error);
        Assert.False(Directory.Exists(Path.Combine(Source, "next")));
    }

    /// <summary>運ぶ先に同じ名前の物が既にあれば始めない（上書きすると、失敗してもその物の中身は戻らない）。</summary>
    [Fact]
    public void 運ぶ先に同じ名前の物があれば始めず_その物の中身も変えない()
    {
        Directory.CreateDirectory(Destination);
        File.WriteAllText(Path.Combine(Destination, "settings.json"), "前から在った");

        var result = StoreMover.Move(Source, Destination);

        Assert.False(result.Succeeded);
        Assert.Equal("前から在った", File.ReadAllText(Path.Combine(Destination, "settings.json")));
        Assert.True(File.Exists(Path.Combine(Source, "settings.json")));
    }

    /// <summary>運んでいる間に運ぶ先へ人が置いた物は、失敗の片付けで消さない（控えるのはこちらが作った物だけ）。</summary>
    [Fact]
    public void 運んでいる間に運ぶ先へ置かれた物は_失敗の片付けで消さない()
    {
        var placed = new List<string>();
        var progress = new SyncProgress(report =>
        {
            if (report.Copied != 1)
            {
                return;
            }

            // まだ写していない分の名前で、先に人が置く。次のコピーがそれに当たって失敗する
            foreach (var file in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(Destination, Path.GetRelativePath(Source, file));
                if (!File.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllText(target, "人が置いた");
                    placed.Add(target);
                }
            }
        });

        var result = StoreMover.Move(Source, Destination, progress);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(placed);
        Assert.All(placed, path => Assert.Equal("人が置いた", File.ReadAllText(path)));
        Assert.True(File.Exists(Path.Combine(Source, "settings.json")));
    }

    // ---- 2. 運んでいる間のほかの書き手 ----

    /// <summary>
    /// 写した後に元が同じ大きさで書き換えられたら、運ばずに失敗で返し、元は消さない。
    /// 前は在るかと大きさだけを突き合わせ、成功として元（書き換えた入力）を消していた。
    /// </summary>
    [Fact]
    public void 運んでいる間に元が同じ大きさで書き換えられたら_失敗にして元を消さない()
    {
        var item = Path.Combine(Source, "items", "123.json");
        var progress = new SyncProgress(report =>
        {
            if (report.Copied == report.Total)
            {
                File.WriteAllText(item, "{ \"id\": \"456\" }");
            }
        });

        var result = StoreMover.Move(Source, Destination, progress);

        Assert.False(result.Succeeded);
        Assert.Contains("123.json", result.Error);
        Assert.Equal("{ \"id\": \"456\" }", File.ReadAllText(item));
        Assert.False(Directory.Exists(Destination));
    }

    /// <summary>写した記録の中身が違えば（同じ大きさでも）、突き合わせで落とす。画像は大きさで見る（StoreMover.Verify に理由）。</summary>
    [Fact]
    public void 写した物の中身が同じ大きさで違えば_突き合わせで落とし元を消さない()
    {
        var progress = new SyncProgress(report =>
        {
            if (report.Copied == report.Total)
            {
                File.WriteAllText(Path.Combine(Destination, "items", "123.json"), "{ \"id\": \"999\" }");
            }
        });

        var result = StoreMover.Move(Source, Destination, progress);

        Assert.False(result.Succeeded);
        Assert.Contains("中身が違います", result.Error);
        Assert.Equal("{ \"id\": \"123\" }", File.ReadAllText(Path.Combine(Source, "items", "123.json")));
    }

    /// <summary>突き合わせの後（場所を書き換える所）で元が書き換えられたら、その物は元に残す。</summary>
    [Fact]
    public void 突き合わせの後に書き換えられた元のファイルは消さずに残す()
    {
        var item = Path.Combine(Source, "items", "123.json");

        var result = StoreMover.Move(Source, Destination, commit: () =>
        {
            File.WriteAllText(item, "{ \"id\": \"123\", \"memo\": \"後から\" }");
        });

        Assert.True(result.Succeeded);
        Assert.False(result.SourceRemoved);
        Assert.Contains("後から", File.ReadAllText(item));
        Assert.False(File.Exists(Path.Combine(Source, "settings.json")));
    }

    [Fact]
    public void 中身の比べは同じなら真_一バイトでも違えば偽()
    {
        var a = Path.Combine(_root, "a.bin");
        var b = Path.Combine(_root, "b.bin");
        var bytes = Enumerable.Range(0, 3_000_000).Select(index => (byte)index).ToArray();
        File.WriteAllBytes(a, bytes);
        File.WriteAllBytes(b, bytes);
        Assert.True(StoreMover.SameContent(a, b));

        bytes[2_500_000] ^= 1;
        File.WriteAllBytes(b, bytes);
        Assert.False(StoreMover.SameContent(a, b));

        File.WriteAllBytes(a, []);
        File.WriteAllBytes(b, []);
        Assert.True(StoreMover.SameContent(a, b));
    }

    // ---- 3. 書き出しの途中の失敗 ----

    /// <summary>
    /// 写し始めた後に読めなくなったら、書き出しごと失敗にし、前の正常な zip を残す。
    /// 前は「開けなかった」と同じに数えて飛ばし、作りかけの項目入りの zip で前の zip を上書きしていた。
    /// 読めなくするのは、ファイルの範囲の錠（開けるが、読むと落ちる）。
    /// </summary>
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void 書き出しの途中で読めなくなると失敗にし_前のzipを残し_作りかけを残さない()
    {
        var zip = Path.Combine(_root, "backup.zip");
        BackupArchive.Export(Source, zip, includeImages: true);
        var previous = File.ReadAllBytes(zip);

        using (var holder = new FileStream(Path.Combine(Source, "images", "123", "a.webp"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            holder.Lock(0, holder.Length);

            var failed = Assert.Throws<BackupReadException>(() => BackupArchive.Export(Source, zip, includeImages: true));

            Assert.Equal(Path.Combine("images", "123", "a.webp"), failed.RelativePath);
            Assert.True(failed.PreviousKept);
            holder.Unlock(0, holder.Length);
        }

        Assert.Equal(previous, File.ReadAllBytes(zip));
        Assert.False(File.Exists(zip + ".tmp"));
    }

    /// <summary>開く前の失敗（ほかのアプリが掴んでいて開けない）は、今までどおり飛ばして数える。</summary>
    [Fact]
    public void 開けないファイルは飛ばして数え_書き出しは完成する()
    {
        var zip = Path.Combine(_root, "backup.zip");

        BackupResult result;
        using (new FileStream(Path.Combine(Source, "images", "123", "a.webp"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = BackupArchive.Export(Source, zip, includeImages: true);
        }

        Assert.Equal(1, result.SkippedLocked);
        using var archive = ZipFile.OpenRead(zip);
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName == "images/123/a.webp");
    }

    // ---- 4. 戻すの片付け ----

    /// <summary>
    /// 戻すを止めたとき、消すのは戻すが作った物だけ。戻している間に人が置いた物（と、それが入ったフォルダ）は残す。
    /// 前は展開先を丸ごと再帰で消していた。
    /// </summary>
    [Fact]
    public void 戻すを止めると_作った物だけ消し_後から置かれた物は残す()
    {
        var zip = Path.Combine(_root, "backup.zip");
        BackupArchive.Export(Source, zip, includeImages: true);
        var destination = Path.Combine(_root, "restored");
        var placedAtRoot = Path.Combine(destination, "後から置いた.txt");
        var placedInside = Path.Combine(destination, "items", "後から置いた.txt");
        using var stop = new CancellationTokenSource();
        var progress = new SyncBackupProgress(_ =>
        {
            Directory.CreateDirectory(Path.Combine(destination, "items"));
            File.WriteAllText(placedAtRoot, "人の物");
            File.WriteAllText(placedInside, "人の物");
            stop.Cancel();
        });

        Assert.ThrowsAny<OperationCanceledException>(() => BackupArchive.Restore(zip, destination, progress, stop.Token));

        Assert.Equal("人の物", File.ReadAllText(placedAtRoot));
        Assert.Equal("人の物", File.ReadAllText(placedInside));
        Assert.Equal(
            new[] { placedAtRoot, placedInside }.Order(StringComparer.Ordinal).ToArray(),
            Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
    }
}
