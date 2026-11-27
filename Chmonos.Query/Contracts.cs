using System.Text.Json.Serialization;

namespace Chmonos.Query;

// 外から使う API の答えの形（docs/spec/local-api-shapes.md）。**ここの名前と意味は外の道具との約束**：
// 項目を足すのは互換、消す・名前を変える・意味を変えるのは非互換（/v2/）。保存の JSON の形とは別に持つ（中を直しても外の約束が壊れない）。
// 列挙の値は変えない英字の文字列にする（保存の JSON が日本語の値でも）。値が無いときは項目を省かず null を書く。
// **既定の答えは結果だけ**（2026-10-12・ユーザ判断）：どう決まったか・人が何を打ち消したかの帳簿は include で頼まれたときだけ返す。
// 頼まなかった項目は項目ごと出さない（頼んで中身が無ければ [] か null。「頼んでいない」と「無い」を見分けられる）。

/// <summary>ショップ（商品が属する物）。</summary>
/// <param name="Key">束ねる鍵（サブドメイン。自分で作ったショップは <c>local-</c> で始まる鍵）。</param>
/// <param name="Subdomain">BOOTH のサブドメイン。自分で作ったショップは null。</param>
/// <param name="IsLocal">自分で作ったショップか（BOOTH に無い）。</param>
public sealed record ShopRef(string Key, string? Name, string? Subdomain, bool IsLocal, string? Url);

/// <summary>BOOTH のカテゴリ。</summary>
public sealed record BoothCategoryRef(int Id, string Name, string? ParentName);

/// <summary>画像1枚。中身は送らず、場所を返す（ブラウザの画面向けには <c>/v1/images/{itemId}/{name}</c> で本体を返す）。</summary>
/// <param name="Origin"><c>booth</c>（BOOTH の画像）・<c>user</c>（自分で足した画像）・<c>orphaned</c>（BOOTH から消えた画像の控え）。</param>
/// <param name="Role">画像の役割（<c>booth</c>・<c>modified</c>・<c>other</c>）。</param>
public sealed record ImageRef(string Name, string Path, string Origin, string Role, string? Caption);

/// <summary>対応アバターへの紐付け。</summary>
/// <param name="Source">どこから見つけたか（<c>supportSection</c>・<c>supportList</c>・<c>tag</c>・<c>variation</c>・<c>h2Link</c>・<c>manual</c>）。</param>
/// <param name="State"><c>confirmed</c>（確認済み）・<c>candidate</c>（推定で、まだ人が選んでいない）・<c>rejected</c>（人が消した）。</param>
/// <param name="IsOwned">そのアバターを持っているか（所持している商品か、手で「持っている」にした）。</param>
public sealed record AvatarLinkRef(string AvatarId, string Name, string Source, string State, bool IsOwned);

/// <summary>
/// 画面の検索と同じ数え方の対応アバター（人が消した紐付けと説明文のリンクを除き、まだ人が確認していない推定は数える）。
/// </summary>
/// <param name="Match"><c>direct</c>（商品が名指ししている）・<c>viaBase</c>（共通素体を通して対応する）。</param>
public sealed record CompatibleAvatarRef(string AvatarId, string Name, string Match, bool IsOwned);

/// <summary>共通素体への紐付け（素体は配布されているとは限らないので名前で持つ）。</summary>
public sealed record AvatarBaseLinkRef(string Name, string Source, string State);

public sealed record UserTagRef(string Top, IReadOnlyList<string> Subs);

/// <summary>バリエーション（BOOTH の中の値の状態・種類は約束にしない。BOOTH が変えたときに外の約束まで揺れるので）。</summary>
public sealed record VariationRef(long Id, string? Name, int Price);

/// <param name="Kind"><c>self</c>（自分用）・<c>given</c>（贈った）・<c>received</c>（貰った）。</param>
/// <param name="Price">払った額。分からなければ null（0 は無料）。</param>
/// <param name="VariationName">買った時点の名前（BOOTH から消えたバリエーションでも残る）。</param>
public sealed record PurchaseRef(long? VariationId, string? VariationName, int? Price, string Kind, DateOnly? PurchasedAt, string? Note);

/// <summary>zip の中の <c>.unitypackage</c>。</summary>
/// <param name="Roots">パッケージが入れる先（<c>Assets/…</c>）。</param>
public sealed record UnityPackageRef(string Entry, IReadOnlyList<string> Roots);

/// <summary>手元のファイル1つ（同じ中身の置き場が複数あれば <paramref name="Paths"/> に並ぶ）。</summary>
/// <param name="State">
/// <c>present</c>（ある）・<c>missing</c>（記録の上では持っているが置き場が見つからない）・
/// <c>detached</c>（管理から外した）・<c>oldVersion</c>（上書きで残った古い版）。所持に数えるのは present と missing。
/// </param>
/// <param name="Replaced">上書きされた古い版なら、上書きした物の場所と日時。</param>
/// <param name="UnityPackages">中の <c>.unitypackage</c>。まだ調べていなければ null（空は「調べて、無かった」）。</param>
/// <param name="RegisteredAt">この商品のファイルとして最初に記録した日時。v1.3.0 より前に記録した物は null（推して埋めない）。</param>
/// <param name="DownloadSource">
/// ダウンロード元の記録（Zone.Identifier）から読んだ BOOTH のダウンロード。**null はまだ読んでいない**、
/// 中の2つが null の物は「読んだが BOOTH の物と分からなかった」（保存の記録の区別をそのまま返す）。
/// </param>
/// <param name="Contents">zip の中のファイルの一覧（長くなり得る）。<c>include=contents</c> のときだけ。</param>
public sealed record FileRef(
    string Hash,
    long SizeBytes,
    long? VariationId,
    IReadOnlyList<string> Paths,
    string State,
    DateTimeOffset? MissingSince,
    bool IsArchiveBroken,
    ReplacedRef? Replaced,
    IReadOnlyList<UnityPackageRef>? UnityPackages,
    DateTimeOffset? RegisteredAt,
    DownloadSourceRef? DownloadSource,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Contents);

public sealed record ReplacedRef(string Path, DateTimeOffset At);

/// <summary>ファイルを落とした BOOTH のダウンロード（<see cref="FileRef.DownloadSource"/>）。</summary>
/// <param name="DownloadableId">BOOTH のダウンロードの ID（配布ファイルごとの番号。上げ直すと変わる）。</param>
/// <param name="ItemUrl">商品ページの URL。</param>
public sealed record DownloadSourceRef(long? DownloadableId, string? ItemUrl);

/// <summary>商品として登録したフォルダ（zip が無く、展開した物だけが残っている物）。</summary>
/// <param name="RegisteredAt">この商品のフォルダとして記録した日時（記録に無ければ null）。</param>
public sealed record FolderRef(string Path, int FileCount, long TotalBytes, DateTimeOffset? MissingSince, IReadOnlyList<string> UnityPackages, DateTimeOffset? RegisteredAt);

/// <summary>説明文に貼られた動画。題は取ってあれば。</summary>
public sealed record VideoRef(string Id, string Url, string? Title);

/// <summary>一覧の1行（要約）。詳しくは <c>/v1/items/{id}</c>。</summary>
/// <param name="AvatarIds">対応アバターの ID（画面の検索と同じ数え方。<see cref="CompatibleAvatarRef"/>）。</param>
/// <param name="PriceText">BOOTH の値段の表示（詳細の <c>priceText</c> と同じ）。並べる欄ではない。</param>
/// <param name="PublishedAt">並べる欄 <c>publishedAt</c> の値。</param>
/// <param name="AcquiredAt">
/// 並べる欄 <c>acquiredAt</c> の値（購入1件の日付のうち最も早い物、購入記録が無ければ商品の入手日）。詳細の <c>acquiredAt</c> と同じ値。
/// </param>
/// <param name="WishCount">並べる欄 <c>wishCount</c> の値。</param>
/// <param name="Paid">並べる欄 <c>paid</c> の値（自分用に払った額の合計。額を1つも入れていなければ null、無料は 0）。</param>
public sealed record ItemSummary(
    string Id,
    string Name,
    ShopRef? Shop,
    string? Category,
    bool IsOwned,
    bool IsAdult,
    bool IsHidden,
    bool IsFavorite,
    IReadOnlyList<string> AvatarIds,
    ImageRef? Thumbnail,
    string? PriceText,
    DateTimeOffset? PublishedAt,
    DateOnly? AcquiredAt,
    int WishCount,
    long? Paid);

/// <summary>商品1件（詳細）。</summary>
public sealed record ItemDetail
{
    public required string Id { get; init; }

    /// <summary>表示に使う名前（手で直した名前があればそれ。画面と同じ）。</summary>
    public required string Name { get; init; }

    /// <summary>BOOTH の商品名（取れていなければ null）。</summary>
    public required string? BoothName { get; init; }

    public required string? Url { get; init; }

    public required ShopRef? Shop { get; init; }

    /// <summary>画面と同じ実質の1つ（自分で入れたカテゴリがあればそれ、無ければ BOOTH のカテゴリ）。</summary>
    public required string? Category { get; init; }

    /// <summary><c>user</c>（自分で入れた）・<c>booth</c>。カテゴリが無ければ null。</summary>
    public required string? CategorySource { get; init; }

    public required BoothCategoryRef? BoothCategory { get; init; }

    public required IReadOnlyList<string> BoothTags { get; init; }

    public required string? PriceText { get; init; }

    public required int WishCount { get; init; }

    public required DateTimeOffset? PublishedAt { get; init; }

    public required bool IsAdult { get; init; }

    public required bool IsEndOfSale { get; init; }

    public required bool IsSoldOut { get; init; }

    /// <summary>販売終了か非公開と確定した（画面の「公開状況」の「販売終了・非公開」と同じ）。BOOTHに無い商品は false。</summary>
    public required bool IsDelisted { get; init; }

    /// <summary>仮の ID で登録した、BOOTH に無い商品。</summary>
    public required bool IsLocalOnly { get; init; }

    public required bool IsHidden { get; init; }

    public required bool IsFavorite { get; init; }

    /// <summary>手元のファイルかフォルダを1つ以上持つ（画面の所持と同じ）。</summary>
    public required bool IsOwned { get; init; }

    /// <summary>この商品がアバターとして登録簿にあるか。</summary>
    public required bool IsAvatar { get; init; }

    /// <summary>未読の「商品の更新」の知らせがあるか（カードの札「更新あり」と同じ）。</summary>
    public required bool HasUpdate { get; init; }

    /// <summary>BOOTH の商品の更新を知らせるか（商品ごとの設定。切っていれば更新の知らせを出さない）。</summary>
    public required bool NotifyOnUpdate { get; init; }

    public required bool HasBrokenArchive { get; init; }

    public required bool HasMissingFiles { get; init; }

    public required IReadOnlyList<VariationRef> Variations { get; init; }

    public required IReadOnlyList<PurchaseRef> Purchases { get; init; }

    /// <summary>
    /// バリエーションごとの向き先（人が決めた行と検出した行を合わせた結果。同じバリエーションに両方あれば人が決めた行が勝つ）。
    /// 合わせ方は画面の「持っていない分の見分け」と同じ所（<c>AvatarCompatibilityIndex.EffectiveVariationTargets</c>）で、
    /// 設定「名前から見つけた向き先も使う」を切っていれば人が決めた行だけ。出どころ別は <c>include=variationTargets</c>。
    /// </summary>
    public required IReadOnlyList<VariationTargetRef> VariationTargets { get; init; }

    /// <summary>変換ツールのプロファイルとしての扱い（人が決めた扱い・自動の判定・つなぎ先）。</summary>
    public required ConversionRef Conversion { get; init; }

    /// <summary>
    /// 入手日（画面の並べ替えと同じ：購入1件の日付のうち最も早い物、購入記録が無ければ商品の入手日の欄）。一覧の要約の <c>acquiredAt</c> と同じ値。
    /// 前は商品の欄そのもので、要約と同じ名前なのに違う値になり得た（2026-10-14・ユーザ判断で揃えた）
    /// </summary>
    public required DateOnly? AcquiredAt { get; init; }

    /// <summary>商品の入手日の欄に入れた値そのもの（購入記録の日付を見ない）。直すときに今の値を知るため。</summary>
    public required DateOnly? AcquiredAtEntered { get; init; }

    /// <summary>画面の検索と同じ数え方の対応アバター（検索の条件「対応アバター」が当てる物）。</summary>
    public required IReadOnlyList<CompatibleAvatarRef> CompatibleAvatars { get; init; }

    public required IReadOnlyList<UserTagRef> UserTags { get; init; }

    /// <summary>属性の値（評価していない属性は入らない。0 は評価して 0）。</summary>
    public required IReadOnlyDictionary<string, int> Attributes { get; init; }

    /// <summary>
    /// 付いているが値はまだの属性（0 とは別。<c>attributes</c> には入らない）。無い商品では欄ごと出さない。
    /// ほとんどの商品が持たないのと、前の版の答えと形を変えないため。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? PendingAttributes { get; init; }

    public required string? Memo { get; init; }

    /// <summary>手元のファイル（既定は「ある」「見つからない」だけ。外した物・古い版は <c>include=allFiles</c>）。</summary>
    public required IReadOnlyList<FileRef> Files { get; init; }

    public required IReadOnlyList<FolderRef> Folders { get; init; }

    /// <summary>画面のカードと同じ1枚（設定の「サムネイルにする画像」に従う）。</summary>
    public required ImageRef? Thumbnail { get; init; }

    public required IReadOnlyList<VideoRef> Videos { get; init; }

    /// <summary>画像の全部（画面の商品ページと同じ並び）。<c>include=images</c> のときだけ。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ImageRef>? Images { get; init; }

    /// <summary>対応アバターの紐付けの帳簿（出どころと、確認済み・候補・消したの状態）。<c>include=avatarLinks</c> のときだけ。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AvatarLinksRef? AvatarLinks { get; init; }

    /// <summary>記録の古さ。<c>include=meta</c> のときだけ。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ItemMetaRef? Meta { get; init; }

    /// <summary>バリエーションごとの向き先を、人が決めた行と検出した行に分けてそのまま。<c>include=variationTargets</c> のときだけ。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VariationTargetSourcesRef? VariationTargetSources { get; init; }

    /// <summary>
    /// 変換扱いのつなぎ先の帳簿：人が外した物（<c>rejected</c>）も含めた全部の行を、保存の順のまま状態つきで。<c>include=conversionTargets</c> のときだけ
    /// （対応アバターの <c>avatarLinks</c> と同じ分け方）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ConversionTargetRef>? ConversionTargets { get; init; }

    /// <summary>
    /// BOOTH で見つけた中身の更新の控え。<c>include=contentUpdate</c> のときだけ。
    /// 見つけていない商品でも頼まれれば欄は返し、中の <c>foundAt</c> を null にする（<c>meta</c> の <c>fetchedAt</c> と同じ形。
    /// 欄そのものを null にすると、頼まなかった時の「欄が無い」と書き分ける作りが直列化に要るため）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContentUpdateRef? ContentUpdate { get; init; }
}

/// <summary>バリエーション1つの向き先。</summary>
/// <param name="VariationId">どのバリエーションか。null は「バリエーションを選ばない購入」の行（人が決めた行だけが持つ）。</param>
/// <param name="Avatars">向き先のアバター（商品 ID）。</param>
/// <param name="Bases">向き先の共通素体（名前。その仲間全員に効く）。</param>
/// <param name="All">商品の対応アバター全部向け（全部入りのセットの行など）。</param>
/// <param name="RejectedAvatars">人が外した、名前から見つけたアバター。出どころ別（<c>include=variationTargets</c>）の行だけ。</param>
/// <param name="RejectedBases">人が外した、名前から見つけた共通素体。出どころ別の行だけ。</param>
public sealed record VariationTargetRef(
    long? VariationId,
    IReadOnlyList<string> Avatars,
    IReadOnlyList<string> Bases,
    bool All,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? RejectedAvatars = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? RejectedBases = null);

/// <param name="Decided">人が決めた行（保存のまま。外しただけの行・バリエーションを選ばない購入の行も）。</param>
/// <param name="Detected">バリエーションの名前から検出した行（設定に関わらず全部）。</param>
public sealed record VariationTargetSourcesRef(IReadOnlyList<VariationTargetRef> Decided, IReadOnlyList<VariationTargetRef> Detected);

/// <summary>変換扱い。</summary>
/// <param name="Mode">人が決めた扱い：<c>auto</c>（自動の判定に任せる）・<c>profile</c>（扱う）・<c>notProfile</c>（扱わない）。</param>
/// <param name="HasProfileFiles">自動の判定：手元のファイルの中にプロファイルのファイルがあるか（画面の「今の扱い：プロファイル（自動の判定）」と同じ答え）。</param>
/// <param name="Targets">
/// つなぎ先の結果（人が決めた物と、自動で推して確認待ちの物。状態つき）。人が外した物は入れない——対応アバターの <c>compatibleAvatars</c> と同じく、
/// 既定は結果だけで、確認していない推定は入れる。外した物も含む帳簿は <c>include=conversionTargets</c>。
/// </param>
public sealed record ConversionRef(string Mode, bool HasProfileFiles, IReadOnlyList<ConversionTargetRef> Targets);

/// <summary>プロファイルのつなぎ先1つ。アバターか共通素体のどちらか。</summary>
/// <param name="AvatarId">つなぎ先のアバター（商品 ID）。素体なら null。</param>
/// <param name="BaseName">つなぎ先の共通素体の名前。アバターなら null。</param>
/// <param name="Slug">中のプロファイルの名前（分からなければ null）。</param>
/// <param name="Forward">順方向：ほかのアバターの衣装を、つなぎ先に合わせられる。</param>
/// <param name="Reverse">逆方向：つなぎ先の衣装を、ほかのアバターに合わせられる。</param>
/// <param name="State"><c>confirmed</c>（人が決めた）・<c>candidate</c>（自動で推して確認待ち）・<c>rejected</c>（人が外した）。</param>
public sealed record ConversionTargetRef(string? AvatarId, string? BaseName, string? Slug, bool Forward, bool Reverse, string State);

/// <param name="FoundAt">中身の更新を見つけた日時。見つけていなければ null。</param>
/// <param name="HasNewerFiles">見つけた後に新しいファイルかフォルダを取り込んだか（<c>LocalBlock.HasFilesNewerThanUpdate</c>）。見つけていなければ false。</param>
public sealed record ContentUpdateRef(DateTimeOffset? FoundAt, bool HasNewerFiles);

/// <summary>対応アバターの紐付けの帳簿。消した紐付け・説明文のリンク（要確認）も、状態つきでそのまま返す。</summary>
/// <param name="DetectedAt">対応アバターの検出を最後に走らせた日時。</param>
public sealed record AvatarLinksRef(IReadOnlyList<AvatarLinkRef> Avatars, IReadOnlyList<AvatarBaseLinkRef> Bases, DateTimeOffset? DetectedAt);

/// <param name="FetchedAt">BOOTH の情報を最後に取った日時。</param>
public sealed record ItemMetaRef(DateTimeOffset? FetchedAt);
