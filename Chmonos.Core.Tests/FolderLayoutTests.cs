using Chmonos.Core.Booth;
using Chmonos.Core.Models;
using Chmonos.Core.Resolution;

namespace Chmonos.Core.Tests;

/// <summary>フォルダの置き方を手掛かりにする（docs/spec/folder-layout.md）。名前・ID・ショップは作り物。</summary>
public class FolderLayoutTests
{
    private static readonly FolderLayoutRule ShopThenItem = new() { Root = @"D:\作り物の置き場", ItemNameLevel = 1, ShopNameLevel = 2 };

    [Theory]
    [InlineData("【期間限定無料】ふわふわ帽子", "ふわふわ帽子")]
    [InlineData("【8/5(水)まで無料 free】ふわふわ帽子", "ふわふわ帽子")]
    [InlineData("【85(水)まで無料 free】ふわふわ帽子", "ふわふわ帽子")] // Windows で使えない「/」が消えた
    [InlineData("[60% OFF] ふわふわ帽子", "ふわふわ帽子")]
    [InlineData("48時間限定無料 ふわふわ帽子", "ふわふわ帽子")]
    [InlineData("ふわふわ帽子", "ふわふわ帽子")]
    [InlineData("【無料】", "【無料】")] // 落として何も残らなければ元のまま
    [InlineData("フリース帽子", "フリース帽子")] // 名前の一部の「フリー」は落とさない（点検33）
    [InlineData("記念碑セット", "記念碑セット")]
    [InlineData("限定カラーの帽子", "限定カラーの帽子")]
    [InlineData("無料 ふわふわ帽子 free", "ふわふわ帽子")] // 頭と尻の飾りの語は落とす
    [InlineData("【FREE】ふわふわ帽子", "ふわふわ帽子")]
    [InlineData("ふわふわ帽子(1)", "ふわふわ帽子(1)")] // 数字だけの括弧は番号なので残す（点検34）
    [InlineData("【2026】ふわふわ帽子", "【2026】ふわふわ帽子")]
    public void 販売の飾りを落とす(string name, string expected)
        => Assert.Equal(expected, SaleDecorations.Strip(name));

    [Fact]
    public void 末尾のショップ名は_ショップ名を渡したときだけ落とす()
    {
        Assert.Equal("ふわふわ帽子", SaleDecorations.Strip("ふわふわ帽子 - 作り物工房", "作り物工房"));
        Assert.Equal("ふわふわ帽子 - 作り物工房", SaleDecorations.Strip("ふわふわ帽子 - 作り物工房"));
        Assert.Equal("帽子 - Long Name", SaleDecorations.Strip("帽子 - Long Name", "作り物工房"));
    }

    [Theory]
    [InlineData("ふわふわ帽子", "ふわふわ帽子", true)]
    [InlineData("ふわふわ帽子", "【期間限定無料】ふわふわ帽子", true)]
    [InlineData("ＦｕｗａＨａｔ", "fuwa hat", true)] // 全角半角・大小・空白
    [InlineData("ふわふわ帽子 Ver2 たくさんの色と形のセット", "ふわふわ帽子 Ver2 たくさんの色", true)] // 長い名前が切られた
    [InlineData("帽子", "ふわふわ帽子", false)] // 4字未満を含むで当てない
    [InlineData("ふわふわ帽子(1)", "ふわふわ帽子(2)", false)] // 番号違いは別の商品
    [InlineData("ふわふわ帽子", "もこもこ手袋", false)]
    public void フォルダ名と商品名を比べる(string itemName, string folder, bool same)
        => Assert.Equal(same, SaleDecorations.SameName(itemName, folder));

    [Fact]
    public void 置き方の段からフォルダ名を読む()
    {
        var hint = FolderLayoutHint.For(@"D:\作り物の置き場\作り物工房\ふわふわ帽子\hat.zip", [ShopThenItem]);
        Assert.Equal(new FolderLayoutHint("ふわふわ帽子", "作り物工房"), hint);

        // 指定のフォルダより上の段は読まない（直下のファイルの「2つ上」は置き方の外）
        Assert.Equal(new FolderLayoutHint("作り物工房", null), FolderLayoutHint.For(@"D:\作り物の置き場\作り物工房\hat.zip", [ShopThenItem]));
        Assert.Null(FolderLayoutHint.For(@"D:\作り物の置き場\hat.zip", [ShopThenItem]));

        // 指定の外・名前の頭だけ同じフォルダには効かない
        Assert.Null(FolderLayoutHint.For(@"E:\ほか\作り物工房\ふわふわ帽子\hat.zip", [ShopThenItem]));
        Assert.Null(FolderLayoutHint.For(@"D:\作り物の置き場2\作り物工房\ふわふわ帽子\hat.zip", [ShopThenItem]));
    }

    [Fact]
    public void ドライブの直下を指定しても段を読む()
    {
        var rule = FolderLayoutRule.DefaultFor(@"D:\");
        Assert.Equal(new FolderLayoutHint("ふわふわ帽子", "作り物工房"), FolderLayoutHint.For(@"D:\作り物工房\ふわふわ帽子\hat.zip", [rule]));
        Assert.Equal(new FolderLayoutHint("作り物工房", null), FolderLayoutHint.For(@"D:\作り物工房\hat.zip", [rule]));
        Assert.Null(FolderLayoutHint.For(@"E:\作り物工房\ふわふわ帽子\hat.zip", [rule]));
    }

    [Fact]
    public void いちばん深い指定を使い_オフなら上の指定にも落ちない()
    {
        var inner = new FolderLayoutRule { Root = @"D:\作り物の置き場\まとめ", ItemNameLevel = 2, ShopNameLevel = null };
        Assert.Equal(new FolderLayoutHint("商品の名前", null),
            FolderLayoutHint.For(@"D:\作り物の置き場\まとめ\商品の名前\版1\hat.zip", [ShopThenItem, inner]));

        Assert.Null(FolderLayoutHint.For(@"D:\作り物の置き場\まとめ\商品の名前\版1\hat.zip", [ShopThenItem, inner with { Enabled = false }]));

        // 段を両方「使わない」にした深い指定も、上の指定を通さない（点検33）
        Assert.Null(FolderLayoutHint.For(@"D:\作り物の置き場\まとめ\商品の名前\版1\hat.zip", [ShopThenItem, inner with { ItemNameLevel = null }]));
    }

    [Fact]
    public void 対象がフォルダなら_フォルダそのものを1つ上と数える()
    {
        Assert.Equal(new FolderLayoutHint("ふわふわ帽子", "作り物工房"),
            FolderLayoutHint.ForFolder(@"D:\作り物の置き場\作り物工房\ふわふわ帽子", [ShopThenItem], []));
    }

    [Fact]
    public void 使わない段は_保存して読み直しても使わないのまま()
    {
        // 保存の JSON は null の欄を書かない。既定の段が null でないと、読み直すと既定に戻っていた
        var path = Path.Combine(Path.GetTempPath(), $"chmonos-layout-{Guid.NewGuid():N}.json");
        try
        {
            var rule = ShopThenItem with { ShopNameLevel = null };
            Storage.JsonStore.Write(path, new AppSettings { FolderLayouts = [rule] });
            Assert.Equal([rule], Storage.JsonStore.Read<AppSettings>(path)!.FolderLayouts);

            // 計算で出せる値は書かない
            Assert.DoesNotContain("isUsable", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 段が無い_同じ段を2つに使う指定は効かない()
    {
        Assert.False((ShopThenItem with { ItemNameLevel = null, ShopNameLevel = null }).IsUsable);
        Assert.False((ShopThenItem with { ItemNameLevel = 2, ShopNameLevel = 2 }).IsUsable);
        Assert.False((ShopThenItem with { ItemNameLevel = 4 }).IsUsable && (ShopThenItem with { ItemNameLevel = 4, ShopNameLevel = null }).IsUsable);
    }

    [Fact]
    public void ショップのサブドメインは手元の商品から引く()
    {
        var items = new[] { Item("9100001", "別の帽子", "作り物工房", "fake-studio", @"D:\別\a.zip") };
        var hint = FolderLayoutHint.For(@"D:\作り物の置き場\作り物工房\ふわふわ帽子\hat.zip", [ShopThenItem], items);
        Assert.Equal("fake-studio", hint!.ShopSubdomain);

        Assert.Null(FolderLayoutHint.For(@"D:\作り物の置き場\知らない工房\ふわふわ帽子\hat.zip", [ShopThenItem], items)!.ShopSubdomain);
    }

    [Fact]
    public void 当てはまりを数える()
    {
        var items = new[]
        {
            Item("9100001", "ふわふわ帽子", "作り物工房", "fake-studio", @"D:\作り物の置き場\作り物工房\【期間限定無料】ふわふわ帽子\hat.zip"),
            Item("9100002", "もこもこ手袋", "作り物工房", "fake-studio", @"D:\作り物の置き場\作り物工房\まとめ\glove.zip"),
            Item("9100003", "外の商品", "作り物工房", "fake-studio", @"E:\ほか\x.zip"),
            Item("9100004", "直下の商品", "作り物工房", "fake-studio", @"D:\作り物の置き場\直下.zip"),
        };

        // オフでも数える（オンにする前に確かめられるように）。段の足りない直下のファイルも母数に入れる（点検33）
        Assert.Equal(new FolderLayoutFit(3, 1, 2), FolderLayoutFit.Measure(items, ShopThenItem with { Enabled = false }));
    }

    [Fact]
    public void ショップの一覧のページから商品を読む()
    {
        var html = Card("9100001", "ふわふわ帽子") + Card("9100002", "もこもこ &amp; 手袋") + "<li data-item=\"{壊れた\"></li>";
        Assert.Equal([new ShopItemList.Entry("9100001", "ふわふわ帽子"), new ShopItemList.Entry("9100002", "もこもこ & 手袋")], ShopItemList.Parse(html));
    }

    [Fact]
    public async Task ショップの一覧は全ページ取り_同じショップは取り直さない()
    {
        var client = new FakeClient(shopPages: new()
        {
            [1] = Card("9100001", "ふわふわ帽子") + "<a href=\"?page=2\">2</a>",
            [2] = Card("9100002", "もこもこ手袋"),
        });
        var lists = new ShopItemList(client);

        Assert.Equal(2, (await lists.GetAsync("fake-studio"))!.Count);
        Assert.Equal(2, (await lists.GetAsync("FAKE-studio"))!.Count);
        Assert.Equal(2, client.ShopPageRequests);
    }

    [Fact]
    public async Task 置き方から出した候補を上に並べ_確度が高いには数えない()
    {
        // ファイル名では外れの商品しか出ない。フォルダ名とショップの一覧で正解が出る
        var client = new FakeClient(
            search: new()
            {
                ["hat"] = [("9100009", "関係の無い帽子", "other-shop")],
                ["ふわふわ帽子"] = [("9100001", "ふわふわ帽子", "fake-studio")],
            },
            shopPages: new() { [1] = Card("9100001", "ふわふわ帽子") + Card("9100002", "もこもこ手袋") },
            shopNames: new() { ["fake-studio"] = "作り物工房", ["other-shop"] = "ほかの工房" });
        var resolver = new FallbackResolver(client);

        var proposal = await resolver.ProposeAsync(
            @"D:\作り物の置き場\作り物工房\【期間限定無料】ふわふわ帽子\hat.zip",
            layout: new FolderLayoutHint("【期間限定無料】ふわふわ帽子", "作り物工房", "fake-studio"));

        var top = proposal.Candidates[0];
        Assert.Equal("9100001", top.ItemId);
        Assert.Contains("フォルダ名が商品名と一致", top.Reasons);
        Assert.Contains("フォルダ名がショップ名と一致", top.Reasons);
        Assert.Contains("ショップの商品一覧に同じ名前があった", top.Reasons);
        Assert.Equal(5, top.LayoutPoints);
        Assert.False(top.IsStrong);

        // ファイル名での検索も回している
        Assert.Contains(proposal.Candidates, candidate => candidate.ItemId == "9100009");
    }

    [Fact]
    public async Task 置き方を渡さなければ_今までどおりファイル名だけで探す()
    {
        var client = new FakeClient(search: new() { ["hat"] = [("9100009", "関係の無い帽子", "other-shop")] });
        var proposal = await new FallbackResolver(client).ProposeAsync(@"D:\作り物の置き場\作り物工房\ふわふわ帽子\hat.zip");

        Assert.Equal(["9100009"], proposal.Candidates.Select(candidate => candidate.ItemId));
        Assert.Equal(0, client.ShopPageRequests);
    }

    [Fact]
    public async Task 置き方を渡さなければ_確かめる商品は今までどおり3件まで()
    {
        var client = new FakeClient(search: new()
        {
            ["hat"] = [.. Enumerable.Range(1, 6).Select(index => ($"910001{index}", $"帽子{index}", "other-shop"))],
        });
        await new FallbackResolver(client).ProposeAsync(@"D:\作り物の置き場\hat.zip");
        Assert.Equal(["9100011", "9100012", "9100013"], client.JsonRequests);
    }

    [Fact]
    public async Task ファイル名から検索語が作れなくても_置き方のフォルダ名では探す()
    {
        var client = new FakeClient(search: new() { ["ふわふわ帽子"] = [("9100001", "ふわふわ帽子", "fake-studio")] });
        var proposal = await new FallbackResolver(client).ProposeAsync(
            @"D:\作り物の置き場\作り物工房\ふわふわ帽子\v1.0.zip", layout: new FolderLayoutHint("ふわふわ帽子", "作り物工房"));

        Assert.Equal("9100001", Assert.Single(proposal.Candidates).ItemId);
        Assert.Empty(await new FallbackResolver(client).ProposeAsync(@"D:\作り物の置き場\作り物工房\ふわふわ帽子\v1.0.zip") is { } none ? none.Candidates : []);
    }

    [Fact]
    public async Task フォルダ名の検索が正常に0件なら_届かなかったとは言わない()
    {
        // 前は候補の有無で決めていて、正常な0件でも「問い合わせできませんでした」と言った（点検34）
        var path = @"D:\作り物の置き場\作り物工房\知らない帽子\v1.0.zip";
        var layout = new FolderLayoutHint("知らない帽子", "作り物工房");

        var empty = await new FallbackResolver(new FakeClient(search: [])).ProposeAsync(path, layout: layout);
        Assert.Empty(empty.Candidates);
        Assert.False(empty.BoothUnreachable);

        var failed = await new FallbackResolver(new FakeClient(search: [], failSearches: int.MaxValue)).ProposeAsync(path, layout: layout);
        Assert.True(failed.BoothUnreachable);
    }

    [Fact]
    public async Task 最初の検索だけ失敗し_引き直しが正常に0件なら_届かなかったとは言わない()
    {
        // 前は引き直しの届いたかを捨てていて、最初の検索の失敗だけで「問い合わせできませんでした」と言った（点検35）
        var client = new FakeClient(search: [], failSearches: 1);
        var proposal = await new FallbackResolver(client).ProposeAsync(@"D:\作り物の置き場\ふわふわ 帽子 セット.zip");

        Assert.True(client.SearchRequests >= 2);
        Assert.Empty(proposal.Candidates);
        Assert.False(proposal.BoothUnreachable);
    }

    [Fact]
    public async Task 消えたショップの一覧は_同じショップで聞き直さない()
    {
        var client = new FakeClient(shopPages: []);
        var lists = new ShopItemList(client);
        Assert.Empty((await lists.GetAsync("gone-shop"))!);
        Assert.Empty((await lists.GetAsync("gone-shop"))!);
        Assert.Equal(1, client.ShopPageRequests);
    }

    private static string Card(string id, string name)
        => $"<li class=\"js-mount-point-shop-item-card\" data-item=\"{{&quot;id&quot;:{id},&quot;name&quot;:&quot;{name}&quot;}}\"></li>";

    private static ItemRecord Item(string id, string name, string shopName, string subdomain, string path) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = name, Shop = new BoothShop { Name = shopName, Subdomain = subdomain } },
        Local = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = "h" + id, Paths = [path], SizeBytes = 1 }] },
    };

    private sealed class FakeClient(
        Dictionary<string, (string Id, string Name, string Shop)[]>? search = null,
        Dictionary<int, string>? shopPages = null,
        Dictionary<string, string>? shopNames = null,
        int failSearches = 0) : IBoothClient
    {
        public int SearchRequests { get; private set; }

        public int ShopPageRequests { get; private set; }

        public List<string> JsonRequests { get; } = [];

        private (string Id, string Name, string Shop)? Find(string id)
            => (search ?? []).Values.SelectMany(cards => cards).FirstOrDefault(card => card.Id == id) is { Id: not null } card ? card : null;

        public Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => ++SearchRequests <= failSearches ? Task.FromResult(BoothFetchResult<string>.Temporary("作り物の失敗")) : Task.FromResult(BoothFetchResult<string>.Success(string.Concat(
                ((search ?? []).TryGetValue(query, out var cards) ? cards : []).Select(card =>
                    $"""<li class="item-card l-card" data-product-id="{card.Id}" data-product-name="{card.Name}" data-product-brand="{card.Shop}">"""))));

        public Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default)
        {
            JsonRequests.Add(itemId);
            var card = Find(itemId) ?? (itemId, "もこもこ手袋", "fake-studio");
            var shopName = (shopNames ?? []).GetValueOrDefault(card.Shop, card.Shop);
            return Task.FromResult(BoothFetchResult<string>.Success($$"""
                { "id": {{itemId}}, "name": "{{card.Name}}", "url": "https://booth.pm/ja/items/{{itemId}}",
                  "shop": { "name": "{{shopName}}", "subdomain": "{{card.Shop}}", "url": "https://{{card.Shop}}.booth.pm/" },
                  "images": [], "variations": [] }
                """));
        }

        public Task<BoothFetchResult<string>> GetShopItemsPageAsync(string subdomain, int page, CancellationToken cancellationToken = default)
        {
            ShopPageRequests++;
            return Task.FromResult((shopPages ?? []).TryGetValue(page, out var html)
                ? BoothFetchResult<string>.Success(html)
                : BoothFetchResult<string>.NotFound());
        }

        public Task<BoothFetchResult<string>> GetItemHtmlAsync(string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<byte[]>> GetBinaryAsync(string url, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BoothFetchResult<string>> GetTextUntilAsync(string url, Func<string, bool> found, int maxBytes = 262144, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public int CurrentIntervalMs => 0;

        public bool IsThrottled => false;

#pragma warning disable CS0067
        public event Action<BoothActivity>? ActivityChanged;
#pragma warning restore CS0067
    }
}
