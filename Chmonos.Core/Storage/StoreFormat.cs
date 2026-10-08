using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Chmonos.Core.Storage;

/// <summary>
/// 保存データの形式の版（ユーザ判断 2026-10-08・`docs/spec/data-format.md`）。
///
/// アプリの版ごとに「読める形式の範囲」（<see cref="OldestReadable"/>〜<see cref="Current"/>）と「書く形式」（<see cref="Current"/>）を持つ。
/// 範囲内なら読み、書くときは最新の形式で書く。範囲より新しい形式は書き込まずに止める
/// （古い版は知らない欄を保てず、保存のたびに消してしまうため）。
/// </summary>
public static class StoreFormat
{
    /// <summary>
    /// 書く形式の版。**欄の名前・意味・型を変える、欄を消す、保存する列挙に値を足す、配列を包む**ときに1つ上げ、
    /// <see cref="Migrations"/> に1つ前の版から直す関数を足す。省略できる欄を足すだけなら上げない。
    /// </summary>
    public const int Current = 1;

    /// <summary>読める最も古い版。v1.0.0 の形（版の欄が無い）が 1。</summary>
    public const int OldestReadable = 1;

    /// <summary>ファイルごとの版の欄の名前。オブジェクトの JSON のいちばん外側の最初に書く。</summary>
    public const string VersionProperty = "formatVersion";

    /// <summary>保存先ごとの版を書くファイル（保存先の直下）。</summary>
    public const string MarkerFileName = "format.json";

    /// <summary>形式を上げる前の控えを置くフォルダ（保存先の直下）。</summary>
    public const string BackupsDirName = "format-backups";

    /// <summary>
    /// 1つ前の版から直す関数（鍵は直した後の版）。読むときに古い順に当てる。型ごとに分けたいときは、関数の中で型を見る。
    /// 今は版 1 しか無いので空
    /// </summary>
    private static readonly IReadOnlyDictionary<int, Func<JsonNode, Type, JsonNode>> Migrations = new Dictionary<int, Func<JsonNode, Type, JsonNode>>();

    /// <summary>
    /// 版の欄を持てる型か。いちばん外側が配列・辞書の物（JSON の配列か、鍵が自由なオブジェクト）は持てないので 1 とみなす。
    /// 辞書に版の欄を足すと、読むときにその欄が辞書の1件として入ってしまう
    /// </summary>
    internal static bool CarriesVersion(Type type)
        => type != typeof(string) && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    /// <summary>書いた JSON（オブジェクト）の最初の欄に、今の版を足す。インデントは書き方（<see cref="JsonStore.Options"/>）に合わせる。</summary>
    internal static byte[] Stamp(byte[] json)
    {
        var open = Array.IndexOf(json, (byte)'{');
        if (open < 0)
        {
            return json;
        }

        var next = open + 1;
        while (next < json.Length && json[next] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t')
        {
            next++;
        }

        var newLine = JsonStore.Options.NewLine;
        var isEmpty = next < json.Length && json[next] == (byte)'}';
        var stamp = isEmpty
            ? $"{newLine}  \"{VersionProperty}\": {Current}{newLine}"
            : $"{newLine}  \"{VersionProperty}\": {Current},";
        var stampBytes = Encoding.UTF8.GetBytes(stamp);

        var rest = isEmpty ? next : open + 1;
        var result = new byte[open + 1 + stampBytes.Length + (json.Length - rest)];
        json.AsSpan(0, open + 1).CopyTo(result);
        stampBytes.CopyTo(result.AsSpan(open + 1));
        json.AsSpan(rest).CopyTo(result.AsSpan(open + 1 + stampBytes.Length));
        return result;
    }

    /// <summary>
    /// JSON のいちばん外側の版の欄を読む。欄が無い・オブジェクトでなければ 1（v1.0.0 の形）。
    /// 手で直したファイルでは最初の欄とは限らないので、いちばん外側の欄を全部見る（中には降りない）
    /// </summary>
    public static int VersionOf(ReadOnlySpan<byte> json)
    {
        // 壊れた JSON は版を決めずに 1 とし、本読み（Deserialize）に今までどおりの例外を出させる。
        // ここで投げると、読み手の型（JsonReaderException）が変わり、壊れた記録の扱いが揃わない
        try
        {
            return ReadVersion(json);
        }
        catch (JsonException)
        {
            return OldestReadable;
        }
    }

    private static int ReadVersion(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return OldestReadable;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals(VersionProperty))
            {
                return reader.Read() && reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var version)
                    ? version
                    : throw new JsonException($"{VersionProperty} が数ではありません。");
            }

            reader.Read();
            reader.Skip();
        }

        return OldestReadable;
    }

    /// <summary>古い版の JSON を今の版へ直す。読める範囲より古ければ例外。</summary>
    internal static JsonNode Migrate(JsonNode node, int from, Type type, string path)
    {
        if (from < OldestReadable)
        {
            throw new FormatTooOldException(path, from);
        }

        for (var version = from + 1; version <= Current; version++)
        {
            node = Migrations[version](node, type);
        }

        return node;
    }

    // --- 保存先ごとの版 ---

    /// <summary>保存先の版と、このアプリとの関係。</summary>
    public enum Verdict
    {
        /// <summary>書く版と同じ。そのまま開く。</summary>
        Same,

        /// <summary>書く版より古いが読める。控えを取ってから開く。</summary>
        Older,

        /// <summary>読める範囲より古い。開かない。</summary>
        TooOld,

        /// <summary>書く版より新しい。開かず、新しい版を案内する。</summary>
        TooNew,
    }

    /// <summary>保存先の印（<see cref="MarkerFileName"/>）。版は <see cref="JsonStore"/> が書く版の欄そのもの。</summary>
    public sealed record Marker
    {
        /// <summary>最後に書いたアプリの版（人が読むため。判断には使わない）。</summary>
        public string? WrittenBy { get; init; }
    }

    /// <summary>保存先の版。印が無ければ 1（v1.0.0 が使った保存先か、初めて開く保存先）。</summary>
    public static int StoreVersion(string root)
    {
        var path = Path.Combine(root, MarkerFileName);
        if (!File.Exists(path))
        {
            return OldestReadable;
        }

        using var stream = JsonStore.OpenShared(path);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return VersionOf(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    public static Verdict Check(int storeVersion)
        => storeVersion > Current ? Verdict.TooNew
            : storeVersion < OldestReadable ? Verdict.TooOld
            : storeVersion < Current ? Verdict.Older
            : Verdict.Same;

    /// <summary>保存先の印を今の版で書く（印が無かった・古かったとき）。</summary>
    public static void MarkCurrent(string root, string appVersion)
        => JsonStore.Write(Path.Combine(root, MarkerFileName), new Marker { WrittenBy = appVersion });

    /// <summary>
    /// 形式を上げる前の控えを取る。保存先の直下と items・modifications・unitypackages の JSON を、
    /// <c>format-backups/v{古い版}-{日時}/</c> の同じ相対の場所へ写す。写した先のフォルダを返す。
    /// 画像・.cache・logs・items/.prev は写さない（画像は形式を持たず、控えの控えは要らない）
    /// </summary>
    public static string Backup(string root, int fromVersion, DateTimeOffset now)
    {
        var target = Path.Combine(root, BackupsDirName, $"v{fromVersion}-{now.LocalDateTime:yyyyMMdd-HHmmss}");
        foreach (var source in BackupSources(root))
        {
            var relative = Path.GetRelativePath(root, source);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }

        return target;
    }

    private static IEnumerable<string> BackupSources(string root)
    {
        foreach (var file in StoreTree.Files(root, "*.json", recurse: false))
        {
            yield return file;
        }

        foreach (var folder in new[] { "items", "modifications", "unitypackages" })
        {
            var directory = Path.Combine(root, folder);
            if (Directory.Exists(directory))
            {
                foreach (var file in StoreTree.Files(directory, "*.json", recurse: false))
                {
                    yield return file;
                }
            }
        }
    }

    /// <summary>控えのファイル数と大きさ。設定の「データ」の行に出す（画像・商品の情報の行と同じ数え方）。</summary>
    public static (int Files, long Bytes) BackupUsage(string root)
    {
        var directory = Path.Combine(root, BackupsDirName);
        if (!Directory.Exists(directory))
        {
            return (0, 0);
        }

        var files = StoreTree.FilesWithLength(directory).ToList();
        return (files.Count, files.Sum(file => file.Length));
    }

    /// <summary>控えを全部消す（設定の「データ」の［控えを削除…］）。</summary>
    public static void DeleteBackups(string root)
    {
        var directory = Path.Combine(root, BackupsDirName);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

/// <summary>
/// このアプリより新しい形式のファイル（ユーザ判断 2026-10-08）。読まずに止め、上書きしない。
/// 読めないファイルとして扱われるよう、壊れた JSON（<see cref="JsonException"/>）ではなく読み書きの失敗の側に置く
/// </summary>
public sealed class FormatTooNewException(string path, int version)
    : IOException($"「{Path.GetFileName(path)}」は新しい版の Chmonos で書かれています（形式 {version}）。")
{
    public int Version { get; } = version;
}

/// <summary>読める範囲より古い形式のファイル。</summary>
public sealed class FormatTooOldException(string path, int version)
    : IOException($"「{Path.GetFileName(path)}」は古い形式（{version}）のため読めません。");
