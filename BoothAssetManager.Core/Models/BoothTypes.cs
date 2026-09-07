namespace BoothAssetManager.Core.Models;

/// <summary>商品JSONの category。親カテゴリを持つ2階層構造。</summary>
public sealed class BoothCategory
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary>親カテゴリ名（例：「3Dモデル」）。親を持たない場合は null。</summary>
    public string? ParentName { get; init; }
}

public sealed class BoothShop
{
    public string? Uuid { get; init; }

    public required string Name { get; init; }

    /// <summary>ショップのサブドメイン。ショップ単位の集計キーに使う。</summary>
    public required string Subdomain { get; init; }

    public string? Url { get; init; }

    public string? ThumbnailUrl { get; init; }
}

public sealed class BoothImage
{
    /// <summary>元画像のURL。ローカルの保存名（ハッシュ8桁）はこのURLから導出する。</summary>
    public required string OriginalUrl { get; init; }

    public string? Caption { get; init; }
}

/// <summary>
/// 商品のバリエーション。単一バリエーションの商品では <see cref="Name"/> が null になる。
/// </summary>
public sealed class BoothVariation
{
    public required long Id { get; init; }

    public string? Name { get; init; }

    /// <summary>価格（数値）。商品トップの price と違いこちらは計算に使える。</summary>
    public int Price { get; init; }

    /// <summary>BOOTHのステータス文字列（例：addable_to_cart）。</summary>
    public string? Status { get; init; }

    /// <summary>digital / physical などの種別。</summary>
    public string? Type { get; init; }
}
