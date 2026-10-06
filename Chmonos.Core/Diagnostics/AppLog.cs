namespace Chmonos.Core.Diagnostics;

/// <summary>
/// 失敗を1つのテキストファイルに書き足す（技術的負債 2-1、2026-09-14）。
///
/// 前はアプリが何も書き残さず、裏の作業で黙って飛ばした失敗は、友人の手元で起きても後から追えなかった。
/// **1MB を超えたら1つ前の分（*.old.log）へ回して2つだけ残す。**失敗が続いても保存先を食い潰さない。
/// **ここで失敗しても投げない。**ログが書けないことでアプリを止める方が困る。
/// </summary>
public sealed class LogFile(string path, long maxBytes = 1_000_000)
{
    private readonly object _gate = new();

    public string Path => path;

    /// <summary>1件の文の上限。手で直した JSON の値は長さに限りが無いので、ログ1件で1MB の回しを食い切らないように抑える。</summary>
    public const int MaxMessageChars = 2000;

    /// <summary>
    /// 見出しと文を1行に収める（外部の点検 2026-10-06）。文には外から来た値（手で直した JSON の ID・ファイル名・BOOTH の文）が入る。
    /// 改行がそのままだと、日時と重さに似せた偽の行を作れた。改行と制御文字、向きを変える字（U+202E など）は
    /// 「\u000A」の形で見えるように書き、長すぎる文は切って印を付ける。例外の中身は下の字下げの行で、仕様どおり複数行のまま
    /// </summary>
    internal static string OneLine(string text, int max)
    {
        var builder = new System.Text.StringBuilder(Math.Min(text.Length, max) + 32);
        foreach (var c in text)
        {
            if (builder.Length >= max)
            {
                builder.Append($"…（{text.Length:N0}字のうち先頭だけ）");
                break;
            }

            if (char.IsControl(c) || c is (>= (char)0x202A and <= (char)0x202E) or (>= (char)0x2066 and <= (char)0x2069) or (char)0x200E or (char)0x200F)
            {
                builder.Append($"\\u{(int)c:X4}");
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>回した1つ前の分。</summary>
    public string OldPath => System.IO.Path.ChangeExtension(path, ".old.log");

    public void Write(string level, string where, string message, Exception? exception = null)
    {
        var text = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {level} [{OneLine(where, 200)}] {OneLine(message, MaxMessageChars)}{Environment.NewLine}";
        if (exception is not null)
        {
            // 例外の中身は字下げして、次の行の見出しと見分けられるようにする
            text += string.Join(Environment.NewLine, exception.ToString().Split('\n').Select(line => "    " + line.TrimEnd('\r')))
                + Environment.NewLine;
        }

        lock (_gate)
        {
            try
            {
                if (System.IO.Path.GetDirectoryName(path) is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(path) && new FileInfo(path).Length > maxBytes)
                {
                    File.Move(path, OldPath, overwrite: true);
                }

                File.AppendAllText(path, text);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>
/// どこからでも失敗を書き残す口。
///
/// **静的にしているのは、失敗はどの層のどの catch でも起きるから。**全部のサービスに書き手を配ると、
/// 書き残したいだけの所まで引数が増える。書き先は起動時に1回だけ決める（<see cref="Use"/>）。
/// 決まる前（試験・初回起動の窓）は何もしない。
/// </summary>
public static class AppLog
{
    private static LogFile? s_file;

    public static void Use(LogFile? file) => Volatile.Write(ref s_file, file);

    /// <summary>失敗。<paramref name="where"/> は何をしていたか（人が読んで分かる言葉で）。</summary>
    public static void Error(string where, Exception exception)
        => Volatile.Read(ref s_file)?.Write("失敗", where, exception.Message, exception);

    /// <summary>失敗ではないが、後から見て分かるようにしておきたいこと。</summary>
    public static void Warn(string where, string message)
        => Volatile.Read(ref s_file)?.Write("注意", where, message);
}
