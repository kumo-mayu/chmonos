namespace BoothAssetManager.Core.Models;

/// <summary>
/// ユーザ入力とローカルの状態。BOOTHの再取得では絶対に触らない。
///
/// このブロック配下をレコードにしているのは、更新時に <c>with</c> で
/// 「変えたい項目だけ」を書き換えられるようにするため。
/// 手でコピーを書くと、項目を足した時に写し忘れてユーザ入力が消える事故が起きる。
/// </summary>
public sealed record LocalBlock
{
    /// <summary>userTagの割当て。トップレベルは複数選べ、サブはトップごとに従属する。</summary>
    public IReadOnlyList<UserTagAssignment> UserTags { get; init; } = [];

    /// <summary>属性名 → 0-100 の連続値。キーが無いものは「未評価」で、0とは区別する。</summary>
    public IReadOnlyDictionary<string, int> Attributes { get; init; } = new Dictionary<string, int>();

    public string? Memo { get; init; }

    /// <summary>
    /// 出品者が宣言した対応アバター。正はavatar-registry側で、ここは参照と表示用のキャッシュ。
    /// 再検出では Manual 以外を作り直す。
    /// </summary>
    public IReadOnlyList<AvatarLink> Avatars { get; init; } = [];

    /// <summary>共通素体への対応宣言。素体は商品IDを持たないことがあるので名前で参照する。</summary>
    public IReadOnlyList<AvatarBaseLink> AvatarBases { get; init; } = [];

    /// <summary>ユーザ自身が実際に着せた記録。検出は絶対に触らない。</summary>
    public IReadOnlyList<AvatarUsage> UsedOn { get; init; } = [];

    /// <summary>最後に対応アバターを検出した日時。</summary>
    public DateTimeOffset? AvatarsDetectedAt { get; init; }

    /// <summary>
    /// 購入記録。買った1回が1レコード。BOOTH側からvariationが消えても残す。
    /// 同じvariationが複数行あってよい（同じものを何度も買う・複数人に贈る）。
    /// </summary>
    public IReadOnlyList<Purchase> Purchases { get; init; } = [];

    /// <summary>
    /// 旧形式の購入記録。読み込みのためだけに置いてある。
    ///
    /// 読み込み時に <see cref="Purchases"/> へ移し、ここは null にする。
    /// null のときは書き出されないので、一度保存すれば古い形は消える。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("orderedVariations")]
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OrderedVariation>? LegacyOrderedVariations { get; init; }

    public IReadOnlyList<LocalFileRecord> LocalFiles { get; init; } = [];

    /// <summary>
    /// フォルダとして所有しているもの。zipが残っていない展開済みの配布物に使う。
    /// 配下のファイルはスキャン対象から外れる。
    /// </summary>
    public IReadOnlyList<LocalFolderRecord> LocalFolders { get; init; } = [];

    /// <summary>入手日。未入力ならファイルの日付にフォールバックする（統計の時系列にも使う）。</summary>
    public DateOnly? AcquiredAt { get; init; }

    public bool NotifyOnUpdate { get; init; } = true;

    /// <summary>非表示。検索とショップ件数からは除くが、統計の金額には含める。</summary>
    public bool IsHidden { get; init; }

    /// <summary>最後にBOOTHから取得した日時。</summary>
    public DateTimeOffset? LastFetchedAt { get; init; }

    /// <summary>次回の取得予定。取得が特定の日に集中しないよう、保存時にばらつきを持たせて決める。</summary>
    public DateTimeOffset? NextFetchDueAt { get; init; }

    /// <summary>404が連続した回数。一時エラーでは増やさない。既定では3回で非公開と確定する。</summary>
    public int ConsecutiveNotFoundCount { get; init; }

    /// <summary>非公開・削除済みと確定したか。確定後も低頻度で復活チェックは続ける。</summary>
    public bool IsDelisted { get; init; }
}
