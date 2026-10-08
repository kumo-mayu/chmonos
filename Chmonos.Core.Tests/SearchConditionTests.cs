using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 検索欄の記法のうち、状態・数・登録簿で当てる物（ユーザ判断 2026-10-08）。
/// <c>is:</c>・<c>has:</c>・<c>paid:</c>・<c>price:</c>・<c>wish:</c>・<c>usertag:</c>・<c>avatar:</c>・<c>category:</c>。
/// </summary>
public class SearchConditionTests
{
    private static ItemRecord Item(string id = "1000001", Func<LocalBlock, LocalBlock>? local = null, Func<BoothBlock, BoothBlock>? booth = null)
        => new()
        {
            Id = id,
            Booth = (booth ?? (b => b))(new BoothBlock { Name = "作り物の衣装", FetchedAt = DateTimeOffset.UtcNow }),
            Local = (local ?? (l => l))(new LocalBlock()),
        };

    private static bool Matches(string query, ItemRecord item, SearchFacts? facts = null)
        => SearchQuery.Matches(SearchQuery.Parse(query), SearchText.Build(item), SearchOptions.Default, facts);

    private static SearchFacts Facts(AvatarRegistry? registry = null, params string[] unread)
    {
        var avatars = registry ?? new AvatarRegistry();
        return new SearchFacts(() => AvatarCompatibilityIndex.Build(avatars), () => avatars, id => unread.Contains(id));
    }

    // --- 範囲の書き方 ---

    [Theory]
    [InlineData("-400", null, 400L)]
    [InlineData("100-500", 100L, 500L)]
    [InlineData("500-", 500L, null)]
    [InlineData("<=400", null, 400L)]
    [InlineData(">=500", 500L, null)]
    [InlineData("<400", null, 399L)]
    [InlineData(">500", 501L, null)]
    [InlineData("500", 500L, 500L)]
    [InlineData("1,000-2,000円", 1000L, 2000L)]
    [InlineData("100ー500", 100L, 500L)]
    [InlineData("100〜500", 100L, 500L)]
    public void 範囲の書き方を読む(string text, long? min, long? max)
    {
        Assert.True(SearchConditions.TryParseRange(SearchQuery.Normalize(text), out var range));
        Assert.Equal(new SearchConditions.NumberRange(min, max), range);
    }

    /// <summary>逆向きは「以上」とも「以下」とも読めるので受けない。数でない物・区切りだけも受けない</summary>
    [Theory]
    [InlineData("500<=")]
    [InlineData("1000>=")]
    [InlineData("-")]
    [InlineData("abc")]
    [InlineData("100-abc")]
    [InlineData("")]
    public void 読めない範囲は受けない(string text)
        => Assert.False(SearchConditions.TryParseRange(SearchQuery.Normalize(text), out _));

    /// <summary>全角の数字・不等号でも打てる（NFKC で畳んでから読む）</summary>
    [Fact]
    public void 全角で打った範囲も読む()
        => Assert.True(Matches("wish:＞＝１０", Item(booth: b => b with { WishListsCount = 12 })));

    // --- 数の前置き ---

    [Fact]
    public void Paidは自分に払った額で見る()
    {
        var item = Item(local: l => l with
        {
            Purchases = [new Purchase { Price = 300 }, new Purchase { Price = 5000, Kind = PurchaseKind.Given }],
        });

        Assert.True(Matches("paid:-400", item));
        Assert.False(Matches("paid:1000-", item));
    }

    /// <summary>値段を入れていない商品は 0 円ではない。範囲には当たらず、除くと残る（値段空欄と 0 円を分ける）</summary>
    [Fact]
    public void 払った額の無い商品は範囲に当たらない()
    {
        var item = Item(local: l => l with { Purchases = [new Purchase()] });

        Assert.False(Matches("paid:-400", item));
        Assert.False(Matches("paid:0", item));
        Assert.True(Matches("-paid:-400", item));
    }

    /// <summary>BOOTH の価格は種類のどれかが入れば当たる（条件「価格」の既定と同じ）</summary>
    [Fact]
    public void PriceはBOOTHの種類のどれかで見る()
    {
        var item = Item(booth: b => b with
        {
            Variations = [new BoothVariation { Id = 1, Price = 0 }, new BoothVariation { Id = 2, Price = 1500 }],
        });

        Assert.True(Matches("price:1000-2000", item));
        Assert.True(Matches("price:0", item));
        Assert.False(Matches("price:2001-", item));
        Assert.False(Matches("price:1-999", item));
    }

    [Fact]
    public void Wishはスキ数で見る()
    {
        var item = Item(booth: b => b with { WishListsCount = 120 });

        Assert.True(Matches("wish:100-", item));
        Assert.False(Matches("wish:<100", item));
    }

    /// <summary>
    /// 範囲の前置きを括弧に当てると、中の「-400」は除くではなく「400以下」（ユーザ判断 2026-10-08）。
    /// 2つの範囲のどちらか、は OR で書く（空白で並べると AND。払った額は1つの値なので、離れた2つの範囲を AND にすると0件）
    /// </summary>
    [Theory]
    [InlineData(300, true)]
    [InlineData(700, false)]
    [InlineData(1500, true)]
    public void 範囲の前置きの括弧の中は範囲として読む(int paid, bool expected)
    {
        var item = Item(local: l => l with { Purchases = [new Purchase { Price = paid }] });

        Assert.Equal(expected, Matches("paid:(-400 OR 1000-2000)", item));
        Assert.Equal(expected, Matches("paid:-400 OR paid:1000-2000", item));
        Assert.False(Matches("paid:-400 paid:1000-2000", item));
    }

    /// <summary>
    /// 読み替えるのは括弧の中だけ。前置きの後に空白を入れた paid: -400 は今までどおり「400ちょうどを除く」（ユーザ判断 2026-10-08）。
    /// 範囲を除くのは前置きの外に「-」を書く
    /// </summary>
    [Fact]
    public void 範囲の前置きの後の空白と_範囲を除く書き方()
    {
        var item = Item(local: l => l with { Purchases = [new Purchase { Price = 300 }] });

        Assert.True(Matches("paid: -400", item));
        Assert.False(Matches("paid: -300", item));
        Assert.False(Matches("-paid:-400", item));
        Assert.True(Matches("-paid:1000-", item));

        // 範囲でない前置きの中の「-」は、今までどおり除く
        Assert.False(Matches("name:(-作り物)", item));
    }

    /// <summary>読めない範囲はどの商品にも当たらない（打ちかけで全件が出たり、ふつうの文字として探したりしない）</summary>
    [Fact]
    public void 読めない範囲はどれにも当たらない()
        => Assert.False(Matches("wish:abc", Item(booth: b => b with { WishListsCount = 0 })));

    // --- is: と has: ---

    [Fact]
    public void Isは絞り込みの条件と同じ式で見る()
    {
        Assert.True(Matches("is:favorite", Item(local: l => l with { IsFavorite = true })));
        Assert.False(Matches("is:favorite", Item()));
        Assert.True(Matches("is:hidden", Item(local: l => l with { IsHidden = true })));
        Assert.True(Matches("is:r18", Item(booth: b => b with { IsAdult = true })));
        Assert.True(Matches("is:local", Item("local-aaaa1111")));
        Assert.True(Matches("is:delisted", Item(local: l => l with { IsDelisted = true })));

        // BOOTHに無い商品は公開状況を持たない（条件「公開状況」の「販売終了・非公開」に入らないのと同じ）
        Assert.False(Matches("is:delisted", Item("local-aaaa1111", local: l => l with { IsDelisted = true })));
    }

    /// <summary>前置きの語は大文字・全角でも同じ。知らない語はどれにも当たらない</summary>
    [Fact]
    public void Isの語は畳んで比べ_知らない語は当たらない()
    {
        var item = Item(local: l => l with { IsFavorite = true });

        Assert.True(Matches("is:Favorite", item));
        Assert.True(Matches("ＩＳ：ｆａｖｏｒｉｔｅ", item));
        Assert.False(Matches("is:fav", item));
    }

    [Fact]
    public void Hasのupdateは未読の知らせの表で見る()
    {
        var item = Item("1000001");

        Assert.True(Matches("has:update", item, Facts(null, "1000001")));
        Assert.False(Matches("has:update", item, Facts(null, "1000002")));

        // 事実を渡さない検索欄（タグの管理など）では当たらない
        Assert.False(Matches("has:update", item));
    }

    [Fact]
    public void 否定_OR_括弧と組める()
    {
        var favorite = Item(local: l => l with { IsFavorite = true });
        var hidden = Item(local: l => l with { IsHidden = true });

        Assert.True(Matches("is:(favorite OR hidden)", hidden));
        Assert.False(Matches("-is:favorite", favorite));
        Assert.True(Matches("作り物 is:favorite", favorite));
        Assert.False(Matches("別の名前 is:favorite", favorite));
    }

    /// <summary>is:hidden を書いたかを見る（検索画面が非表示の商品も照らすため）。否定の中も数える</summary>
    [Fact]
    public void Isのhiddenを書いたかが分かる()
    {
        Assert.True(SearchQuery.Mentions(SearchQuery.Parse("夏 is:hidden"), SearchField.Is, "hidden"));
        Assert.True(SearchQuery.Mentions(SearchQuery.Parse("-is:hidden"), SearchField.Is, "hidden"));
        Assert.False(SearchQuery.Mentions(SearchQuery.Parse("hidden"), SearchField.Is, "hidden"));
    }

    // --- 名前の前置き ---

    /// <summary>ユーザータグは完全一致。区切りの無い名前は大分類か小分類のどちらか（ユーザ判断 2026-10-08）</summary>
    [Fact]
    public void Usertagは名前の完全一致で当たる()
    {
        var item = Item(local: l => l with { UserTags = [new UserTagAssignment { Top = "衣装", Subs = ["ワンピース"] }] });

        Assert.True(Matches("usertag:衣装", item));
        Assert.True(Matches("usertag:ワンピース", item));
        Assert.False(Matches("usertag:衣", item));
        Assert.False(Matches("usertag:ピース", item));

        // 前置きが無ければ、ユーザータグは文字の対象ではない（今までどおり）
        Assert.False(Matches("ワンピース", item));
    }

    [Fact]
    public void Usertagはスラッシュで大分類と小分類を分けて指せる()
    {
        var dress = Item(local: l => l with { UserTags = [new UserTagAssignment { Top = "衣装", Subs = ["ワンピース"] }] });
        var topOnly = Item(local: l => l with { UserTags = [new UserTagAssignment { Top = "衣装" }] });
        var otherTop = Item(local: l => l with { UserTags = [new UserTagAssignment { Top = "小物", Subs = ["ワンピース"] }] });

        Assert.True(Matches("usertag:衣装/ワンピース", dress));
        Assert.False(Matches("usertag:衣装/ワンピース", otherTop));

        // 大分類/：小分類は問わない（小分類の無い付け方にも当たる）
        Assert.True(Matches("usertag:衣装/", topOnly));
        Assert.False(Matches("usertag:衣装/", otherTop));

        // /小分類：大分類は問わない。小分類の名前が大分類と同じでも、大分類には当てない
        Assert.True(Matches("usertag:/ワンピース", otherTop));
        Assert.False(Matches("usertag:/衣装", topOnly));

        // 全角の「／」と、括弧で囲んだ空白入りも同じ
        Assert.True(Matches("usertag:衣装／ワンピース", dress));
        Assert.True(Matches("usertag:\"衣装 / ワンピース\"", dress));
    }

    [Fact]
    public void Usertagはアスタリスクの所だけ何でもよい()
    {
        var item = Item(local: l => l with { UserTags = [new UserTagAssignment { Top = "衣装小物", Subs = ["夏のワンピース"] }] });

        Assert.True(Matches("usertag:衣装*", item));
        Assert.True(Matches("usertag:*ワンピース", item));
        Assert.True(Matches("usertag:*の*", item));
        Assert.True(Matches("usertag:衣装*/夏*", item));
        Assert.False(Matches("usertag:*スカート", item));
        Assert.False(Matches("usertag:小物*", item));
    }

    [Theory]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "a*", true)]
    [InlineData("abc", "*c", true)]
    [InlineData("abc", "a*c", true)]
    [InlineData("abc", "*", true)]
    [InlineData("abc", "a*b*c", true)]
    [InlineData("ac", "a*b*c", false)]
    [InlineData("aba", "ab*ba", false)]
    [InlineData("ABC", "abc", true)]
    public void 名前の型を照らす(string name, string pattern, bool expected)
        => Assert.Equal(expected, SearchConditions.Like(name, pattern));

    [Fact]
    public void Categoryは条件のカテゴリと同じ値で当たる()
    {
        var item = Item(booth: b => b with { Category = new BoothCategory { Id = 1, Name = "衣装", ParentName = "3Dモデル" } });

        Assert.True(Matches("category:衣装", item));
        Assert.True(Matches("category:3dモデル", item));
        Assert.True(Matches("category:小物", Item(local: l => l with { Category = "小物" })));
    }

    [Fact]
    public void Avatarは登録簿の名前と呼び方と素体で当たる()
    {
        var registry = new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = "2000001",
                    BoothName = "オリジナル3Dモデル『作り物ちゃん』",
                    Aliases = [new AvatarAlias { Text = "つくもの" }, new AvatarAlias { Text = "消した呼び方", Rejected = true }],
                },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = "作り物素体" }],
        };
        var item = Item(local: l => l with
        {
            Avatars = [new AvatarLink { AvatarItemId = "2000001", Source = AvatarLinkSource.Manual }],
            AvatarBases = [new AvatarBaseLink { BaseName = "作り物素体", Source = AvatarLinkSource.Manual }],
        });
        var facts = Facts(registry);

        Assert.True(Matches("avatar:作り物ちゃん", item, facts));
        Assert.True(Matches("avatar:つくもの", item, facts));
        Assert.True(Matches("avatar:2000001", item, facts));
        Assert.True(Matches("avatar:作り物素体", item, facts));
        Assert.False(Matches("avatar:消した呼び方", item, facts));
    }

    /// <summary>消した対応と、説明文のリンク（要確認）は数えない（条件「対応アバター」と同じ）</summary>
    [Fact]
    public void Avatarは消した対応と要確認を数えない()
    {
        var registry = new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "2000001", BoothName = "作り物ちゃん" }],
        };
        var rejected = Item(local: l => l with
        {
            Avatars = [new AvatarLink { AvatarItemId = "2000001", Source = AvatarLinkSource.Manual, Rejected = true }],
        });
        var unconfirmed = Item(local: l => l with
        {
            Avatars = [new AvatarLink { AvatarItemId = "2000001", Source = AvatarLinkSource.H2Link }],
        });

        Assert.False(Matches("avatar:作り物ちゃん", rejected, Facts(registry)));
        Assert.False(Matches("avatar:作り物ちゃん", unconfirmed, Facts(registry)));
    }

    // --- 覚えた答え ---

    /// <summary>
    /// 記録の外の事実を見る式は答えを覚えない。材料は記録が同じ商品で使い回すので、覚えると既読にしても古い答えが返る
    /// </summary>
    [Fact]
    public void 事実を見る式は答えを覚えない()
    {
        var item = Item("1000001");
        var haystack = SearchText.Build(item);
        var node = SearchQuery.Parse("has:update");

        Assert.True(SearchQuery.Matches(node, haystack, SearchOptions.Default, Facts(null, "1000001")));
        Assert.False(SearchQuery.Matches(node, haystack, SearchOptions.Default, Facts()));
    }

    /// <summary>状態・数の語は別表記で広げない（決まった語なので、広げると関係の無い語で当たる）</summary>
    [Fact]
    public void 状態の前置きはふつうの文字の前置きと区別される()
    {
        Assert.True(SearchConditions.IsCondition(SearchField.Paid));
        Assert.True(SearchConditions.IsCondition(SearchField.Is));
        Assert.True(SearchConditions.IsCondition(SearchField.UserTag));
        Assert.False(SearchConditions.IsCondition(SearchField.Category));
        Assert.False(SearchConditions.IsCondition(SearchField.Name));
    }

    // --- 管理画面の検索欄（ItemTextFilter） ---

    /// <summary>管理画面の検索欄も、事実を渡せば avatar:・has:update が効く（ユーザ判断 2026-10-08「繋ぐ方針」）</summary>
    [Fact]
    public void 管理画面の検索欄も事実を渡せば記録の外の記法が効く()
    {
        var item = Item("1000001");

        Assert.True(ItemTextFilter.Create("has:update", Facts(null, "1000001"))!.Matches(item));
        Assert.False(ItemTextFilter.Create("has:update", Facts(null, "1000002"))!.Matches(item));
        Assert.False(ItemTextFilter.Create("has:update")!.Matches(item));

        // 記録だけで決まる記法は、事実が無くても効く
        Assert.True(ItemTextFilter.Create("-is:favorite")!.Matches(item));
    }

    /// <summary>
    /// 引用符で囲んだ語は、造語変換の読みでも当てない（点検27：辞書で広げるのは止めたが、読みの照合が残っていた）。囲まない語は今までどおり読みで当たる
    /// </summary>
    [Fact]
    public void 引用符で囲んだ語は読みでも当てない()
    {
        var haystack = SearchHaystack.FromValues(new Dictionary<SearchField, string[]> { [SearchField.Name] = ["鳥"] }, readings: "とり");
        var options = SearchOptions.Default with { IncludeReadings = true };

        Assert.True(SearchQuery.Matches(SearchQuery.Parse("とり"), haystack, options));
        Assert.False(SearchQuery.Matches(SearchQuery.Parse("\"とり\""), haystack, options));
        Assert.True(SearchQuery.Matches(SearchQuery.Parse("-\"とり\""), haystack, options));
    }
}
