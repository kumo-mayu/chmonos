namespace Chmonos.Core.Models;

/// <summary>
/// userTagの割当て。サブレベルはトップレベルに従属するので、トップを外すとサブも一緒に外れる。
/// item側はIDではなく名前で参照する（JSONを直接開いて読めることを優先）。
/// リネーム時はアプリが全itemを一括で書き換える。
/// </summary>
public sealed record UserTagAssignment
{
    public required string Top { get; init; }

    public IReadOnlyList<string> Subs { get; init; } = [];
}

/// <summary>
/// 対応アバターをどこから拾ったか。UIでの見せ方（確定か推定か）と、
/// 再検出で置き換えてよいかの判断に使う。
///
/// 実測での確からしさが違うので、既定の confirmed もここで変える。
/// </summary>
public enum AvatarLinkSource
{
    /// <summary>説明文の「対応アバター」節から。出品者の明示的な宣言なので最も強い。</summary>
    SupportSection,

    /// <summary>BOOTHのタグとの照合。</summary>
    Tag,

    /// <summary>variation名との照合。</summary>
    Variation,

    /// <summary>説明文中のその他の見出しの下にあったリンク。実測の適合率が低いので要確認へ回す。</summary>
    H2Link,

    /// <summary>
    /// 説明文の対応一覧。h2 を使わずに書かれた対応の一覧（本文中の「【対応アバター】」の行の下、
    /// 平文の説明文、アバターの行が5行以上続く一覧）から拾ったもの。
    ///
    /// <see cref="SupportSection"/> とは分けて持つ。対応アバター節から挙がったかどうかで
    /// 「3Dモデル（その他）」「VRoid」をアバターとして受け入れるかが変わるので、そこへ混ぜない。
    /// </summary>
    SupportList,

    /// <summary>ユーザが手で指定した／手で消したもの。再検出で置き換えない。</summary>
    Manual,
}

/// <summary>
/// 対応アバターへの参照。アバターはBOOTH商品なので自然キー（商品ID）で参照する。
/// <see cref="Name"/> は表示用のキャッシュで、正はavatar-registry側。
/// </summary>
public sealed record AvatarLink
{
    public required string AvatarItemId { get; init; }

    public string? Name { get; init; }

    public AvatarLinkSource Source { get; init; }

    /// <summary>ユーザが確認済みか。未確認の推定は「要確認」に出す。</summary>
    public bool Confirmed { get; init; }

    /// <summary>
    /// ユーザがこの対応を消したという記録。
    /// 消しただけだと次の検出で復活するので、Manual として残して検出から除く。
    /// </summary>
    public bool Rejected { get; init; }
}

/// <summary>
/// 共通素体への対応宣言。素体は配布されているとは限らずBOOTH商品IDが無いので、
/// アバター（ID参照）とは別の配列に、名前で持つ。
/// </summary>
public sealed record AvatarBaseLink
{
    public required string BaseName { get; init; }

    public AvatarLinkSource Source { get; init; }

    public bool Confirmed { get; init; }

    public bool Rejected { get; init; }
}

/// <summary>
/// その購入が誰のためのものだったか。
///
/// JSONには日本語で書く。この値は「自分用か、贈ったか、貰ったか」という
/// 人が判断して入れるものなので、手で開いたときにそのまま読めて直せる方がよい
/// （JSONは人が読める形を保つ、という方針の一部）。
/// </summary>
public enum PurchaseKind
{
    /// <summary>自分用。既定。</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("自分用")]
    ForSelf,

    /// <summary>人から貰った。手元にファイルが来るが、自分は払っていない。</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("貰った")]
    Received,

    /// <summary>人に贈った。払ったがファイルは手元に来ない。</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("贈った")]
    Given,
}

/// <summary>
/// 買った1回ぶんの記録。
///
/// 旧形式は variationId をキーにしていたので、同じ商品を2回買った記録が持てなかった。
/// 3人に同じものを贈れば3行になり、支出も3回ぶんになる。
/// キーにしていると3回目が1回目を上書きして支出が1/3になる。
/// この制約は贈答に限らず、増刷や買い直しでも踏む。
///
/// BOOTH側から消えても名前を引けるよう、購入時点の名前を写し取って自立させる。
/// </summary>
public sealed record Purchase
{
    /// <summary>
    /// どのバリエーションを買ったか。**分からなければ null。**
    ///
    /// 「分からない」は「無い」ではない。BOOTHから取れない商品にはバリエーションが1件も無く、
    /// それでも買った金額は分かっている。仮のバリエーションを作ると、後で本物のIDへ
    /// 寄せたときに本物と並んで残り、二重計上に見える。
    ///
    /// バリエーション単位の販売終了もあるので、**普通の商品でも起こり得る**——
    /// 買ったあとにそのバリエーションが消えると、後から記録を入れる行が存在しなくなる。
    /// </summary>
    public long? VariationId { get; init; }

    /// <summary>購入時点のvariation名。BOOTH側に現存しない場合はこちらを表示に使う。</summary>
    public string? NameSnapshot { get; init; }

    /// <summary>購入価格。0 は無料。編集画面の空欄は 0 で記録する（2026-09-29）。null は読めなかった値・手で消した JSON。</summary>
    public int? Price { get; init; }

    public PurchaseKind Kind { get; init; } = PurchaseKind.ForSelf;

    /// <summary>誰に贈ったか、などの覚え書き。</summary>
    public string? Note { get; init; }

    /// <summary>BOOTH側の現在のvariation一覧に存在するか。消えても記録は残し、統計の支出には含める。</summary>
    public bool ExistsOnBooth { get; init; } = true;

    /// <summary>
    /// 自分の財布から出たか。贈答は出ているが、貰い物は出ていない。
    /// 計算で出るものなので保存しない（書くと、手で直せる値だと誤解される）。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsOwnSpending => Kind is PurchaseKind.ForSelf or PurchaseKind.Given;

    /// <summary>
    /// <see cref="ExistsOnBooth"/> を今のバリエーション一覧から計算し直す。
    ///
    /// この項目は**人が決める値ではない**のに、編集画面と再取得の両方が書く経路にある。
    /// 片方が開いた時点の写しを持ち回ると、もう片方が入れた値を古い写しで上書きしてしまう。
    /// **保存する側が毎回ここを通して導けば、2人が書いても食い違いようがない。**
    ///
    /// 消えたバリエーションの記録自体は消さない。実際に払っているので支出には残す。
    /// </summary>
    public static IReadOnlyList<Purchase> Reconcile(
        IReadOnlyList<Purchase> purchases,
        IReadOnlyList<BoothVariation> variations)
    {
        if (purchases.Count == 0)
        {
            return purchases;
        }

        var present = variations.Select(variation => variation.Id).ToHashSet();

        // バリエーションを指していない記録は照合しない。
        // 指していないものが「消えた」ことにはならないので、常に現存扱いにする
        return purchases
            .Select(purchase =>
            {
                var exists = purchase.VariationId is not { } id || present.Contains(id);
                return purchase.ExistsOnBooth == exists ? purchase : purchase with { ExistsOnBooth = exists };
            })
            .ToList();
    }
}

/// <summary>zip の中の <c>.unitypackage</c> 1つの要約（<see cref="LocalFileRecord.UnityPackages"/>）。</summary>
public sealed record UnityPackageSummary
{
    /// <summary>zip の中の場所（区切りは <c>/</c>）。</summary>
    public required string Entry { get; init; }

    /// <summary>入る先の一番上（<c>Assets/FUKA</c> など）を多い順に。読めなかった物は空。</summary>
    public IReadOnlyList<string> Roots { get; init; } = [];
}

/// <summary>
/// ローカルに持っているファイル1件。同一性はハッシュで、同じ中身が複数箇所にあれば
/// 1レコードが複数の <see cref="Paths"/> を持つ。移動はパスの差し替えとして扱う。
/// </summary>
public sealed record LocalFileRecord
{
    /// <summary>SHA-256（大文字16進）。このレコードの同一性そのもの。</summary>
    public required string Hash { get; init; }

    /// <summary>保存場所。通常は1つ、同じ中身を複数箇所に置いていれば複数になる。</summary>
    public required IReadOnlyList<string> Paths { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>どのvariationのファイルかの紐付け。任意で、判別できなければ null のままでよい。</summary>
    public long? VariationId { get; init; }

    /// <summary>アーカイブ内のファイル名一覧。欠落復旧の照合と、動作環境の推測に使う。</summary>
    public IReadOnlyList<string> Contents { get; init; } = [];

    /// <summary>
    /// 中の <c>.unitypackage</c> ごとの、Unity のどこに入るか（2026-09-13 ユーザ判断）。**まだ読んでいなければ null**
    /// （unitypackage の無い zip も null のまま）。取り込みの裏で1度だけ読んで書く——中身はハッシュが同じなら変わらない。
    ///
    /// 全部のパスはここに書かず、別の控え（<c>unitypackages/&lt;ハッシュ&gt;.json</c>）に置く。1つで千本を超える物があり
    /// （手元の最多 1,766 本）、ここに書くと全商品を読む検索のメモリと、人が読める形を損なう。
    /// 入り先は計算で出せる値だが、zip を消した後も「どこに入れた物か」として役に立つので、<see cref="Contents"/> と同じく記録にする
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UnityPackageSummary>? UnityPackages { get; init; }

    /// <summary>
    /// 「この商品から外した」印（ユーザ判断 2026-09-12）。**外しても行は消さない。**
    /// 手掛かりでこの商品に紐付いていたこと自体は確かなので、消すと何を外したのかが見えなくなる。
    /// 印の付いたファイルは所持・容量・検索・Unityへ送る対象に数えず（<see cref="LocalBlock.OwnedFiles"/>）、
    /// 取り込みはこの商品へ戻さない。以前は別の detached.json に持っていた。
    /// 外していなければ書き出さない（全ファイルに false が並ぶと読みにくい）。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Detached { get; init; }

    /// <summary>
    /// 取り込みで zip として開けなかったか（途中で切れたダウンロード・中身がでたらめ）。未確定の記録の同じ印
    /// （<see cref="UnresolvedFile.ArchiveBroken"/>）を、商品に結び付いた後も持つ（ユーザ判断 2026-09-30）。
    ///
    /// ダウンロード元の記録から商品が1つに決まると未確定を通らないので、前は印がどこにも出ず、
    /// 商品ページでは中身の一覧が空なだけだった（「一時的に展開して開く」を押して初めて分かった）。
    /// **開いてみないと分からない事実**なので記録に持つ（商品ページを開くたびに zip を開き直さない）。
    /// 同じ中身は何度開いても同じ答えなので、消えるのは落とし直して別の中身（別の記録）になったとき。
    /// 開けた物には書き出さない（全ファイルに false が並ぶと読みにくい）。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ArchiveBroken { get; init; }

    /// <summary>
    /// 記録のどの場所にも無いと見た日時（ユーザ判断 2026-10-04）。また見つかったら消す。無い間は最初に見た日時のまま。
    /// フォルダの <see cref="LocalFolderRecord.MissingSince"/> に倣う。
    /// </summary>
    /// <remarks>
    /// 前は「無い」が記録に残るのが、取り込みがその商品のファイルを扱って場所を外したとき（<c>LocalFileMerger</c>）だけで、
    /// 手で zip を消しても、カードの印・検索の条件・統計に出ず、その場でディスクを見る商品ページとだけ食い違っていた。
    /// 書くのは、取り込みのたびに記録の場所を全部見たとき（<c>ImportPipeline</c>）と、使おうとして無いと分かったとき（商品ページ・開く・送る）。
    /// どちらも <see cref="Services.FileMissingMarks"/> の1つの決まりで当てる。場所はこの欄のために外さない（覚えている場所を消さない）。
    /// つながっていないドライブの上にしか場所が無いときは書かない（無くなったのではなく、今は見えないだけ）。
    /// 計算では出せない（ディスクを見た結果）ので JSON に書く。無ければ書き出さない。
    /// </remarks>
    public DateTimeOffset? MissingSince { get; init; }
}
