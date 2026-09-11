using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoothAssetManager.Core.Storage;

/// <summary>
/// JSONの読み書き。書き込みは一時ファイルへ書いてからリネームで置き換えるので、
/// 途中で落ちても元のファイルは無傷のまま残る。
/// </summary>
public static class JsonStore
{
    /// <summary>
    /// 日本語をエスケープせずそのまま書き、インデントを付ける。
    /// ユーザがJSONを直接開いて読み書きすることを前提にした設定。
    /// 読み込み側はコメントと末尾カンマを許して、手編集に耐えるようにしている。
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>ファイルが無ければ null を返す。壊れていれば例外を投げる（黙って握り潰さない）。</summary>
    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Options);
    }

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken = default) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
    }

    public static void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, Options);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    /// <summary>テキストファイル（説明HTMLなど）も同じく置き換え方式で書く。</summary>
    public static async Task WriteTextAsync(string path, string text, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, text, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            // 置き換えに失敗すると .tmp が残り、誰も片付けなかった（友人のストアに2件残っていた）。
            // 本体を読んでいる人がいると置き換えは失敗しうる。例外はそのまま上へ返す
            TryDelete(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// 書きかけで残った一時ファイル（<c>*.tmp</c>）を片付ける。起動時に呼ぶ。
    ///
    /// **10分より古いものだけ消す。**同じ保存先を別のプロセスが今まさに書いているかもしれない
    /// （二重起動は止めているが、念のため）。書き込みは一瞬で終わるので、10分残っていれば書きかけではない。
    /// </summary>
    public static int DeleteStaleTemporaryFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var deleted = 0;
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        foreach (var file in Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff && TryDelete(file))
            {
                deleted++;
            }
        }

        return deleted;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
