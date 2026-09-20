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

        // 手で書いた null の配列を空として受ける（EmptyForNull に理由を書いてある）
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
        {
            Modifiers = { EmptyForNull.Apply },
        },
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
        var gate = GateFor(path);
        gate.Wait();
        try
        {
            var temporaryPath = PrepareTemporary(path);
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, value, Options);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, path, overwrite: true);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        var gate = GateFor(path);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var temporaryPath = PrepareTemporary(path);
            try
            {
                await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, path, overwrite: true);
            }
            catch
            {
                // 中断（OperationCanceledException）でも書きかけを残さない。
                // 残った .tmp は誰も片付けず、画像の保存先に溜まり続けていた
                TryDelete(temporaryPath);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 同じファイルへの書き込みを1本ずつにする。
    ///
    /// 置き換え方式は「一時ファイルへ書く→本体へ Move」の2手なので、2本が重なると
    /// 一時ファイルの取り合い（<c>FileShare.None</c>）か、本体への Move の取り合いで
    /// **保存そのものが例外で落ちる**。落ちた保存はほとんど投げっぱなしなので、画面には何も出ない。
    ///
    /// これは「落ちない」ための錠で、**読んでから書くまでを守る物ではない**。
    /// 書き手が複数いるファイルは <see cref="JsonFileStore{T}.UpdateAsync"/> の錠を通すこと。
    /// </summary>
    private static SemaphoreSlim GateFor(string path)
        => s_gates.GetOrAdd(Path.GetFullPath(path), static _ => new SemaphoreSlim(1, 1));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> s_gates
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 一時ファイルの名前を決める。**同じ本体に対して毎回違う名前にする。**
    ///
    /// 固定の「本体+.tmp」だと、同じファイルに2本が同時に書いたとき、
    /// 後から来た方が <c>FileShare.None</c> で弾かれて保存そのものが落ちていた
    /// （落ちた保存はほとんど投げっぱなしなので、画面には何も出ない）。
    /// </summary>
    private static string PrepareTemporary(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return $"{path}.{Environment.ProcessId:x}-{Interlocked.Increment(ref s_temporarySequence):x}.tmp";
    }

    private static int s_temporarySequence;

    /// <summary>テキストファイル（説明HTMLなど）も同じく置き換え方式で書く。</summary>
    public static async Task WriteTextAsync(string path, string text, CancellationToken cancellationToken = default)
    {
        var gate = GateFor(path);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var temporaryPath = PrepareTemporary(path);
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
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 書きかけで残った一時ファイル（<c>*.tmp</c>）を片付ける。起動時に呼ぶ。
    ///
    /// **10分より古いものだけ消す。**同じ保存先を別のプロセスが今まさに書いているかもしれない
    /// （二重起動は止めているが、念のため）。書き込みは一瞬で終わるので、10分残っていれば書きかけではない。
    /// </summary>
    public static int DeleteStaleTemporaryFiles(string directory, bool includeSubdirectories = false)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var deleted = 0;
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        var scope = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var file in Directory.EnumerateFiles(directory, "*.tmp", scope))
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
