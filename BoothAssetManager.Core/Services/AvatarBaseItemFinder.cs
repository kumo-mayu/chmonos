using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 共通素体そのものがBOOTHで配布されている商品の候補を探す（ユーザ指摘 2026-09-17：「素体単体を検知できていない」）。
///
/// 素体の商品IDは、手で「配布商品を結ぶ」を押したときにしか入らず、友人のデータでは11の素体すべてが「配布なし」だった。
/// 登録簿を見ると、素体の商品は「名前に素体名を含み、カテゴリが『3Dモデル（その他）』で、対応アバターの節に挙がる」形で入っていた
/// （11のうち3つで1件ずつ）。同じ名前を含んでも、カテゴリが「3Dキャラクター」の物はその素体を使ったアバター。
///
/// **自動では結ばず、候補として見せて押したら入る**（推定か観測かが後から分からなくなるため。候補の出し方の決まり）。
/// </summary>
public static class AvatarBaseItemFinder
{
    private const string CharacterCategory = "3Dキャラクター";

    /// <summary>名前が短すぎると、関係の無い商品まで当たる。</summary>
    private const int MinNameLength = 2;

    /// <summary>素体名か呼び方を商品名に含み、アバター（3Dキャラクター）ではなく、その素体に属するアバターでもない登録簿の項目。</summary>
    public static IReadOnlyList<AvatarRegistryEntry> Candidates(AvatarBaseGroup group, IEnumerable<AvatarRegistryEntry> entries)
    {
        var names = group.Aliases
            .Where(alias => !alias.Rejected)
            .Select(alias => alias.Text.Trim())
            .Prepend(group.Name.Trim())
            .Where(name => name.Length >= MinNameLength)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return entries
            .Where(entry => entry.BoothName is { } boothName
                && !string.Equals(entry.Category, CharacterCategory, StringComparison.Ordinal)
                && !string.Equals(entry.BaseName, group.Name, StringComparison.CurrentCultureIgnoreCase)
                && names.Any(name => boothName.Contains(name, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
    }
}
