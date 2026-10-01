namespace Chmonos.Core.Models;

/// <summary>
/// ユーザが自分で足した画像1枚。
///
/// BOOTHから取れる画像だけでは足りない場面がある——**BOOTHに無い商品は
/// 画像が1枚も無く**、普通の商品でも自分で撮った着用例を並べたいことがある。
///
/// <see cref="LocalBlock"/> に置くので、再取得で <c>Booth</c> ブロックを
/// 丸ごと差し替えても消えない。
/// </summary>
public sealed record UserImage
{
    /// <summary>
    /// 保存したファイル名（<c>user-3f9c1b7e.webp</c>）。
    /// 商品の画像フォルダの中にある。
    /// </summary>
    public required string FileName { get; init; }

    public DateTimeOffset? AddedAt { get; init; }

    /// <summary>覚え書き。「改変後」「〇〇に着せたところ」など。</summary>
    public string? Caption { get; init; }
}

/// <summary>
/// 自分で足した画像の保存名を決める。
///
/// **`user-` を付けるのは、フォルダを開いた人に一目で分かるようにするため。**
/// BOOTHの画像は元URLのハッシュ8桁で、並ぶと区別が付かない
/// （商品IDに <c>local-</c> を付けたのと同じ理由）。
///
/// **中身のハッシュ**にしているので、同じ絵を2回落としても1枚にまとまる。
/// </summary>
public static class UserImageName
{
    public const string Prefix = "user-";

    /// <summary>保存する形式。BOOTHの画像と同じ圧縮を通すので同じ拡張子になる。</summary>
    public const string Extension = ".webp";

    private const int HashLength = 8;

    /// <summary>画像の中身から保存名を作る。</summary>
    public static string For(ReadOnlySpan<byte> content)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(content);
        return Prefix + Convert.ToHexStringLower(hash)[..HashLength] + Extension;
    }

    /// <summary>自分で足した画像か。ファイル名だけで判断できる。</summary>
    public static bool IsUserAdded(string? fileName)
        => fileName is not null
            && System.IO.Path.GetFileName(fileName).StartsWith(Prefix, StringComparison.Ordinal);
}
