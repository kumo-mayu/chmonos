using System.Security.Cryptography;
using System.Text;

namespace Chmonos.Core.Storage;

/// <summary>
/// 元の URL などから、保存先のファイル名に使う短い名前を作る（SHA-256 の16進の頭8字・小文字）。同じ値なら常に同じ名前になる。
/// 商品の絵とショップのアイコンが同じ式を別々に持っていたので1つにした。**式を変えると、保存先の今あるファイルが見つからなくなる。**
/// 8字（32ビット）なのは、名前を分けるのが1商品・1ショップの中の数十枚だけで、重なる見込みが無視できるため。
/// </summary>
internal static class ShortHash
{
    public static string Of(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();
}
