namespace Chmonos.Core.Models;

/// <summary>
/// ユーザ入力とローカルの状態。BOOTHの再取得では絶対に触らない。
///
/// このブロック配下をレコードにしているのは、更新時に <c>with</c> で
/// 「変えたい項目だけ」を書き換えられるようにするため。
/// 手でコピーを書くと、項目を足した時に写し忘れてユーザ入力が消える事故が起きる。
/// </summary>
public sealed record LocalBlock
{
    /// <summary>
    /// ユーザが付けた商品名。BOOTHから取れない商品のために持つ。
    ///
    /// 観測（<c>Booth.Name</c>）とは別に置く。BOOTHが復活しても両方残り、どちらも捨てずに済む。
    /// 画面には**こちらを優先して出し、ユーザによる命名であることを明記する**
    /// （<c>AvatarRegistryEntry.DisplayName</c> と同じ形）。検索は両方の名前を対象にする。
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// ユーザが入れたショップ。BOOTHから取れない商品のために持つ。
    ///
    /// **商品が非公開でもショップは見られる場合がある。**URLを貼れば本物の
    /// サブドメインが取れ、既にあるショップに正しく束ねられる。
    /// </summary>
    public LocalShop? Shop { get; init; }

    /// <summary>
    /// ユーザが入れたカテゴリ。**子の名前1つだけ。**
    ///
    /// BOOTHから取れない商品には分類が無いので、統計にも絞り込みにも出てこない。
    /// 絞り込みは子の名前だけの平坦な一覧なので、親は持たなくてよい。
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// 自分で足した画像。BOOTHの画像とは別に持つ——
    /// 混ぜると「BOOTH側から消えた画像」と区別が付かなくなり、
    /// 自分で足したのに「削除済」と出る。
    /// </summary>
    public IReadOnlyList<UserImage> UserImages { get; init; } = [];

    /// <summary>
    /// サムネイルに使う画像のファイル名。**BOOTHの画像も指名できる。**
    /// 2枚目の方が分かりやすい商品は普通にある。
    ///
    /// 指名が無ければ並びの1枚目。指名した先が消えていたら黙って1枚目に戻すが、
    /// **指名そのものは残す**——画像を取り直せば戻ってくるため。
    /// </summary>
    public string? ThumbnailImage { get; init; }

    /// <summary>
    /// 画像に付けた役割。鍵はファイル名。
    ///
    /// **付けていない画像は書かない。**付いていなければ出どころから決まる——
    /// BOOTHの画像は「BOOTH」、自分で足した画像は「その他」。
    /// 全画像分を書き出すと、観測しただけのものまで人が決めたように見える。
    ///
    /// <see cref="ImageRole.Modified"/> は自動では付かない。
    /// どれが改変後の姿かは人にしか分からない。
    /// </summary>
    public IReadOnlyDictionary<string, ImageRole> ImageRoles { get; init; }
        = new Dictionary<string, ImageRole>();

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

    /// <summary>最後に対応アバターを検出した日時。</summary>
    public DateTimeOffset? AvatarsDetectedAt { get; init; }

    /// <summary>
    /// 購入記録。買った1回が1レコード。BOOTH側からvariationが消えても残す。
    /// 同じvariationが複数行あってよい（同じものを何度も買う・複数人に贈る）。
    /// </summary>
    public IReadOnlyList<Purchase> Purchases { get; init; } = [];

    /// <summary>紐付いたファイル。この商品から外したもの（<see cref="LocalFileRecord.Detached"/>）も印付きで残る。</summary>
    public IReadOnlyList<LocalFileRecord> LocalFiles { get; init; } = [];

    /// <summary>
    /// 手元に持っているファイル（外したもの・上書きで残った古い版を除く）。所持・容量・検索・Unityへ送る対象はこちらで数える。
    /// 計算で出せるので書き出さない。
    /// </summary>
    /// <remarks>
    /// 古い版（<see cref="LocalFileRecord.IsOldVersion"/>）は同じ場所で新しい中身に置き換わり、どこにも無いと分かっている記録なので、
    /// 持ち物に数えない（ユーザ判断 2026-10-05 ⑤-B）。前は古い版だけが残った商品が「所持」のまま、容量にも古い版の大きさが足されていた。
    /// 記録は商品ページの「古い版」の行と「古い版の記録を片付ける」のために残すので、行の一覧は <see cref="LocalFiles"/> を使う。
    /// この中身がどの商品に結ばれているか（取り込みの結び直しなど）は <see cref="AttachedFiles"/>。
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<LocalFileRecord> OwnedFiles => LocalFiles.Where(IsHeld).ToList();

    /// <summary>
    /// この商品に結んだままの記録（外したものを除き、古い版は含む）。**持っているかではなく、中身がどの商品のものか**を見る所用：
    /// 取り込みの結び直し・未確定の均し・ほかの商品が同じ中身を持つかの確かめ。
    /// </summary>
    /// <remarks>
    /// 古い版を落とすと、古い版の中身をまた見つけたときに取り込みがこの商品へ結び直さず（印も下りない）、
    /// 未確定に出たり、同じ中身を2つの商品に結んだりする。所持から外すのとは別の問いなので分けた（2026-10-05 ⑤-B）。
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<LocalFileRecord> AttachedFiles => LocalFiles.Where(file => !file.Detached).ToList();

    /// <summary>
    /// 手元に持っているファイル（<see cref="OwnedFiles"/>）を1つ以上持っているか。Unity へ送る・中身を読むなど、ファイルが要る場面の問い。
    /// 「所持か」は <see cref="IsOwned"/>（フォルダも数える）。<see cref="OwnedFiles"/> は呼ぶたびに並びを作るので、有無だけならこちら。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasOwnedFiles => LocalFiles.Any(IsHeld);

    /// <summary>持ち物に数えるファイルか：外しておらず、上書きで残った古い版でもない。</summary>
    private static bool IsHeld(LocalFileRecord file) => !file.Detached && !file.IsOldVersion;

    /// <summary>
    /// 所持か。**ファイルかフォルダを1つ以上持つこと**（CLAUDE.md の定義。外したファイル・上書きで残った古い版は数えない）。
    /// 検索・改変・統計・ショップ・アバターが同じ答えを使うよう、ここ1か所で決める。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsOwned => HasOwnedFiles || LocalFolders.Count > 0;

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

    /// <summary>
    /// お気に入り（#70）。検索カードの星で切り替える（ユーザ指示）。
    /// 分類や属性とは別の、「すぐ見つけたい」だけの印。検索の「条件を追加」で絞れる。
    /// </summary>
    public bool IsFavorite { get; init; }

    /// <summary>最後にBOOTHから取得した日時。</summary>
    public DateTimeOffset? LastFetchedAt { get; init; }

    /// <summary>次回の取得予定。取得が特定の日に集中しないよう、保存時にばらつきを持たせて決める。</summary>
    public DateTimeOffset? NextFetchDueAt { get; init; }

    /// <summary>404が連続した回数。一時エラーでは増やさない。既定では3回で非公開と確定する。</summary>
    public int ConsecutiveNotFoundCount { get; init; }

    /// <summary>非公開・削除済みと確定したか。確定後も低頻度で復活チェックは続ける。</summary>
    public bool IsDelisted { get; init; }
}
