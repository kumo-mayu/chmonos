namespace Chmonos.Core.Models;

/// <summary>
/// BOOTHから取得した情報。再取得時はこのブロックを丸ごと差し替える。
/// ここにユーザ入力を混ぜてはいけない（混ぜると再取得で消える）。
///
/// <see cref="LocalBlock"/> と同じくレコードにしているのは、
/// 取り込みの②で <see cref="H2Sections"/> だけを後から入れるため。
/// 手でコピーを書くと、項目を足した時に写し忘れて情報が消える。
/// </summary>
public sealed record BoothBlock
{
    /// <summary>
    /// いつ観測したか。**一度も取れていなければ null。**
    ///
    /// BOOTHから取れない商品（非公開・販売終了で消えたもの）も手元に置けるようにしたので、
    /// 「取得した日時」に嘘を書かずに済む形が要る。登録した日時を入れると、
    /// ⑦の期限計算も「最終取得」の表示も狂う。
    ///
    /// **「一度も取れていない」はここから導く。**別のフラグを持たない——
    /// 二重に持つと片方だけ更新される事故が起きる。
    /// </summary>
    public DateTimeOffset? FetchedAt { get; init; }

    /// <summary>
    /// BOOTHから一度でも取れたか。
    /// 印の文言を分けるために使う——「BOOTHで見つかりません（情報を取れたことがありません）」と
    /// 「販売終了」は、同じ <c>IsDelisted</c> でも中身が違う。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool WasEverFetched => FetchedAt is not null;

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

    /// <summary>
    /// 装飾記号と空白を除いた見出し。更新履歴セクションの判定に使う。
    ///
    /// **見出しから計算で出せるので JSON に書かない**（2026-09-24。CLAUDE.md の決め事）。前は節ごとに書き出していた。
    /// 前に書かれたファイルに残っていても、読むときに無視される（次にその商品を書いたときに消える）。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string NormalizedHeading => _normalizedHeading ??= Booth.H2SectionExtractor.NormalizeHeading(Heading);

    private string? _normalizedHeading;

    /// <summary>本文（タグ除去済み）。</summary>
    public required string Text { get; init; }
}
