using System.Text.Json;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Booth;

/// <summary>
/// 商品JSONから <see cref="BoothBlock"/> を組み立てる。
/// BOOTHは予告なく項目を足し引きするので、欠けていても落ちないように全て防御的に読む。
///
/// 実データで確認した注意点：
/// ・トップの <c>price</c> は「¥ 2,500」という整形済み文字列なので計算に使えない（数値は variation 側）
/// ・件数の項目名は <c>wish_lists_count</c>（単数形ではない）
/// ・<c>category</c> は親を持つ2階層構造
/// ・単一バリエーションの商品では <c>variations[].name</c> が null
/// </summary>
public static class BoothItemMapper
{
    public static BoothBlock Map(
        string json,
        DateTimeOffset fetchedAt,
        IReadOnlyList<H2Section>? sections = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new BoothBlock
        {
            FetchedAt = fetchedAt,
            Name = ReadString(root, "name"),
            Description = ReadString(root, "description"),
            IsAdult = ReadBoolean(root, "is_adult"),
            IsEndOfSale = ReadBoolean(root, "is_end_of_sale"),
            IsSoldOut = ReadBoolean(root, "is_sold_out"),
            PublishedAt = ReadDateTimeOffset(root, "published_at"),
            PriceText = ReadString(root, "price"),
            WishListsCount = ReadInt32(root, "wish_lists_count"),
            Url = ReadString(root, "url"),
            Tags = ReadTags(root),
            Category = ReadCategory(root),
            Shop = ReadShop(root),
            Images = ReadImages(root),
            Embeds = ReadEmbeds(root),
            Variations = ReadVariations(root),
            H2Sections = sections ?? [],
        };
    }

    /// <summary>商品IDだけを取り出す（未確定ファイルの候補確認で、全体を組み立てずに済ませたい時に使う）。</summary>
    public static string? ReadItemId(string json)
    {
        using var document = JsonDocument.Parse(json);
        var idElement = TryGet(document.RootElement, "id");
        return idElement?.ValueKind switch
        {
            JsonValueKind.Number => idElement.Value.GetInt64().ToString(),
            JsonValueKind.String => idElement.Value.GetString(),
            _ => null,
        };
    }

    private static IReadOnlyList<string> ReadTags(JsonElement root)
    {
        var element = TryGet(root, "tags");
        if (element is not { ValueKind: JsonValueKind.Array })
        {
            return [];
        }

        var tags = new List<string>();
        foreach (var tag in element.Value.EnumerateArray())
        {
            var name = tag.ValueKind == JsonValueKind.String ? tag.GetString() : ReadString(tag, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                tags.Add(name);
            }
        }

        return tags;
    }

    private static BoothCategory? ReadCategory(JsonElement root)
    {
        var element = TryGet(root, "category");
        if (element is not { ValueKind: JsonValueKind.Object })
        {
            return null;
        }

        var name = ReadString(element.Value, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var parent = TryGet(element.Value, "parent");
        return new BoothCategory
        {
            Id = ReadInt32(element.Value, "id"),
            Name = name,
            ParentName = parent is { ValueKind: JsonValueKind.Object } ? ReadString(parent.Value, "name") : null,
        };
    }

    private static BoothShop? ReadShop(JsonElement root)
    {
        var element = TryGet(root, "shop");
        if (element is not { ValueKind: JsonValueKind.Object })
        {
            return null;
        }

        var name = ReadString(element.Value, "name");
        var subdomain = ReadString(element.Value, "subdomain");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(subdomain))
        {
            return null;
        }

        return new BoothShop
        {
            Uuid = ReadString(element.Value, "uuid"),
            Name = name,
            Subdomain = subdomain,
            Url = ReadString(element.Value, "url"),
            ThumbnailUrl = ReadString(element.Value, "thumbnail_url"),
        };
    }

    private static IReadOnlyList<BoothImage> ReadImages(JsonElement root)
    {
        var element = TryGet(root, "images");
        if (element is not { ValueKind: JsonValueKind.Array })
        {
            return [];
        }

        var images = new List<BoothImage>();
        foreach (var image in element.Value.EnumerateArray())
        {
            var original = ReadString(image, "original");
            if (string.IsNullOrWhiteSpace(original))
            {
                continue;
            }

            images.Add(new BoothImage
            {
                OriginalUrl = original,
                Caption = ReadString(image, "caption"),
            });
        }

        return images;
    }

    private static IReadOnlyList<string> ReadEmbeds(JsonElement root)
    {
        var element = TryGet(root, "embeds");
        if (element is not { ValueKind: JsonValueKind.Array })
        {
            return [];
        }

        var embeds = new List<string>();
        foreach (var embed in element.Value.EnumerateArray())
        {
            var value = embed.ValueKind == JsonValueKind.String ? embed.GetString() : embed.GetRawText();
            if (!string.IsNullOrWhiteSpace(value))
            {
                embeds.Add(value);
            }
        }

        return embeds;
    }

    private static IReadOnlyList<BoothVariation> ReadVariations(JsonElement root)
    {
        var element = TryGet(root, "variations");
        if (element is not { ValueKind: JsonValueKind.Array })
        {
            return [];
        }

        var variations = new List<BoothVariation>();
        foreach (var variation in element.Value.EnumerateArray())
        {
            var idElement = TryGet(variation, "id");
            if (idElement is not { ValueKind: JsonValueKind.Number })
            {
                continue;
            }

            variations.Add(new BoothVariation
            {
                Id = idElement.Value.GetInt64(),
                Name = ReadString(variation, "name"),
                Price = ReadInt32(variation, "price"),
                Status = ReadString(variation, "status"),
                Type = ReadString(variation, "type"),
            });
        }

        return variations;
    }

    private static JsonElement? TryGet(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return element.TryGetProperty(propertyName, out var value) ? value : null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        var value = TryGet(element, propertyName);
        return value?.ValueKind == JsonValueKind.String ? value.Value.GetString() : null;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        var value = TryGet(element, propertyName);
        return value?.ValueKind == JsonValueKind.True;
    }

    private static int ReadInt32(JsonElement element, string propertyName)
    {
        var value = TryGet(element, propertyName);
        return value?.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out var number) ? number : 0;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        var text = ReadString(element, propertyName);
        return DateTimeOffset.TryParse(text, out var value) ? value : null;
    }
}
