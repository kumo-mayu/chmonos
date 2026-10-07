using Chmonos.Core.Diagnostics;

namespace Chmonos.Core.Storage;

/// <summary>書き出しの途中の記録の中身。人が開いて読めるよう、場所と始めた日時だけ。</summary>
public sealed record BackupWritingFile
{
    /// <summary>書きかけの一時ファイルの場所（<c>&lt;書き出す名前&gt;.zip.tmp</c>）。</summary>
    public required string TemporaryFile { get; init; }

    public DateTimeOffset StartedAt { get; init; }
}

/// <summary>
/// バックアップの書き出しの途中だという記録（2026-10-07 ユーザ判断「次回片付ける」）。
///
/// 書き出しは使う人が選んだフォルダへ <c>.zip.tmp</c> を書き、書き終えてから本当の名前へ置き換える。
/// 途中でアプリが止まる（強制終了・電源）と、その <c>.tmp</c> が選んだフォルダに残り、アプリは場所を覚えていないので
/// 誰も片付けなかった（大きさは、いちばん大きくてバックアップ1つ分）。書き始める前に保存先へ場所を書いておき、
/// 書き終えたら消す。次の起動で記録が残っていれば、書きかけを消す。
/// </summary>
public static class BackupWritingRecord
{
    public const string FileName = "backup-writing.json";

    /// <summary>
    /// 書き始める前に置く。書き出しの間は保存先の書き込みの門を閉じていることがあるので、門を通さずに書く
    /// （<see cref="JsonStore.WriteOutsideStore"/>。記録は保存先の中だが、書き出す物には入れない）。
    /// </summary>
    public static void Begin(string root, string temporaryFile)
    {
        try
        {
            JsonStore.WriteOutsideStore(
                Path.Combine(root, FileName),
                new BackupWritingFile { TemporaryFile = temporaryFile, StartedAt = DateTimeOffset.Now });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 記録を書けなくても書き出しは進める。止まったときに .tmp が残るだけで、書き出しそのものは困らない
            AppLog.Warn("バックアップの書き出し", $"途中の記録を書けなかった：{exception.Message}");
        }
    }

    /// <summary>書き終えた・失敗して片付けたときに外す。</summary>
    public static void End(string root)
    {
        try
        {
            File.Delete(Path.Combine(root, FileName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("バックアップの書き出し", $"途中の記録を消せなかった：{exception.Message}");
        }
    }

    /// <summary>
    /// 起動の後に呼ぶ。記録が残っていれば、前の書き出しが途中で止まったので、書きかけを消して記録も外す。
    /// 記録は手で直せる JSON なので、消すのは名前が <c>.zip.tmp</c> で終わるファイルだけ（ほかの場所を書かれても消さない）。
    /// </summary>
    /// <returns>書きかけを消したか。</returns>
    public static bool CleanUp(string root)
    {
        var recordPath = Path.Combine(root, FileName);
        if (!File.Exists(recordPath))
        {
            return false;
        }

        var deleted = false;
        try
        {
            if (JsonStore.Read<BackupWritingFile>(recordPath)?.TemporaryFile is { Length: > 0 } temporary
                && IsWritingName(temporary)
                && File.Exists(temporary))
            {
                File.Delete(temporary);
                deleted = true;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or NotSupportedException)
        {
            AppLog.Warn("バックアップの書き出し", $"前の書きかけを片付けられなかった：{exception.Message}");
        }

        End(root);
        return deleted;
    }

    /// <summary>
    /// 書き出しが作る一時ファイルの名前か（<c>&lt;名前&gt;.zip.tmp</c>、同じ名前の物が既にあったときの <c>&lt;名前&gt;.zip.&lt;8桁&gt;.tmp</c>）。
    /// 記録は手で直せる JSON なので、この形の名前のほかは消さない
    /// </summary>
    internal static bool IsWritingName(string path)
        => System.Text.RegularExpressions.Regex.IsMatch(
            Path.GetFileName(path), @"\.zip(\.[0-9a-f]{8})?\.tmp$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
