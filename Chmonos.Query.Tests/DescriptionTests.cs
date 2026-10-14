using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Query.Tests;

/// <summary>説明文の答え（<c>GET /v1/items/{id}/description</c>。docs/spec/local-api-shapes.md「説明文」）。</summary>
public sealed class DescriptionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-query-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    /// <summary>
    /// 写しは説明文を落としている（アプリと控えを共有しないとき）が、答えは記録のファイルから読んだ見出しごとの文と、
    /// 保存先の h2.html そのままを返す（作り物の文。別の変換を挟まない）
    /// </summary>
    [Fact]
    public async Task 写しが落とした説明文も_ファイルから読んで返す()
    {
        var store = new DataStore(new AppPaths(_root));
        await store.Items.SaveAsync(new ItemRecord
        {
            Id = "9500001",
            Booth = new BoothBlock
            {
                Name = "作り物の衣装",
                Description = "作り物の短い説明",
                H2Sections =
                [
                    new H2Section { Heading = "◆対応アバター◆", Text = "作り物のアバター" },
                    new H2Section { Heading = "利用規約", Text = "作り物の規約の文" },
                ],
            },
        });
        const string html = "<h2>◆対応アバター◆</h2><p>作り物のアバター</p><h2>利用規約</h2><p>作り物の規約の文</p>";
        await store.Items.SaveDescriptionHtmlAsync("9500001", html);
        await store.Items.SaveAsync(new ItemRecord { Id = "9500002", Booth = new BoothBlock { Name = "説明の無い作り物" } });

        var snapshot = await QuerySnapshot.LoadAsync(store);
        Assert.False(snapshot.SharesItemCache);
        Assert.Empty(snapshot.ItemsById["9500001"].Booth.H2Sections);

        var answer = await Descriptions.OfAsync(snapshot.ItemsById["9500001"], snapshot);
        Assert.Equal("作り物の短い説明", answer.Summary);
        Assert.Equal([("◆対応アバター◆", "作り物のアバター"), ("利用規約", "作り物の規約の文")], answer.Sections.Select(section => (section.Heading, section.Text)));
        Assert.Equal(html, answer.Html);

        // 説明が無い商品は誤りにせず、空で返す（「無い」と「読めない」を分ける）
        var empty = await Descriptions.OfAsync(snapshot.ItemsById["9500002"], snapshot);
        Assert.Null(empty.Summary);
        Assert.Empty(empty.Sections);
        Assert.Null(empty.Html);

        var json = JsonSerializer.Serialize(empty, QueryJson.Context.ItemDescription);
        Assert.Equal("""{"id":"9500002","summary":null,"sections":[],"html":null}""", json);
    }
}
