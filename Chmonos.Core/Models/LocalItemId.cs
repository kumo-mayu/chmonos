using System.Security.Cryptography;

namespace Chmonos.Core.Models;

/// <summary>
/// BOOTHから取れない商品に与える仮のID。形は2つあり、どちらも <c>local-</c> と小文字の16進（<see cref="Storage.StoreIds"/>）。
///
/// - **ファイルから作った商品：<c>local-{ファイルのハッシュ先頭8桁}</c>**（<see cref="For"/>）。
///   非公開・削除済みの商品は、買っていて手元にファイルがあってもIDが分からない。それでも登録できるようにするための鍵。
/// - **名前だけで作った商品：<c>local-{乱数12桁}</c>**（<see cref="NewUnique"/>。2026-11-20 のメモ「ファイルの無いlocalItemが作成できない」）。
///   ファイルが無いのでハッシュが無く、追う元のファイルも無いので乱数で良い。
///
/// **連番にしない。**保存先を移したり手でJSONを消したりすると番号が衝突する。
/// **GUIDにしない。**人が読めず「どのファイルの商品だったか」が追えない。
/// ハッシュ由来なら、手元のファイルの記録（<c>localFiles[].hash</c>）や <c>scan-cache.json</c> と突き合わせられる。
///
/// **乱数の方は桁を8にしない。**ハッシュ由来の8桁と同じ桁だと、後で先頭8桁が同じファイルを「BOOTHに無い商品」として登録したとき、
/// 「同じファイルなら同じ商品へ行き着く」道（<c>ItemService.RegisterLocalItemAsync</c>）が、関わりの無いこの商品へファイルを足してしまう。
/// 桁が違えば2つの形は決して重ならず、JSON を開いた人にも「ファイルから作った物か」が桁で分かる。
/// 12桁（48ビット）は、手元の規模（数千件）で乱数どうしが重なる見込みが1億分の1を下回る長さ。それでも作る側は錠の中で在るかを見て引き直す。
///
/// 接頭辞を付けるのは、**JSONを開いた人に一目で分かるようにするため。**
/// 数字だけのIDと並んで区別が付かないのは「欠けを隠さない」に反する。
///
/// この鍵の商品はBOOTHへ問い合わせない（①②も⑦も走らない）。
/// 存在しないIDを叩き続けることになるので、取得の入口すべてで弾く。
/// **IDからハッシュを読み戻さない**（2026-11-20 に全部の使い道を見て、どこも読み戻していなかった）。乱数の形が混ざるので、読み戻す処理を足さないこと。
/// </summary>
public static class LocalItemId
{
    public const string Prefix = "local-";

    /// <summary>鍵に使うハッシュの長さ。手元の規模では8桁で十分に分かれる。</summary>
    public const int HashLength = 8;

    /// <summary>名前だけで作る商品の乱数の桁。<see cref="HashLength"/> と違う桁にする理由はこの型の説明。</summary>
    public const int RandomLength = 12;

    /// <summary>ファイルのハッシュから仮IDを作る。</summary>
    public static string For(string hash)
    {
        var trimmed = (hash ?? string.Empty).Trim().ToLowerInvariant();
        return Prefix + trimmed[..Math.Min(HashLength, trimmed.Length)];
    }

    /// <summary>ファイルに依らない仮IDを乱数で作る（名前だけで作る商品）。重なりは作る側が錠の中で見る。</summary>
    public static string NewUnique()
        => Prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(RandomLength / 2));

    /// <summary>仮IDか。BOOTHへ問い合わせてよいかの判断はすべてここを通す。</summary>
    public static bool IsLocal(string? itemId)
        => itemId is not null && itemId.StartsWith(Prefix, StringComparison.Ordinal);
}
