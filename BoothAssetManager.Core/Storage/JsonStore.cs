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
        await File.WriteAllTextAsync(temporaryPath, text, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
