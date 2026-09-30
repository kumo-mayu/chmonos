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

    /// <summary>どの保存先の置き場所でも、一時の場所の中なら取り込まない（別の保存先のアプリが展開した物も、閉じると消える）。</summary>
    [Fact]
    public void 一時の場所の中かを見分ける()
    {
        var ofStore = TemporaryUnpacker.RootFor(Path.Combine(_dir, "store-a"));

        Assert.True(TemporaryUnpacker.IsInsideTemporaryArea(Path.Combine(ofStore, "x-1234", "a.png")));
        Assert.True(TemporaryUnpacker.IsInsideTemporaryArea(Path.Combine(TemporaryUnpacker.TemporaryArea, "unpacked", "x-1234", "a.png")));
        Assert.False(TemporaryUnpacker.IsInsideTemporaryArea(@"D:\dl\a.png"));
    }

    /// <summary>
    /// 置き場所は保存先ごとに分かれる（ユーザ判断 2026-09-30）。前は全部の保存先が1つの置き場所を使い、
    /// 起動と終了の片付けが、保存先の違う別のアプリの展開した物まで消していた。
    /// </summary>
    [Fact]
    public void 置き場所は保存先ごとに分かれる()
    {
        var area = Path.Combine(_dir, "area");
        var storeA = Path.Combine(_dir, "store-a");
        var storeB = Path.Combine(_dir, "store-b");

        var rootA = TemporaryUnpacker.RootFor(storeA, area);

        Assert.NotEqual(rootA, TemporaryUnpacker.RootFor(storeB, area));
        Assert.Equal(area, Path.GetDirectoryName(rootA));

        // 同じ保存先なら、書き方が違っても同じ置き場所（大文字小文字・末尾の区切り）
        Assert.Equal(rootA, TemporaryUnpacker.RootFor(storeA.ToUpperInvariant() + Path.DirectorySeparatorChar, area));
    }

    [Fact]
    public void 片付けは自分の保存先の分だけを消す()
    {
        var area = Path.Combine(_dir, "area");
        var mine = new TemporaryUnpacker(TemporaryUnpacker.RootFor(Path.Combine(_dir, "store-a"), area));
        var theirs = new TemporaryUnpacker(TemporaryUnpacker.RootFor(Path.Combine(_dir, "store-b"), area));
        var zip = MakeZip("both.zip", ("a.txt", "a"), ("中/p.unitypackage", "p"));

        var myFolder = mine.Unpack(zip);
        var theirFolder = theirs.Unpack(zip);
        var theirPackage = theirs.ExtractEntry(zip, "中/p.unitypackage");
        Assert.NotEqual(myFolder, theirFolder);

        Assert.True(mine.CleanUp());

        Assert.False(Directory.Exists(myFolder));
        Assert.True(File.Exists(Path.Combine(theirFolder, "a.txt")));
        Assert.True(File.Exists(theirPackage));
    }

    /// <summary>
    /// 保存先ごとに分ける前の版が残した物（置き場所の直下の展開と、Unity へ送る前の取り出し）は、起動のときに片付ける。
    /// 今の版の、保存先ごとの置き場所には触れない。
    /// </summary>
    [Fact]
    public void 分ける前の置き場所に残った物を片付ける()
    {
        var area = Path.Combine(_dir, "area");
        var legacy = Path.Combine(area, "unpacked");
        Directory.CreateDirectory(Path.Combine(legacy, "old-1a2b3c4d"));
        File.WriteAllText(Path.Combine(legacy, "old-1a2b3c4d", "a.txt"), "a");
        File.WriteAllText(Path.Combine(legacy, "old-1a2b3c4d.done"), "done");
        Directory.CreateDirectory(Path.Combine(legacy, "packages", "5e6f7a8b"));
        File.WriteAllText(Path.Combine(legacy, "packages", "5e6f7a8b", "p.unitypackage"), "p");

        var current = new TemporaryUnpacker(TemporaryUnpacker.RootFor(Path.Combine(_dir, "store-a"), area));
        var kept = current.Unpack(MakeZip("kept.zip", ("a.txt", "a")));

        Assert.True(TemporaryUnpacker.RemoveLegacyRoot(area));

        Assert.False(Directory.Exists(legacy));
        Assert.True(File.Exists(Path.Combine(kept, "a.txt")));

        // 残っていなくても失敗にしない（毎回の起動で呼ぶ）
        Assert.True(TemporaryUnpacker.RemoveLegacyRoot(area));
    }

    /// <summary>
    /// 置き場所を渡さない組み立て（命令と、Unity へ送る列）は、伝えられた保存先の置き場所を使う。
    /// 閉じるときの片付けと同じ場所でないと、展開した物が残る。
    /// </summary>
    [Fact]
    public void 置き場所を渡さなければ伝えられた保存先の置き場所を使う()
    {
        var store = Path.Combine(_dir, "store-a");

        TemporaryUnpacker.UseStore(store);

        Assert.Equal(TemporaryUnpacker.RootFor(store), new TemporaryUnpacker().Root);
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

    /// <summary>
    /// 展開が失敗したら、書きかけをその場で消す（ユーザ判断 2026-09-30）。前は次に押すかアプリを閉じるまで残り、
    /// 空き容量が足りなくて失敗した人の空きを食っていた。
    /// 失敗は zip の中身で作る：「a」というファイルの後に「a/下.txt」が来ると、フォルダ「a」を作れずに失敗する。
    /// </summary>
    [Fact]
    public void 失敗すると書きかけを消す()
    {
        var zip = MakeZip("ぶつかる.zip", ("先.psd", Big('a')), ("a", "ファイル"), ("a/下.txt", "フォルダが要る"));

        Assert.ThrowsAny<IOException>(() => new TemporaryUnpacker(Root).Unpack(zip));

        Assert.Empty(Directory.EnumerateFileSystemEntries(Root));
    }

    /// <summary>
    /// 書きかけを消せなくても（別のアプリが掴んでいる）、元の失敗をそのまま返す。消す側の失敗で上書きすると、
    /// 失敗の文（<see cref="FailureText.Cause"/>）が元の原因を言えなくなる。掴んでいた物を放せば、もう一度押して最初から展開できる。
    /// </summary>
    [Fact]
    public void 書きかけを消せなくても元の失敗を返しもう一度押すと展開し直せる()
    {
        var zip = MakeZip("掴まれる.zip", ("a/1.png", Big('一')), ("b/2.png", "二"));
        FileStream? held = null;
        string? heldPath = null;
        var unpacker = new TemporaryUnpacker(Root);
        var recorder = new Recorder(report =>
        {
            // 1つ目を書いている途中で、2つ目の書き込み先を先に掴む（展開先の名前は、1つ目が書かれた後なら置き場所を見れば分かる）
            if (held is null && report.DoneBytes > 0)
            {
                heldPath = Path.Combine(Directory.EnumerateDirectories(Root).Single(), "b", "2.png");
                Directory.CreateDirectory(Path.GetDirectoryName(heldPath)!);
                held = new FileStream(heldPath, FileMode.Create, FileAccess.Write, FileShare.None);
            }
        });

        IOException failure;
        try
        {
            failure = Assert.ThrowsAny<IOException>(() => unpacker.Unpack(zip, recorder));

            // 掴まれた物は残る（消せない）。終わった印は無い
            Assert.True(File.Exists(heldPath));
            Assert.Empty(Directory.EnumerateFiles(Root, "*.done"));
        }
        finally
        {
            held?.Dispose();
        }

        // 元の失敗（共有違反）のまま。画面は「別のアプリがファイルを開いています」と言える
        Assert.Equal("別のアプリがファイルを開いています。閉じてからもう一度お試しください。", FailureText.Cause(failure));

        var folder = unpacker.Unpack(zip);

        Assert.Equal(Big('一'), File.ReadAllText(Path.Combine(folder, "a", "1.png")));
        Assert.Equal("二", File.ReadAllText(Path.Combine(folder, "b", "2.png")));
        Assert.True(File.Exists(folder + ".done"));
    }

    /// <summary>置き場所の下の全ファイル（取り出しの書きかけ「.part」が残っていないかを見る）。</summary>
    private string[] FilesUnderRoot()
        => Directory.Exists(Root) ? Directory.GetFiles(Root, "*", SearchOption.AllDirectories) : [];

    /// <summary>Unity へ送る前の取り出しも、どこまで来たかを出せる（0 から、その1件の大きさまで）。</summary>
    [Fact]
    public void 取り出しの進み具合は0から大きさまで届く()
    {
        var zip = MakeZip("pack.zip", ("中/大きい.unitypackage", Big('u')), ("ほか.txt", Big('x')));
        var recorder = new Recorder();

        new TemporaryUnpacker(Root).ExtractEntry(zip, "中/大きい.unitypackage", recorder);

        // 合計は取り出す1件の大きさ（zip のほかの物は数えない）
        Assert.Equal(new TemporaryUnpackProgress(0, 300_000), recorder.Reports[0]);
        Assert.Equal(new TemporaryUnpackProgress(300_000, 300_000), recorder.Reports[^1]);
        Assert.Contains(recorder.Reports, report => report.DoneBytes > 0 && report.DoneBytes < 300_000);
    }

    /// <summary>
    /// 取り出しも、ファイルの途中で中止が効き、書きかけを残さない（ユーザ判断 2026-09-30）。
    /// 前は1件を書き切ってから止まり、数GBの unitypackage では「中止」を押してから長く待たされた。
    /// </summary>
    [Fact]
    public void 取り出しを中止すると書きかけを消しもう一度送ると取り出し直せる()
    {
        var zip = MakeZip("pack.zip", ("中/大きい.unitypackage", Big('u')));
        using var stop = new CancellationTokenSource();
        var recorder = new Recorder(report =>
        {
            if (report.DoneBytes > 0)
            {
                stop.Cancel();
            }
        });
        var unpacker = new TemporaryUnpacker(Root);

        Assert.ThrowsAny<OperationCanceledException>(
            () => unpacker.ExtractEntry(zip, "中/大きい.unitypackage", recorder, stop.Token));

        Assert.True(recorder.Reports[^1].DoneBytes < 300_000, $"{recorder.Reports[^1].DoneBytes} バイトまで書いた");
        Assert.Empty(FilesUnderRoot());

        var path = unpacker.ExtractEntry(zip, "中/大きい.unitypackage");

        Assert.Equal(Big('u'), File.ReadAllText(path));
        Assert.Equal(new[] { path }, FilesUnderRoot());
    }

    /// <summary>始める前から中止されていれば、何も書かない。</summary>
    [Fact]
    public void 中止済みなら取り出さない()
    {
        var zip = MakeZip("pack.zip", ("a.unitypackage", "a"));
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => new TemporaryUnpacker(Root).ExtractEntry(zip, "a.unitypackage", null, stop.Token));

        Assert.Empty(FilesUnderRoot());
    }

    /// <summary>
    /// 取り出しが失敗したら、書きかけ（.part）を消す（一時展開の失敗と同じ扱い）。
    /// 失敗は、本物の名前の場所を先にフォルダで塞いで作る（書き終えた後の名前の付け替えが通らない）。
    /// </summary>
    [Fact]
    public void 取り出しが失敗すると書きかけを消しもう一度送ると取り出し直せる()
    {
        var zip = MakeZip("pack.zip", ("中/大きい.unitypackage", Big('u')));
        var unpacker = new TemporaryUnpacker(Root);

        // 取り出す先は zip と中のパスで決まるので、1度取り出して場所を知ってから、そこをフォルダに置き換える
        var path = unpacker.ExtractEntry(zip, "中/大きい.unitypackage");
        File.Delete(path);
        Directory.CreateDirectory(path);

        var failure = Record.Exception(() => unpacker.ExtractEntry(zip, "中/大きい.unitypackage"));

        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.GetType().Name);
        Assert.Empty(FilesUnderRoot());

        Directory.Delete(path);

        Assert.Equal(path, unpacker.ExtractEntry(zip, "中/大きい.unitypackage"));
        Assert.Equal(Big('u'), File.ReadAllText(path));
        Assert.Equal(new[] { path }, FilesUnderRoot());
    }

    /// <summary>
    /// 書きかけを別のアプリが掴んでいて消せなくても、元の失敗をそのまま返す。
    /// 放した後にもう一度送ると、残った書きかけを上書きして取り出せる。
    /// </summary>
    [Fact]
    public void 取り出しの書きかけを消せなくても元の失敗を返す()
    {
        var zip = MakeZip("pack.zip", ("中/大きい.unitypackage", Big('u')));
        var unpacker = new TemporaryUnpacker(Root);
        var path = unpacker.ExtractEntry(zip, "中/大きい.unitypackage");
        File.Delete(path);

        IOException failure;
        using (new FileStream(path + ".part", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            failure = Assert.ThrowsAny<IOException>(() => unpacker.ExtractEntry(zip, "中/大きい.unitypackage"));
        }

        Assert.Equal("別のアプリがファイルを開いています。閉じてからもう一度お試しください。", FailureText.Cause(failure));
        Assert.False(File.Exists(path));

        Assert.Equal(path, unpacker.ExtractEntry(zip, "中/大きい.unitypackage"));
        Assert.Equal(Big('u'), File.ReadAllText(path));
        Assert.Equal(new[] { path }, FilesUnderRoot());
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
