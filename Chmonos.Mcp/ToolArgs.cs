using System.Text.Json;
using System.Text.Json.Nodes;

namespace Chmonos.Mcp;

/// <summary>道具の引数を読む。型の違う値は、黙って既定に倒さず、AI に直し方を言う（<see cref="ToolArgumentException"/>）。</summary>
internal sealed class ToolArgs(IReadOnlyDictionary<string, JsonElement>? values)
{
    private readonly IReadOnlyDictionary<string, JsonElement> _values = values ?? new Dictionary<string, JsonElement>();

    private JsonElement? Raw(string name)
        => _values.TryGetValue(name, out var value) && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined ? value : null;

    public string? String(string name)
    {
        if (Raw(name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ToolArgumentException($"{name} は文字列で渡してください。");
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public bool? Bool(string name)
        => Raw(name) is { } value
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new ToolArgumentException($"{name} は true か false で渡してください。"),
            }
            : null;

    public int Int(string name, int fallback, int min, int max)
    {
        if (Raw(name) is not { } value)
        {
            return fallback;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            throw new ToolArgumentException($"{name} は整数で渡してください。");
        }

        if (number < min || number > max)
        {
            throw new ToolArgumentException($"{name} は {min}〜{max} で渡してください。");
        }

        return number;
    }

    public IReadOnlyList<string> Strings(string name)
    {
        if (Raw(name) is not { } value)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(element => element.ValueKind != JsonValueKind.String))
        {
            throw new ToolArgumentException($"{name} は文字列の配列で渡してください。");
        }

        return value.EnumerateArray().Select(element => element.GetString()!.Trim()).Where(text => text.Length > 0).ToList();
    }

    public JsonNode? Node(string name) => Raw(name) is { } value ? JsonNode.Parse(value.GetRawText()) : null;
}

/// <summary>引数の誤り。道具の答え（isError）として AI に返す。</summary>
internal sealed class ToolArgumentException(string message) : Exception(message);
