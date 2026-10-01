// 「まとめて除外」の重さを、アプリを起動せずに測る。
//
// 2026-09-30 までは、フォルダビューの「フォルダごと除外」と未確定の「まとめて除外」が、ファイル1個ごとに命令（UiCommand.ExcludeFile）を呼んでいた。
// 命令は1個ごとに、除外の記録（excluded.json）を錠の中で丸ごと読み直して丸ごと書き、続けて未確定の記録（unresolved.json）も
// 丸ごと読んで丸ごと書く。個数が増えると、読み書きする量は個数の2乗で増える。
// 2026-10-01 に1回の命令（UiCommand.ExcludeFiles）にまとめた。今の道は bulk、前の道の形は each で測る。
//
// 使い方（作業フォルダは必ず渡す。その下に作り物の保存先を作り、終わったら消す）：
//   dotnet run -c Release --project experiments/ExcludeBench -- <作業フォルダ> bulk <個数> [既に除外してある件数] [ほかの未確定の件数]
//   dotnet run -c Release --project experiments/ExcludeBench -- <作業フォルダ> each <個数> [既に除外してある件数] [ほかの未確定の件数]
//   dotnet run -c Release --project experiments/ExcludeBench -- <作業フォルダ> parts <個数> [既に除外してある件数] [ほかの未確定の件数]
//   dotnet run -c Release --project experiments/ExcludeBench -- <作業フォルダ> single <既に除外してある件数> [回数]
//   dotnet run -c Release --project experiments/ExcludeBench -- <作業フォルダ> load <件数> [回数]
//
// bulk   ：今の道そのまま。画面のスレッドの代わりの「1本で順に回すスレッド」から、ItemService.ExcludeAsync に全部を1回で渡す
// each   ：前の道の形。同じ所から ItemService.ExcludeAsync を1個ずつ呼ぶ（命令を1個ずつ呼んでいたときと同じ読み書きの回数）
// parts  ：1個ずつの繰り返しを、除外の記録の分と未確定の記録の分に分けて測る（除外の記録は、印の無い窓口と、裏へ出した窓口の2通り）
// single ：除外の記録が既に大きいときに、1個だけ除外する
// load   ：除外の記録を丸ごと読む時間（設定の「隠したもの」を開くときに、画面のスレッドで読む）
//
// 前の道そのもの（1個ずつ・除外の記録の窓口に印が無い）を測るときは、2026-09-30 の版（b0a65bc）のこのプログラムで bulk を回す。
//
// 組むのは Core の DataStore と ItemService だけ。アプリの一式（AppServiceContainer・MainViewModel）は組まない。
// BOOTH へは行かない（つながらない相手を渡す。呼ばれたら落とす）。
using System.Collections.Concurrent;
using System.Diagnostics;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

// bash から呼ぶと既定の文字コード（CP932）で出て読めないので、UTF-8 で書く
Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length < 3)
{
    Console.Error.WriteLine("使い方: ExcludeBench <作業フォルダ> bulk|each|parts|single|load <個数> …（Program.cs の冒頭）");
    return 2;
}

var work = Path.Combine(Path.GetFullPath(args[0]), "exclude-" + Guid.NewGuid().ToString("N")[..8]);
var mode = args[1];
var count = int.Parse(args[2]);
Directory.CreateDirectory(work);

try
{
    switch (mode)
    {
        case "bulk":
            Bulk(work, count, Number(args, 3, 0), Number(args, 4, 0), oneByOne: false);
            return 0;
        case "each":
            Bulk(work, count, Number(args, 3, 0), Number(args, 4, 0), oneByOne: true);
            return 0;
        case "parts":
            Parts(work, count, Number(args, 3, 0), Number(args, 4, 0));
            return 0;
        case "single":
            Single(work, count, Number(args, 3, 5));
            return 0;
        case "load":
            Load(work, count, Number(args, 3, 5));
            return 0;
        default:
            Console.Error.WriteLine($"知らない場面: {mode}");
            return 2;
    }
}
finally
{
    Directory.Delete(work, recursive: true);
}

static int Number(string[] args, int index, int fallback)
    => args.Length > index && int.TryParse(args[index], out var value) ? value : fallback;

static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:0.00} MB";

static string HashOf(string kind, int index)
    => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{kind}-{index}")));

// 作り物の未確定。ばらのファイル1個が1件（台本 bigcheck と同じ置き場の形のパス。中身の一覧・手掛かりは無い）
static UnresolvedFile Loose(string kind, int index) => new()
{
    Hash = HashOf(kind, index),
    Paths = [$@"D:\Chmonos-bigfiles\{kind}\f{index / 100:0000}\file_{index:00000000}.png"],
    SizeBytes = 1_000_000L + index,
    ModifiedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(index),
    FirstSeenAt = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(9)),
};

static ExcludedEntry Excluded(UnresolvedFile file) => new()
{
    Hash = file.Hash,
    Paths = file.Paths,
    ExcludedAt = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(9)),
    Reason = "フォルダビューからフォルダごと除外",
};

static Stage NewStage(string work, string name, int count, int already, int others)
{
    var paths = new AppPaths(Path.Combine(work, name));
    paths.EnsureCreated();
    var store = new DataStore(paths);
    var targets = Enumerable.Range(0, count).Select(index => Loose("target", index)).ToList();

    // ほかの未確定は前後に半分ずつ（外す物が一覧の頭に固まっていると、探すのが実際より速く出る）
    var rest = Enumerable.Range(0, others).Select(index => Loose("other", index)).ToList();
    List<UnresolvedFile> unresolved = [.. rest.Take(others / 2), .. targets, .. rest.Skip(others / 2)];
    store.Unresolved.SaveAsync(unresolved).GetAwaiter().GetResult();
    store.Excluded.SaveAsync([.. Enumerable.Range(0, already).Select(index => Excluded(Loose("already", index)))]).GetAwaiter().GetResult();
    return new Stage(store, paths, targets);
}

static void Describe(Stage stage, string label)
{
    var excluded = new FileInfo(stage.Paths.ExcludedFile);
    var unresolved = new FileInfo(stage.Paths.UnresolvedFile);
    Console.WriteLine($"  {label}：除外の記録 {stage.Store.Excluded.Load().Count:N0} 件・{Mb(excluded.Length)}／未確定の記録 {stage.Store.Unresolved.Load().Count:N0} 件・{Mb(unresolved.Length)}");
}

static ItemService NewService(DataStore store, AppPaths paths)
{
    // 待ちを差し替えるので PC の門には入らない。相手はつながらない作り物（除外は BOOTH へ行かない。行ったら落とす）
    var client = new BoothClient(new HttpClient(new Unreachable()), new AppSettings(), (_, _) => Task.CompletedTask);
    return new ItemService(store, client, new ImagePipeline(client, paths));
}

// 今の道そのまま：ItemService.ExcludeAsync（命令 ExcludeFiles の中身）に全部を1回で渡す。oneByOne なら前の道の形（1個ずつ）
static void Bulk(string work, int count, int already, int others, bool oneByOne)
{
    var stage = NewStage(work, oneByOne ? "each" : "bulk", count, already, others);
    var service = NewService(stage.Store, stage.Paths);
    Console.WriteLine($"まとめて除外 {count:N0} 個・{(oneByOne ? "1個ずつ" : "1回で")}（既に除外 {already:N0} 件・ほかの未確定 {others:N0} 件）");
    Describe(stage, "前");

    var each = new List<double>(count);
    var result = Pump.Run(async () =>
    {
        if (!oneByOne)
        {
            await service.ExcludeAsync(stage.Targets, "フォルダビューからフォルダごと除外");
            return;
        }

        foreach (var file in stage.Targets)
        {
            var start = Stopwatch.GetTimestamp();
            await service.ExcludeAsync([file], "フォルダビューからフォルダごと除外");
            each.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    });

    Describe(stage, "後");
    Console.WriteLine($"  {result}");
    if (oneByOne)
    {
        var tenth = Math.Max(1, count / 10);
        Console.WriteLine($"  1個あたり：初めの1割 平均 {each.Take(tenth).Average():0.0} ms・終わりの1割 平均 {each.Skip(count - tenth).Average():0.0} ms・最長 {each.Max():0.0} ms");
    }

    if (stage.Store.Excluded.Load().Count != already + count || stage.Store.Unresolved.Load().Count != others)
    {
        throw new InvalidOperationException("場面が組めていない：除外した数か、残った未確定の数が合わない");
    }
}

// 同じ繰り返しを、記録ごとに分けて測る
static void Parts(string work, int count, int already, int others)
{
    Console.WriteLine($"記録ごとに分ける {count:N0} 個（既に除外 {already:N0} 件・ほかの未確定 {others:N0} 件）");

    // 除外の記録：印の無い窓口（錠が空いていれば、読み直しと足す所が呼んだスレッドで走る。2026-09-30 までの形）
    var first = NewStage(work, "parts-excluded", count, already, others);
    var on = new JsonFileStore<List<ExcludedEntry>>(first.Paths.ExcludedFile);
    Console.WriteLine($"  除外の記録だけ（印の無い窓口）：{Pump.Run(() => ExcludeEachAsync(on, first.Targets))}");

    // 除外の記録：裏へ出した窓口（2026-10-01 からの DataStore の形）
    var second = NewStage(work, "parts-excluded-off", count, already, others);
    Console.WriteLine($"  除外の記録だけ（裏へ出した窓口）：{Pump.Run(() => ExcludeEachAsync(second.Store.Excluded, second.Targets))}");

    // 未確定の記録（窓口は裏）
    var third = NewStage(work, "parts-unresolved", count, already, others);
    Console.WriteLine($"  未確定の記録だけ：{Pump.Run(async () =>
    {
        foreach (var file in third.Targets)
        {
            await third.Store.Unresolved.TryUpdateAsync(current => RemoveOne(current, file.Hash));
        }
    })}");
}

// ItemService.ExcludeAsync の前半と同じ変え方（同じ中身が無ければ足す）
static async Task ExcludeEachAsync(JsonFileStore<List<ExcludedEntry>> excluded, IReadOnlyList<UnresolvedFile> targets)
{
    foreach (var file in targets)
    {
        await excluded.UpdateAsync(current =>
        {
            if (!current.Any(entry => string.Equals(entry.Hash, file.Hash, StringComparison.OrdinalIgnoreCase)))
            {
                current.Add(Excluded(file));
            }

            return current;
        });
    }
}

// ItemService.RemoveUnresolvedAsync と同じ変え方
static List<UnresolvedFile>? RemoveOne(List<UnresolvedFile> current, string hash)
    => current.RemoveAll(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)) > 0 ? current : null;

// 除外の記録が既に大きいときの1個
static void Single(string work, int already, int repeat)
{
    // 未確定は 100 件（普通の規模。未確定の記録の分は小さく、除外の記録の分が見える）
    var stage = NewStage(work, "single", repeat + 1, already, 100);
    var service = NewService(stage.Store, stage.Paths);
    Console.WriteLine($"1個だけ除外（既に除外 {already:N0} 件・{Mb(new FileInfo(stage.Paths.ExcludedFile).Length)}・未確定 {100 + repeat + 1} 件）");
    for (var round = 0; round <= repeat; round++)
    {
        var file = stage.Targets[round];
        GC.Collect();
        var result = Pump.Run(() => service.ExcludeAsync([file], "未確定画面から除外"));
        Console.WriteLine($"  {(round == 0 ? "1回目" : $"{round + 1}回目")}：{result}");
    }
}

static void Load(string work, int count, int repeat)
{
    var stage = NewStage(work, "load", 0, count, 0);
    var length = new FileInfo(stage.Paths.ExcludedFile).Length;
    Console.WriteLine($"除外の記録 {count:N0} 件・{Mb(length)}（1件 {(count == 0 ? 0 : length / count)} バイト）");
    for (var round = 0; round <= repeat; round++)
    {
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var loaded = stage.Store.Excluded.Load();
        var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Console.WriteLine($"  読む {(round == 0 ? "1回目" : $"{round + 1}回目")}：{ms:0.0} ms／{Mb(GC.GetAllocatedBytesForCurrentThread() - before)}");
        GC.KeepAlive(loaded);
    }
}

internal sealed record Stage(DataStore Store, AppPaths Paths, IReadOnlyList<UnresolvedFile> Targets);

internal sealed class Unreachable : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => throw new InvalidOperationException("この計測は BOOTH へ行かないはず");
}

/// <summary>
/// 画面のスレッドの代わり。続きを1本のスレッドで順に回す（WPF の Dispatcher と同じく、await の続きはここへ戻る）。
/// 1つの仕事が続けて塞いだ時間が、画面なら「その間は入力も描画も進まない」時間に当たる。
/// 33ms（2コマ）を超えた仕事を「固まり」として数える（アプリの見張りと同じ物差し）。
/// </summary>
internal sealed class Pump : SynchronizationContext
{
    private const double StallMs = 33;

    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

    public static PumpResult Run(Func<Task> work)
    {
        var pump = new Pump();
        var items = new List<double>();
        var allocated = 0L;
        Exception? failure = null;
        GC.Collect();
        var totalBefore = GC.GetTotalAllocatedBytes(precise: true);
        var collectionsBefore = GC.CollectionCount(2);
        var process = Process.GetCurrentProcess();
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

        process.Refresh();
        var stalls = items.Where(ms => ms > StallMs).ToList();
        return new PumpResult(
            wall.Elapsed.TotalMilliseconds,
            items.Count,
            items.Sum(),
            items.Max(),
            stalls.Count,
            stalls.Sum(),
            allocated,
            GC.GetTotalAllocatedBytes(precise: true) - totalBefore,
            GC.CollectionCount(2) - collectionsBefore,
            process.PeakWorkingSet64);
    }
}

internal sealed record PumpResult(
    double WallMs,
    int Items,
    double BusyMs,
    double LongestMs,
    int Stalls,
    double StallMs,
    long PumpBytes,
    long TotalBytes,
    int Gen2,
    long PeakWorkingSet)
{
    public override string ToString()
        => $"全体 {WallMs / 1000:0.00} 秒・このスレッドの仕事 {Items:N0} 個・合計 {BusyMs / 1000:0.00} 秒・最長 {LongestMs:0.0} ms・"
            + $"33ms 超 {Stalls:N0} 回（合計 {StallMs / 1000:0.00} 秒）・割り当て このスレッド {PumpBytes / 1024.0 / 1024.0:0} MB／全体 {TotalBytes / 1024.0 / 1024.0:0} MB・"
            + $"第2世代の回収 {Gen2} 回・作業セットの最大 {PeakWorkingSet / 1024.0 / 1024.0:0} MB";
}
