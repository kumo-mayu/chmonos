using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chmonos.Query;

/// <summary>
/// 内部の一覧（<c>/v1/recent</c>・<c>/v1/folders</c>・<c>/v1/excluded</c>）を何件かずつ返す（docs/spec/local-api.md「共通の決まり」：1回 200 件まで・並びは必ず一意）。
/// 続きの印は、最後に返した1件の並べる鍵と、どの一覧の印かの名前。違う一覧の印は断る。中身は約束にしない。
/// </summary>
internal static class ListPages
{
    /// <param name="ordered">並べ終えた一覧（<paramref name="compare"/> の順で、同じ鍵が2つ無いこと）。</param>
    /// <param name="list">印に入れる一覧の名前（ほかの一覧の印を取り違えて送られたら断るため）。</param>
    public static (IReadOnlyList<T> Page, string? NextCursor) Take<T>(
        IReadOnlyList<T> ordered,
        Func<T, IReadOnlyList<string>> keyOf,
        Comparison<IReadOnlyList<string>> compare,
        string list,
        int keyLength,
        string? cursor,
        int? limit)
    {
        var size = limit ?? Search.SearchEngine.DefaultLimit;
        if (size is < 1 or > Search.SearchEngine.MaxLimit)
        {
            throw QueryException.Invalid("invalidLimit", $"limit は 1〜{Search.SearchEngine.MaxLimit} です: {size}");
        }

        var start = 0;
        if (!string.IsNullOrEmpty(cursor))
        {
            var after = Read(cursor, list, keyLength);
            while (start < ordered.Count && compare(keyOf(ordered[start]), after) <= 0)
            {
                start++;
            }
        }

        var page = ordered.Skip(start).Take(size).ToList();
        var next = start + page.Count < ordered.Count && page.Count > 0 ? Write(list, keyOf(page[^1])) : null;
        return (page, next);
    }

    private static string Write(string list, IReadOnlyList<string> key)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new ListCursor(list, key), ListCursorJsonContext.Default.ListCursor);
        return Convert.ToBase64String(json).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static IReadOnlyList<string> Read(string text, string list, int keyLength)
    {
        ListCursor? cursor;
        try
        {
            var base64 = text.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            cursor = JsonSerializer.Deserialize(Convert.FromBase64String(base64), ListCursorJsonContext.Default.ListCursor);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or DecoderFallbackException)
        {
            throw QueryException.Invalid("invalidCursor", "cursor は前の答えの nextCursor をそのまま送ってください。");
        }

        if (cursor is null || cursor.List != list || cursor.Key is null || cursor.Key.Count != keyLength || cursor.Key.Any(part => part is null))
        {
            throw QueryException.Invalid("invalidCursor", "cursor は同じ URL の前の答えの nextCursor をそのまま送ってください。");
        }

        return cursor.Key;
    }

    /// <summary>鍵の1つを数として比べる（日時の刻み・行の位置）。数でなければ印が壊れている。</summary>
    public static long Number(string part)
        => long.TryParse(part, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : throw QueryException.Invalid("invalidCursor", "cursor は前の答えの nextCursor をそのまま送ってください。");

    public static string Text(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed record ListCursor(string List, IReadOnlyList<string> Key);

/// <summary>続きの印の書き方（外に見せない型なので、答えの書き方とは分ける）。</summary>
[JsonSerializable(typeof(ListCursor))]
internal sealed partial class ListCursorJsonContext : JsonSerializerContext
{
}
