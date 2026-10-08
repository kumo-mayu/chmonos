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

    // --- 保存先ごとの版 ---

    [Fact]
    public void 印が無い保存先は版1()
        => Assert.Equal(1, StoreFormat.StoreVersion(_root));

    [Fact]
    public void 印を今の版で書く()
    {
        StoreFormat.MarkCurrent(_root, "1.1.0");

        Assert.Equal(StoreFormat.Current, StoreFormat.StoreVersion(_root));
        Assert.Contains("\"writtenBy\": \"1.1.0\"", File.ReadAllText(PathOf(StoreFormat.MarkerFileName)));
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

        var target = StoreFormat.Backup(_root, 1, new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(9)));

        Assert.Equal(Path.Combine(_root, StoreFormat.BackupsDirName, "v1-20261008-120000"), target);
        Assert.True(File.Exists(Path.Combine(target, "settings.json")));
        Assert.True(File.Exists(Path.Combine(target, "items", "1000001.json")));
        Assert.True(File.Exists(Path.Combine(target, "modifications", "m1.json")));
        Assert.False(File.Exists(Path.Combine(target, "search-bridge.cache")));
        Assert.False(Directory.Exists(Path.Combine(target, "items", ".prev")));
        Assert.False(Directory.Exists(Path.Combine(target, "images")));
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
