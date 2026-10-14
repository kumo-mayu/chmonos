using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chmonos.Query;

/// <summary>
/// 本文の <c>include</c> を、カンマ区切りの文字列（<c>"contents,images"</c>）でも配列（<c>["contents", "images"]</c>）でも受ける（2026-10-14）。
/// GET の <c>?include=</c> に合わせて文字列だけにしていたが、JSON では配列の方が自然で、配列を送ると「JSON が読めません」になっていた
/// （Unity の API-GAPS 3-2）。中では今までの文字列に直し、名前の読み方（<see cref="Includes.ParseItem"/>）は1つのまま
/// </summary>
public sealed class IncludeListConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.StartArray:
                var names = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    // 配列の中の null や数は入力の誤り（点検32：配列の中の null は 400 で断る決まり）
                    names.Add(reader.TokenType == JsonTokenType.String
                        ? reader.GetString()!
                        : throw QueryException.Invalid("invalidInclude", "include の配列には名前の文字列だけを書きます。"));
                }

                return string.Join(",", names);
            default:
                throw QueryException.Invalid("invalidInclude", "include は名前の配列か、カンマ区切りの文字列です。");
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
