using System.Diagnostics;
using System.Text;
using Chmonos.Core.Storage;

// 購入記録の額の空欄を 0 に書き換える（ユーザ指示 2026-10-06）。
//
// 使い方：
//   NullPriceFix                 … 保存先を決めて、額の空いた購入記録を数えるだけ（何も書かない）
//   NullPriceFix --apply         … 数えた後に確かめて、書き換える（書く前に控えを取る）
//   NullPriceFix --apply --yes   … 確かめを飛ばす（人が見ていない所で走らせるとき）
//   NullPriceFix --store <場所>  … 保存先を明示する（指定が無ければ、アプリと同じ決め方：CHMONOS_HOME → location.json → 既定の場所）
//
// 戻し方：書き換えた後に出る「控え」のフォルダの中の .json を、保存先の items フォルダへ上書きで写す。

Console.OutputEncoding = Encoding.UTF8;

var apply = args.Contains("--apply");
var yes = args.Contains("--yes");
var storeIndex = Array.IndexOf(args, "--store");
if (storeIndex >= 0 && storeIndex + 1 >= args.Length)
{
    return Fail("--store の後に保存先の場所を書いてください。");
}

var unknown = args.Where((arg, index) => arg.StartsWith("--", StringComparison.Ordinal)
    && arg is not ("--apply" or "--yes" or "--store") && index != storeIndex + 1).ToList();
if (unknown.Count > 0)
{
    return Fail($"知らない指定です：{string.Join(" ", unknown)}");
}

// ---- 保存先を決める ----
string root;
string source;
if (storeIndex >= 0)
{
    root = Path.GetFullPath(args[storeIndex + 1]);
    source = "--store で指定";
}
else
{
    // アプリ本体と同じ決め方にする（自前で location.json を読み解くと、アプリと食い違う）。
    // 利用者が自分の保存先に対して走らせる道具なので、利用者の保存先を使ってよい
    StoreLocation.AllowsUserStore = true;
    try
    {
        var resolved = StoreLocation.Resolve();
        root = resolved.Path;
        source = resolved.Source switch
        {
            StoreRootSource.Environment => "環境変数 CHMONOS_HOME",
            StoreRootSource.Configured => $"アプリの設定（{StoreLocation.LocationFile}）",
            _ => "既定の場所",
        };
    }
    catch (Exception exception)
    {
        return Fail($"保存先を決められませんでした：{exception.Message}");
    }
}

Console.WriteLine($"保存先：{root}");
Console.WriteLine($"（決め方：{source}）");
if (!Directory.Exists(new AppPaths(root).ItemsDir))
{
    return Fail("この場所に商品の記録（items フォルダ）がありません。保存先が合っているか確かめてください。");
}

// ---- アプリが開いていたら書かない（アプリの書き込みと上書きし合う） ----
var running = new[] { "Chmonos", "BoothAssetManager.App" }
    .SelectMany(name => Process.GetProcessesByName(name))
    .ToList();
if (running.Count > 0)
{
    return Fail("Chmonos が開いています。閉じてから、もう一度走らせてください。");
}

// **アプリと同じ多重起動の錠を、読む前から終わるまで握る**（外部の点検 2026-10-06）。
// プロセスの一覧を1回見るだけでは、確かめの入力を待つ間にアプリを開かれると、道具が古い記録で
// アプリの入力を上書きした。握っている間は、アプリの側が「もう開いている」として起動をやめる
using var storeLock = SingleInstanceLock.TryAcquire(new AppPaths(root));
if (storeLock is null)
{
    return Fail("Chmonos がこの保存先を開いています。閉じてから、もう一度走らせてください。");
}

// ---- 数える ----
var scan = NullPriceFixer.Scan(root);
Console.WriteLine();
Console.WriteLine($"読んだ商品の記録：{scan.ItemsScanned:N0} 件");
Console.WriteLine($"額が空欄の購入記録：{scan.PurchaseCount:N0} 件（{scan.Hits.Count:N0} 商品）");
if (scan.Unreadable.Count > 0)
{
    Console.WriteLine($"読めなかった記録：{scan.Unreadable.Count:N0} 件（壊れているか、途中で切れています。この道具は触りません）");
}

if (scan.PurchaseCount == 0)
{
    Console.WriteLine();
    Console.WriteLine("書き換える物はありません。");
    return 0;
}

if (!apply)
{
    Console.WriteLine();
    Console.WriteLine("まだ何も書き換えていません。0円に書き換えるには --apply を付けて走らせてください。");
    return 0;
}

// ---- 確かめて書き換える ----
if (!yes)
{
    Console.WriteLine();
    Console.Write($"額が空欄の購入記録 {scan.PurchaseCount:N0} 件を 0円に書き換えます。よろしいですか？ [y/N] ");
    var answer = Console.ReadLine()?.Trim();
    if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("やめました。何も書き換えていません。");
        return 0;
    }
}

var backupDir = Path.Combine(root, $"backup-null-price-{DateTime.Now:yyyyMMdd-HHmmss}");
NullPriceApplied applied;
try
{
    applied = NullPriceFixer.Apply(root, backupDir);
}
catch (Exception exception)
{
    // 途中で止まっても、書き終えた記録は控えがあり、まだの記録は前のまま（1件ずつ控え → 置き換えの順で書く）
    Console.Error.WriteLine($"途中で止まりました：{exception.Message}");
    if (Directory.Exists(backupDir))
    {
        Console.Error.WriteLine($"書き換え済みの記録の控え：{backupDir}");
    }

    return 1;
}

Console.WriteLine();
Console.WriteLine($"書き換えた商品の記録：{applied.Changed.Count:N0} 件");
if (applied.BackupDir is { } backup)
{
    Console.WriteLine($"書き換える前の控え：{backup}");
    Console.WriteLine("元に戻すときは、控えの中の .json を items フォルダへ上書きで写してください。");
}

return 0;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}
