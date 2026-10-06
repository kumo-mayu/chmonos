using Chmonos.Core.Booth;
using Chmonos.Core.Resolution;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 「IDを変える」の自動検索（ユーザ判断 2026-10-06）。前の商品名で1回、足りなければ手元のファイルの名前で3本まで。
/// 通信は作り物（本物の BOOTH へは行かない）。
/// </summary>
public sealed class FallbackReplacementTests
{
    private const string FromId = "9900001";

    [Fact]
    public async Task FindsTheRelistedItemByThePreviousName()
    {
        var booth = new FakeBooth();
        booth.Search("ふわふわパーカー", ("9900002", "ふわふわパーカー", "shopa"));
        var resolver = new FallbackResolver(booth);

        var proposal = await resolver.FindReplacementAsync(
            FromId, "【VRChat向け】ふわふわパーカー", "shopa", [@"C:\dl\Parka_v1.zip"]);

        var candidate = Assert.Single(proposal.Candidates);
        Assert.Equal("9900002", candidate.ItemId);
        Assert.Equal("前の商品名", candidate.FoundBy);

        // 名前で見つかったので、ファイルの名前では引かない
        Assert.Equal(1, proposal.Searches);
        Assert.Equal(["ふわふわパーカー"], booth.Queries);
    }

    [Fact]
    public async Task FallsBackToTheFileNamesWhenTheNameDoesNotHit()
    {
        var booth = new FakeBooth();
        booth.Search("ふわふわパーカー", ("9900010", "まったく別の帽子", "other"));
        booth.Search("Winter Coat", ("9900003", "Winter Coat 冬のコート", "shopb"));
        var resolver = new FallbackResolver(booth);

        var proposal = await resolver.FindReplacementAsync(
            FromId, "ふわふわパーカー", "shopa", [@"C:\dl\readme.txt", @"C:\dl\Winter_Coat.zip"]);

        // zip を先に引き、当たったので3本目へは進まない
        Assert.Equal(["ふわふわパーカー", "Winter Coat"], booth.Queries);
        var top = proposal.Candidates[0];
        Assert.Equal("9900003", top.ItemId);
        Assert.Equal("「Winter_Coat.zip」の名前", top.FoundBy);
    }

    [Fact]
    public async Task MarksTheCandidatesFromTheSameShop()
    {
        var booth = new FakeBooth();
        booth.Search("ふわふわパーカー",
            ("9900004", "ふわふわパーカー 新版", "otherShop"),
            ("9900005", "ふわふわパーカー", "ShopA"));
        var resolver = new FallbackResolver(booth);

        var proposal = await resolver.FindReplacementAsync(FromId, "ふわふわパーカー", "shopa", []);

        Assert.True(proposal.Candidates.Single(candidate => candidate.ItemId == "9900005").IsSameShop);
        Assert.False(proposal.Candidates.Single(candidate => candidate.ItemId == "9900004").IsSameShop);
    }

    [Fact]
    public async Task NeverOffersTheCurrentId()
    {
        var booth = new FakeBooth();
        booth.Search("ふわふわパーカー", (FromId, "ふわふわパーカー", "shopa"), ("9900006", "ふわふわパーカー", "shopa"));
        var resolver = new FallbackResolver(booth);

        var proposal = await resolver.FindReplacementAsync(FromId, "ふわふわパーカー", "shopa", []);

        Assert.DoesNotContain(proposal.Candidates, candidate => candidate.ItemId == FromId);
        Assert.Contains(proposal.Candidates, candidate => candidate.ItemId == "9900006");

        // 今のIDの商品JSONも取りに行かない（問い合わせを無駄にしない）
        Assert.DoesNotContain(FromId, booth.JsonRequests);
    }

    /// <summary>外れると困る所：どれだけ手掛かりがあっても、検索は4回まで。</summary>
    [Fact]
    public async Task SearchesAtMostFourTimes()
    {
        var booth = new FakeBooth();
        var resolver = new FallbackResolver(booth);

        var proposal = await resolver.FindReplacementAsync(
            FromId,
            "ふわふわパーカー",
            "shopa",
            [@"C:\dl\Alpha.zip", @"C:\dl\Bravo.zip", @"C:\dl\Charlie.zip", @"C:\dl\Delta.zip", @"C:\dl\Echo.unitypackage"]);

        Assert.Equal(FallbackResolver.MaxReplacementSearches, booth.Queries.Count);
        Assert.Equal(4, proposal.Searches);
        Assert.Empty(proposal.Candidates);
        Assert.False(proposal.BoothUnreachable);
    }

    [Fact]
    public async Task SameQueryFromSeveralFilesIsSearchedOnce()
    {
        var booth = new FakeBooth();
        var resolver = new FallbackResolver(booth);

        await resolver.FindReplacementAsync(
            FromId, null, null, [@"C:\dl\Coat_v1.zip", @"C:\dl\Coat_v2.zip", @"C:\other\Coat.zip"]);

        Assert.Equal(["Coat"], booth.Queries);
    }

    [Fact]
    public async Task SaysUnreachableOnlyWhenNoSearchArrived()
    {
        var booth = new FakeBooth { Offline = true };
        var resolver = new FallbackResolver(booth);

        var proposal = await resolver.FindReplacementAsync(FromId, "ふわふわパーカー", null, []);

        Assert.True(proposal.BoothUnreachable);
        Assert.Empty(proposal.Candidates);
    }

    [Fact]
    public async Task AttachesTheFirstImageOfTheShownCandidates()
    {
        var booth = new FakeBooth();
        booth.Search("ふわふわパーカー", ("9900007", "ふわふわパーカー", "shopa"));
        var resolver = new FallbackResolver(booth);

        var proposal = await resolver.FindReplacementWithImagesAsync(FromId, "ふわふわパーカー", "shopa", []);

        Assert.Equal([1, 2, 3], Assert.Single(proposal.Candidates).Image);
        Assert.Equal(["https://booth.pximg.net/9900007/a.jpg"], booth.BinaryRequests);
    }

    private sealed class FakeBooth : IBoothClient
    {
        private readonly Dictionary<string, (string Id, string Name, string Shop)[]> _results = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Name, string Shop)> _items = new(StringComparer.Ordinal);

        public bool Offline { get; init; }

        public List<string> Queries { get; } = [];

        public List<string> JsonRequests { get; } = [];

        public List<string> BinaryRequests { get; } = [];

        public void Search(string query, params (string Id, string Name, string Shop)[] cards)
        {
            _results[query] = cards;
            foreach (var card in cards)
            {
                _items[card.Id] = (card.Name, card.Shop);
            }
        }

        public Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            if (Offline)
            {
                return Task.FromResult(BoothFetchResult<string>.Unreachable("offline"));
            }

            var cards = _results.TryGetValue(query, out var found) ? found : [];
            return Task.FromResult(BoothFetchResult<string>.Success(string.Concat(cards.Select(card =>
                $"""<li class="item-card l-card" data-product-id="{card.Id}" data-product-name="{card.Name}" data-product-brand="{card.Shop}">"""))));
        }

        public Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default)
        {
            JsonRequests.Add(itemId);
            var (name, shop) = _items[itemId];
            return Task.FromResult(BoothFetchResult<string>.Success($$"""
                { "id": {{itemId}}, "name": "{{name}}", "url": "https://booth.pm/ja/items/{{itemId}}",
                  "shop": { "name": "{{shop}} の店", "subdomain": "{{shop}}", "url": "https://{{shop}}.booth.pm/" },
                  "images": [ { "original": "https://booth.pximg.net/{{itemId}}/a.jpg" } ], "variations": [] }
                """));
        }

        public Task<BoothFetchResult<string>> GetItemHtmlAsync(string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<byte[]>> GetBinaryAsync(string url, CancellationToken cancellationToken = default)
        {
            BinaryRequests.Add(url);
            return Task.FromResult(BoothFetchResult<byte[]>.Success([1, 2, 3]));
        }

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
