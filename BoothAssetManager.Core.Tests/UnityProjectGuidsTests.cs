using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>プロジェクトの「GUID → 今のパス」の表（2026-09-29）。ディスクは差し替える。</summary>
public sealed class UnityProjectGuidsTests
{
    private const string GuidA = "0123456789abcdef0123456789abcdef";
    private const string GuidB = "fedcba9876543210fedcba9876543210";
    private static readonly DateTime Early = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void metaの頭の行からGUIDを読む()
    {
        Assert.Equal(GuidA, UnityProjectGuids.ParseGuid(["fileFormatVersion: 2", $"guid: {GuidA}", "folderAsset: yes"]));
    }

    [Fact]
    public void GUIDは小文字にそろえる()
        => Assert.Equal(GuidA, UnityProjectGuids.ParseGuid(["fileFormatVersion: 2", $"guid: {GuidA.ToUpperInvariant()}"]));

    [Theory]
    [InlineData("guid: 0123")]
    [InlineData("guid: zz23456789abcdef0123456789abcdef")]
    [InlineData("fileFormatVersion: 2")]
    public void GUIDの形でなければ読まない(string line)
        => Assert.Null(UnityProjectGuids.ParseGuid(["fileFormatVersion: 2", line]));

    [Fact]
    public void 頭の数行より後のGUIDは見ない()
    {
        // 本体の設定の中に同じ形の行がある（参照先の guid）。頭のものだけがそのアセットの GUID
        var lines = new[] { "fileFormatVersion: 2", "a: 1", "b: 1", "c: 1", "d: 1", $"guid: {GuidA}" };

        Assert.Null(UnityProjectGuids.ParseGuid(lines));
    }

    /// <summary>差し替えたディスク。読んだ .meta の数を数える。</summary>
    private sealed class FakeProject
    {
        public Dictionary<string, (DateTime Written, string? Guid)> Metas { get; } = new(StringComparer.OrdinalIgnoreCase);
        private int _reads;

        public int Reads => _reads;

        // 読むのは並べて行うので、数えるのも取り合わないように
        public UnityProjectGuids Table() => new(
            () => Metas.Select(pair => new UnityMetaFile(pair.Key, pair.Value.Written)).ToList(),
            path =>
            {
                Interlocked.Increment(ref _reads);
                return Metas[path].Guid;
            });
    }

    [Fact]
    public void metaからアセットのパスを引く表を作る()
    {
        var project = new FakeProject();
        project.Metas["Assets/Mine/Sweater.fbx.meta"] = (Early, GuidA);
        project.Metas["Packages/com.x/package.json.meta"] = (Early, GuidB);

        var table = project.Table().Current();

        Assert.Equal("Assets/Mine/Sweater.fbx", table[GuidA]);
        Assert.Equal("Packages/com.x/package.json", table[GuidB]);
    }

    [Fact]
    public void 二回目は更新時刻が変わったmetaだけ読み直す()
    {
        var project = new FakeProject();
        project.Metas["Assets/A/a.fbx.meta"] = (Early, GuidA);
        project.Metas["Assets/A/b.fbx.meta"] = (Early, GuidB);
        var guids = project.Table();
        guids.Current();
        Assert.Equal(2, project.Reads);

        guids.Current();
        Assert.Equal(2, project.Reads);

        // 利用者がフォルダを移すと .meta は新しい場所に現れる（移した後の更新時刻は同じこともあるが、場所が新しいので読む）
        project.Metas.Remove("Assets/A/a.fbx.meta");
        project.Metas["Assets/Moved/a.fbx.meta"] = (Early, GuidA);
        var table = guids.Current();

        Assert.Equal(3, project.Reads);
        Assert.Equal("Assets/Moved/a.fbx", table[GuidA]);
        Assert.Equal("Assets/A/b.fbx", table[GuidB]);
    }

    [Fact]
    public void 同じGUIDが二か所にあれば表に入れない()
    {
        // Unity を閉じたままフォルダを写すと、同じ GUID の .meta が2つになる。どちらか言い切れない
        var project = new FakeProject();
        project.Metas["Assets/A/a.fbx.meta"] = (Early, GuidA);
        project.Metas["Assets/A copy/a.fbx.meta"] = (Early, GuidA);
        project.Metas["Assets/B/b.fbx.meta"] = (Early, GuidB);

        var table = project.Table().Current();

        Assert.False(table.ContainsKey(GuidA));
        Assert.True(table.ContainsKey(GuidB));
    }

    [Fact]
    public void GUIDを読めなかったmetaは飛ばす()
    {
        var project = new FakeProject();
        project.Metas["Assets/A/a.fbx.meta"] = (Early, null);

        Assert.Empty(project.Table().Current());
    }
}
