using System.Security.Cryptography;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// 商品の記録の指紋。**読み直した記録が前と同じかを見分けて、画面の作り直しを省くため**に使う
/// （検索のカード・検索用の文字列）。
///
/// 記録は <c>record</c> だが、中の一覧（ファイル・タグ・画像）は参照で比べられるので、
/// 読み直すたびに中身が同じでも「違う」になる。保存するときと同じ形に書き出して比べれば、
/// JSON に出る物（＝画面が見る物）が同じかどうかを1回で見分けられる。
/// 計算で出す値（<c>[JsonIgnore]</c>）は元の欄から出るので、元が同じなら同じになる。
///
/// 128ビットに畳むのは、2000件の文字列を丸ごと抱えると10MB前後になるため（1件16バイトで済む）。
/// 比べるのは同じ商品の前後だけなので、偶然の一致は実質起きない。
/// </summary>
public static class ItemFingerprint
{
    public static Guid Of(ItemRecord item)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(item, JsonStore.Options);
        return new Guid(MD5.HashData(bytes));
    }
}
