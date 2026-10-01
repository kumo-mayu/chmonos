using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 「一度も取れていない」を表せること。
/// BOOTHから取れない商品を手元に置けるようにするための土台。
/// </summary>
public class BoothBlockFetchedAtTests : IDisposable
{
    private readonly string _root;

    public BoothBlockFetchedAtTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-fa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>取得した日時に嘘を書かずに、商品を作れること。</summary>
    [Fact]
    public void CanBuildABlockThatWasNeverFetched()
    {
        var booth = new BoothBlock();

        Assert.Null(booth.FetchedAt);
        Assert.False(booth.WasEverFetched);
    }

    [Fact]
    public void KnowsWhenItWasFetched()
    {
        var booth = new BoothBlock { FetchedAt = DateTimeOffset.Now };

        Assert.True(booth.WasEverFetched);
    }

    /// <summary>計算で出るものはJSONに書かない。書くと手で直せる値だと誤解される。</summary>
    [Fact]
    public void DoesNotWriteTheDerivedFlag()
    {
        var path = Path.Combine(_root, "item.json");
        JsonStore.Write(path, new ItemRecord
        {
            Id = "111",
            Booth = new BoothBlock { Name = "手で入れた商品" },
            Local = new LocalBlock(),
        });

        var text = File.ReadAllText(path);

        Assert.DoesNotContain("wasEverFetched", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fetchedAt", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>一度書いたものを読み直しても、null のままであること。</summary>
    [Fact]
    public void SurvivesASaveAndLoad()
    {
        var path = Path.Combine(_root, "item.json");
        JsonStore.Write(path, new ItemRecord
        {
            Id = "111",
            Booth = new BoothBlock { Name = "手で入れた商品" },
            Local = new LocalBlock(),
        });

        var loaded = JsonStore.Read<ItemRecord>(path);

        Assert.NotNull(loaded);
        Assert.False(loaded!.Booth.WasEverFetched);
    }
}
