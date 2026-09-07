namespace BoothAssetManager.Core.Models;

/// <summary>
/// BOOTHから取得した情報。再取得時はこのブロックを丸ごと差し替える。
/// ここにユーザ入力を混ぜてはいけない（混ぜると再取得で消える）。
/// </summary>
public sealed class BoothBlock
{
    public required DateTimeOffset FetchedAt { get; init; }

    public string? Name { get; init; }

    /// <summary>商品JSONの description。実測では400文字程度の短い要約で、本文は h2 セクション側にある。</summary>
    public string? Description { get; init; }

    public bool IsAdult { get; init; }

    public bool IsEndOfSale { get; init; }

    public bool IsSoldOut { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>商品JSONの price。「¥ 2,500」のような整形済み文字列なので表示専用。金額計算には使わない。</summary>
    public string? PriceText { get; init; }

    /// <summary>商品JSONの wish_lists_count。</summary>
    public int WishListsCount { get; init; }

    public string? Url { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public BoothCategory? Category { get; init; }

    public BoothShop? Shop { get; init; }

    public IReadOnlyList<BoothImage> Images { get; init; } = [];

    /// <summary>商品JSONの embeds（YouTube等の埋め込みHTML断片）。</summary>
    public IReadOnlyList<string> Embeds { get; init; } = [];

    public IReadOnlyList<BoothVariation> Variations { get; init; } = [];

    /// <summary>
    /// 商品ページの説明文を見出し単位で分解したもの（タグ除去済みプレーンテキスト）。
    /// 検索と更新差分の判定に使う。表示用の生HTMLは <c>items/{id}.h2.html</c> に別途置く。
    /// </summary>
    public IReadOnlyList<H2Section> H2Sections { get; init; } = [];
}

/// <summary>説明文の1セクション。見出しは出品者の自由記述で、装飾記号が付くことが多い。</summary>
public sealed class H2Section
{
    /// <summary>見出しの原文（装飾記号を含む）。</summary>
    public required string Heading { get; init; }

    /// <summary>装飾記号と空白を除いた見出し。更新履歴セクションの判定に使う。</summary>
    public required string NormalizedHeading { get; init; }

    /// <summary>本文（タグ除去済み）。</summary>
    public required string Text { get; init; }
}
