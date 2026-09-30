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

        using var stream = OpenShared(path);
        return JsonSerializer.Deserialize<T>(stream, Options);
    }

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken = default) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = OpenShared(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
    }

    /// <summary>
    /// 配列の JSON に、要素が1つでもあるか。**頭だけ読んで答える**（ファイルが無ければ偽）。
    ///
    /// 起動の画面決めは画面のスレッドで、未確定が「在るか」だけを知りたい。記録を丸ごと読むと、
    /// 未確定が数万件ある保存先では起動のたびに数百ms 止まる（8万件・37.7MB で 170〜280ms。初回はもっと長い）。
    /// 頭の 4KB で「[」の次が「]」かを見れば足りる。コメントや空白が 4KB より長く続いて決まらなければ、全部読んで数える。
    /// 配列でない・JSON として読めない頭なら、丸ごと読むときと同じく例外を投げる。
    /// </summary>
    public static bool ArrayHasItems(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var head = new byte[4096];
        int length;
        using (var stream = OpenShared(path))
        {
            length = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }

        // UTF-8 の印（BOM）は JSON の字ではないので飛ばす（メモ帳で直して保存すると付く）
        var span = head.AsSpan(0, length);
        if (span.StartsWith("﻿"u8))
        {
            span = span[3..];
        }

        var isWhole = length < head.Length;
        var reader = new Utf8JsonReader(span, isFinalBlock: isWhole, new JsonReaderState(new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }));

        if (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException($"「{Path.GetFileName(path)}」は配列ではありません。");
            }

            if (reader.Read())
            {
                return reader.TokenType != JsonTokenType.EndArray;
            }
        }

        // 頭だけでは決まらなかった（4KB を超えるコメント・1つ目の値が 4KB をまたぐ文字列など）
        return Read<List<JsonElement>>(path) is { Count: > 0 };
    }

    /// <summary>
    /// 同期で書く。保存先を運んでいる間はスレッドを止めて待つので、**画面のスレッドから呼ばない**（<see cref="StoreWriteGate.Enter"/>）。
    /// 画面から来得る保存は <see cref="WriteAsync{T}"/> を使う。
    /// </summary>
    public static void Write<T>(string path, T value) => Write(path, value, throughStoreGate: true);

    /// <summary>
    /// **保存先の外**のファイルを書く（<c>location.json</c>）。運ぶ門を通らない。
    ///
    /// 保存先の場所を覚えるファイルは、運び終えて門を閉じたままにした後（開き直す直前）に画面のスレッドで書く。
    /// 門を通すと、開くことの無い門を画面のスレッドが待ち続けて固まる。運ぶ対象でもないので、止める理由も無い。
    /// </summary>
    internal static void WriteOutsideStore<T>(string path, T value) => Write(path, value, throughStoreGate: false);

    private static void Write<T>(string path, T value, bool throughStoreGate)
    {
        var gate = GateFor(path);
        gate.Wait();
        try
        {
            // 保存先を運んでいる間は待つ。書いている間は「書いている」に数えられ、運ぶ側はこれが抜けるのを待つ（StoreWriteGate）
            using var writing = throughStoreGate ? StoreWriteGate.Enter() : null;
            var temporaryPath = PrepareTemporary(path);
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, value, Options);
                    stream.Flush(flushToDisk: true);
                }

                Replace(temporaryPath, path);
            }
            catch (Exception failure)
            {
                DiscardTemporary(temporaryPath, failure);
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
            using var writing = await StoreWriteGate.EnterAsync(cancellationToken);
            var temporaryPath = PrepareTemporary(path);
            try
            {
                await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                await ReplaceAsync(temporaryPath, path, cancellationToken);
            }
            catch (Exception failure)
            {
                // 中断（OperationCanceledException）でも書きかけを残さない。
                // 残った .tmp は誰も片付けず、画像の保存先に溜まり続けていた
                DiscardTemporary(temporaryPath, failure);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 読むために開く。**書き込みの置き換え（削除を伴う）を塞がない共有で開く。**
    ///
    /// <c>File.OpenRead</c> は削除の共有を許さないので、読んでいる最中に同じファイルの保存が
    /// 置き換えをすると <c>UnauthorizedAccessException</c> で保存が落ちていた
    /// （画面が一覧を読み直すのと、裏の取得が商品を書くのは普通に重なる）。
    /// 削除を許して開いても、読んでいる側は開いた時点の中身を最後まで読める。
    /// </summary>
    public static FileStream OpenShared(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>テキストを読む（説明HTMLなど）。開き方は <see cref="OpenShared"/> と同じ理由で揃える。</summary>
    public static string ReadText(string path)
    {
        using var stream = OpenShared(path);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <inheritdoc cref="ReadText"/>
    public static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = OpenShared(path);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// 置き換えが共有のせいで弾かれたとき、やり直す回数。待ちは 10・20・40・80・160ms で計約0.3秒。
    ///
    /// こちらの読み込みは <see cref="OpenShared"/> で開くので弾かれないが、
    /// セキュリティソフト・バックアップ・エディタなど**アプリの外の読み手**は削除の共有を許さずに開くことがある。
    /// どれも数十ms で離すので、1回で諦めて保存ごと落とすより少し待つ方がよい。
    /// 長く握られていれば（エディタで開きっぱなし等）諦めて投げる——それ以上待っても画面を待たせるだけ。
    /// </summary>
    private const int ReplaceRetries = 5;

    private static TimeSpan ReplaceRetryDelay(int attempt) => TimeSpan.FromMilliseconds(10 << attempt);

    /// <summary>
    /// 共有のせいで置き換えられなかったか。削除の共有を許さずに開かれていると、上書きの Move は
    /// アクセス拒否（<see cref="UnauthorizedAccessException"/>）か共有違反（32）・ロック違反（33）、
    /// <c>File.Replace</c> なら「置き換えられる側を消せない」（1175）で返る。
    /// </summary>
    private static bool IsSharingProblem(Exception exception)
        => exception is UnauthorizedAccessException
            || (exception is IOException io && (io.HResult & 0xFFFF) is 32 or 33 or 1175);

    /// <summary>
    /// 一時ファイルを本体の場所へ据える。**本体があれば <see cref="File.Replace(string, string, string?, bool)"/> で置き換える。**
    ///
    /// 上書きの <c>File.Move</c>（MoveFileEx）は、削除の共有を許して開かれている本体でも拒否された
    /// （試験で確かめた）。<c>File.Replace</c>（ReplaceFile）は本体を削除の共有つきで開いた読み手を妨げず、
    /// 読み手は開いた時点の中身を読み切れる。本体が無ければ置き換える物が無いので Move で置く。
    /// </summary>
    private static void MoveOver(string temporaryPath, string path)
        => MoveOver(temporaryPath, path, static (temporary, target)
            => File.Replace(temporary, target, destinationBackupFileName: null, ignoreMetadataErrors: true));

    /// <summary>置き換えの道具を差し替えられる形（1176・1177 は実のディスクでは起こせないので、試験で作る）。</summary>
    internal static void MoveOver(string temporaryPath, string path, Action<string, string> replace)
    {
        if (File.Exists(path))
        {
            try
            {
                replace(temporaryPath, path);
                return;
            }
            catch (FileNotFoundException) when (File.Exists(temporaryPath))
            {
                // 確かめた後に本体が消された（商品を外した等）。置き換える物が無いので下の Move で置く
            }
            catch (IOException exception) when (IsStrandedReplacement(exception) && File.Exists(temporaryPath))
            {
                // 本体はもう退けられ、新しい中身は一時ファイルにしか無い。前はここで投げ、呼んだ側の片付けが
                // 一時ファイルまで消していたので、その JSON は丸ごと消えていた。本体の場所へ据え直す
                try
                {
                    File.Move(temporaryPath, path, overwrite: true);
                    return;
                }
                catch (Exception moveFailure) when (moveFailure is IOException or UnauthorizedAccessException)
                {
                    throw new StrandedReplacementException(temporaryPath, path, moveFailure);
                }
            }
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    /// <summary>
    /// <c>ReplaceFile</c> が本体を退けた後で、一時ファイルを本体の名前にできなかったか。
    /// 1176（ERROR_UNABLE_TO_MOVE_REPLACEMENT）：控えの名前を渡していないと、本体はもう無く一時ファイルだけが残る。
    /// 1177（ERROR_UNABLE_TO_MOVE_REPLACEMENT_2）：本体は別の名前へ退けられ、一時ファイルは元の名前のまま残る。
    /// どちらも本体の名前は空いていて、新しい中身は一時ファイルにあるので、同じく Move で据えられる
    /// （Win32 の <c>ReplaceFileW</c> の説明による）。
    /// </summary>
    private static bool IsStrandedReplacement(IOException exception)
        => (exception.HResult & 0xFFFF) is 1176 or 1177;

    /// <summary>
    /// 置き換えの途中で本体が退けられ、一時ファイルを据え直すこともできなかった。
    /// **中身は一時ファイルにしか無いので、呼んだ側はそれを消さない。**
    /// </summary>
    internal sealed class StrandedReplacementException(string temporaryPath, string path, Exception inner)
        : IOException($"「{path}」の置き換えの途中で失敗しました。書いた中身は「{temporaryPath}」に残っています。", inner);

    /// <summary>失敗した書き込みの一時ファイルを片付ける。ただし中身がそこにしか無いときは残す。</summary>
    private static void DiscardTemporary(string temporaryPath, Exception failure)
    {
        if (failure is not StrandedReplacementException)
        {
            TryDelete(temporaryPath);
        }
    }

    private static void Replace(string temporaryPath, string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                MoveOver(temporaryPath, path);
                return;
            }
            catch (Exception exception) when (attempt < ReplaceRetries && IsSharingProblem(exception))
            {
                Thread.Sleep(ReplaceRetryDelay(attempt));
            }
        }
    }

    private static async Task ReplaceAsync(string temporaryPath, string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                MoveOver(temporaryPath, path);
                return;
            }
            catch (Exception exception) when (attempt < ReplaceRetries && IsSharingProblem(exception))
            {
                await Task.Delay(ReplaceRetryDelay(attempt), cancellationToken);
            }
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
    private static KeyedGate<string>.Handle GateFor(string path)
        => s_gates.For(Path.GetFullPath(path));

    /// <summary>
    /// ファイルごとの錠。使っている人がいなくなった錠は捨てる（<see cref="KeyedGate{TKey}"/>）。
    /// 前は書いたファイルの数だけ溜まり続けた（商品・説明・控え・画像の印で、2000件の取り込みなら数千）。
    /// </summary>
    private static readonly KeyedGate<string> s_gates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>今表にあるファイルごとの錠の数（試験で、捨てられたかを見る）。</summary>
    internal static int GateCount => s_gates.Count;

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
            using var writing = await StoreWriteGate.EnterAsync(cancellationToken);
            var temporaryPath = PrepareTemporary(path);
            try
            {
                // JSON と同じく、置き換える前にディスクへ書き出す。書き出さずに置き換えると、
                // 電源が落ちたときに「置き換えは済んだが中身が空」の説明HTMLが残り得る
                await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true))
                    {
                        await writer.WriteAsync(text.AsMemory(), cancellationToken);
                    }

                    stream.Flush(flushToDisk: true);
                }

                await ReplaceAsync(temporaryPath, path, cancellationToken);
            }
            catch (Exception failure)
            {
                // 置き換えに失敗すると .tmp が残り、誰も片付けなかった（友人のストアに2件残っていた）。
                // 本体を読んでいる人がいると置き換えは失敗しうる。例外はそのまま上へ返す
                DiscardTemporary(temporaryPath, failure);
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
