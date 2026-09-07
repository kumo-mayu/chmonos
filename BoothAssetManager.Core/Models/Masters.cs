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
/// アバターの登録簿（<c>avatar-registry.json</c>）。対応アバターの名寄せと共通素体の関係を持つ。
/// ネットワーク取得を伴って少しずつ育つ知識なので、毎回作り直さず永続化する。
/// </summary>
public sealed class AvatarRegistry
{
    public IReadOnlyList<AvatarRegistryEntry> Entries { get; init; } = [];
}

public sealed class AvatarRegistryEntry
{
    /// <summary>BOOTH商品ID。アバターは実在の商品なので、これを自然キーにする。</summary>
    public required string ItemId { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>categoryが3Dキャラクターだと確認できたか。false は「確認したが該当しない」を意味する。</summary>
    public bool IsAvatar { get; init; }

    /// <summary>確認した日時。設定されていれば再確認は不要（該当しない場合も含めたキャッシュ）。</summary>
    public DateTimeOffset? CheckedAt { get; init; }

    /// <summary>このアバターが使っている共通素体への参照。素体を持たない/未設定なら null。</summary>
    public string? BaseItemId { get; init; }

    /// <summary>これ自体が素体として扱われるか。</summary>
    public bool IsBase { get; init; }

    /// <summary>実際に呼ばれていた表記の履歴。variation名との照合に使う。</summary>
    public IReadOnlyList<AvatarAlias> Aliases { get; init; } = [];
}

public sealed class AvatarAlias
{
    public required string Text { get; init; }

    /// <summary>この表記を見かけた回数。</summary>
    public int Count { get; init; }
}
