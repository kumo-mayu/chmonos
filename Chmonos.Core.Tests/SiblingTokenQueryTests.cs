using Chmonos.Core.Booth;
using Chmonos.Core.Resolution;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 同じ商品をアバターごとに分けた zip（「商品名_アバター名」）の間で変わる語を、自動検索の検索語から外す（2026-09-29）。
/// 兄弟は未確定の一覧の同じフォルダ・同じ種類・頭の語が同じファイルで、商品IDは見ない。
/// 名前はすべて作り物。
/// </summary>
public class SiblingTokenQueryTests
{
    private const string Folder = @"D:\DL";

    private static string At(string name, string folder = Folder) => Path.Combine(folder, name);

    /// <summary>
    /// 3体のアバターに分けた衣装の zip と、同じアバター名が別の商品の zip にも出る一覧。
    /// アバター名は、頭の語が違う zip に2本以上出て初めて変わる語とみなす。
    /// </summary>
    private static List<string> AvatarSplitListing() =>
    [
        At("PollenkiteCape_Vornaq.zip"),
        At("PollenkiteCape_Tessivel.zip"),
        At("PollenkiteCape_Ormelink.zip"),
        At("MistralHood_Vornaq.zip"),
        At("MistralHood_Tessivel.zip"),
        At("EmbersockBoots_Vornaq_v1.1.zip"),
        At("EmbersockBoots_Tessivel_v1.1.zip"),
    ];

    [Fact]
    public void DropsTheAvatarNameThatChangesBetweenSiblings()
    {
        var target = At("PollenkiteCape_Vornaq.zip");

        var varying = SiblingTokens.Varying(target, AvatarSplitListing());

        Assert.Equal(["Vornaq"], varying);
        Assert.Equal("Pollenkite Cape", FileNameQuery.ToSearchQuery(target, SiblingTokens.NotProductName(null, varying)));
    }

    [Fact]
    public void KeepsANameThatDoesNotRecurInOtherProducts()
    {
        // Ormelink は兄弟の間では入れ替わるが、頭の語が違う zip には出ない。同じショップの別の商品を並べた形と見分けられないので外さない
        var varying = SiblingTokens.Varying(At("PollenkiteCape_Ormelink.zip"), AvatarSplitListing());

        Assert.Empty(varying);
    }

    [Fact]
    public void KeepsWordsOfDifferentProductsFromTheSameShop()
    {
        // 「ショップ名_商品名」の兄弟。入れ替わる語は商品名で、ほかの頭の語の zip には出ない
        List<string> listing =
        [
            At("Kestrelworks_Hat.zip"),
            At("Kestrelworks_Boots.zip"),
            At("Kestrelworks_Gloves.zip"),
            At("MistralHood_Vornaq.zip"),
        ];

        Assert.Empty(SiblingTokens.Varying(At("Kestrelworks_Hat.zip"), listing));
    }

    [Theory]
    [InlineData("PollenkiteCape_v1.2.zip", "PollenkiteCape_v1.3.zip")]
    [InlineData("PollenkiteCape_ver1.2_Vornaq.zip", "PollenkiteCape_ver1.3_Vornaq.zip")]
    [InlineData("PollenkiteCape_Vornaq_1.0.2.zip", "PollenkiteCape_Vornaq_1.1.0.zip")]
    [InlineData("PollenkiteCape_Vornaq_v2.zip", "PollenkiteCape_Vornaq_v3.zip")]
    public void VersionOnlySiblingsDropNothing(string target, string sibling)
    {
        // 版の数字だけが違う兄弟はアバター違いではない。語が同じなので何も外さない
        List<string> listing = [.. AvatarSplitListing().Where(path => !path.Contains("Pollenkite", StringComparison.Ordinal)), At(target), At(sibling)];

        Assert.Empty(SiblingTokens.Varying(At(target), listing));
        Assert.Empty(SiblingTokens.Varying(At(sibling), listing));
    }

    [Fact]
    public void AddedWordOnlyDropsNothing()
    {
        // 片方に語が足されただけ（本体と追加分）は、入れ替わる語が無い。Extra がほかの商品の zip に出ていても外さない
        List<string> listing =
        [
            At("PollenkiteCape.zip"),
            At("PollenkiteCape_Extra.zip"),
            At("MistralHood_Extra.zip"),
            At("EmbersockBoots_Extra.zip"),
        ];

        Assert.Empty(SiblingTokens.Varying(At("PollenkiteCape_Extra.zip"), listing));
    }

    [Fact]
    public void SiblingsMustShareTheFolderAndTheKind()
    {
        List<string> otherFolder =
        [
            At("PollenkiteCape_Vornaq.zip"),
            At("PollenkiteCape_Tessivel.zip", @"E:\Other"),
            .. AvatarSplitListing().Where(path => !path.Contains("Pollenkite", StringComparison.Ordinal)),
        ];
        Assert.Empty(SiblingTokens.Varying(At("PollenkiteCape_Vornaq.zip"), otherFolder));

        List<string> otherKind =
        [
            At("PollenkiteCape_Vornaq.zip"),
            At("PollenkiteCape_Tessivel.unitypackage"),
            .. AvatarSplitListing().Where(path => !path.Contains("Pollenkite", StringComparison.Ordinal)),
        ];
        Assert.Empty(SiblingTokens.Varying(At("PollenkiteCape_Vornaq.zip"), otherKind));
    }

    [Fact]
    public void KeepsTheQueryWhenTooLittleWouldRemain()
    {
        // 外すと2字の英字しか残らない。これだけで引いても1ページに埋もれるので、外さずに今のまま引く
        List<string> listing =
        [
            At("Qx_Vornaq.zip"),
            At("Qx_Tessivel.zip"),
            .. AvatarSplitListing().Where(path => !path.Contains("Pollenkite", StringComparison.Ordinal)),
        ];

        Assert.Empty(SiblingTokens.Varying(At("Qx_Vornaq.zip"), listing));
    }

    [Fact]
    public void KeepsTheQueryWhenOnlyAnAvatarNameWouldRemain()
    {
        // 残る語が登録簿のアバター名だけなら、外すと何も残らない
        List<string> listing =
        [
            At("Brindle_Vornaq.zip"),
            At("Brindle_Tessivel.zip"),
            .. AvatarSplitListing().Where(path => !path.Contains("Pollenkite", StringComparison.Ordinal)),
        ];

        Assert.Empty(SiblingTokens.Varying(At("Brindle_Vornaq.zip"), listing, token => token == "Brindle"));
    }

    [Fact]
    public async Task ProposeSearchesWithoutTheChangingWord()
    {
        var client = new RecordingClient();
        var resolver = new FallbackResolver(client);

        await resolver.ProposeAsync(At("PollenkiteCape_Vornaq.zip"), listed: AvatarSplitListing());
        Assert.Equal("Pollenkite Cape", client.Queries[0]);

        // 一覧を渡さなければ今までどおり（アバター名は登録簿に無いので残る）
        client.Queries.Clear();
        await resolver.ProposeAsync(At("PollenkiteCape_Vornaq.zip"));
        Assert.Equal("Pollenkite Cape Vornaq", client.Queries[0]);
    }

    /// <summary>検索語だけを控える。結果は0件（引き直しの語も控える）。</summary>
    private sealed class RecordingClient : IBoothClient
    {
        public List<string> Queries { get; } = [];

        public Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(BoothFetchResult<string>.Success(string.Empty));
        }

        public Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

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
