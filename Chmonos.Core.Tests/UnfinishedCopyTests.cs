using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 引越し・戻すの途中でプロセスごと止まったときの写しかけ（実機の確かめ 2026-10-07）。
/// 前は印が無く、写しかけの場所を後で選ぶと「既にあるライブラリ」に見え、「選んだ場所のデータを使う」で
/// 商品の大半が欠けたライブラリへ切り替わった。
///
/// 止まった所は、進み具合の知らせから投げる例外で作る。運ぶ処理が拾うのは入出力・権限・中止の例外だけなので、
/// それ以外は片付けを通らずに抜ける——プロセスが落ちて片付けが走らないのと同じ姿が残る
/// </summary>
public sealed class UnfinishedCopyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-unfinished-" + Guid.NewGuid().ToString("N"));

    private string Source => Path.Combine(_root, "src");

    private string Destination => Path.Combine(_root, "dst");

    public UnfinishedCopyTests()
    {
        Directory.CreateDirectory(Path.Combine(Source, "items"));
        Directory.CreateDirectory(Path.Combine(Source, "images", "123"));
        File.WriteAllText(Path.Combine(Source, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(Source, "items", "123.json"), "{ \"id\": \"123\" }");
        File.WriteAllText(Path.Combine(Source, "items", "456.json"), "{ \"id\": \"456\" }");
        File.WriteAllBytes(Path.Combine(Source, "images", "123", "a.webp"), new byte[64]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class Crash : Exception;

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// <summary>全部を写し終えた直後（突き合わせの前）に止める。写し先には商品も設定もそろっている。</summary>
    private void CrashMoveAtTheEnd(Func<StoreMoveResult> run)
        => Assert.Throws<Crash>(() => run());

    private SyncProgress<StoreMoveProgress> CrashAfterLast()
        => new(report =>
        {
            if (report.Copied == report.Total)
            {
                throw new Crash();
            }
        });

    [Fact]
    public void 引越しの途中で止まった場所は_ライブラリと見ず_写しかけと分かる()
    {
        CrashMoveAtTheEnd(() => StoreMover.Move(Source, Destination, CrashAfterLast()));

        // 印が無ければ、商品と設定がそろっているのでライブラリに見えていた
        Assert.True(File.Exists(Path.Combine(Destination, "settings.json")));
        Assert.True(UnfinishedCopy.IsAt(Destination));
        Assert.False(StoreLocation.LooksLikeStore(Destination));

        var marker = UnfinishedCopy.Read(Destination);
        Assert.NotNull(marker);
        Assert.Equal(UnfinishedCopyKind.Move, marker.Kind);
        Assert.Equal(Source, marker.From);

        // 人が開いて読める形（名前の付いた欄と、何の物かの説明）
        var text = File.ReadAllText(UnfinishedCopy.MarkerPath(Destination));
        Assert.Contains("\"kind\": \"move\"", text);
        Assert.Contains("途中で止まったコピー", text);
    }

    /// <summary>名前が「Chmonos」でない写しかけ（置き換えの先など）を選んでも、中に新しく作らずその場所を返す。</summary>
    [Fact]
    public void 写しかけの場所を選ぶと_中に作らずその場所を使う()
    {
        CrashMoveAtTheEnd(() => StoreMover.Move(Source, Destination, CrashAfterLast()));

        Assert.Equal(Destination, StoreLocation.RootFor(Destination));
    }

    [Fact]
    public void 写しかけへもう一度引っ越すと_断り_写しかけは触らない()
    {
        CrashMoveAtTheEnd(() => StoreMover.Move(Source, Destination, CrashAfterLast()));
        var before = UnfinishedCopy.Plan(Destination).Files.Count;

        var moved = StoreMover.Move(Source, Destination);
        var replaced = StoreMover.Replace(Source, Destination);

        Assert.False(moved.Succeeded);
        Assert.Equal(StoreMover.UnfinishedRefusal, moved.Error);
        Assert.False(replaced.Succeeded);
        Assert.Equal(StoreMover.UnfinishedRefusal, replaced.Error);
        Assert.Equal(before, UnfinishedCopy.Plan(Destination).Files.Count);
        Assert.True(File.Exists(Path.Combine(Source, "settings.json")));
    }

    [Fact]
    public void 引越しを写し終えると印は消える()
    {
        var sawMarker = false;
        var result = StoreMover.Move(Source, Destination, new SyncProgress<StoreMoveProgress>(_ => sawMarker |= UnfinishedCopy.IsAt(Destination)));

        Assert.True(result.Succeeded);
        Assert.True(sawMarker);
        Assert.False(UnfinishedCopy.IsAt(Destination));
        Assert.True(StoreLocation.LooksLikeStore(Destination));
    }

    [Fact]
    public void 引越しを止めると_写しと一緒に印も消える()
    {
        using var stop = new CancellationTokenSource();
        var result = StoreMover.Move(Source, Destination, new SyncProgress<StoreMoveProgress>(_ => stop.Cancel()), stop.Token);

        Assert.False(result.Succeeded);
        Assert.Null(result.LeftoverAt);
        Assert.False(Directory.Exists(Destination));
    }

    [Fact]
    public void 戻すの途中は印があり_戻し終えると印は消える()
    {
        var zip = Path.Combine(_root, "backup.zip");
        BackupArchive.Export(Source, zip, includeImages: true);
        var restored = Path.Combine(_root, "restored");

        var sawMarker = false;
        BackupArchive.Restore(zip, restored, new SyncProgress<BackupProgress>(_ => sawMarker |= UnfinishedCopy.IsAt(restored)));

        Assert.True(sawMarker);
        Assert.False(UnfinishedCopy.IsAt(restored));
        Assert.True(StoreLocation.LooksLikeStore(restored));
    }

    [Fact]
    public void 戻すの途中の印は_何の写しかけかを書く_書き出しには入らない()
    {
        var zip = Path.Combine(_root, "backup.zip");
        BackupArchive.Export(Source, zip, includeImages: true);
        var restored = Path.Combine(_root, "restored");

        UnfinishedCopyMarker? seen = null;
        BackupArchive.Restore(zip, restored, new SyncProgress<BackupProgress>(_ => seen ??= UnfinishedCopy.Read(restored)));

        Assert.NotNull(seen);
        Assert.Equal(UnfinishedCopyKind.Restore, seen.Kind);
        Assert.Equal(zip, seen.From);
        Assert.True(seen.CreatedFolder);
    }

    /// <summary>
    /// 片付けは写しで作った物だけを消す。写し始める前から写し先にあった物（番兵）は、フォルダの中の物も残す。
    /// 写し先のフォルダは元から在ったので畳まない
    /// </summary>
    [Fact]
    public void 片付けは写しで作った物だけを消し_元からあった物は残す()
    {
        Directory.CreateDirectory(Path.Combine(Destination, "notes"));
        File.WriteAllText(Path.Combine(Destination, "keep.txt"), "番兵");
        File.WriteAllText(Path.Combine(Destination, "notes", "memo.txt"), "番兵");

        CrashMoveAtTheEnd(() => StoreMover.Move(Source, Destination, CrashAfterLast()));

        var plan = UnfinishedCopy.Plan(Destination);
        Assert.Equal(4, plan.Files.Count);
        Assert.DoesNotContain(plan.Files, file => file.EndsWith("keep.txt", StringComparison.Ordinal) || file.EndsWith("memo.txt", StringComparison.Ordinal));

        var cleanup = UnfinishedCopy.Clean(Destination);

        Assert.Equal(4, cleanup.Removed);
        Assert.Equal(0, cleanup.Left);
        Assert.Equal(
            ["keep.txt", Path.Combine("notes", "memo.txt")],
            Directory.EnumerateFiles(Destination, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(Destination, file)).Order().ToArray());
        Assert.False(UnfinishedCopy.IsAt(Destination));

        // 元のデータには触れない
        Assert.Equal(4, Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public void 写すために作ったフォルダは_片付けると畳む()
    {
        CrashMoveAtTheEnd(() => StoreMover.Move(Source, Destination, CrashAfterLast()));

        UnfinishedCopy.Clean(Destination);

        Assert.False(Directory.Exists(Destination));
        Assert.True(Directory.Exists(_root));
    }

    /// <summary>置き換えの途中で止まった写しかけは、片付けると退けておいた元のライブラリが選んだ場所に戻る。</summary>
    [Fact]
    public void 置き換えの写しかけを片付けると_退けた元のライブラリが戻る()
    {
        Directory.CreateDirectory(Path.Combine(Destination, "items"));
        File.WriteAllText(Path.Combine(Destination, "settings.json"), "{\"old\":true}");
        File.WriteAllText(Path.Combine(Destination, "items", "999.json"), "{ \"id\": \"999\" }");

        CrashMoveAtTheEnd(() => StoreMover.Replace(Source, Destination, CrashAfterLast()));
        Assert.False(StoreLocation.LooksLikeStore(Destination));
        Assert.NotNull(UnfinishedCopy.Plan(Destination).ParkedAt);

        var cleanup = UnfinishedCopy.Clean(Destination);

        Assert.True(cleanup.ParkedRestored);
        Assert.Equal("{\"old\":true}", File.ReadAllText(Path.Combine(Destination, "settings.json")));
        Assert.Equal(
            ["999.json"],
            Directory.EnumerateFiles(Path.Combine(Destination, "items")).Select(path => Path.GetFileName(path)!).ToArray());
        Assert.Equal(["items", "settings.json"], Directory.EnumerateFileSystemEntries(Destination).Select(path => Path.GetFileName(path)!).Order().ToArray());
        Assert.True(StoreLocation.LooksLikeStore(Destination));
    }

    [Fact]
    public void 印が読めなければ_何も消さない()
    {
        CrashMoveAtTheEnd(() => StoreMover.Move(Source, Destination, CrashAfterLast()));
        File.WriteAllText(UnfinishedCopy.MarkerPath(Destination), "壊れた");
        var before = Directory.EnumerateFiles(Destination, "*", SearchOption.AllDirectories).Count();

        Assert.Null(UnfinishedCopy.Plan(Destination).Marker);
        var cleanup = UnfinishedCopy.Clean(Destination);

        Assert.Equal(0, cleanup.Removed);
        Assert.True(cleanup.Left > 0);
        Assert.Equal(before, Directory.EnumerateFiles(Destination, "*", SearchOption.AllDirectories).Count());
        Assert.False(StoreLocation.LooksLikeStore(Destination));
    }

    /// <summary>
    /// 退けた物を戻している途中で落ちた姿から片付け直しても、戻した物を消さない（外部の点検 2026-10-07）。
    /// 戻した物は退けたフォルダの外にあり、写し始める前の控えにも無いので、前は「写しで作った物」に見えて消された
    /// </summary>
    [Fact]
    public void 退けた物を戻す途中で止まっても_片付け直しで戻した物を消さない()
    {
        Directory.CreateDirectory(Path.Combine(Destination, "items"));
        File.WriteAllText(Path.Combine(Destination, "settings.json"), "{\"old\":true}");
        File.WriteAllText(Path.Combine(Destination, "items", "999.json"), "{ \"id\": \"999\" }");
        CrashMoveAtTheEnd(() => StoreMover.Replace(Source, Destination, CrashAfterLast()));
        var parked = UnfinishedCopy.Plan(Destination).ParkedAt!;

        // 片付けが写しを消し、戻す名前を印に書き、items だけ戻したところで落ちた姿
        foreach (var file in UnfinishedCopy.Plan(Destination).Files)
        {
            File.Delete(file);
        }

        foreach (var folder in Directory.EnumerateDirectories(Destination).Where(path => !path.StartsWith(parked, StringComparison.OrdinalIgnoreCase)))
        {
            Directory.Delete(folder, recursive: true);
        }

        JsonStore.WriteOutsideStore(UnfinishedCopy.MarkerPath(Destination), UnfinishedCopy.Read(Destination)! with { Restoring = ["items", "settings.json"] });
        Directory.Move(Path.Combine(parked, "items"), Path.Combine(Destination, "items"));

        var cleanup = UnfinishedCopy.Clean(Destination);

        Assert.Equal(0, cleanup.Left);
        Assert.True(File.Exists(Path.Combine(Destination, "items", "999.json")));
        Assert.Equal("{\"old\":true}", File.ReadAllText(Path.Combine(Destination, "settings.json")));
        Assert.True(StoreLocation.LooksLikeStore(Destination));
    }

    /// <summary>退けた物を戻しきれなければ印を外さない。外すと、退けたフォルダに残った物が写しかけの一部とも分からなくなる。</summary>
    [Fact]
    public void 退けた物を戻しきれなければ_印を残す()
    {
        Directory.CreateDirectory(Path.Combine(Destination, "items"));
        File.WriteAllText(Path.Combine(Destination, "settings.json"), "{\"old\":true}");
        CrashMoveAtTheEnd(() => StoreMover.Replace(Source, Destination, CrashAfterLast()));
        var parked = UnfinishedCopy.Plan(Destination).ParkedAt!;

        UnfinishedCopyCleanup cleanup;
        using (new FileStream(Path.Combine(parked, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            cleanup = UnfinishedCopy.Clean(Destination);
        }

        Assert.False(cleanup.ParkedRestored);
        Assert.True(cleanup.Left > 0);
        Assert.True(UnfinishedCopy.IsAt(Destination));

        // 掴んでいた物を離せば、片付け直しで全部戻る
        var again = UnfinishedCopy.Clean(Destination);
        Assert.Equal(0, again.Left);
        Assert.Equal("{\"old\":true}", File.ReadAllText(Path.Combine(Destination, "settings.json")));
        Assert.True(Directory.Exists(Path.Combine(Destination, "items")));
    }

    /// <summary>
    /// 新しい保存先の場所を記録できず、写しを消しきれなかったら、印を残す（外部の点検 2026-10-07）。
    /// 前は印を外した後で消していたので、消し残しが印の無い欠けたライブラリに見えた
    /// </summary>
    [Fact]
    public void 引越しで場所を記録できず写しを消しきれなければ_印を残す()
    {
        FileStream? held = null;
        try
        {
            var result = StoreMover.Move(Source, Destination, commit: () =>
            {
                held = new FileStream(Path.Combine(Destination, "items", "123.json"), FileMode.Open, FileAccess.Read, FileShare.None);
                throw new IOException("記録できない");
            });

            Assert.False(result.Succeeded);
            Assert.NotNull(result.LeftoverAt);
            Assert.True(UnfinishedCopy.IsAt(Destination));
            Assert.False(StoreLocation.LooksLikeStore(Destination));
        }
        finally
        {
            held?.Dispose();
        }
    }

    [Fact]
    public void 戻すで場所を記録できず写しを消しきれなければ_印を残す()
    {
        var zip = Path.Combine(_root, "backup.zip");
        BackupArchive.Export(Source, zip, includeImages: true);
        FileStream? held = null;
        try
        {
            Assert.Throws<IOException>(() => BackupArchive.Restore(zip, Destination, commit: () =>
            {
                held = new FileStream(Path.Combine(Destination, "items", "123.json"), FileMode.Open, FileAccess.Read, FileShare.None);
                throw new IOException("記録できない");
            }));

            Assert.True(UnfinishedCopy.IsAt(Destination));
            Assert.False(StoreLocation.LooksLikeStore(Destination));
        }
        finally
        {
            held?.Dispose();
        }
    }

    /// <summary>
    /// 場所を記録できず、印も置き直せなければ、写しを消さずに残す（外部の点検 2026-10-07）。
    /// 印の無いまま消し始めると、消し残しが欠けたライブラリになる。残すのは突き合わせの済んだ完全な写し
    /// </summary>
    [Fact]
    public void 場所を記録できず印も置き直せなければ_写しを消さずに残す()
    {
        var result = StoreMover.Move(Source, Destination, commit: () =>
        {
            // 印の場所にフォルダを置いて、置き直しを失敗させる
            Directory.CreateDirectory(UnfinishedCopy.MarkerPath(Destination));
            throw new IOException("記録できない");
        });

        Assert.False(result.Succeeded);
        Assert.Equal(Destination, result.LeftoverAt);
        Assert.True(File.Exists(Path.Combine(Destination, "items", "123.json")));
        Assert.True(File.Exists(Path.Combine(Destination, "items", "456.json")));
        Assert.True(File.Exists(Path.Combine(Source, "items", "123.json")));
    }

    /// <summary>突き合わせの後で元へ書き足された物が残っていれば、「全部移した」とは言わない（外部の点検 2026-10-07）。</summary>
    [Fact]
    public void 突き合わせの後で元へ増えた物が残れば_全部移したとは言わない()
    {
        var result = StoreMover.Move(Source, Destination, commit: () =>
            File.WriteAllText(Path.Combine(Source, "items", "789.json"), "{ \"id\": \"789\" }"));

        Assert.True(result.Succeeded);
        Assert.False(result.SourceRemoved);
        Assert.True(File.Exists(Path.Combine(Source, "items", "789.json")));
    }

    /// <summary>
    /// 別の Chmonos が開いている場所への置き換えは、何も退けずに断る（外部の点検 2026-10-07）。
    /// 前は1つずつ退けていき、そのアプリが掴む app.lock に当たって失敗し、退けた分を戻さずに返した
    /// </summary>
    [Fact]
    public void 別のアプリが開いている場所への置き換えは_何も退けずに断る()
    {
        Directory.CreateDirectory(Path.Combine(Destination, "items"));
        File.WriteAllText(Path.Combine(Destination, "settings.json"), "{}");
        using var held = new FileStream(Path.Combine(Destination, "app.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var result = StoreMover.Replace(Source, Destination);

        Assert.False(result.Succeeded);
        Assert.Equal(StoreMover.InUseRefusal, result.Error);
        Assert.True(Directory.Exists(Path.Combine(Destination, "items")));
        Assert.Empty(Directory.EnumerateDirectories(Destination, "_置き換え前-*"));
    }

    /// <summary>退ける途中で失敗したら、退けた分を元へ戻す。</summary>
    [Fact]
    public void 退ける途中で失敗したら_退けた分を戻す()
    {
        Directory.CreateDirectory(Path.Combine(Destination, "items"));
        File.WriteAllText(Path.Combine(Destination, "items", "999.json"), "{}");
        File.WriteAllText(Path.Combine(Destination, "zz-掴まれている.txt"), "x");

        StoreMoveResult result;
        using (new FileStream(Path.Combine(Destination, "zz-掴まれている.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = StoreMover.Replace(Source, Destination);
        }

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(Destination, "items", "999.json")));
        Assert.Empty(Directory.EnumerateDirectories(Destination, "_置き換え前-*"));
    }
}
