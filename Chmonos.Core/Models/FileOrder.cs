using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chmonos.Core.Models;

/// <summary>
/// 商品ページと編集画面の「手元のファイル」を何で並べるか（設定の <c>fileOrder</c>・ユーザ指示 2026-11-11）。
/// 向きは別の欄（<see cref="AppSettings.FileOrderDescending"/>）。並べ方そのものは <see cref="Services.LocalFileOrder"/>。
/// どの順・どの向きでも、外したファイルは後ろに置く。
/// </summary>
/// <remarks>
/// 前は「新しい順」「大きい順」を別の選択肢にしていたが、向きを選べるようにして「追加した順」「大きさ順」の向きにまとめた
/// （ユーザ判断 2026-11-11「それぞれの並び順で向きを選べるようにして、重複するものは統合」）。
/// その頃の値（newest・largest）や知らない値は、<see cref="FileOrderJsonConverter"/> が既定の <see cref="Added"/> として読む。
/// </remarks>
public enum FileOrder
{
    /// <summary>記録の順（ファイルを記録に足した順。前からの並び）。既定。逆向きは記録の順を逆にするだけ（日時は使わない）。</summary>
    Added,

    /// <summary>ファイル名の順。数字も1字ずつ比べる（file10 が file2 より前）。</summary>
    Name,

    /// <summary>ファイル名の順で、名前の中の数を数として比べる（v1.9 → v1.10、Ver2.9.0 → Ver2.19.0）。</summary>
    NameWithVersion,

    /// <summary>大きさ（<see cref="LocalFileRecord.SizeBytes"/>）の順。</summary>
    Size,
}

/// <summary>
/// 設定の並べる物と向きの組（<see cref="AppSettings.FileSort"/>）。画面の部品へ1つで渡すためにまとめる。
/// </summary>
public readonly record struct FileSort(FileOrder Order, bool Descending)
{
    /// <summary>既定（追加した順の古い物が上）。前からの並び。</summary>
    public static FileSort Default => new(FileOrder.Added, false);

    /// <summary>
    /// 並べる物を選び直したときの既定の向き（docs/spec/ui-rules.md「並べ替えの出し方」：項目を変えたら、その項目の既定の向きに戻す）。
    /// 大きさは大きい物を上にする（前の「大きい順」と同じ）。ほかは今までの向き
    /// </summary>
    public static bool DefaultDescending(FileOrder order) => order == FileOrder.Size;
}

/// <summary>
/// <see cref="FileOrder"/> を名前（camelCase）で読み書きし、**読めない値は既定の <see cref="FileOrder.Added"/> にする**。
/// まだ配っていない間に書いた「newest」「largest」が手元の写しに残っていることがあり、
/// 1つの欄が読めないだけで settings.json 全体を読めなくしないため。
/// </summary>
public sealed class FileOrderJsonConverter : JsonConverter<FileOrder>
{
    public override FileOrder Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return FileOrder.Added;
        }

        return Enum.TryParse<FileOrder>(reader.GetString(), ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : FileOrder.Added;
    }

    public override void Write(Utf8JsonWriter writer, FileOrder value, JsonSerializerOptions options)
        => writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}
