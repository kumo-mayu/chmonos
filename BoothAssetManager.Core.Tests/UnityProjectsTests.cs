using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

public sealed class UnityProjectsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "unity-projects-" + Guid.NewGuid().ToString("N")[..8]);

    public UnityProjectsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>手元の実ファイルと同じ形。<c>data</c> の下にパスをキーにして並ぶ。</summary>
    private const string HubJson = """
        {
          "schema_version": "v1",
          "data": {
            "D:\\work\\Unity\\ring_test": {
              "title": "ring_test",
              "lastModified": 1775208314042,
              "path": "D:\\work\\Unity\\ring_test",
              "version": "2022.3.22f1"
            },
            "C:\\Users\\me\\test_ring": {
              "title": "test_ring",
              "path": "C:\\Users\\me\\test_ring",
              "version": "2022.3.22f1"
            }
          }
        }
        """;

    private const string VccJson = """
        {
          "pathToUnityExe": "",
          "userProjects": [
            "D:\\work\\VRChatProjects\\kip01",
            "D:\\work\\Unity\\ring_test"
          ]
        }
        """;

    [Fact]
    public void HubのJSONからパスを引ける()
    {
        var paths = UnityProjects.PathsFromHubJson(HubJson);

        Assert.Equal(2, paths.Count);
        Assert.Contains(@"D:\work\Unity\ring_test", paths);
        Assert.Contains(@"C:\Users\me\test_ring", paths);
    }

    [Fact]
    public void HubのJSONにpathが無ければキーを使う()
    {
        var paths = UnityProjects.PathsFromHubJson(
            """{"data":{"D:\\work\\Unity\\only_key":{"title":"only_key"}}}""");

        Assert.Equal([@"D:\work\Unity\only_key"], paths);
    }

    [Fact]
    public void 形が違うJSONでも落ちずに空を返す()
    {
        Assert.Empty(UnityProjects.PathsFromHubJson("{}"));
        Assert.Empty(UnityProjects.PathsFromHubJson("""{"data":[]}"""));
        Assert.Empty(UnityProjects.PathsFromHubJson("これはJSONではない"));
        Assert.Empty(UnityProjects.PathsFromVccSettings("{}"));
        Assert.Empty(UnityProjects.PathsFromVccSettings("""{"userProjects":"1件だけ"}"""));
        Assert.Empty(UnityProjects.PathsFromVccSettings("<xml/>"));
    }

    [Fact]
    public void VCCのJSONからパスを引ける()
    {
        var paths = UnityProjects.PathsFromVccSettings(VccJson);

        Assert.Equal([@"D:\work\VRChatProjects\kip01", @"D:\work\Unity\ring_test"], paths);
    }

    [Fact]
    public void バージョンはハッシュ無しの行から読む()
    {
        var version = UnityProjects.VersionFromProjectVersionText(
            """
            m_EditorVersion: 2022.3.22f1
            m_EditorVersionWithRevision: 2022.3.22f1 (887be4894c44)
            """);

        Assert.Equal("2022.3.22f1", version);
    }

    [Fact]
    public void バージョンが書かれていなければnull()
    {
        Assert.Null(UnityProjects.VersionFromProjectVersionText(string.Empty));
        Assert.Null(UnityProjects.VersionFromProjectVersionText("m_EditorVersion:"));
        Assert.Null(UnityProjects.VersionFromProjectVersionText("m_SomethingElse: 2022.3.22f1"));
    }

    private string MakeProject(string name, string? version = "2022.3.22f1", bool locked = false)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.Combine(path, "Assets"));
        Directory.CreateDirectory(Path.Combine(path, "ProjectSettings"));

        if (version is not null)
        {
            File.WriteAllText(
                Path.Combine(path, "ProjectSettings", "ProjectVersion.txt"),
                $"m_EditorVersion: {version}\n");
        }

        if (locked)
        {
            Directory.CreateDirectory(Path.Combine(path, "Temp"));
            File.WriteAllText(Path.Combine(path, "Temp", "UnityLockfile"), string.Empty);
        }

        return path;
    }

    [Fact]
    public void 実在するプロジェクトはバージョンまで読める()
    {
        var path = MakeProject("real");

        var candidate = UnityProjects.Describe(path, UnityProjectSource.Hub);

        Assert.True(candidate.Exists);
        Assert.Equal("real", candidate.Name);
        Assert.Equal(_dir, candidate.Folder);
        Assert.Equal("2022.3.22f1", candidate.Version);
        Assert.False(candidate.IsOpen);
        Assert.NotNull(candidate.LastWrite);
    }

    [Fact]
    public void ロックファイルがあれば開いていると見る()
    {
        var path = MakeProject("opened", locked: true);

        Assert.True(UnityProjects.IsProjectOpen(path));
        Assert.True(UnityProjects.Describe(path, UnityProjectSource.Vcc).IsOpen);
    }

    [Fact]
    public void 消えていても候補から外さない()
    {
        var candidate = UnityProjects.Describe(
            Path.Combine(_dir, "gone"), UnityProjectSource.Vcc);

        Assert.False(candidate.Exists);
        Assert.Equal("gone", candidate.Name);
        Assert.Null(candidate.Version);
        Assert.False(candidate.IsOpen);
    }

    [Fact]
    public void バージョンが読めなくても実在は伝える()
    {
        var path = MakeProject("noversion", version: null);

        var candidate = UnityProjects.Describe(path, UnityProjectSource.Hub);

        Assert.True(candidate.Exists);
        Assert.Null(candidate.Version);
    }

    [Fact]
    public void 体裁を見て判定する()
    {
        Assert.True(UnityProjects.LooksLikeProject(MakeProject("shaped")));

        var bare = Path.Combine(_dir, "bare");
        Directory.CreateDirectory(bare);
        Assert.False(UnityProjects.LooksLikeProject(bare));
    }

    /// <summary>Hubの一覧を書く。パスの区切りは <c>JsonSerializer</c> に逃がす。</summary>
    private string WriteHubJson(params string[] paths)
    {
        var entries = paths.Select((path, index) =>
            $"\"p{index}\":{{\"path\":{System.Text.Json.JsonSerializer.Serialize(path)}}}");

        var file = Path.Combine(_dir, "hub.json");
        File.WriteAllText(file, $"{{\"data\":{{{string.Join(",", entries)}}}}}");
        return file;
    }

    private string WriteVccJson(params string[] paths)
    {
        var entries = paths.Select(path => System.Text.Json.JsonSerializer.Serialize(path));

        var file = Path.Combine(_dir, "vcc.json");
        File.WriteAllText(file, $"{{\"userProjects\":[{string.Join(",", entries)}]}}");
        return file;
    }

    [Fact]
    public void 両方に載っているものは1件にまとめる()
    {
        var shared = MakeProject("shared");
        var hubOnly = MakeProject("hubonly");

        var found = UnityProjects.Discover(
            WriteHubJson(shared, hubOnly),
            WriteVccJson(shared));

        Assert.Equal(2, found.Count);
        Assert.Equal(
            UnityProjectSource.Hub | UnityProjectSource.Vcc,
            found.Single(candidate => candidate.Name == "shared").Source);
        Assert.Equal(
            UnityProjectSource.Hub,
            found.Single(candidate => candidate.Name == "hubonly").Source);
    }

    [Fact]
    public void 末尾の区切りが違っても同じものとして扱う()
    {
        var path = MakeProject("trailing");

        var found = UnityProjects.Discover(
            WriteHubJson(path),
            WriteVccJson(path + Path.DirectorySeparatorChar));

        Assert.Single(found);
        Assert.Equal(UnityProjectSource.Hub | UnityProjectSource.Vcc, found[0].Source);
    }

    [Fact]
    public void 開いているものを先に出す()
    {
        var closed = MakeProject("closed");
        var opened = MakeProject("opened", locked: true);

        var found = UnityProjects.Discover(
            WriteHubJson(closed, opened),
            Path.Combine(_dir, "no-vcc.json"));

        Assert.Equal("opened", found[0].Name);
        Assert.Equal("closed", found[1].Name);
    }

    [Fact]
    public void 一覧が無くても落ちない()
    {
        Assert.Empty(UnityProjects.Discover(
            Path.Combine(_dir, "nothing.json"),
            Path.Combine(_dir, "nothing-either.json")));
    }
}
