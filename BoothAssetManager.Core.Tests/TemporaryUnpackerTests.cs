using System.IO.Compression;
using System.Text;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// zip を一時フォルダへ展開する（#56）。unitypackage になっていない配布物を Unity へ入れるための逃げ道。
/// </summary>
public sealed class TemporaryUnpackerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bam-unpack-" + Guid.NewGuid().ToString("N")[..8]);

    private string Root => Path.Combine(_dir, "unpacked");

    public TemporaryUnpackerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string MakeZip(string name, params (string Path, string Text)[] entries)
    {
        var path = System.IO.Path.Combine(_dir, name);
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);
        foreach (var (entryPath, text) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entryPath).Open());
            writer.Write(text);
        }

        return path;
    }

    [Fact]
    public void 日本語の名前も含めて展開する()
    {
        var zip = MakeZip("テクスチャ集.zip", ("ふわもこ/目_01.png", "png"), ("readme.txt", "説明"));

        var folder = new TemporaryUnpacker(Root).Unpack(zip);

        Assert.StartsWith(Root, folder);
        Assert.Equal("png", File.ReadAllText(Path.Combine(folder, "ふわもこ", "目_01.png")));
        Assert.Equal("説明", File.ReadAllText(Path.Combine(folder, "readme.txt")));
    }

    [Fact]
    public void 置き場所の外へ出る名前は書き出さない()
    {
        var zip = MakeZip("slip.zip", ("../../outside.txt", "外"), ("inside.txt", "中"));

        var folder = new TemporaryUnpacker(Root).Unpack(zip);

        Assert.True(File.Exists(Path.Combine(folder, "inside.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "outside.txt")));
        Assert.False(File.Exists(Path.Combine(Root, "outside.txt")));
    }

    [Fact]
    public void 同じzipは展開し直さない()
    {
        var zip = MakeZip("same.zip", ("a.txt", "元"));
        var unpacker = new TemporaryUnpacker(Root);

        var first = unpacker.Unpack(zip);
        File.WriteAllText(Path.Combine(first, "a.txt"), "展開した後に触った");
        var second = unpacker.Unpack(zip);

        Assert.Equal(first, second);
        Assert.Equal("展開した後に触った", File.ReadAllText(Path.Combine(second, "a.txt")));
    }

    [Fact]
    public void 片付けると置き場所ごと消える()
    {
        var unpacker = new TemporaryUnpacker(Root);
        unpacker.Unpack(MakeZip("gone.zip", ("a.txt", "a")));

        Assert.True(unpacker.CleanUp());
        Assert.False(Directory.Exists(Root));
    }

    [Fact]
    public void zipの中の1ファイルだけを取り出す()
    {
        // Unity の「Custom Package...」には実在するパスを渡す（#69）
        var zip = MakeZip("pack.zip", ("中/Sig_Ring.unitypackage", "tar.gz"), ("readme.txt", "説明"));
        var unpacker = new TemporaryUnpacker(Root);

        var path = unpacker.ExtractEntry(zip, "中/Sig_Ring.unitypackage");

        Assert.Equal("Sig_Ring.unitypackage", Path.GetFileName(path));
        Assert.Equal("tar.gz", File.ReadAllText(path));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "readme.txt")));
        Assert.Equal(path, unpacker.ExtractEntry(zip, "中/Sig_Ring.unitypackage"));
    }

    /// <summary>
    /// 同じ名前のファイルが別のフォルダにあっても取り違えない。
    /// 前はファイル名だけで控えの場所を決めていたので、PC 版を先に取り出すと Quest 版を頼んでも PC 版が返っていた。
    /// </summary>
    [Fact]
    public void 同じ名前の別のフォルダのファイルを取り違えない()
    {
        var zip = MakeZip("pack.zip", ("PC/X.unitypackage", "pc"), ("Quest/X.unitypackage", "quest"));
        var unpacker = new TemporaryUnpacker(Root);

        var pc = unpacker.ExtractEntry(zip, "PC/X.unitypackage");
        var quest = unpacker.ExtractEntry(zip, "Quest/X.unitypackage");

        Assert.NotEqual(pc, quest);
        Assert.Equal("pc", File.ReadAllText(pc));
        Assert.Equal("quest", File.ReadAllText(quest));
        Assert.Equal("X.unitypackage", Path.GetFileName(quest));
    }

    /// <summary>
    /// 深いフォルダの中の物でも、Unity のファイル選択に渡せる長さ（260 字未満）に収める。
    /// 同じ名前で別のフォルダの物は、畳んだ後も取り違えない。
    /// </summary>
    [Fact]
    public void 長すぎるパスは短く畳み取り違えない()
    {
        var deep = string.Join('/', Enumerable.Repeat(new string('深', 40), 6));
        var zip = MakeZip("deep.zip", ($"{deep}/PC/X.unitypackage", "pc"), ($"{deep}/Quest/X.unitypackage", "quest"));
        var unpacker = new TemporaryUnpacker(Root);

        var pc = unpacker.ExtractEntry(zip, $"{deep}/PC/X.unitypackage");
        var quest = unpacker.ExtractEntry(zip, $"{deep}/Quest/X.unitypackage");

        Assert.True(pc.Length < 260, $"{pc.Length} 字");
        Assert.True(quest.Length < 260, $"{quest.Length} 字");
        Assert.NotEqual(pc, quest);
        Assert.Equal("pc", File.ReadAllText(pc));
        Assert.Equal("quest", File.ReadAllText(quest));
        Assert.Equal("X.unitypackage", Path.GetFileName(pc));
    }

    /// <summary>zip の中のパスに <c>..</c> があっても、置き場所の外へは書かない。</summary>
    [Fact]
    public void 上へ出る名前でも置き場所の中に取り出す()
    {
        var zip = MakeZip("pack.zip", ("../../evil.unitypackage", "x"));

        var path = new TemporaryUnpacker(Root).ExtractEntry(zip, "../../evil.unitypackage");

        Assert.StartsWith(Path.GetFullPath(Root), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void zipの中に無いファイルは投げる()
    {
        var zip = MakeZip("pack.zip", ("a.txt", "a"));

        Assert.Throws<FileNotFoundException>(() => new TemporaryUnpacker(Root).ExtractEntry(zip, "無い.unitypackage"));
    }

    [Fact]
    public void 無いzipでは投げる()
        => Assert.Throws<FileNotFoundException>(() => new TemporaryUnpacker(Root).Unpack(Path.Combine(_dir, "無い.zip")));

    [Fact]
    public void 既定の置き場所の中かを見分ける()
    {
        Assert.True(TemporaryUnpacker.IsInsideDefaultRoot(Path.Combine(TemporaryUnpacker.DefaultRoot, "x-1234", "a.png")));
        Assert.False(TemporaryUnpacker.IsInsideDefaultRoot(@"D:\dl\a.png"));
    }

    /// <summary>
    /// 展開の途中でもう一度押しても、2本目は1本目の書きかけを消さずに待ち、同じ展開先を返す（大容量の確かめ #3）。
    /// 前は2本目が書きかけを消して展開し直し、1本目が「別のアプリがファイルを開いています」で失敗していた。
    /// </summary>
    [Fact]
    public async Task 展開の途中の2本目は1本目を待つ()
    {
        var zip = MakeZip("重ねる.zip", ("a/1.png", "一"), ("b/2.png", "二"));
        Task<string>? second = null;
        var secondFinishedEarly = true;

        var first = new TemporaryUnpacker(Root)
        {
            OnGateEntered = () =>
            {
                // 1本目が錠の中にいる間に、別のインスタンス（押し直しで作られる命令と同じ）で押す
                second = Task.Run(() => new TemporaryUnpacker(Root).Unpack(zip));
                secondFinishedEarly = second.Wait(200);
            },
        }.Unpack(zip);

        Assert.False(secondFinishedEarly);
        Assert.Equal(first, await second!);
        Assert.Equal("一", File.ReadAllText(Path.Combine(first, "a", "1.png")));
        Assert.Equal("二", File.ReadAllText(Path.Combine(first, "b", "2.png")));
    }

    /// <summary>届いた進み具合をその場で受ける（<see cref="Progress{T}"/> は別のスレッドへ回すので、順と数を確かめられない）。</summary>
    private sealed class Recorder(Action<TemporaryUnpackProgress>? onReport = null) : IProgress<TemporaryUnpackProgress>
    {
        public List<TemporaryUnpackProgress> Reports { get; } = [];

        public void Report(TemporaryUnpackProgress value)
        {
            Reports.Add(value);
            onReport?.Invoke(value);
        }
    }

    /// <summary>1回で読む量（81,920 バイト）を超える中身。ファイルの途中で進み具合が届き、途中で止められることを確かめるため。</summary>
    private static string Big(char letter) => new(letter, 300_000);

    /// <summary>下の帯に「どこまで来たか」を出せるよう、書き出す前に合計を知らせ、最後は合計まで届く。</summary>
    [Fact]
    public void 進み具合は0から合計まで届く()
    {
        var zip = MakeZip("進む.zip", ("a/大きい.psd", Big('a')), ("b.txt", "小さい"), ("空のフォルダ/", string.Empty));
        var total = 300_000 + Encoding.UTF8.GetByteCount("小さい");
        var recorder = new Recorder();

        new TemporaryUnpacker(Root).Unpack(zip, recorder);

        Assert.Equal(new TemporaryUnpackProgress(0, total), recorder.Reports[0]);
        Assert.Equal(new TemporaryUnpackProgress(total, total), recorder.Reports[^1]);

        // 1ファイルの途中でも届く（件数で出すと、大きい1ファイルの間ずっと止まって見える）
        Assert.Contains(recorder.Reports, report => report.DoneBytes > 0 && report.DoneBytes < 300_000);
        Assert.True(recorder.Reports.Zip(recorder.Reports.Skip(1)).All(pair => pair.First.DoneBytes <= pair.Second.DoneBytes));
    }

    /// <summary>置き場所の外へ出る名前は書き出さないので、合計にも数えない（数えると帯が最後まで届かない）。</summary>
    [Fact]
    public void 書き出さない物は合計に数えない()
    {
        var zip = MakeZip("slip2.zip", ("../../outside.txt", Big('x')), ("inside.txt", "中"));
        var recorder = new Recorder();

        new TemporaryUnpacker(Root).Unpack(zip, recorder);

        Assert.Equal(new TemporaryUnpackProgress(3, 3), recorder.Reports[^1]);
    }

    /// <summary>
    /// 中止を押すと、ファイルの途中でも止まり、書きかけを残さない（ユーザ判断 2026-09-30）。
    /// 終わった印も無いので、同じ zip をもう一度押せば最初から展開し直せる。
    /// </summary>
    [Fact]
    public void 中止すると書きかけを消しもう一度押すと展開し直せる()
    {
        var zip = MakeZip("止める.zip", ("a/大きい.psd", Big('a')), ("b/後ろ.png", "後"));
        using var stop = new CancellationTokenSource();
        var recorder = new Recorder(report =>
        {
            if (report.DoneBytes > 0)
            {
                stop.Cancel();
            }
        });
        var unpacker = new TemporaryUnpacker(Root);

        Assert.ThrowsAny<OperationCanceledException>(() => unpacker.Unpack(zip, recorder, stop.Token));

        // 1つ目のファイルの途中で止まっている（書き切ってから止まるのでは、数GBの1ファイルで中止が効かない）
        Assert.True(recorder.Reports[^1].DoneBytes < 300_000, $"{recorder.Reports[^1].DoneBytes} バイトまで書いた");
        Assert.Empty(Directory.EnumerateFileSystemEntries(Root));

        var folder = unpacker.Unpack(zip);

        Assert.Equal(Big('a'), File.ReadAllText(Path.Combine(folder, "a", "大きい.psd")));
        Assert.Equal("後", File.ReadAllText(Path.Combine(folder, "b", "後ろ.png")));
        Assert.True(File.Exists(folder + ".done"));
    }

    /// <summary>始める前から中止されていれば、展開先を作らない。</summary>
    [Fact]
    public void 中止済みなら何も書かない()
    {
        var zip = MakeZip("押す前.zip", ("a.txt", "a"));
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => new TemporaryUnpacker(Root).Unpack(zip, null, stop.Token));

        Assert.False(Directory.Exists(Root) && Directory.EnumerateFileSystemEntries(Root).Any());
    }

    /// <summary>
    /// 錠を待っている2本目を中止しても、先に入っている1本目の書きかけに触らない。
    /// 片付けは錠の中でだけ行う——待っている側が消すと、展開先ごとの錠で直した重なり（大容量の確かめ #3）と同じ壊れ方になる。
    /// </summary>
    [Fact]
    public async Task 錠を待つ2本目を中止しても1本目は最後まで展開する()
    {
        var zip = MakeZip("待つ.zip", ("a/1.png", Big('一')), ("b/2.png", "二"));
        using var stopSecond = new CancellationTokenSource();
        Task<string>? second = null;
        var secondWaited = false;

        var first = new TemporaryUnpacker(Root)
        {
            OnGateEntered = () =>
            {
                second = Task.Run(() => new TemporaryUnpacker(Root).Unpack(zip, null, stopSecond.Token));
                secondWaited = !second.Wait(200);
                stopSecond.Cancel();

                // 2本目が出ていくのを見届けてから1本目を進める（出ていく途中で何かを消していれば、この後の展開が欠ける）
                try
                {
                    second.Wait();
                }
                catch (AggregateException)
                {
                }
            },
        }.Unpack(zip);

        Assert.True(secondWaited);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second!);
        Assert.Equal(Big('一'), File.ReadAllText(Path.Combine(first, "a", "1.png")));
        Assert.Equal("二", File.ReadAllText(Path.Combine(first, "b", "2.png")));
        Assert.True(File.Exists(first + ".done"));
    }

    /// <summary>zip に書いてある更新時刻を付ける（前の <c>ExtractToFile</c> と同じ。自前で書き出すようにしても変えない）。</summary>
    [Fact]
    public void 更新時刻はzipに書いてある値にする()
    {
        var path = Path.Combine(_dir, "日付.zip");
        var stamp = new DateTimeOffset(2024, 3, 5, 12, 34, 56, TimeSpan.Zero).ToLocalTime();
        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8))
        {
            var entry = archive.CreateEntry("a.txt");
            entry.LastWriteTime = stamp;
            using var writer = new StreamWriter(entry.Open());
            writer.Write("a");
        }

        var folder = new TemporaryUnpacker(Root).Unpack(path);

        Assert.Equal(stamp.DateTime, File.GetLastWriteTime(Path.Combine(folder, "a.txt")));
    }
}
