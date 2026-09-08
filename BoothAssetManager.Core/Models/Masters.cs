namespace BoothAssetManager.Core.Models;

/// <summary>
/// appTagのマスタ（<c>appTags.json</c>）。トップレベルとサブレベルの2階層に限定する。
/// item側は名前で参照するので、ここでの改名は全itemの一括書き換えを伴う。
/// </summary>
public sealed class AppTagMaster
{
    public IReadOnlyList<AppTagTop> Tops { get; init; } = [];
}

public sealed class AppTagTop
{
    public required string Name { get; init; }

    public string? Memo { get; init; }

    public IReadOnlyList<AppTagSub> Subs { get; init; } = [];
}

public sealed class AppTagSub
{
    public required string Name { get; init; }

    public string? Memo { get; init; }
}

/// <summary>
/// 属性のマスタ（<c>attributes.json</c>）。値そのものはitem側に持つので、ここは表示名とメモだけ。
/// 使用実績ゼロの属性も作れるようにするため、使用状況からの導出ではなくファイルとして持つ。
/// </summary>
public sealed class AttributeMaster
{
    public IReadOnlyList<AttributeDefinition> Attributes { get; init; } = [];
}

public sealed class AttributeDefinition
{
    public required string Name { get; init; }

    /// <summary>付け方の基準を書いておく場所。主観的な尺度なので基準がぶれるのを防ぐ。</summary>
    public string? Memo { get; init; }
}

/// <summary>
/// ショップのバナーを調べた記録（<c>shop-banners.json</c>）。
///
/// バナーのURLはショップページのHTMLにしか無く、ファイル名は乱数（UUID v4）なので
/// 商品の情報からは導けない。取りに行った事実を残しておかないと、バナーを持たない
/// ショップを開くたびに100KB超のHTMLを読み直すことになる。
/// </summary>
public sealed record ShopBannerRecord
{
    public required string Subdomain { get; init; }

    /// <summary>
    /// 最後に見たとき、BOOTHにバナーが置いてあったか。
    /// false でも手元のファイルは消さない（BOOTH側から消えた画像は取り直せないため、
    /// 商品画像と同じくアーカイブとして残す）。
    /// </summary>
    public required bool HasBanner { get; init; }

    /// <summary>取得元。差し替えられたかを次回に見分けるために残す。</summary>
    public string? SourceUrl { get; init; }

    /// <summary>
    /// 最後に確かめた日時。一定期間が過ぎたら見に行き直す。
    /// 後から付けたショップもあれば、たまたまその時だけ出ていなかったこともあるので、
    /// 一度の結果で永久に決めつけない。通信に失敗したときはここを更新しない。
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }
}

/// <summary>
/// アバターの登録簿（<c>avatar-registry.json</c>）。対応アバターの名寄せと共通素体の関係を持つ。
/// ネットワーク取得を伴って少しずつ育つ知識なので、毎回作り直さず永続化する。
/// </summary>
public sealed class AvatarRegistry
{
    public IReadOnlyList<AvatarRegistryEntry> Entries { get; init; } = [];

    /// <summary>共通素体のグループ。名前をキーにする（BOOTH商品とは限らないため）。</summary>
    public IReadOnlyList<AvatarBaseGroup> BaseGroups { get; init; } = [];
}

/// <summary>
/// 調べたBOOTH商品1件。アバターも、アバターでなかったものも同じ場所に置く。
///
/// ここに保存するのは「BOOTHから観測した事実」と「人が明示的に決めたこと」だけで、
/// 判定結果（アバターとして扱うか）は保存しない。判定規則を直したときに
/// 全件を取り直さずに済ませるため。
/// </summary>
public sealed record AvatarRegistryEntry
{
    /// <summary>BOOTH商品ID。アバターは実在の商品なので、これを自然キーにする。</summary>
    public required string ItemId { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>
    /// BOOTHのcategory名をそのまま。判定に使うのは規則側で、ここは観測した事実。
    /// 販売終了などで引けなかった場合は null（「categoryが無い」ではなく「今は観測できない」）。
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// どの文脈で候補に挙がったか（source名 → 回数）。
    ///
    /// 「対応アバター」節から挙がったかどうかで受け入れるcategoryが変わるので、
    /// itemを全件走査せずに判定できるようここに写しておく。
    /// 全件検出のたびに数え直す値で、照合の判断そのものには使わない。
    /// </summary>
    public IReadOnlyDictionary<string, int> SeenAs { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// アバターとして扱うかの上書き。null は「上書きしていないので規則に従う」。
    /// 規則で拾えない例外（販売終了で判定材料が無いものなど）のためだけに使う。
    /// </summary>
    public bool? AvatarOverride { get; init; }

    /// <summary>所属する共通素体グループの名前。持たない／未設定なら null。</summary>
    public string? BaseName { get; init; }

    /// <summary>
    /// 手で「所有している」と指定したか。
    /// 所持itemからの計算と和を取る（本体を取り込んでいないアバターや、BOOTH外で入手したもの用）。
    /// </summary>
    public bool IsOwnedManually { get; init; }

    public string? Memo { get; init; }

    /// <summary>最後にBOOTHへ問い合わせた日時。</summary>
    public DateTimeOffset? CheckedAt { get; init; }

    /// <summary>実際に呼ばれていた表記の履歴。タグやvariation名との照合に使う。</summary>
    public IReadOnlyList<AvatarAlias> Aliases { get; init; } = [];
}

/// <summary>
/// 共通素体のグループ。
///
/// 名前をキーにするのは、配布されていない共通素体が実在するため
/// （同じ作者のScale違いなど。実測では素体本体が商品として無いグループが複数あった）。
/// 配布されている場合だけ <see cref="ItemId"/> を併せ持つ。
/// </summary>
public sealed record AvatarBaseGroup
{
    public required string Name { get; init; }

    /// <summary>素体そのものがBOOTH商品として配布されている場合のID。無ければ null。</summary>
    public string? ItemId { get; init; }

    /// <summary>
    /// このグループの一致から衣装の互換を推し量ってよいか。
    ///
    /// 既定は true。false にするのは頭部などの部位規格で、
    /// 一致しても衣装が合うとは限らないもの（+Head など）。
    /// </summary>
    public bool InferClothing { get; init; } = true;

    public string? Memo { get; init; }

    public IReadOnlyList<AvatarAlias> Aliases { get; init; } = [];
}

public sealed record AvatarAlias
{
    public required string Text { get; init; }

    /// <summary>
    /// 最後の全件検出時点で、この表記を含んでいたitem数。加算ではなく数え直す。
    /// 根拠の表示用で、照合の判断には使わない。
    /// </summary>
    public int Count { get; init; }

    /// <summary>どこから覚えた表記か。誤った別名を消すときの判断材料。</summary>
    public string? Source { get; init; }
}
