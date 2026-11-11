using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

/// <summary>
/// 「手元のファイル」の表示順（設定の fileOrder・ユーザ指示 2026-11-11）。名前はどれも作り物。
/// </summary>
public class LocalFileOrderTests
{
    private static int Version(string x, string y) => LocalFileOrder.Compare(x, y, versions: true);

    private static int Plain(string x, string y) => LocalFileOrder.Compare(x, y, versions: false);

    private static LocalFileRecord File(string name, bool detached = false) => new()
    {
        Hash = name.GetHashCode().ToString("X8"),
        Paths = [@"D:\fake\" + name],
        SizeBytes = 1,
        Detached = detached,
    };

    private static string[] Names(IEnumerable<LocalFileRecord> files) => [.. files.Select(file => LocalFileOrder.NameOf(file) ?? "(無し)")];

    // ---- 版の数字の比べ方 ----

    [Theory]
    [InlineData("Sample_v1.2.zip", "Sample_v1.10.zip")]      // 桁の違い
    [InlineData("Sample_Ver2.9.0.zip", "Sample_Ver2.19.0.zip")]
    [InlineData("Sample_v1.10.zip", "Sample_v2.1.zip")]      // 区切りを落とすと 110 と 21 で逆になる
    [InlineData("Sample_v1.9.1.zip", "Sample_v1.10.zip")]
    [InlineData("Sample_v2.zip", "Sample_v10.zip")]          // _v
    [InlineData("Sample v9.unitypackage", "Sample v10.unitypackage")]
    [InlineData("Sample_v1.2.zip", "Sample_v1.2.1.zip")]     // 拡張子の字で決めない
    [InlineData("Sample.zip", "Sample_v2.zip")]
    [InlineData("Ｓａｍｐｌｅ_v2.zip", "sample_v10.zip")]     // 全角・大小文字は寄せる
    public void バージョンを考えた名前順は_名前の中の数を数として比べる(string earlier, string later)
    {
        Assert.True(Version(earlier, later) < 0, $"{earlier} が {later} より前に来ていません");
        Assert.True(Version(later, earlier) > 0);
    }

    [Fact]
    public void 名前順は_数字も1字ずつ比べ_バージョンを考えた名前順と並びが違う()
    {
        Assert.True(Plain("file10.zip", "file2.zip") < 0);
        Assert.True(Version("file10.zip", "file2.zip") > 0);
    }

    [Fact]
    public void 数字の無い名前は_大小文字とかなを寄せて比べる()
    {
        Assert.True(Plain("あいう.zip", "カキク.zip") < 0);
        Assert.True(Plain("Apple.zip", "banana.zip") < 0);
        Assert.True(Version("Apple.zip", "banana.zip") < 0);
        Assert.True(Version("アイウ.zip", "かきく.zip") < 0);
    }

    [Fact]
    public void 同じ名前は0_同じ数で書き方だけ違う名前は毎回同じ順()
    {
        Assert.Equal(0, Version("Sample_v1.zip", "Sample_v1.zip"));
        Assert.Equal(0, Plain("Sample_v1.zip", "Sample_v1.zip"));

        // v01 と v1 は同じ数。0 にせず元の名前で決め、どちらから比べても同じ答え
        var forward = Version("Sample_v01.zip", "Sample_v1.zip");
        Assert.NotEqual(0, forward);
        Assert.Equal(-Math.Sign(forward), Math.Sign(Version("Sample_v1.zip", "Sample_v01.zip")));
    }

    // ---- 行の並べ方 ----

    [Fact]
    public void 追加した順は記録の順のまま_外したファイルだけ後ろ()
    {
        var files = new[] { File("c_v10.zip"), File("a.zip", detached: true), File("c_v2.zip"), File("b.zip") };

        Assert.Equal(["c_v10.zip", "c_v2.zip", "b.zip", "a.zip"], Names(LocalFileOrder.Sort(files, FileOrder.Added)));
    }

    [Fact]
    public void 名前の順でも外したファイルは後ろ_名前の無いファイルは組の後ろ()
    {
        var missing = new LocalFileRecord { Hash = "MISSING", Paths = [], SizeBytes = 1 };
        var files = new[] { File("c_v10.zip"), missing, File("a.zip", detached: true), File("c_v2.zip"), File("b.zip") };

        Assert.Equal(["b.zip", "c_v10.zip", "c_v2.zip", "(無し)", "a.zip"], Names(LocalFileOrder.Sort(files, FileOrder.Name)));
        Assert.Equal(["b.zip", "c_v2.zip", "c_v10.zip", "(無し)", "a.zip"], Names(LocalFileOrder.Sort(files, FileOrder.NameWithVersion)));
    }

    [Fact]
    public void 古い版は置き換わった場所の名前で並ぶ()
    {
        var old = new LocalFileRecord
        {
            Hash = "OLD",
            Paths = [],
            SizeBytes = 1,
            Replaced = new ReplacedVersion(@"D:\fake\Sample_v1.zip", DateTimeOffset.UnixEpoch),
        };

        Assert.Equal("Sample_v1.zip", LocalFileOrder.NameOf(old));
        Assert.Equal(["Sample_v1.zip", "Sample_v2.zip"], Names(LocalFileOrder.Sort([File("Sample_v2.zip"), old], FileOrder.NameWithVersion)));
    }

    // ---- 設定 ----

    [Fact]
    public void 欄の無い古いsettingsは追加した順で読み_選んだ順は名前で書いて読み戻せる()
    {
        Assert.Equal(FileOrder.Added, JsonSerializer.Deserialize<AppSettings>("""{ "cardWidth": 228 }""", JsonStore.Options)!.FileOrder);

        var json = JsonSerializer.Serialize(new AppSettings { FileOrder = FileOrder.NameWithVersion }, JsonStore.Options);
        Assert.Contains("\"fileOrder\": \"nameWithVersion\"", json);
        Assert.Equal(FileOrder.NameWithVersion, JsonSerializer.Deserialize<AppSettings>(json, JsonStore.Options)!.FileOrder);
    }
}
