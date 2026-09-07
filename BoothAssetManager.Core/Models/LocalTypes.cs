namespace BoothAssetManager.Core.Models;

/// <summary>
/// appTagの割当て。サブレベルはトップレベルに従属するので、トップを外すとサブも一緒に外れる。
/// item側はIDではなく名前で参照する（JSONを直接開いて読めることを優先）。
/// リネーム時はアプリが全itemを一括で書き換える。
/// </summary>
public sealed record AppTagAssignment
{
    public required string Top { get; init; }

    public IReadOnlyList<string> Subs { get; init; } = [];
}

/// <summary>対応アバターの検出元。UIでの見せ方（確定か推定か）を分けるために持つ。</summary>
public enum AvatarLinkSource
{
    /// <summary>説明文中のBOOTH商品リンクから確定したもの。</summary>
    H2Link,

    /// <summary>variation名とavatar-registryの照合による推定。</summary>
    Variation,

    /// <summary>ユーザが手で指定したもの。</summary>
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
}

/// <summary>
/// 購入したvariationの記録。BOOTH側から消えても名前を引けるよう、購入時点の名前を写し取って自立させる。
/// </summary>
public sealed record OrderedVariation
{
    public required long VariationId { get; init; }

    /// <summary>購入時点のvariation名。BOOTH側に現存しない場合はこちらを表示に使う。</summary>
    public string? NameSnapshot { get; init; }

    /// <summary>購入価格。null は未入力、0 は無料配布。</summary>
    public int? Price { get; init; }

    public bool IsGifted { get; init; }

    /// <summary>BOOTH側の現在のvariation一覧に存在するか。消えても記録は残し、統計の支出には含める。</summary>
    public bool ExistsOnBooth { get; init; } = true;
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
}
