using System.Text.RegularExpressions;
using Chmonos.Core.Models;

namespace Chmonos.Core.Storage;

/// <summary>
/// 外のソフトの情報の付け先（docs/spec/local-api-extensions.md「付け先」）。<paramref name="Kind"/> はフォルダの名前、<paramref name="Key"/> はファイルの名前になる。
/// 作るのは決まりの形を通った鍵だけ（URL の文字のまま場所を組まない）。在るかは呼び手が写しで確かめる
/// </summary>
public sealed record ExtensionTarget
{
    public const string Items = "items";
    public const string Modifications = "modifications";
    public const string Files = "files";
    public const string Shops = "shops";
    public const string GlobalKind = "global";

    // BOOTH のサブドメイン（英小文字・数字・ハイフン。DNS の1段は63字まで）か、名前から作った local- の鍵（LocalShopKey）
    private static readonly Regex ShopKeyPattern = new(@"\A[a-z0-9][a-z0-9-]{0,62}\z", RegexOptions.CultureInvariant);

    // ファイルの鍵は SHA-256 の16進（FileHasher は大文字で書く）
    private static readonly Regex FileHashPattern = new(@"\A[0-9A-F]{64}\z", RegexOptions.CultureInvariant);

    private ExtensionTarget(string kind, string key)
    {
        Kind = kind;
        Key = key;
    }

    public string Kind { get; }

    public string Key { get; }

    /// <summary>ソフトごとの全体（鍵は名前空間そのものなので、ファイルは名前空間のフォルダの直下に1つ）。</summary>
    public static ExtensionTarget Global { get; } = new(GlobalKind, GlobalKind);

    public static ExtensionTarget? Item(string? id) => StoreIds.IsItemId(id) ? new(Items, id) : null;

    public static ExtensionTarget? Modification(string? id) => StoreIds.IsModificationId(id) ? new(Modifications, id) : null;

    /// <summary>大文字にそろえる（同じファイルが大小の違いで2つの付け先にならないように）。</summary>
    public static ExtensionTarget? File(string? hash)
        => hash?.ToUpperInvariant() is { } upper && FileHashPattern.IsMatch(upper) ? new(Files, upper) : null;

    /// <summary>
    /// 小文字にそろえる（ショップの一覧は大小を区別しない）。<c>con</c>・<c>nul</c> などは Windows がファイルの名前に使えないので断る
    /// （そういうサブドメインのショップには値を置けない。名前を変えて逃がすと、鍵から場所が一通りに決まらなくなる）
    /// </summary>
    public static ExtensionTarget? Shop(string? key)
        => key?.ToLowerInvariant() is { } lower && ShopKeyPattern.IsMatch(lower) && !ExtensionNamespace.IsDeviceName(lower) ? new(Shops, lower) : null;

    public override string ToString() => $"{Kind}/{Key}";
}
