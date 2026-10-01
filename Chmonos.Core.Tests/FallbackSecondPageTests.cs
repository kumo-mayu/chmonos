using Chmonos.Core.Booth;
using Chmonos.Core.Resolution;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 引き直しでも裏付けが出ず、最初の1語の検索が1ページ埋まっていたら、その2ページ目も見る（research §19）。
/// 「商品名_アバター名」のファイルは AND の検索が外れ、最初の1語だけだと同じ語の商品で60件が埋まる。
/// 通信は偽物で置き換える。商品名は作り物。
/// </summary>
public sealed class FallbackSecondPageTests
{
    private const string File = "Zorvexa_Quillomb.zip";

    [Fact]
    public async Task LooksAtTheSecondPageOnlyWhenAsked()
    {
        var client = FirstWordFillsAPage(secondPage: [("900", "Zorvexa Ribbon")]);

        var off = await new FallbackResolver(client).ProposeAsync(File);
        Assert.DoesNotContain(off.Candidates, candidate => candidate.ItemId == "900");
        Assert.DoesNotContain(2, client.Pages);

        var on = await new FallbackResolver(client) { SearchSecondPage = true }.ProposeAsync(File);
        var found = Assert.Single(on.Candidates, candidate => candidate.ItemId == "900");

        // 2ページ目の先頭は検索の1位ではない
        Assert.DoesNotContain("検索結果の1位", found.Reasons);
        Assert.Contains(2, client.Pages);
    }

    [Fact]
    public async Task DoesNotLookWhenACandidateHasEveryWord()
    {
        // 元の検索で、検索語の語を全部名前に持つ商品が出ていれば、検索は当たっている
        var client = FirstWordFillsAPage(secondPage: [("900", "Zorvexa Ribbon")]);
        client.Results["Zorvexa Quillomb"] = [("800", "Zorvexa Dress for Quillomb")];

        var proposal = await new FallbackResolver(client) { SearchSecondPage = true }.ProposeAsync(File);

        Assert.Equal("800", proposal.Candidates[0].ItemId);
        Assert.DoesNotContain(2, client.Pages);
    }

    [Fact]
    public async Task DoesNotLookWhenTheFirstPageIsNotFull()
    {
        var client = new PagedClient();
        client.Results["Zorvexa"] = [.. Enumerable.Range(1, 59).Select(i => ($"{100 + i}", $"Zorvexa Skirt {i}"))];
        client.SecondPages["Zorvexa"] = [("900", "Zorvexa Ribbon")];

        await new FallbackResolver(client) { SearchSecondPage = true }.ProposeAsync(File);

        Assert.DoesNotContain(2, client.Pages);
    }

    private static PagedClient FirstWordFillsAPage((string Id, string Name)[] secondPage)
    {
        var client = new PagedClient();
        client.Results["Zorvexa"] = [.. Enumerable.Range(1, 60).Select(i => ($"{100 + i}", $"Zorvexa Skirt {i}"))];
        client.SecondPages["Zorvexa"] = secondPage;
        return client;
    }

    private sealed class PagedClient : IBoothClient
    {
        public Dictionary<string, (string Id, string Name)[]> Results { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, (string Id, string Name)[]> SecondPages { get; } = new(StringComparer.Ordinal);

        public List<int> Pages { get; } = [];

        public Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => SearchAsync(query, 1, cancellationToken);

        public Task<BoothFetchResult<string>> SearchAsync(string query, int page, CancellationToken cancellationToken = default)
        {
            Pages.Add(page);
            var source = page <= 1 ? Results : SecondPages;
            return Task.FromResult(BoothFetchResult<string>.Success(string.Concat(
                (source.TryGetValue(query, out var cards) ? cards : []).Select(card =>
                    $"""<li class="item-card l-card" data-product-id="{card.Id}" data-product-name="{card.Name}" data-product-brand="shop{card.Id}">"""))));
        }

        public Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default)
        {
            var name = Results.Values.Concat(SecondPages.Values).SelectMany(cards => cards).First(card => card.Id == itemId).Name;
            return Task.FromResult(BoothFetchResult<string>.Success($$"""
                { "id": {{itemId}}, "name": "{{name}}", "url": "https://booth.pm/ja/items/{{itemId}}",
                  "images": [], "variations": [] }
                """));
        }

        public Task<BoothFetchResult<string>> GetItemHtmlAsync(string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<byte[]>> GetBinaryAsync(string url, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<string>> GetTextUntilAsync(
            string url,
            Func<string, bool> found,
            int maxBytes = 262144,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public int CurrentIntervalMs => 0;

        public bool IsThrottled => false;

#pragma warning disable CS0067
        public event Action<BoothActivity>? ActivityChanged;
#pragma warning restore CS0067
    }
}
