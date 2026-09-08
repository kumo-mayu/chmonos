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
    /// <summary>appTagの割当て。トップレベルは複数選べ、サブはトップごとに従属する。</summary>
    public IReadOnlyList<AppTagAssignment> AppTags { get; init; } = [];

    /// <summary>属性名 → 0-100 の連続値。キーが無いものは「未評価」で、0とは区別する。</summary>
    public IReadOnlyDictionary<string, int> Attributes { get; init; } = new Dictionary<string, int>();

    public string? Memo { get; init; }

    /// <summary>対応アバター。正はavatar-registry側で、ここは参照と表示用のキャッシュ。</summary>
    public IReadOnlyList<AvatarLink> Avatars { get; init; } = [];

    /// <summary>購入記録。BOOTH側からvariationが消えても残す。</summary>
    public IReadOnlyList<OrderedVariation> OrderedVariations { get; init; } = [];

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
