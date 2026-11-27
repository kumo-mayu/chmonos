using System.Text;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 保存データの形式の版（ユーザ判断 2026-10-08・`docs/spec/data-format.md`）。
/// ファイルごとの版の欄・保存先ごとの印・形式を上げる前の控え。
/// </summary>
public class StoreFormatTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-format-" + Guid.NewGuid().ToString("N"));

    public StoreFormatTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string PathOf(string name) => Path.Combine(_root, name);

    private static ItemRecord Item() => new()
    {
        Id = "1000001",
        Booth = new BoothBlock { Name = "作り物の衣装" },
        Local = new LocalBlock(),
    };

    // --- ファイルごとの版 ---

    /// <summary>オブジェクトには、いちばん外側の最初の欄に今の版を書く（人が開いて最初に見える所）</summary>
    [Fact]
    public void オブジェクトの最初の欄に版を書く()
    {
        JsonStore.Write(PathOf("item.json"), Item());

        var text = File.ReadAllText(PathOf("item.json"));
        using var document = JsonDocument.Parse(text);
        Assert.Equal(StoreFormat.Current, document.RootElement.GetProperty(StoreFormat.VersionProperty).GetInt32());
        Assert.Equal(StoreFormat.VersionProperty, document.RootElement.EnumerateObject().First().Name);

        // 書いた物は今までどおり読める（版の欄は型に無いので読み飛ばされる）
        Assert.Equal("作り物の衣装", JsonStore.Read<ItemRecord>(PathOf("item.json"))!.Booth.Name);
    }

    /// <summary>配列・辞書は版の欄を持てないので書かない（辞書に足すと、読むときに1件として入ってしまう）</summary>
    [Fact]
    public void 配列と辞書には版を書かない()
    {
        JsonStore.Write(PathOf("list.json"), new List<string> { "a" });
        JsonStore.Write(PathOf("map.json"), new Dictionary<string, int> { ["a"] = 1 });

        Assert.DoesNotContain(StoreFormat.VersionProperty, File.ReadAllText(PathOf("list.json")));
        Assert.DoesNotContain(StoreFormat.VersionProperty, File.ReadAllText(PathOf("map.json")));
        Assert.Equal(["a"], JsonStore.Read<List<string>>(PathOf("list.json"))!);
    }

    /// <summary>中身の無いオブジェクトでも、読める JSON のまま版を足す</summary>
    [Fact]
    public void 空のオブジェクトにも版を足せる()
    {
        var stamped = Encoding.UTF8.GetString(StoreFormat.Stamp(Encoding.UTF8.GetBytes("{}")));

        using var document = JsonDocument.Parse(stamped);
        Assert.Equal(StoreFormat.Current, document.RootElement.GetProperty(StoreFormat.VersionProperty).GetInt32());
    }

    /// <summary>版の欄が無いファイルは v1.0.0 の形（1）。手で直して最初でない所にあっても読む</summary>
    [Theory]
    [InlineData("{\"id\": \"1\"}", 1)]
    [InlineData("{\"id\": \"1\", \"formatVersion\": 3}", 3)]
    [InlineData("{\"nested\": {\"formatVersion\": 9}}", 1)]
    [InlineData("[1, 2]", 1)]
    [InlineData("// コメント\n{\"formatVersion\": 2,}", 2)]
    [InlineData("{ this is not json", 1)]
    public void ファイルの版を読む(string json, int expected)
        => Assert.Equal(expected, StoreFormat.VersionOf(Encoding.UTF8.GetBytes(json)));

    /// <summary>
    /// 新しい版のファイルは読まずに止める（古い版は知らない欄を保てないので、読んで保存すると消える）。
    /// 壊れた JSON とは分け、読み書きの失敗の側に置く
    /// </summary>
    [Fact]
    public void 新しい版のファイルは読まずに止める()
    {
        File.WriteAllText(PathOf("item.json"), $"{{\"formatVersion\": {StoreFormat.Current + 1}, \"id\": \"1000001\"}}");

        var failure = Assert.Throws<FormatTooNewException>(() => JsonStore.Read<ItemRecord>(PathOf("item.json")));
        Assert.Equal(StoreFormat.Current + 1, failure.Version);
        Assert.IsAssignableFrom<IOException>(failure);
    }

    [Fact]
    public async Task 新しい版のファイルは非同期でも読まずに止める()
    {
        File.WriteAllText(PathOf("item.json"), $"{{\"formatVersion\": {StoreFormat.Current + 1}, \"id\": \"1000001\"}}");

        await Assert.ThrowsAsync<FormatTooNewException>(() => JsonStore.ReadAsync<ItemRecord>(PathOf("item.json")));
    }

    /// <summary>v1.0.0 が書いた版の欄の無いファイルは、そのまま読める</summary>
    [Fact]
    public void 版の欄の無いファイルを読める()
    {
        File.WriteAllText(PathOf("item.json"), "{\"id\": \"1000001\", \"booth\": {\"name\": \"作り物\"}, \"local\": {}}");

        Assert.Equal("作り物", JsonStore.Read<ItemRecord>(PathOf("item.json"))!.Booth.Name);
    }

    /// <summary>
    /// 版 1（v1.0.0〜v1.2.0）が書いたファイルは、版 2 へ移し替えて読める。中身は変わらない（1→2 は欄を足しただけ。洗い出し 2026-11-10・D1）。
    /// 移し替えの関数を表に足し忘れると、版 1 のファイルが全部読めなくなる
    /// </summary>
    [Fact]
    public async Task 版1のファイルを中身を変えずに読める()
    {
        var json = "{\"formatVersion\": 1, \"id\": \"1000001\", \"booth\": {\"name\": \"作り物\"}, "
            + "\"local\": {\"memo\": \"覚え書き\", \"avatarBases\": [{\"baseName\": \"素体A\", \"confirmed\": true}], "
            + "\"purchases\": [{\"price\": 500}]}}";
        File.WriteAllText(PathOf("item.json"), json);

        foreach (var item in new[] { JsonStore.Read<ItemRecord>(PathOf("item.json"))!, (await JsonStore.ReadAsync<ItemRecord>(PathOf("item.json")))! })
        {
            Assert.Equal("作り物", item.Booth.Name);
            Assert.Equal("覚え書き", item.Local.Memo);
            Assert.Equal("素体A", Assert.Single(item.Local.AvatarBases).BaseName);
            Assert.Equal(500, Assert.Single(item.Local.Purchases).Price);
        }

        // 書き直すと今の版になる
        JsonStore.Write(PathOf("item.json"), JsonStore.Read<ItemRecord>(PathOf("item.json"))!);
        Assert.Equal(StoreFormat.Current, StoreFormat.VersionOf(File.ReadAllBytes(PathOf("item.json"))));
    }

    /// <summary>版 1 の保存先（印の無い v1.0.0 の物も）は、控えを取ってから開く「古いが読める」になる。v1.2.0 は版 2 の保存先を開かない</summary>
    [Fact]
    public void 版1の保存先は古いが読める()
    {
        Assert.Equal(3, StoreFormat.Current);
        Assert.Equal(StoreFormat.Verdict.Older, StoreFormat.Check(1));
        Assert.Equal(StoreFormat.Verdict.Older, StoreFormat.Check(StoreFormat.StoreVersion(_root)));
    }

    /// <summary>メモ帳で直すと付く UTF-8 の印（BOM）があっても読める（点検26：バイト列で読むようにして読めなくなっていた）</summary>
    [Fact]
    public async Task BOM付きのJSONを読める()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        File.WriteAllBytes(PathOf("item.json"), [.. bom, .. Encoding.UTF8.GetBytes("{\"formatVersion\": 1, \"id\": \"1000001\", \"booth\": {\"name\": \"作り物\"}, \"local\": {}}")]);
        File.WriteAllBytes(PathOf("list.json"), [.. bom, .. Encoding.UTF8.GetBytes("[\"a\"]")]);

        Assert.Equal("作り物", JsonStore.Read<ItemRecord>(PathOf("item.json"))!.Booth.Name);
        Assert.Equal("作り物", (await JsonStore.ReadAsync<ItemRecord>(PathOf("item.json")))!.Booth.Name);
        Assert.Equal(["a"], JsonStore.Read<List<string>>(PathOf("list.json"))!);
    }

    /// <summary>BOM 付きの新しすぎるファイルも、版を読んで止める（BOM で版が読めず 1 とみなすと、止められない）</summary>
    [Fact]
    public void BOM付きでも新しすぎる版を見分ける()
    {
        var json = Encoding.UTF8.GetBytes($"{{\"formatVersion\": {StoreFormat.Current + 1}}}");
        Assert.Equal(StoreFormat.Current + 1, StoreFormat.VersionOf([0xEF, 0xBB, 0xBF, .. json]));
    }

    /// <summary>
    /// 新しいバージョンが書いた unitypackage の控えは、読めないからといって空から書き直さない（点検26：新しいバージョンが入れた中身を消していた）
    /// </summary>
    [Fact]
    public async Task 新しすぎるunitypackageの控えを書き直さない()
    {
        var paths = new AppPaths(_root);
        var store = new UnityPackagePathStore(paths);
        var hash = new string('a', 64);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.UnityPackageFile(hash))!);
        var newer = $"{{\"formatVersion\": {StoreFormat.Current + 1}, \"packages\": {{}}}}";
        File.WriteAllText(paths.UnityPackageFile(hash), newer);

        Assert.True(store.IsTooNew(hash));
        Assert.Null(store.Load(hash));

        store.Add(hash, "a.unitypackage", [new Chmonos.Core.Services.UnityPackageAsset("0123", "Assets/a.prefab")]);
        await store.SaveAsync(hash, new Dictionary<string, IReadOnlyList<Chmonos.Core.Services.UnityPackageAsset>>());

        Assert.Equal(newer, File.ReadAllText(paths.UnityPackageFile(hash)));
    }

    // --- 保存先ごとの版 ---

    [Fact]
    public void 印が無い保存先は版1()
        => Assert.Equal(1, StoreFormat.StoreVersion(_root));

    /// <summary>印が在るのに版を読めなければ、1 とみなさず例外（点検26：新しいバージョンの保存先を止められなくなる）</summary>
    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("{\"writtenBy\": \"1.1.0\"}")]
    [InlineData("{\"formatVersion\": \"2\"}")]
    [InlineData("[]")]
    public void 読めない印は開かない側に倒す(string json)
    {
        File.WriteAllText(PathOf(StoreFormat.MarkerFileName), json);

        Assert.Throws<StoreFormatUnreadableException>(() => StoreFormat.StoreVersion(_root));
    }

    [Fact]
    public void BOM付きの印も読む()
    {
        File.WriteAllBytes(PathOf(StoreFormat.MarkerFileName), [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{\"formatVersion\": 1}")]);

        Assert.Equal(1, StoreFormat.ReadMarker(_root));
    }

    [Fact]
    public void 印を今の版で書く()
    {
        StoreFormat.MarkCurrent(_root, "1.1.0");

        Assert.Equal(StoreFormat.Current, StoreFormat.StoreVersion(_root));
        Assert.Contains("\"writtenBy\": \"1.1.0\"", File.ReadAllText(PathOf(StoreFormat.MarkerFileName)));
    }

    /// <summary>
    /// 読める下限より古いファイルは、黙って読まずに止める（2026-11-28 の点検：下限の確かめを外しても落ちる試験が無かった）。
    /// 今は下限が 1 なので版 0 は手で書いた物しか無いが、下限を上げた日に、古すぎるファイルをそのまま読む壊れ方を防ぐ
    /// </summary>
    [Fact]
    public async Task 読める下限より古い版のファイルは読まずに止める()
    {
        var path = PathOf("old.json");
        File.WriteAllText(path, $$"""{ "{{StoreFormat.VersionProperty}}": {{StoreFormat.OldestReadable - 1}}, "id": "1000001" }""");

        Assert.False(StoreFormat.LeavesAsIs(StoreFormat.OldestReadable - 1));
        Assert.Throws<FormatTooOldException>(() => JsonStore.Read<ItemRecord>(path));
        await Assert.ThrowsAsync<FormatTooOldException>(() => JsonStore.ReadAsync<ItemRecord>(path));
    }

    /// <summary>
    /// 中身を変える段を当てる通り道（2026-11-28 の点検：今の段は全部 null なので、段を飛ばしても順を違えても落ちなかった）。
    /// 作り物の表で、段ごとに自分の版を配列へ足し、古い順に、ファイルの版より後の段だけが当たる事を見る
    /// </summary>
    [Fact]
    public void 中身を変える段はファイルの版より後の段を古い順に全部当てる()
    {
        var steps = Enumerable.Range(StoreFormat.OldestReadable + 1, StoreFormat.Current - StoreFormat.OldestReadable)
            .ToDictionary(version => version, version => (Func<System.Text.Json.Nodes.JsonNode, Type, System.Text.Json.Nodes.JsonNode>?)((node, _) =>
            {
                node["steps"]!.AsArray().Add(version);
                return node;
            }));
        int[] Applied(int from)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse("""{ "steps": [] }""")!;
            return StoreFormat.Migrate(node, from, typeof(ItemRecord), "作り物.json", steps)["steps"]!.AsArray().Select(step => (int)step!).ToArray();
        }

        Assert.Equal(Enumerable.Range(StoreFormat.OldestReadable + 1, StoreFormat.Current - StoreFormat.OldestReadable), Applied(StoreFormat.OldestReadable));
        Assert.Equal([StoreFormat.Current], Applied(StoreFormat.Current - 1));
        Assert.Empty(Applied(StoreFormat.Current));
        Assert.False(StoreFormat.LeavesAsIs(StoreFormat.OldestReadable, steps));
        Assert.True(StoreFormat.LeavesAsIs(StoreFormat.Current, steps));

        // 段を表に足し忘れたら黙って飛ばさず、読めないと分かる
        steps.Remove(StoreFormat.Current);
        Assert.Throws<KeyNotFoundException>(() => Applied(StoreFormat.OldestReadable));
    }

    [Fact]
    public void 保存先の版とアプリの関係を決める()
    {
        Assert.Equal(StoreFormat.Verdict.Same, StoreFormat.Check(StoreFormat.Current));
        Assert.Equal(StoreFormat.Verdict.TooNew, StoreFormat.Check(StoreFormat.Current + 1));
        Assert.Equal(StoreFormat.Verdict.TooOld, StoreFormat.Check(StoreFormat.OldestReadable - 1));
    }

    // --- 控え ---

    /// <summary>JSON だけを同じ相対の場所へ写す。画像・.cache・items/.prev は写さない</summary>
    [Fact]
    public void 控えはJSONだけを同じ場所へ写す()
    {
        File.WriteAllText(PathOf("settings.json"), "{}");
        File.WriteAllText(PathOf("search-bridge.cache"), "x");
        Directory.CreateDirectory(PathOf("items/.prev"));
        File.WriteAllText(PathOf("items/1000001.json"), "{}");
        File.WriteAllText(PathOf("items/.prev/1000001.json"), "{}");
        Directory.CreateDirectory(PathOf("modifications"));
        File.WriteAllText(PathOf("modifications/m1.json"), "{}");
        Directory.CreateDirectory(PathOf("images/1000001"));
        File.WriteAllBytes(PathOf("images/1000001/a.png"), [1, 2, 3]);

        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(9));
        var target = StoreFormat.Backup(_root, 1, now);

        // フォルダ名はその PC の時刻で付ける（使う人が見て分かるように）。試験は走らせる PC の時間帯に左右されないよう、同じ式で見込む
        // （GitHub の環境は UTC で、日本時間の決め打ちでは落ちた。2026-10-08）
        Assert.Equal(Path.Combine(_root, StoreFormat.BackupsDirName, $"v1-{now.LocalDateTime:yyyyMMdd-HHmmss}"), target);
        Assert.True(File.Exists(Path.Combine(target, "settings.json")));
        Assert.True(File.Exists(Path.Combine(target, "items", "1000001.json")));
        Assert.True(File.Exists(Path.Combine(target, "modifications", "m1.json")));
        Assert.False(File.Exists(Path.Combine(target, "search-bridge.cache")));
        Assert.False(Directory.Exists(Path.Combine(target, "items", ".prev")));
        Assert.False(Directory.Exists(Path.Combine(target, "images")));
    }

    /// <summary>
    /// 写すフォルダの一覧から1つ外しても前の試験は落ちなかった（2026-11-27 の点検）。使う人のデータのフォルダを1つずつ置いて、全部写る事を見る。
    /// unityscans は VPM の履歴を持ち、作り直せない（書き出しには入れている）。控えから漏れると、古い版へ戻した時に新しい形のまま残って読めなかった
    /// </summary>
    [Theory]
    [InlineData("items")]
    [InlineData("modifications")]
    [InlineData("unitypackages")]
    [InlineData("zipimages")]
    [InlineData(AppPaths.UnityScansDirName)]
    public void 控えは使う人のデータのフォルダを全部写す(string folder)
    {
        Directory.CreateDirectory(PathOf(folder));
        File.WriteAllText(PathOf($"{folder}/a.json"), "{}");

        var target = StoreFormat.Backup(_root, 1, new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));

        Assert.True(File.Exists(Path.Combine(target, folder, "a.json")));
    }

    /// <summary>控えに入れるフォルダ（JSON を写す）。<c>extensions</c> だけは入れ子の下まで写す</summary>
    private static readonly string[] BackedUpFolders = ["items", "modifications", "unitypackages", "zipimages", AppPaths.UnityScansDirName, "extensions"];

    /// <summary>
    /// 控えに入れないフォルダ（docs/spec/data-format.md「控え」）。画像は形式を持たず、logs は作り直せ、.prev・_broken はアプリの仕組みの一部、
    /// 保管庫は書き直さないので戻す物が無い、format-backups は控えそのもの
    /// </summary>
    private static readonly string[] LeftOutFolders = ["images", "logs", "items/.prev", "items/_broken", AppPaths.NotificationArchiveDirName, StoreFormat.BackupsDirName];

    /// <summary>
    /// 保存先の直下の JSON（AppPaths にある物を全部）と、入れる・入れないフォルダの全部に JSON を置き、写った物を一覧で照らす
    /// （2026-11-28 の点検：直下は settings.json しか見ておらず、直下を settings.json だけにしても落ちなかった）。
    /// api.json（外から使う API のトークン）は控えに写るのが良いかを決めていない（点検の低9・要確認）ので、ここでは置かない
    /// </summary>
    [Fact]
    public void 控えに入る物と入らない物を一覧で照らす()
    {
        var paths = new AppPaths(_root);
        var rootJsons = typeof(AppPaths).GetProperties()
            .Where(property => property.PropertyType == typeof(string) && property.Name.EndsWith("File", StringComparison.Ordinal) && property.Name != nameof(AppPaths.ApiFile))
            .Select(property => (string)property.GetValue(paths)!)
            .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(path) == _root)
            .Append(PathOf(StoreFormat.MarkerFileName))
            .Select(path => Path.GetFileName(path))
            .ToList();
        Assert.Contains("settings.json", rootJsons);
        Assert.Contains("notifications.json", rootJsons);
        foreach (var name in rootJsons)
        {
            File.WriteAllText(PathOf(name), "{}");
        }

        foreach (var folder in BackedUpFolders.Concat(LeftOutFolders))
        {
            Directory.CreateDirectory(PathOf(folder));
            File.WriteAllText(PathOf($"{folder}/a.json"), "{}");
        }

        Directory.CreateDirectory(PathOf("extensions/ns/items"));
        File.WriteAllText(PathOf("extensions/ns/items/1000001.json"), "{}");
        File.WriteAllText(PathOf("items/1000001.h2.html"), "<html></html>");
        File.WriteAllText(PathOf("search-bridge.cache"), "x");

        var target = StoreFormat.Backup(_root, 1, new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero));

        var expected = rootJsons
            .Concat(BackedUpFolders.Select(folder => $"{folder}/a.json"))
            .Append("extensions/ns/items/1000001.json")
            .Order(StringComparer.Ordinal);
        var copied = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(target, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
        Assert.Equal(expected, copied);
    }

    /// <summary>
    /// AppPaths に新しいフォルダを足したら、控えに入れるか入れないかを上の一覧のどちらかに書く（入れ忘れに気付くため）。
    /// 一覧のフォルダの下にある物（images/_avatars など）は、そのフォルダの扱いに従う
    /// </summary>
    [Fact]
    public void AppPathsのフォルダは控えの入れる入れないのどちらかに載っている()
    {
        var paths = new AppPaths(_root);
        var folders = typeof(AppPaths).GetProperties()
            .Where(property => property.PropertyType == typeof(string) && (property.Name.EndsWith("Dir", StringComparison.Ordinal) || property.Name.EndsWith("Root", StringComparison.Ordinal)) && property.Name != nameof(AppPaths.Root))
            .Select(property => Path.GetRelativePath(_root, (string)property.GetValue(paths)!).Replace('\\', '/'))
            .Append(AppPaths.UnityScansDirName)
            .Append(Path.GetRelativePath(_root, Path.GetDirectoryName(paths.LogFile)!).Replace('\\', '/'))
            .ToList();
        Assert.Contains("items", folders);

        var listed = BackedUpFolders.Concat(LeftOutFolders).ToList();
        Assert.All(folders, folder => Assert.True(
            listed.Any(known => folder == known || folder.StartsWith(known + "/", StringComparison.Ordinal)),
            $"{folder} が控えの一覧のどちらにも無い"));
    }

    /// <summary>
    /// 控えの範囲を spec（docs/spec/data-format.md「控え」）にも書いてある事を照らす（2026-11-28：unityscans が spec の写す一覧にも入れない一覧にも無かった）。
    /// 文を解析して一覧を作るのではなく、名前が節に出るかだけを見る（文の書き換えで落ちないように）
    /// </summary>
    [Fact]
    public void 控えの範囲はspecの控えの節に書いてある()
    {
        var section = SpecSection("data-format.md", "## 控え");

        Assert.All(BackedUpFolders, folder => Assert.Contains($"`{folder}/`", section, StringComparison.Ordinal));
        Assert.All(["`logs`", "`items/.prev`", "`items/_broken`", $"`{AppPaths.NotificationArchiveDirName}/`", "画像"],
            name => Assert.Contains(name, section, StringComparison.Ordinal));
    }

    private static string SpecSection(string file, string heading)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "docs", "spec", file);
            if (File.Exists(path))
            {
                // git の設定で CRLF に変えて取り出されても見出しを探せるように揃える
                var text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
                var start = text.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
                Assert.True(start >= 0, $"{file} に「{heading}」の節が無い");
                var end = text.IndexOf("\n## ", start + heading.Length + 2, StringComparison.Ordinal);
                return end < 0 ? text[start..] : text[start..end];
            }
        }

        throw new FileNotFoundException($"docs/spec/{file} が見つからない");
    }

    [Fact]
    public void 控えの数と大きさを数えて消せる()
    {
        Assert.Equal((0, 0L), StoreFormat.BackupUsage(_root));

        File.WriteAllText(PathOf("settings.json"), "{\"a\": 1}");
        StoreFormat.Backup(_root, 1, new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        StoreFormat.Backup(_root, 1, new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));

        var (files, bytes) = StoreFormat.BackupUsage(_root);
        Assert.Equal(2, files);
        Assert.Equal(2 * new FileInfo(PathOf("settings.json")).Length, bytes);

        StoreFormat.DeleteBackups(_root);
        Assert.Equal((0, 0L), StoreFormat.BackupUsage(_root));
        Assert.True(File.Exists(PathOf("settings.json")));
    }
}
