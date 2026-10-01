using System.Text.Json.Serialization;

namespace Chmonos.Core.Models;

/// <summary>
/// 画像の役割。
///
/// **3つで固定**（ユーザ判断）。サムネイルに出す種類を選ばせるには、
/// 集合が小さく安定している方が扱いやすい——役割が増えるほど
/// 「どれをサムネに出すか」の設定も伸びる。
/// 自分の語彙で分類したいものは、ユーザータグが既にある。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ImageRole>))]
public enum ImageRole
{
    /// <summary>BOOTHの商品画像として出ているもの。</summary>
    [JsonStringEnumMemberName("BOOTH")]
    Booth,

    /// <summary>自分が改変した姿。着用例や組み合わせの記録。</summary>
    [JsonStringEnumMemberName("改変例")]
    Modified,

    /// <summary>上のどちらでもないもの。</summary>
    [JsonStringEnumMemberName("その他")]
    Other,
}

/// <summary>
/// サムネイルにどの役割の画像を出すか。
///
/// <see cref="Default"/> のときだけ、商品ごとの★の指名が効く（ユーザ判断）。
/// 特定の役割を選んでいるときは役割が勝つ——
/// 「改変例を出す」と決めたのに、★を付けた商品だけ別の絵になるのは筋が通らない。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThumbnailRole>))]
public enum ThumbnailRole
{
    /// <summary>役割で選ばない。★の指名があればそれ、無ければ並びの1枚目。</summary>
    [JsonStringEnumMemberName("デフォルト")]
    Default,

    [JsonStringEnumMemberName("BOOTH")]
    Booth,

    [JsonStringEnumMemberName("改変例")]
    Modified,

    [JsonStringEnumMemberName("その他")]
    Other,
}

public static class ImageRoles
{
    /// <summary>画面に出す名前。内部の語を漏らさない。</summary>
    public static string Label(ImageRole role) => role switch
    {
        ImageRole.Booth => "BOOTH",
        ImageRole.Modified => "改変例",
        _ => "その他",
    };

    public static string Label(ThumbnailRole role) => role switch
    {
        ThumbnailRole.Default => "デフォルト",
        ThumbnailRole.Booth => "BOOTH",
        ThumbnailRole.Modified => "改変例",
        _ => "その他",
    };

    /// <summary><see cref="ThumbnailRole"/> を、対応する <see cref="ImageRole"/> にする。</summary>
    public static ImageRole? AsImageRole(ThumbnailRole role) => role switch
    {
        ThumbnailRole.Booth => ImageRole.Booth,
        ThumbnailRole.Modified => ImageRole.Modified,
        ThumbnailRole.Other => ImageRole.Other,
        _ => null,
    };
}
