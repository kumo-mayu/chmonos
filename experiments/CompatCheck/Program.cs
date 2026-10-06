using System.Text.Json;
using System.Text.RegularExpressions;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

// 前の版のアプリが書いた保存先を、今の Core の型で読めるかを確かめる（担当L100・2026-10-06）。
//
// 各ファイルを「今の型で読む → 今の型で書き出す（メモリの中だけ）」にかけ、元の JSON とキーの単位で比べる。
// 読んで書き戻すと消える欄・既定で足される欄・値が変わる欄・読めずに落ちるファイルを、種類ごとの件数で出す。
//
// **読むだけ。**保存先には何も書かない（JsonStore.Write も DataStore も使わない。読むのは JsonStore.Read だけ）。
// 通信の部品も組まない。出力は件数と欄の名前だけにする——友人のデータの写しに当てるので、
// 商品名・ファイル名・ID・タグの名前は出さない（辞書の鍵は {key} に畳み、文字の値は出さない。CLAUDE.md の4）。

var root = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))
    ?? Environment.GetEnvironmentVariable(AppPaths.RootVariable);
if (string.IsNullOrWhiteSpace(root))
{
    Console.Error.WriteLine("保存先を引数か CHMONOS_HOME で指定してください（読むだけ。写しの写しに当てる）。");
    return 2;
}

root = Path.GetFullPath(root);
if (Refused(root) is { } reason)
{
    Console.Error.WriteLine($"この保存先は開きません：{reason}");
    return 2;
}

if (!Directory.Exists(root))
{
    Console.Error.WriteLine("保存先がありません。");
    return 2;
}

var paths = new AppPaths(root);
var checks = new List<FileKind>
{
    One<ItemRecord>("items/*.json", Directory.Exists(paths.ItemsDir) ? Directory.GetFiles(paths.ItemsDir, "*.json") : []),
    One<ItemRecord>("items/.prev/*.json", Files(Path.Combine(paths.ItemsDir, ".prev"))),
    One<ModificationRecord>("modifications/*.json", Files(paths.ModificationsDir)),
    One<UnityPackagePathsFile>("unitypackages/*.json", Files(paths.UnityPackagesDir)),
    Single<AppSettings>(paths.SettingsFile),
    Single<List<UnresolvedFile>>(paths.UnresolvedFile),
    Single<List<ExcludedEntry>>(paths.ExcludedFile),
    Single<UserTagMaster>(paths.UserTagsFile),
    Single<AttributeMaster>(paths.AttributesFile),
    Single<AvatarRegistry>(paths.AvatarRegistryFile),
    Single<List<NotificationRecord>>(paths.NotificationsFile),
    Single<SearchHistoryList>(paths.SearchHistoryFile),
    Single<SavedSearchList>(paths.SavedSearchesFile),
    Single<RecentLog>(paths.RecentFile),
    Single<List<ShopBannerRecord>>(paths.ShopBannersFile),
    Single<List<ShopNoteRecord>>(paths.ShopNotesFile),
    Single<List<VideoTitleRecord>>(paths.VideoTitlesFile),
    Single<List<ScanCacheEntry>>(paths.ScanCacheFile),
    Single<ImportState>(paths.ImportStateFile),
    Single<EditSession>(paths.EditSessionFile),
    Single<List<VolumeRecord>>(paths.VolumesFile),
    Single<UiState>(paths.UiStateFile),
    Single<List<QueuedRegistration>>(paths.RegistrationQueueFile),
};

var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    // 作り直せる控え（JSON ではない）と、錠のファイル
    Path.GetFileName(paths.SearchBridgeCacheFile),
    Path.GetFileName(paths.KanjiReadingsCacheFile),
    Path.GetFileName(paths.LockFile),
};
foreach (var check in checks)
{
    known.Add(check.Label);
}

Console.WriteLine($"保存先：{Path.GetFileName(root)}（読むだけ）");
Console.WriteLine();
foreach (var check in checks)
{
    check.Report();
}

// 今の Core が知らないファイル（前の版だけが書いた物・今は読まれない物）
var unknown = Directory.GetFiles(root).Select(Path.GetFileName).OfType<string>()
    .Where(name => !known.Contains(name)).Order(StringComparer.Ordinal).ToList();
var unknownDirs = Directory.GetDirectories(root).Select(Path.GetFileName).OfType<string>()
    .Where(name => name is not ("items" or "images" or "modifications" or "unitypackages" or "logs")).ToList();
Console.WriteLine("== 今の Core が読まない物（保存先の直下）");
foreach (var name in unknown.Concat(unknownDirs.Select(dir => dir + "/")))
{
    Console.WriteLine($"  {name}");
}

if (unknown.Count + unknownDirs.Count == 0)
{
    Console.WriteLine("  （無い）");
}

return 0;

static string? Refused(string root)
{
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.Equals(root.TrimEnd('\\'), Path.Combine(local, "Chmonos"), StringComparison.OrdinalIgnoreCase))
    {
        return "本番の保存先";
    }

    // 友人のデータの写しそのものは開かない。写しの写し（New-ChmonosSandbox -From tester）に当てる
    var name = Path.GetFileName(root.TrimEnd('\\'));
    return name is "friendtest" or "tester" or "eval" ? "友人のデータの写しそのもの（写しの写しを作って当てる）" : null;
}

static string[] Files(string dir) => Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : [];

static FileKind Single<T>(string path) where T : class
    => new(Path.GetFileName(path), File.Exists(path) ? [path] : [], RoundTrip<T>);

static FileKind One<T>(string label, string[] files) where T : class
    => new(label, files, RoundTrip<T>);

static JsonElement? RoundTrip<T>(string path) where T : class
    => JsonStore.Read<T>(path) is { } value ? JsonSerializer.SerializeToElement(value, JsonStore.Options) : null;

/// <summary>1種類のファイルを読んで比べ、件数を集める。</summary>
internal sealed class FileKind(string label, string[] files, Func<string, JsonElement?> roundTrip)
{
    public string Label { get; } = label;

    private readonly Dictionary<string, int> _dropped = [];
    private readonly Dictionary<string, int> _added = [];
    private readonly Dictionary<string, Dictionary<string, int>> _changed = [];
    private readonly Dictionary<string, int> _lengthChanged = [];
    private readonly Dictionary<string, int> _failures = [];
    private int _readNull;
    private int _clean;

    public void Report()
    {
        Console.WriteLine($"== {Label}：{files.Length}件");
        if (files.Length == 0)
        {
            Console.WriteLine();
            return;
        }

        foreach (var file in files)
        {
            Compare(file);
        }

        Console.WriteLine($"  そのまま書き戻せる：{_clean}件");
        Print("  落ちる（読めない）", _failures);
        if (_readNull > 0)
        {
            Console.WriteLine($"  中身が null：{_readNull}件");
        }

        Print("  消える欄（今の型に無い・書き戻すと無くなる）", _dropped);
        Print("  足される欄（元に無く、既定の値で書かれる）", _added);
        Print("  配列の長さが変わる", _lengthChanged);
        if (_changed.Count > 0)
        {
            Console.WriteLine("  値が変わる：");
            foreach (var (key, kinds) in _changed.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"    {key}：" + string.Join("・", kinds.Select(pair => $"{pair.Key} {pair.Value}件")));
            }
        }

        Console.WriteLine();
    }

    private static void Print(string heading, Dictionary<string, int> counts)
    {
        if (counts.Count == 0)
        {
            return;
        }

        Console.WriteLine(heading + "：");
        foreach (var (key, count) in counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"    {key}：{count}件");
        }
    }

    private void Compare(string file)
    {
        JsonElement original;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            original = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            Count(_failures, "JSON として読めない");
            return;
        }

        JsonElement? written;
        try
        {
            written = roundTrip(file);
        }
        catch (JsonException failure)
        {
            // 例外の文は型の名前と場所だけで、値は含まない
            Count(_failures, $"{Normalize(failure.Path ?? "$")}（{Shorten(failure.Message)}）");
            return;
        }
        catch (Exception failure) when (failure is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            Count(_failures, $"{failure.GetType().Name}");
            return;
        }

        if (written is not { } after)
        {
            _readNull++;
            return;
        }

        var seen = new FileDiff();
        Walk(original, after, "$", seen);
        if (seen.IsEmpty)
        {
            _clean++;
        }

        // 1ファイルの中で同じ欄が何度出ても1件と数える（「何件のファイルで」を出したい）
        foreach (var key in seen.Dropped)
        {
            Count(_dropped, key);
        }

        foreach (var key in seen.Added)
        {
            Count(_added, key);
        }

        foreach (var key in seen.Length)
        {
            Count(_lengthChanged, key);
        }

        foreach (var (key, kind) in seen.Changed)
        {
            if (!_changed.TryGetValue(key, out var kinds))
            {
                _changed[key] = kinds = [];
            }

            Count(kinds, kind);
        }
    }

    private static string Shorten(string message)
    {
        var cut = message.IndexOf(" Path:", StringComparison.Ordinal);
        return cut > 0 ? message[..cut] : message;
    }

    private static void Count(Dictionary<string, int> counts, string key)
        => counts[key] = counts.GetValueOrDefault(key) + 1;

    private sealed class FileDiff
    {
        public HashSet<string> Dropped { get; } = [];

        public HashSet<string> Added { get; } = [];

        public HashSet<string> Length { get; } = [];

        public HashSet<(string, string)> Changed { get; } = [];

        public bool IsEmpty => Dropped.Count + Added.Count + Length.Count + Changed.Count == 0;
    }

    /// <summary>
    /// 鍵が利用者の物（属性の名前・画像の名前・zip の中の場所・GUID）になる辞書。中の鍵は出さずに {key} に畳む。
    /// packages は辞書の辞書（zip の中の場所 → GUID → パス）。
    /// </summary>
    private static readonly HashSet<string> Dictionaries = ["imageRoles", "attributes", "seenAs", "volumes", "paneWidths"];

    private static readonly HashSet<string> DictionariesOfDictionaries = ["packages"];

    private static readonly Regex PropertyName = new("^[a-z][a-zA-Z0-9]{0,39}$", RegexOptions.CultureInvariant);

    private static void Walk(JsonElement before, JsonElement after, string path, FileDiff diff, int dictionaryDepth = 0)
    {
        if (before.ValueKind == JsonValueKind.Object && after.ValueKind == JsonValueKind.Object)
        {
            var afterProps = after.EnumerateObject().ToDictionary(prop => prop.Name, prop => prop.Value, StringComparer.Ordinal);
            var beforeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in before.EnumerateObject())
            {
                beforeNames.Add(prop.Name);
                var (child, depth) = Child(path, prop.Name, dictionaryDepth);
                if (!afterProps.TryGetValue(prop.Name, out var counterpart))
                {
                    // null は書くときに省く決まり（WhenWritingNull）なので、無くなっても失う物は無い
                    if (prop.Value.ValueKind != JsonValueKind.Null)
                    {
                        diff.Dropped.Add(child);
                    }

                    continue;
                }

                Walk(prop.Value, counterpart, child, diff, depth);
            }

            foreach (var (name, value) in afterProps)
            {
                if (!beforeNames.Contains(name))
                {
                    var (child, _) = Child(path, name, dictionaryDepth);
                    diff.Added.Add(child + Describe(value));
                }
            }

            return;
        }

        if (before.ValueKind == JsonValueKind.Array && after.ValueKind == JsonValueKind.Array)
        {
            var left = before.EnumerateArray().ToList();
            var right = after.EnumerateArray().ToList();
            if (left.Count != right.Count)
            {
                diff.Length.Add(path);
                return;
            }

            for (var index = 0; index < left.Count; index++)
            {
                Walk(left[index], right[index], path + "[]", diff);
            }

            return;
        }

        if (SameScalar(before, after))
        {
            return;
        }

        diff.Changed.Add((path, ChangeKind(before, after)));
    }

    /// <summary>辞書の中の鍵は畳む。普通の欄は名前を出す（camelCase の欄の名前で無い物も畳む）。</summary>
    private static (string Path, int Depth) Child(string path, string name, int dictionaryDepth)
    {
        if (dictionaryDepth > 0)
        {
            return (path + ".{key}", dictionaryDepth - 1);
        }

        var shown = PropertyName.IsMatch(name) ? name : "{key}";
        var depth = Dictionaries.Contains(name) ? 1 : DictionariesOfDictionaries.Contains(name) ? 2 : 0;
        return (path + "." + shown, depth);
    }

    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => $" = {value.GetRawText()}",
        JsonValueKind.Number => $" = {value.GetRawText()}",
        JsonValueKind.Array => value.GetArrayLength() == 0 ? " = []" : " = [..]",
        JsonValueKind.Object => " = {..}",
        JsonValueKind.String => " = \"..\"",
        _ => "",
    };

    private static bool SameScalar(JsonElement before, JsonElement after)
    {
        if (before.ValueKind != after.ValueKind)
        {
            return false;
        }

        return before.ValueKind switch
        {
            JsonValueKind.Number => before.TryGetDecimal(out var left) && after.TryGetDecimal(out var right)
                ? left == right
                : before.GetRawText() == after.GetRawText(),
            JsonValueKind.String => before.GetString() == after.GetString(),
            _ => before.GetRawText() == after.GetRawText(),
        };
    }

    /// <summary>真偽と数は値を出す（名前ではない）。文字は出さない（名前やパスが入り得る）。</summary>
    private static string ChangeKind(JsonElement before, JsonElement after)
    {
        if (before.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number
            && after.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number)
        {
            return $"{before.GetRawText()}→{after.GetRawText()}";
        }

        return $"{Kind(before)}→{Kind(after)}";
    }

    private static string Kind(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => "文字",
        JsonValueKind.Null => "null",
        JsonValueKind.Array => value.GetArrayLength() == 0 ? "空の配列" : "配列",
        JsonValueKind.Object => "物",
        _ => value.ValueKind.ToString(),
    };

    /// <summary>例外の場所の添字を畳み、欄の名前で無い区切り（辞書の鍵）も {key} に畳む。</summary>
    private static string Normalize(string jsonPath)
    {
        var text = Regex.Replace(jsonPath, @"\[\d+\]", "[]");
        text = Regex.Replace(text, @"\['[^']*'\]", ".{key}");
        return string.Join('.', text.Split('.').Select((part, index) =>
            index == 0 || PropertyName.IsMatch(part.Replace("[]", "")) || part == "{key}" ? part : "{key}"));
    }
}
