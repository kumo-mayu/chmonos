using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Search;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 引き直した語で出た候補にも、**元の検索語で**点を付ける（2026-09-29）。
/// 引き直した語で付けると、その語で引いた商品は名前にその語を含むのが当たり前なので「商品名と一致」が必ず付き、
/// 別表記なら「読みで一致」も重なって、元の検索の正解より高い点を取っていた。
/// 通信は偽物で置き換える。商品名は作り物。
/// </summary>
public sealed class FallbackRetryScoreTests
{
    // 辞書は一式で1回だけ組んだ物を使う（組むのに数秒かかる。SharedDictionaries）
    private readonly SearchBridge _bridge = SharedDictionaries.Bridge;
    private readonly KanjiReadings _readings = SharedDictionaries.Readings;

    [Fact]
    public async Task ScoresRetryCandidatesAgainstTheOriginalQuery()
    {
        if (!_bridge.IsAvailable || !_readings.IsAvailable)
        {
            return;
        }

        // 元の検索語は「Hoshizora」。別表記の「星空」でも引き直す
        var alternate = FallbackResolver.RetryQueries("Hoshizora.zip", "Hoshizora", _bridge);
        Assert.Contains("星空", alternate);

        var client = new FakeClient(new Dictionary<string, (string Id, string Name)[]>
        {
            ["Hoshizora"] = [("111", "Hoshizora Skybox")],
            ["星空"] = [("222", "星空の天球ドーム")],
        });
        var resolver = new FallbackResolver(client, _bridge, _readings);

        var proposal = await resolver.ProposeAsync("Hoshizora.zip");

        var original = Assert.Single(proposal.Candidates, candidate => candidate.ItemId == "111");
        var retried = Assert.Single(proposal.Candidates, candidate => candidate.ItemId == "222");

        // 別表記の候補は読みの一致（2点）だけで、「商品名と一致」を重ねない
        Assert.DoesNotContain("商品名がファイル名と一致", retried.Reasons);
        Assert.Contains(retried.Reasons, reason => reason.StartsWith("ファイル名が商品名と読みで一致", StringComparison.Ordinal));
        Assert.True(original.Score >= retried.Score);
        Assert.Equal("111", proposal.Candidates[0].ItemId);
    }

    private sealed class FakeClient(IReadOnlyDictionary<string, (string Id, string Name)[]> results) : IBoothClient
    {
        private readonly Dictionary<string, string> _names = results.Values
            .SelectMany(cards => cards)
            .ToDictionary(card => card.Id, card => card.Name);

        public Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult(BoothFetchResult<string>.Success(string.Concat(
                (results.TryGetValue(query, out var cards) ? cards : []).Select(card =>
                    $"""<li class="item-card l-card" data-product-id="{card.Id}" data-product-name="{card.Name}" data-product-brand="shop{card.Id}">"""))));

        public Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default)
            => Task.FromResult(BoothFetchResult<string>.Success($$"""
                { "id": {{itemId}}, "name": "{{_names[itemId]}}", "url": "https://booth.pm/ja/items/{{itemId}}",
                  "images": [], "variations": [] }
                """));

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
