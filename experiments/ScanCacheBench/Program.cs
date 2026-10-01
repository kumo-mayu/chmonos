// 走査の控え（scan-cache.json）を読む・書き換える重さを、アプリを起動せずに測る。
//
// 使い方（作業フォルダは必ず渡す。その下に作り物の保存先とファイルを作り、終わったら消す）：
//   dotnet run -c Release --project experiments/ScanCacheBench -- <作業フォルダ> load <件数> [short|long] [回数]
//   dotnet run -c Release --project experiments/ScanCacheBench -- <作業フォルダ> update <件数> [short|long] [回数]
//   dotnet run -c Release --project experiments/ScanCacheBench -- <作業フォルダ> find <件数> <商品の数> <監視フォルダのファイル数> [回数]
//   dotnet run -c Release --project experiments/ScanCacheBench -- <作業フォルダ> make <件数> <自分で作った写し>
//
// make   ：アプリで測るときの台。自分で作った写し（Chmonos-sandboxes\scn… の物だけ）の控えに作り物を足す
// load   ：JsonFileStore.Load() 1回の時間と割り当て。1回目（JIT が乗る）と2回目以降を分けて出す
// update ：画面のスレッドの代わりの「1本で順に回すスレッド」から UpdateAsync を呼び、そのスレッドが続けて塞がった時間を出す
// find   ：同じスレッドから MissingFileFinder.FindAsync を丸ごと呼ぶ（見つからないファイルが1件ある形）
//
// 組むのは Core の DataStore と MissingFileFinder だけ。アプリの一式（AppServiceContainer・MainViewModel）は組まない。
// 通信もしない。1件あたりの大きさはパスの長さで変わるので、短い形（英数 約50字）と長い形（日本語まじり 約75字）を選べる。
using System.Collections.Concurrent;
using System.Diagnostics;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

// bash から呼ぶと既定の文字コード（CP932）で出て読めないので、UTF-8 で書く
Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length < 3)
{
    Console.Error.WriteLine("使い方: ScanCacheBench <作業フォルダ> load|update|find <件数> …（Program.cs の冒頭）");
    return 2;
}

var work = Path.Combine(Path.GetFullPath(args[0]), "scancache-" + Guid.NewGuid().ToString("N")[..8]);
var mode = args[1];
var count = int.Parse(args[2]);
Directory.CreateDirectory(work);

try
{
    switch (mode)
    {
        case "load":
            Load(work, count, LongPaths(args, 3), Repeat(args, 4, 10));
            return 0;
        case "update":
            Update(work, count, LongPaths(args, 3), Repeat(args, 4, 5));
            return 0;
        case "find":
            Find(work, count, int.Parse(args[3]), int.Parse(args[4]), Repeat(args, 5, 5));
            return 0;
        case "make":
            Make(count, Path.GetFullPath(args[3]));
            return 0;
        default:
            Console.Error.WriteLine($"知らない場面: {mode}");
            return 2;
    }
}
finally
{
    // 作り物は残さない（8万件の控えと、監視フォルダの小さなファイル数万個）
    Directory.Delete(work, recursive: true);
}

static bool LongPaths(string[] args, int index) => args.Length > index && args[index] == "long";

static int Repeat(string[] args, int index, int fallback)
    => args.Length > index && int.TryParse(args[index], out var value) ? value : fallback;

// 作り物の控え。zip は7割（うち半分は手掛かりの商品IDが1つ、半分は空の一覧）、残りは zip 以外（手掛かりは書かれない）。
// 取り込みは対象の拡張子のファイルを全部ハッシュして控えるので、1ファイルが1件になる
static List<ScanCacheEntry> Entries(int count, bool longPaths)
{
    var random = new Random(20260930);
    var entries = new List<ScanCacheEntry>(count);
    var hash = new byte[32];
    for (var index = 0; index < count; index++)
    {
        random.NextBytes(hash);
        var archive = index % 10 < 7;
        var extension = archive ? ".zip" : ".png";
        var path = longPaths
            ? $@"D:\VRChat\BOOTH\取り込み元{index % 7}\ショップ{index / 40 % 500:000}\商品のフォルダ_{index / 4:00000}\配布ファイル_{index:00000}_ver1.0{extension}"
            : $@"D:\Chmonos-bigfiles\loose\f{index / 100:0000}\file_{index:00000000}{extension}";
        entries.Add(new ScanCacheEntry
        {
            Path = path,
            SizeBytes = 1_000_000L + random.Next(0, 900_000_000),
            ModifiedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(random.NextInt64(0, TimeSpan.TicksPerDay * 270)),
            Hash = Convert.ToHexString(hash),
            ClueItemIds = !archive ? null : index % 2 == 0 ? [(1_000_000 + random.Next(0, 6_000_000)).ToString()] : [],
        });
    }

    return entries;
}

static DataStore NewStore(string work, out AppPaths paths)
{
    paths = new AppPaths(Path.Combine(work, "store"));
    paths.EnsureCreated();
    return new DataStore(paths);
}

static (double Min, double Median, double Max) Spread(IReadOnlyList<double> values)
{
    var sorted = values.OrderBy(value => value).ToList();
    return (sorted[0], sorted[sorted.Count / 2], sorted[^1]);
}

static string Ms((double Min, double Median, double Max) spread)
    => $"最小 {spread.Min:0.0}・中央 {spread.Median:0.0}・最大 {spread.Max:0.0} ms";

static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:0.00} MB";

// 自分で作った写しの控えに、作り物を足す（アプリで測るときの台を作る）。今ある控えは残す。
// 写しの名前が「scn」「mis」で始まる物（%LOCALAPPDATA%\Chmonos-sandboxes\scn…）にしか書かない（本番・ほかの担当の写しを書き換えないように）
static void Make(int count, string storeRoot)
{
    // scn は測った担当、mis は直した担当が台本から作った写しの名前（直した後に、同じ台で測り直した）
    var name = Path.GetFileName(storeRoot);
    var parent = Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(storeRoot)) ?? string.Empty);
    if (!string.Equals(parent, "Chmonos-sandboxes", StringComparison.OrdinalIgnoreCase)
        || !(name.StartsWith("scn", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("mis", StringComparison.OrdinalIgnoreCase))
        || !File.Exists(Path.Combine(storeRoot, "settings.json")))
    {
        throw new InvalidOperationException($"ここには書かない: {storeRoot}（自分で作った写し Chmonos-sandboxes\\scn…・mis… だけ）");
    }

    var paths = new AppPaths(storeRoot);
    var written = new DataStore(paths).ScanCache
        .UpdateAsync(current => [.. current, .. Entries(count, longPaths: false)])
        .GetAwaiter().GetResult();
    Console.WriteLine($"控えに {count:N0} 件足した: 合わせて {written.Count:N0} 件・{Mb(new FileInfo(paths.ScanCacheFile).Length)}");
}

static void Load(string work, int count, bool longPaths, int repeat)
{
    var store = NewStore(work, out var paths);
    store.ScanCache.SaveAsync(Entries(count, longPaths)).GetAwaiter().GetResult();
    var length = new FileInfo(paths.ScanCacheFile).Length;
    Console.WriteLine($"件数 {count:N0}・パス {(longPaths ? "長い" : "短い")}・ファイル {Mb(length)}（1件 {(count == 0 ? 0 : length / count)} バイト）");

    // 1回目：このプロセスで初めて読む（JSON の読み手の支度と JIT が乗る）。ファイルは書いたばかりなので OS の控えには載っている
    var (firstMs, firstBytes, _) = MeasureLoad(store);
    Console.WriteLine($"  読む 1回目：{firstMs:0.0} ms／{Mb(firstBytes)}");

    var times = new List<double>();
    var bytes = 0L;
    List<ScanCacheEntry> last = [];
    for (var round = 0; round < repeat; round++)
    {
        (var ms, bytes, last) = MeasureLoad(store);
        times.Add(ms);
    }

    Console.WriteLine($"  読む 2回目以降（{repeat}回）：{Ms(Spread(times))}／{Mb(bytes)}");

    // 順に並べる（温まるまでの数回が遅い。アプリではめったに呼ばれない読みなので、1回目に近い側が実際の姿）
    Console.WriteLine($"    順に：{string.Join("・", times.Select(ms => ms.ToString("0.0")))}");

    // 読んだ物をパスで引ける形にする所（呼ぶ側は必ずこれを続けて行う）
    var indexTimes = new List<double>();
    var indexBytes = 0L;
    for (var round = 0; round < repeat; round++)
    {
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var index = new ScanCacheIndex(last);
        indexTimes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        indexBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(index);
    }

    Console.WriteLine($"  パスで引ける形にする（{repeat}回）：{Ms(Spread(indexTimes))}／{Mb(indexBytes)}");

    // 読んだ一覧を持ち続けたときに残る量（写しを持つ案の代償）
    last = [];
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var baseline = GC.GetTotalMemory(forceFullCollection: true);
    var held = store.ScanCache.Load();
    var retained = GC.GetTotalMemory(forceFullCollection: true) - baseline;
    Console.WriteLine($"  読んだ一覧を持ち続ける量：{Mb(retained)}（1件 {(count == 0 ? 0 : retained / count)} バイト）");
    GC.KeepAlive(held);
}

static (double Ms, long Bytes, List<ScanCacheEntry> Value) MeasureLoad(DataStore store)
{
    GC.Collect();
    var before = GC.GetAllocatedBytesForCurrentThread();
    var start = Stopwatch.GetTimestamp();
    var value = store.ScanCache.Load();
    var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    return (ms, GC.GetAllocatedBytesForCurrentThread() - before, value);
}

static void Update(string work, int count, bool longPaths, int repeat)
{
    var store = NewStore(work, out var paths);
    store.ScanCache.SaveAsync(Entries(count, longPaths)).GetAwaiter().GetResult();
    Console.WriteLine($"件数 {count:N0}・パス {(longPaths ? "長い" : "短い")}・ファイル {Mb(new FileInfo(paths.ScanCacheFile).Length)}");

    for (var round = 0; round <= repeat; round++)
    {
        var added = round;
        GC.Collect();

        // MissingFileFinder が控えに足すときと同じ変え方（今の控えをパスで引ける形にし、1件足して一覧へ戻す）
        var result = Pump.Run(() => store.ScanCache.UpdateAsync(current =>
        {
            var index = new ScanCacheIndex(current);
            index.Set($@"D:\足した物\found_{added}.zip", 5000, DateTimeOffset.UnixEpoch, new string('A', 64));
            return index.ToList();
        }));

        Console.WriteLine($"  書き換え {(round == 0 ? "1回目" : $"{round + 1}回目")}：{result}");
    }
}

static void Find(string work, int count, int itemCount, int fileCount, int repeat)
{
    var store = NewStore(work, out var paths);
    var watched = Path.Combine(work, "watched");

    // 監視フォルダ：100個ずつのフォルダに小さなファイル。大きさが合うのは、探している1個だけ（ほかはハッシュを取らない）
    var files = new List<string>(fileCount);
    var filler = new byte[64];
    for (var index = 0; index < fileCount; index++)
    {
        var folder = Path.Combine(watched, $"f{index / 100:0000}");
        if (index % 100 == 0)
        {
            Directory.CreateDirectory(folder);
        }

        var path = Path.Combine(folder, $"file_{index:00000000}.png");
        File.WriteAllBytes(path, filler);
        files.Add(path);
    }

    var target = Path.Combine(watched, "moved-to", "探している物.zip");
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllBytes(target, new byte[5000]);
    var targetHash = FileHasher.ComputeSha256Async(target).GetAwaiter().GetResult();

    // 商品：1件に1ファイル。1件目だけ、記録の場所にファイルが無い（監視フォルダの中の別の場所へ移した形）
    ItemRecord Item(int index) => new()
    {
        Id = (9_000_000 + index).ToString(),
        Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = $"作り物 {index}" },
        Local = new LocalBlock
        {
            LocalFiles =
            [
                index == 0
                    ? new LocalFileRecord { Hash = targetHash, SizeBytes = 5000, Paths = [Path.Combine(watched, "moved-from", "探している物.zip")] }
                    : new LocalFileRecord { Hash = $"{index:X64}", SizeBytes = 64, Paths = [files[index % files.Count]] },
            ],
        },
    };

    for (var index = 0; index < itemCount; index++)
    {
        store.Items.SaveAsync(Item(index)).GetAwaiter().GetResult();
    }

    var pristine = paths.ScanCacheFile + ".pristine";
    if (count > 0)
    {
        store.ScanCache.SaveAsync(Entries(count, longPaths: false)).GetAwaiter().GetResult();
        File.Copy(paths.ScanCacheFile, pristine);
    }

    Console.WriteLine($"控え {count:N0} 件（{(count > 0 ? Mb(new FileInfo(pristine).Length) : "ファイル無し")}）・商品 {itemCount:N0} 件・監視フォルダ {fileCount:N0} ファイル");

    var finder = new MissingFileFinder(store);
    for (var round = 0; round <= repeat; round++)
    {
        // 毎回、同じ状態から（結び直した商品を元の「見つからない」へ戻し、足したハッシュを控えから消す）
        store.Items.SaveAsync(Item(0)).GetAwaiter().GetResult();
        if (count > 0)
        {
            File.Copy(pristine, paths.ScanCacheFile, overwrite: true);
        }
        else
        {
            File.Delete(paths.ScanCacheFile);
        }

        GC.Collect();
        MissingFileSearchResult? found = null;
        var result = Pump.Run(async () => found = await finder.FindAsync([watched]));
        if (found is not { MissingBefore: 1, Relinked: 1, Hashed: 1 })
        {
            throw new InvalidOperationException($"場面が組めていない：{found}");
        }

        Console.WriteLine($"  探す {(round == 0 ? "1回目" : $"{round + 1}回目")}：{result}");
    }
}

/// <summary>
/// 画面のスレッドの代わり。続きを1本のスレッドで順に回す（WPF の Dispatcher と同じく、await の続きはここへ戻る）。
/// 1つの仕事が続けて塞いだ時間が、画面なら「その間は入力も描画も進まない」時間に当たる。
/// </summary>
internal sealed class Pump : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

    public static PumpResult Run(Func<Task> work)
    {
        var pump = new Pump();
        var items = new List<double>();
        var allocated = 0L;
        Exception? failure = null;
        var totalBefore = GC.GetTotalAllocatedBytes(precise: true);
        var wall = Stopwatch.StartNew();

        var thread = new Thread(() =>
        {
            SetSynchronizationContext(pump);
            pump.Post(_ => work().ContinueWith(
                task =>
                {
                    failure = task.Exception?.GetBaseException();
                    wall.Stop();
                    pump._queue.CompleteAdding();
                },
                TaskScheduler.Default), null);

            foreach (var (callback, state) in pump._queue.GetConsumingEnumerable())
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                callback(state);
                items.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }
        });
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("測っている処理が失敗した", failure);
        }

        return new PumpResult(
            wall.Elapsed.TotalMilliseconds,
            items.Count,
            items.Sum(),
            items.Max(),
            allocated,
            GC.GetTotalAllocatedBytes(precise: true) - totalBefore);
    }
}

internal sealed record PumpResult(double WallMs, int Items, double BusyMs, double LongestMs, long PumpBytes, long TotalBytes)
{
    public override string ToString()
        => $"全体 {WallMs:0.0} ms・このスレッドの仕事 {Items} 個・合計 {BusyMs:0.0} ms・最長 {LongestMs:0.0} ms・"
            + $"このスレッドの割り当て {PumpBytes / 1024.0 / 1024.0:0.00} MB（全体 {TotalBytes / 1024.0 / 1024.0:0.00} MB）";
}
