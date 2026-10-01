using System.Diagnostics;
using System.IO;
using System.Text;

namespace Chmonos.Core.Services;

/// <summary>
/// 確かめ用の足跡。**環境変数 <c>CHMONOS_UITRACE</c> を付けたときだけ**、出した窓の文言・押されたボタン・
/// 実行した命令を1行ずつファイルに書く（ユーザ指示 2026-09-20）。
///
/// **なぜ要るか：**画面の確かめで「どの文言が出たか」を見るのに、毎回撮って読んでいた。
/// 窓の中身は UI Automation に出ない物もあり（Win32 の MessageBox のボタン、Unity の画面）、
/// 撮った絵を読むのは遅くて外しやすい。足跡があれば1行を読むだけで確かめられる。
///
/// **本番の動きは変えない。**環境変数が無ければ何もしない（書き込みも起きない）。
/// 値にパスを入れればそこへ、<c>1</c> のような値なら <c>%TEMP%\chmonos-uitrace.log</c> へ書く。
/// 書けなくても黙って諦める——確かめの道具のために、アプリを止めてはいけない
/// </summary>
public static class UiTrace
{
    private const string Variable = "CHMONOS_UITRACE";

    private static readonly Lock Gate = new();

    private static readonly string? Path = Resolve();

    /// <summary>足跡を書くか。重い文字列を組み立てる前に見る。</summary>
    public static bool IsOn => Path is not null;

    /// <param name="kind">何の足跡か（「知らせ」「選ぶ」「命令」「Unity」）。</param>
    /// <param name="text">中身。改行は「⏎」に畳む（1件1行にして、grep で読めるようにする）。</param>
    public static void Write(string kind, string text)
    {
        if (Path is null)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("HH:mm:ss.fff"))
            .Append('\t').Append(kind)
            .Append('\t').Append(text.ReplaceLineEndings("⏎"))
            .AppendLine()
            .ToString();

        try
        {
            lock (Gate)
            {
                File.AppendAllText(Path, line, Encoding.UTF8);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 書けなくても確かめ以外には響かない
        }
    }

    private static string? Resolve()
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var path = value.Contains(System.IO.Path.DirectorySeparatorChar) || value.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            ? value
            : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chmonos-uitrace.log");

        try
        {
            var folder = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // どの起動の足跡かが分かるように、始まりを1行書く（前の分は残す。続けて確かめることがある）
            File.AppendAllText(
                path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t起動\tpid={Environment.ProcessId} 保存先={Environment.GetEnvironmentVariable("CHMONOS_HOME")}{Environment.NewLine}",
                Encoding.UTF8);
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Debug.WriteLine($"足跡を書けない: {exception.Message}");
            return null;
        }
    }
}
