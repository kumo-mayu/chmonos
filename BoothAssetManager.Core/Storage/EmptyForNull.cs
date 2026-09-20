using System.Reflection;
using System.Text.Json.Serialization.Metadata;

namespace BoothAssetManager.Core.Storage;

/// <summary>
/// 手で書いた JSON の <c>"tags": null</c> を、**空として受ける**。
///
/// JSONは人が開いて直せる形を保つ方針なので、欠けた配列で落ちてはいけない（CLAUDE.md）。
/// 既定の <c>= []</c> は「欄そのものが無いとき」にしか効かず、
/// <c>null</c> と書いてあると init セッタがそれで上書きしてしまう。
/// 落ちるのは読んだ瞬間ではなく、後から一覧・検索・統計が触ったとき（NullReferenceException）で、
/// どの商品が原因かも分からない。2026-09-18 に3か所だけ手で塞いだが、
/// 同じ形の欄が40以上あり、新しく足した欄で同じ事故が起きる。
///
/// **<c>?</c> を付けて宣言した欄は触らない。**「まだ調べていない（null）」と「調べて0件だった（空）」を
/// 区別している欄があるため（<see cref="Models.LocalFileRecord.UnityPackages"/> など）。
/// </summary>
internal static class EmptyForNull
{
    public static void Apply(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        var nullability = new NullabilityInfoContext();
        foreach (var property in info.Properties)
        {
            if (property.Set is not { } set || AllowsNull(property, nullability))
            {
                continue;
            }

            if (CreateEmpty(property.PropertyType) is not { } empty)
            {
                continue;
            }

            property.Set = (target, value) => set(target, value ?? empty);
        }
    }

    /// <summary>空の入れ物を1つだけ作って使い回す（読み取り専用なので共有してよい）。</summary>
    private static object? CreateEmpty(Type type)
    {
        if (!type.IsInterface || !type.IsGenericType)
        {
            return null;
        }

        var arguments = type.GetGenericArguments();
        var definition = type.GetGenericTypeDefinition();

        if (definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>)
            || definition == typeof(IEnumerable<>) || definition == typeof(IList<>) || definition == typeof(ICollection<>))
        {
            return Array.CreateInstance(arguments[0], 0);
        }

        if (definition == typeof(IReadOnlyDictionary<,>) || definition == typeof(IDictionary<,>))
        {
            return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments));
        }

        return null;
    }

    private static bool AllowsNull(JsonPropertyInfo property, NullabilityInfoContext nullability)
    {
        if (property.AttributeProvider is not PropertyInfo reflected)
        {
            // 出どころが分からない欄は触らない（位置指定のレコードなど）
            return true;
        }

        return nullability.Create(reflected).WriteState != NullabilityState.NotNull;
    }
}
