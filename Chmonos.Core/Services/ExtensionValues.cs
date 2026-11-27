using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 外のソフトの情報の値を、今の宣言に当てて確かめ・合わせる（docs/spec/local-api-extensions.md「中身の形」「URL と答えの形」）。
/// どれも錠の中で今の宣言と値に当てる関数として使う（<c>UiCommand.ChangeExtensionValues</c>）。宣言を錠の外で読むと、
/// 外した直後の欄へ書けたり、型を変えた欄に古い型の値が入ったりする
/// </summary>
public static class ExtensionValues
{
    /// <summary>
    /// 値を合わせる。<paramref name="replace"/> が真なら PUT（今の宣言の欄を丸ごと置き換える）、偽なら PATCH（名指しした欄だけ。null で消す）。
    /// **PUT でも宣言から外した欄の値は残す**（spec「宣言を変えた時」：値は残して隠す。消すのは欄を消す操作と DELETE だけ）。
    /// 大きさは隠した値も含めた書いた後の物で見る（外すだけで上限を超えて溜められないように）
    /// </summary>
    public static (Dictionary<string, JsonElement>? Values, ExtensionProblem? Problem) Apply(
        ExtensionSchemaFile? schema,
        ExtensionValueFile? current,
        IReadOnlyDictionary<string, JsonElement> input,
        bool replace)
    {
        var fields = (schema?.Fields ?? []).ToDictionary(field => field.Key, StringComparer.Ordinal);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in current?.Values ?? [])
        {
            if (!replace || !fields.ContainsKey(key))
            {
                result[key] = value;
            }
        }

        foreach (var (key, value) in input)
        {
            if (!fields.TryGetValue(key, out var field))
            {
                return (null, new ExtensionProblem(400, "unknownField", $"欄「{key}」は宣言にありません（先に PUT …/schema で宣言してください）。"));
            }

            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                result.Remove(key);
                continue;
            }

            if (Check(field, value) is { } problem)
            {
                return (null, problem);
            }

            result[key] = value.Clone();
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ExtensionValueFile { Values = result }, Storage.JsonStore.Options).Length;
        return bytes > ExtensionLimits.ValueBytes
            ? (null, TooLarge($"値が大きすぎます（{bytes:N0} バイト。上限は宣言から外した欄の値も含めて {ExtensionLimits.ValueBytes:N0} バイト）。"))
            : (result, null);
    }

    /// <summary>
    /// 答えに出す値。宣言から外した欄と表から外した列は、<paramref name="showHidden"/>（持ち主が頼んだ時）でなければ入れない
    /// （ほかのソフトの答えに出さない。spec「宣言を変えた時」）
    /// </summary>
    public static IReadOnlyDictionary<string, JsonElement> Visible(ExtensionSchemaFile? schema, ExtensionValueFile? file, bool showHidden)
    {
        var values = file?.Values ?? [];
        if (showHidden)
        {
            return new SortedDictionary<string, JsonElement>(values, StringComparer.Ordinal);
        }

        var result = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in schema?.Fields ?? [])
        {
            if (values.TryGetValue(field.Key, out var value))
            {
                result[field.Key] = field.Type == ExtensionFieldTypes.Table ? VisibleRows(field, value) : value;
            }
        }

        return result;
    }

    /// <summary>
    /// 値の ETag。保存した値の全部（隠した欄も）から作るので、答えに隠した欄を入れたかどうかで変わらない
    /// （If-Match はどちらの答えの印でも同じ物に当たる）。並びは欄の名前の順にそろえる
    /// </summary>
    public static string TagOf(ExtensionValueFile? file)
        => Tag(JsonSerializer.SerializeToUtf8Bytes(new SortedDictionary<string, JsonElement>(file?.Values ?? [], StringComparer.Ordinal)));

    /// <summary>メモ欄の ETag（値とは別。片方を書いても、もう片方の If-Match が外れない）。</summary>
    public static string TagOf(ExtensionMemoFile? file)
        => Tag(Encoding.UTF8.GetBytes(file?.Memo is { ValueKind: not JsonValueKind.Null } memo ? memo.GetRawText() : "null"));

    /// <summary>
    /// If-Match が今の印に当たるか（強い比べ方。RFC 9110 §13.1.1：書き込みの前提には弱い印を使わない）。<c>*</c> は値が無くても当てる
    /// （「在れば」の意味だが、付け先は在るので、値の有無で断ると最初の書き込みができない）
    /// </summary>
    public static bool Matches(string? ifMatch, string tag)
        => string.IsNullOrWhiteSpace(ifMatch)
            || ifMatch.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(one => one == "*" || one == tag);

    public static ExtensionProblem TooLarge(string message) => new(413, "extensionTooLarge", message);

    public static ExtensionProblem PreconditionFailed()
        => new(412, "preconditionFailed", "値は読んだ後に変わっています。読み直してから、もう一度送ってください。");

    private static string Tag(byte[] bytes) => $"\"{Convert.ToHexString(SHA256.HashData(bytes), 0, 16).ToLowerInvariant()}\"";

    private static JsonElement VisibleRows(ExtensionField field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return value;
        }

        var columns = (field.Columns ?? []).Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
        var rows = value.EnumerateArray()
            .Select(row => row.ValueKind == JsonValueKind.Object
                ? row.EnumerateObject().Where(cell => columns.Contains(cell.Name)).ToDictionary(cell => cell.Name, cell => cell.Value, StringComparer.Ordinal)
                : null)
            .Where(row => row is not null)
            .ToList();
        return JsonSerializer.SerializeToElement(rows);
    }

    /// <summary>欄（表の列も）の型に値が合うか。合わなければ 400 invalidValue。数の min・max は目安なので断らない（spec）。</summary>
    private static ExtensionProblem? Check(ExtensionField field, JsonElement value)
    {
        var ok = field.Type switch
        {
            ExtensionFieldTypes.Text => value.ValueKind == JsonValueKind.String,
            ExtensionFieldTypes.Number => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number),
            ExtensionFieldTypes.Bool => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            ExtensionFieldTypes.Date => value.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            ExtensionFieldTypes.Url => value.ValueKind == JsonValueKind.String && IsWebUrl(value.GetString()),
            ExtensionFieldTypes.Choice => value.ValueKind == JsonValueKind.String
                && (field.Options ?? []).Any(option => string.Equals(option.Value, value.GetString(), StringComparison.Ordinal)),
            ExtensionFieldTypes.TextList => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(one => one.ValueKind == JsonValueKind.String),
            ExtensionFieldTypes.AvatarList => value.ValueKind == JsonValueKind.Array
                && value.EnumerateArray().All(one => one.ValueKind == JsonValueKind.String && Storage.StoreIds.IsItemId(one.GetString())),
            ExtensionFieldTypes.Table => value.ValueKind == JsonValueKind.Array,
            _ => false,
        };

        if (!ok)
        {
            return Invalid(field);
        }

        return field.Type == ExtensionFieldTypes.Table ? CheckRows(field, value) : null;
    }

    private static ExtensionProblem? CheckRows(ExtensionField field, JsonElement rows)
    {
        if (rows.GetArrayLength() > ExtensionLimits.TableRows)
        {
            return TooLarge($"表「{field.Key}」の行が多すぎます（上限 {ExtensionLimits.TableRows:N0} 行）。");
        }

        var columns = (field.Columns ?? []).ToDictionary(column => column.Key, StringComparer.Ordinal);
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                return Invalid(field);
            }

            foreach (var cell in row.EnumerateObject())
            {
                if (!columns.TryGetValue(cell.Name, out var column))
                {
                    return new ExtensionProblem(400, "unknownField", $"表「{field.Key}」の列「{cell.Name}」は宣言にありません。");
                }

                if (cell.Value.ValueKind != JsonValueKind.Null && Check(column, cell.Value) is { } problem)
                {
                    return problem;
                }
            }
        }

        return null;
    }

    /// <summary>http・https だけ受ける（spec「URL の型」：file: や javascript: を画面で開かせない）。</summary>
    private static bool IsWebUrl(string? text)
        => Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static ExtensionProblem Invalid(ExtensionField field)
        => new(400, "invalidValue", $"欄「{field.Key}」の値が型（{field.Type}）に合いません。");
}
