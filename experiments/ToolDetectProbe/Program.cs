using System.Diagnostics;
using System.IO;
using System.Text;
using Chmonos.App.Services;
using Microsoft.Win32;

// 使い方：ToolDetectProbe [--open] [--out <書き出す先>]
//   何も付けない … 見分けの結果と、見分けが読む記録の生の中身を書き出す（読むだけ）
//   --open       … 加えて、unityhub:// と vcc:// を開こうとし、VCC・ALCOM・Hub を開く関数も呼ぶ。
//                  Windows が「開くアプリを選んでください」を出すかは、外から画面を撮って見る（vm-kit.ps1 の Save-VmShot）
var open = args.Contains("--open");
var outIndex = Array.IndexOf(args, "--out");
var outPath = outIndex >= 0 && outIndex + 1 < args.Length ? args[outIndex + 1] : null;

var report = new StringBuilder();
void Line(string text) => report.AppendLine(text);

Line($"# ToolDetectProbe {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
Line($"OS: {Environment.OSVersion.VersionString} / 64bit: {Environment.Is64BitOperatingSystem} / 道具 64bit: {Environment.Is64BitProcess}");
Line("");

// ---- アプリが使う見分けの結果（画面に出る物はここから決まる） ----
Line("## 見分けの結果");
Line($"UnityLaunch.HasHub: {Safe(UnityLaunch.HasHub)}");
Line($"VccLaunch.IsAvailable: {Safe(VccLaunch.IsAvailable)} / FindExe: {Safe(() => VccLaunch.FindExe() ?? "(なし)")}");
Line($"AlcomLaunch.IsAvailable: {Safe(AlcomLaunch.IsAvailable)} / FindExe: {Safe(() => AlcomLaunch.FindExe() ?? "(なし)")}");
Line($"UnityTools.Detect: {Safe(() => UnityTools.Detect().ToString())}");
Line("");

// ---- 見分けが読む記録の生の中身（試験の想定と本物の形を突き合わせるため） ----
Line("## リンクの登録（開くコマンド）");
foreach (var scheme in new[] { "unityhub", "vcc" })
{
    Line($"{scheme}: InstalledApps.OpenCommand = {Safe(() => InstalledApps.OpenCommand(scheme) ?? "(なし)")}");
    foreach (var (hive, path) in new[]
    {
        (Registry.CurrentUser, $@"Software\Classes\{scheme}"),
        (Registry.LocalMachine, $@"Software\Classes\{scheme}"),
        (Registry.LocalMachine, $@"Software\WOW6432Node\Classes\{scheme}"),
    })
    {
        Line($@"  {hive.Name}\{path}: {Dump(hive, path)}");
    }
}

Line("");
Line("## アンインストールの一覧（Unity・VRChat・ALCOM・Creator Companion に関わる物だけ）");
// 大文字小文字を区別して当てる（unity だと Visual Studio Community の「community」まで拾った）
var keywords = new[] { "Unity", "VRChat", "ALCOM", "Creator Companion" };
try
{
    var hits = InstalledApps.Uninstall().Select(record => record.ToString() ?? "")
        .Where(text => keywords.Any(word => text.Contains(word, StringComparison.Ordinal)))
        .ToList();
    Line(hits.Count == 0 ? "(なし)" : string.Join(Environment.NewLine, hits.Select(hit => "  " + hit)));
}
catch (Exception exception)
{
    Line($"読めなかった: {exception.GetType().Name}: {exception.Message}");
}

Line("");
Line("## Unity のエディタの登録");
Line($@"  HKCU\Software\Unity Technologies: {Dump(Registry.CurrentUser, @"Software\Unity Technologies")}");
Line($@"  HKLM\Software\Unity Technologies: {Dump(Registry.LocalMachine, @"Software\Unity Technologies")}");

if (open)
{
    // ---- 開こうとしたときの返り方（試験では作れない所：失敗が返るのか、Windows が窓を出して「開けた」に見えるのか） ----
    Line("");
    Line("## 開こうとしたとき");
    foreach (var link in new[] { "unityhub://", "vcc://vpm/addRepo?url=https%3A%2F%2Fexample.invalid%2Findex.json" })
    {
        Line($"Process.Start(\"{link}\", UseShellExecute): {TryStart(link)}");
        Thread.Sleep(3000);
    }

    Line($"VccLaunch.Open: {Safe(() => VccLaunch.Open().ToString())}");
    Thread.Sleep(3000);
    Line($"AlcomLaunch.Open: {Safe(() => AlcomLaunch.Open().ToString())}");
    Thread.Sleep(3000);
    Line($"UnityLaunch.OpenProject(一時フォルダ): {Safe(() => UnityLaunch.OpenProject(Path.GetTempPath()).ToString())}");
}

var text = report.ToString();
Console.Write(text);
if (outPath is not null)
{
    File.WriteAllText(outPath, text, new UTF8Encoding(false));
}

return 0;

static string Safe<T>(Func<T> read)
{
    try
    {
        return read()?.ToString() ?? "(null)";
    }
    catch (Exception exception)
    {
        return $"例外 {exception.GetType().Name}: {exception.Message}";
    }
}

// 鍵の下の値と子の鍵を1段だけ書き出す（shell\open\command の中身まで見えるよう、子の既定値も読む）
static string Dump(RegistryKey hive, string path)
{
    try
    {
        using var key = hive.OpenSubKey(path);
        if (key is null)
        {
            return "(なし)";
        }

        var values = key.GetValueNames().Select(name => $"{(name.Length == 0 ? "(既定)" : name)}={key.GetValue(name)}");
        var command = key.OpenSubKey(@"shell\open\command")?.GetValue(null);
        return $"[{string.Join("; ", values)}] 子=[{string.Join(", ", key.GetSubKeyNames())}]" + (command is null ? "" : $" open={command}");
    }
    catch (Exception exception)
    {
        return $"読めない（{exception.GetType().Name}: {exception.Message}）";
    }
}

static string TryStart(string link)
{
    try
    {
        using var process = Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        return process is null ? "返った（プロセスなし）" : $"返った（プロセス {process.ProcessName}）";
    }
    catch (Exception exception)
    {
        return $"例外 {exception.GetType().Name}: {exception.Message}";
    }
}
