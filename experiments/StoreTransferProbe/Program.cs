using System.Diagnostics;
using System.Security.Cryptography;
using Chmonos.Core.Storage;

// 保存先の引越しとバックアップを、作り物の数GBで通して測る（2026-10-01。open.md「引越し・バックアップ（E8）」の残り）。
//
// 使い方：StoreTransferProbe <作業フォルダ（Cドライブ）> <作業フォルダ（別のドライブ）> [件数]
//
// 画面の操作と同じく、書き込みの門（StoreWriteGate.HoldAsync）を持って Core の処理を呼ぶ。
// 最後の location.json の書き換えと開き直しは通さない——置き場が %LOCALAPPDATA%\Chmonos に固定で、
// 書くと本番の指す先が変わる。そこは App の試験（SettingsBackupJobTests など）が見る
if (args.Length < 2)
{
    Console.WriteLine("使い方：StoreTransferProbe <作業フォルダ（C）> <作業フォルダ（別のドライブ）> [件数]");
    return 2;
}

// 書き出しだけを測り直す：StoreTransferProbe export <保存先> <zip>
if (args[0] == "export")
{
    var again = await ExportAsync("書き出しだけ（画像あり）", args[1], args[2], includeImages: true);
    Console.WriteLine($"  {again.Files:N0} ファイル / zip {Gb(new FileInfo(args[2]).Length)}");
    return 0;
}

var workC = Path.GetFullPath(args[0]);
var workD = Path.GetFullPath(args[1]);
var itemCount = args.Length > 2 ? int.Parse(args[2]) : 8000;

// 作り物の大きさは実際の写しに合わせる：友人の写しは 207 件で画像 2,121 枚・37MB（1枚 約17KB）、
// items 414・unitypackages 301。数GBにするため、件数を増やし、1枚を少し大きくし（25KB）、大きめの添付も混ぜる
const int ImagesPerItem = 10;
const int ImageBytes = 25 * 1024;

foreach (var dir in new[] { workC, workD })
{
    if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
    {
        Console.WriteLine($"作業フォルダが空ではありません：{dir}");
        return 2;
    }
}

var source = Path.Combine(workC, "src");
Step("作り物の保存先を作る", () => Generate(source, itemCount));
var original = Step("元の中身の控え（ハッシュ）", () => Manifest(source));
Console.WriteLine($"  {original.Count:N0} ファイル / {Gb(original.Values.Sum(v => v.Length))}");

var failures = new List<string>();

// 1. 同じドライブの引越し
var movedC = Path.Combine(workC, "moved");
var r1 = await MoveAsync("引越し（同じドライブ C→C）", source, movedC);
Check("C→C 成功", r1.Succeeded);
// 根元のフォルダは畳まない作り（本番の根元には location.json も置かれる）。中のファイルが残っていないかを見る
Check("C→C 元のファイルを消した", r1.SourceRemoved && NoFilesUnder(source));
Check("C→C 中身が一致", Same(original, Manifest(movedC)));

// 2. ドライブをまたぐ引越し
var movedD = Path.Combine(workD, "moved");
var r2 = await MoveAsync("引越し（別のドライブ C→D）", movedC, movedD);
Check("C→D 成功", r2.Succeeded);
Check("C→D 元のファイルを消した", r2.SourceRemoved && NoFilesUnder(movedC));
Check("C→D 中身が一致", Same(original, Manifest(movedD)));

// 3. 半分で止める。元は無傷のはず。運ぶ先に残った物と、同じ先へもう一度運べるかを見る
var halfC = Path.Combine(workC, "half");
var r3 = await MoveAsync("引越しを半分で止める（D→C）", movedD, halfC, cancelAtHalf: true);
Check("止めると失敗として返る", !r3.Succeeded);
Check("止めても元は無傷", Same(original, Manifest(movedD)));
var leftover = Directory.Exists(halfC) ? Directory.EnumerateFiles(halfC, "*", SearchOption.AllDirectories).Count() : 0;
Console.WriteLine($"  止めた後に運ぶ先に残ったファイル：{leftover:N0}（返った文：{r3.Error}）");
Check("止めた後、運ぶ先に何も残らない（2026-10-02 の直し）", leftover == 0 && r3.LeftoverAt is null && !Directory.Exists(halfC));
var summary = Directory.Exists(halfC) ? StoreMover.Summarize(halfC).ToString() : "（運ぶ先のフォルダごと無い）";
Console.WriteLine($"  残った先の見え方（次に同じ先を選んだとき）：{summary}");
var r3b = await MoveAsync("止めた先へもう一度運ぶ（D→C）", movedD, halfC);
Console.WriteLine($"  もう一度運ぶ：成功={r3b.Succeeded} 文={r3b.Error}");
if (r3b.Succeeded)
{
    Check("やり直しで中身が一致", Same(original, Manifest(halfC)));
    // 次の段のため D へ戻す
    var back = await MoveAsync("D へ戻す（C→D）", halfC, movedD);
    Check("D へ戻す 成功", back.Succeeded);
}

var store = movedD;

// 4. 書き出し（画像あり・なし）と戻す
var zipAll = Path.Combine(workC, "backup-all.zip");
var e1 = await ExportAsync("書き出し（画像あり。D→Cの zip）", store, zipAll, includeImages: true);
Console.WriteLine($"  {e1.Files:N0} ファイル / zip {Gb(new FileInfo(zipAll).Length)} / 飛ばした {e1.SkippedLocked}");
Check("画像ありは全部入る", e1.Files == original.Count);

var zipLite = Path.Combine(workC, "backup-lite.zip");
var e2 = await ExportAsync("書き出し（画像なし）", store, zipLite, includeImages: false);
Console.WriteLine($"  {e2.Files:N0} ファイル / zip {Gb(new FileInfo(zipLite).Length)}");

var restored = Path.Combine(workC, "restored");
var n = await RestoreAsync("戻す（画像ありの zip → C）", zipAll, restored);
Check("戻した数が一致", n == original.Count);
Check("戻した中身が一致", Same(original, Manifest(restored)));

var restoredD = Path.Combine(workD, "restored");
var nD = await RestoreAsync("戻す（画像ありの zip → 別のドライブ D）", zipAll, restoredD);
Check("D へ戻した中身が一致", nD == original.Count && Same(original, Manifest(restoredD)));

// 5. 書き出しを半分で止める：書きかけ（.tmp）も zip も残らないはず
var zipCancel = Path.Combine(workC, "backup-cancel.zip");
var canceled = false;
try
{
    await ExportAsync("書き出しを半分で止める", store, zipCancel, includeImages: true, cancelAtHalf: true);
}
catch (OperationCanceledException)
{
    canceled = true;
}

Check("書き出しを止めると中止が返る", canceled);
Check("止めた書き出しは何も残さない", !File.Exists(zipCancel) && !File.Exists(zipCancel + ".tmp"));

// 6. 戻すを半分で止める（画面には中止の口が無い。Core に渡したときの残り方だけを見る）
var restoreCancel = Path.Combine(workC, "restore-cancel");
var restoreCanceled = false;
try
{
    await RestoreAsync("戻すを半分で止める（画面には中止の口が無い）", zipAll, restoreCancel, cancelAtHalf: true);
}
catch (OperationCanceledException)
{
    restoreCanceled = true;
}

var restoreLeft = Directory.Exists(restoreCancel) ? Directory.EnumerateFiles(restoreCancel, "*", SearchOption.AllDirectories).Count() : 0;
Console.WriteLine($"  中止={restoreCanceled} 残ったファイル：{restoreLeft:N0}");
Check("戻すを止めた後、展開先に何も残らない（2026-10-02 の直し）", restoreCanceled && restoreLeft == 0 && !Directory.Exists(restoreCancel));

Console.WriteLine();
Console.WriteLine(failures.Count == 0 ? "すべて期待どおり" : $"期待と違った：{string.Join(" / ", failures)}");
return failures.Count == 0 ? 0 : 1;

void Check(string what, bool ok)
{
    Console.WriteLine($"  [{(ok ? "OK" : "NG")}] {what}");
    if (!ok)
    {
        failures.Add(what);
    }
}

async Task<StoreMoveResult> MoveAsync(string title, string from, string to, bool cancelAtHalf = false)
{
    using var stop = new CancellationTokenSource();
    var reports = 0;
    var progress = new SyncProgress<StoreMoveProgress>(p =>
    {
        reports++;
        if (cancelAtHalf && p.Copied >= p.Total / 2)
        {
            stop.Cancel();
        }
    });

    return await MeasureAsync(title, async () =>
    {
        // 画面と同じく、運んでいる間は書き込みの門を持つ（CommandHandler の MoveStore と同じ）
        using var hold = await StoreWriteGate.HoldAsync();
        var result = await Task.Run(() => StoreMover.Move(from, to, progress, stop.Token));
        return (result, $"進み具合の知らせ {reports:N0} 回 / コピー {result.Copied:N0} / {Gb(result.Bytes)}");
    });
}

async Task<BackupResult> ExportAsync(string title, string root, string zip, bool includeImages, bool cancelAtHalf = false)
{
    using var stop = new CancellationTokenSource();
    var progress = new SyncProgress<BackupProgress>(p =>
    {
        if (cancelAtHalf && p.Done >= p.Total / 2)
        {
            stop.Cancel();
        }
    });

    return await MeasureAsync(title, async () =>
    {
        using var hold = await StoreWriteGate.HoldAsync();
        var result = await Task.Run(() => BackupArchive.Export(root, zip, includeImages, progress, stop.Token));
        return (result, string.Empty);
    });
}

async Task<int> RestoreAsync(string title, string zip, string destination, bool cancelAtHalf = false)
{
    using var stop = new CancellationTokenSource();
    var progress = new SyncProgress<BackupProgress>(p =>
    {
        if (cancelAtHalf && p.Done >= p.Total / 2)
        {
            stop.Cancel();
        }
    });

    return await MeasureAsync(title, async () =>
    {
        using var hold = await StoreWriteGate.HoldAsync();
        var result = await Task.Run(() => BackupArchive.Restore(zip, destination, progress, stop.Token));
        return (result, string.Empty);
    });
}

// 時間と、その間のメモリの山（作業セット・GC のヒープ）を測る
async Task<T> MeasureAsync<T>(string title, Func<Task<(T Result, string Note)>> run)
{
    Console.WriteLine($"■ {title}");
    GC.Collect();
    var process = Process.GetCurrentProcess();
    process.Refresh();
    var baseline = process.WorkingSet64;
    long peakWs = baseline, peakHeap = GC.GetTotalMemory(false);
    using var sampling = new CancellationTokenSource();
    var sampler = Task.Run(async () =>
    {
        while (!sampling.IsCancellationRequested)
        {
            process.Refresh();
            peakWs = Math.Max(peakWs, process.WorkingSet64);
            peakHeap = Math.Max(peakHeap, GC.GetTotalMemory(false));
            try { await Task.Delay(50, sampling.Token); } catch (OperationCanceledException) { }
        }
    });

    var watch = Stopwatch.StartNew();
    try
    {
        var (result, note) = await run();
        watch.Stop();
        sampling.Cancel();
        await sampler;
        Console.WriteLine($"  {watch.Elapsed.TotalSeconds:F1} 秒 / 作業セットの山 {Mb(peakWs)}（始め {Mb(baseline)}）/ ヒープの山 {Mb(peakHeap)}{(note.Length > 0 ? " / " + note : string.Empty)}");
        return result;
    }
    catch
    {
        watch.Stop();
        sampling.Cancel();
        await sampler;
        Console.WriteLine($"  {watch.Elapsed.TotalSeconds:F1} 秒で投げた / 作業セットの山 {Mb(peakWs)}");
        throw;
    }
}

T Step<T>(string title, Func<T> run)
{
    var watch = Stopwatch.StartNew();
    var result = run();
    Console.WriteLine($"■ {title}：{watch.Elapsed.TotalSeconds:F1} 秒");
    return result;
}

static int Generate(string root, int items)
{
    // 中身は乱数。同じ大きさの別物にして、取り違えを控えのハッシュで拾えるようにする
    var random = new Random(20261001);
    var buffer = new byte[ImageBytes];
    Directory.CreateDirectory(Path.Combine(root, "items"));
    File.WriteAllText(Path.Combine(root, "settings.json"), "{\n  \"saveImages\": true\n}\n");
    foreach (var name in new[] { "appTags.json", "userTags.json", "attributes.json", "notifications.json", "excluded.json", "unresolved.json" })
    {
        File.WriteAllText(Path.Combine(root, name), "[]\n");
    }

    var files = 7;
    for (var i = 0; i < items; i++)
    {
        var id = (9_000_000 + i).ToString();
        File.WriteAllText(Path.Combine(root, "items", id + ".json"),
            $"{{\n  \"id\": \"{id}\",\n  \"booth\": {{ \"name\": \"作り物の商品{i}\" }},\n  \"local\": {{ \"memo\": \"{new string('あ', random.Next(0, 200))}\" }}\n}}\n");
        var images = Path.Combine(root, "images", id);
        Directory.CreateDirectory(images);
        for (var k = 0; k < ImagesPerItem; k++)
        {
            random.NextBytes(buffer);
            File.WriteAllBytes(Path.Combine(images, $"{k}.jpg"), buffer);
        }

        files += 1 + ImagesPerItem;
    }

    // 大きめの添付（unitypackage の控えのような物）を少し混ぜる：10MB × 20
    var big = new byte[10 * 1024 * 1024];
    Directory.CreateDirectory(Path.Combine(root, "unitypackages"));
    for (var i = 0; i < 20; i++)
    {
        random.NextBytes(big);
        File.WriteAllBytes(Path.Combine(root, "unitypackages", $"big{i}.bin"), big);
        files++;
    }

    return files;
}

static Dictionary<string, (long Length, string Hash)> Manifest(string root)
{
    var result = new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
    {
        using var stream = File.OpenRead(file);
        result[Path.GetRelativePath(root, file)] = (stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
    }

    return result;
}

static bool Same(Dictionary<string, (long Length, string Hash)> a, Dictionary<string, (long Length, string Hash)> b)
    => a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && other == pair.Value);

static bool NoFilesUnder(string root)
    => !Directory.Exists(root) || !Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any();

static string Gb(long bytes) => $"{bytes / 1024d / 1024 / 1024:F2} GB";
static string Mb(long bytes) => $"{bytes / 1024d / 1024:F0} MB";

// 画面の Progress<T> は画面のスレッドへ送るが、ここには画面が無い。呼ばれたその場で数える
sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
