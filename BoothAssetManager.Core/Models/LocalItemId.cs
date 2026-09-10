namespace BoothAssetManager.Core.Models;

/// <summary>
/// BOOTHから取れない商品に与える仮のID。**<c>local-{ファイルのハッシュ先頭8桁}</c>。**
///
/// 非公開・削除済みの商品は、買っていて手元にファイルがあってもIDが分からない。
/// それでも登録できるようにするための鍵。
///
/// **連番にしない。**保存先を移したり手でJSONを消したりすると番号が衝突する。
/// **GUIDにしない。**人が読めず「どのファイルの商品だったか」が追えない。
/// ハッシュ由来なら <c>detached.json</c> や <c>scan-cache.json</c> と突き合わせられる。
///
/// 接頭辞を付けるのは、**JSONを開いた人に一目で分かるようにするため。**
/// 数字だけのIDと並んで区別が付かないのは「欠けを隠さない」に反する。
///
/// この鍵の商品はBOOTHへ問い合わせない（①②も⑦も走らない）。
/// 存在しないIDを叩き続けることになるので、取得の入口すべてで弾く。
/// </summary>
public static class LocalItemId
{
    public const string Prefix = "local-";

    /// <summary>鍵に使うハッシュの長さ。手元の規模では8桁で十分に分かれる。</summary>
    public const int HashLength = 8;

    /// <summary>ファイルのハッシュから仮IDを作る。</summary>
    public static string For(string hash)
    {
        var trimmed = (hash ?? string.Empty).Trim().ToLowerInvariant();
        return Prefix + trimmed[..Math.Min(HashLength, trimmed.Length)];
    }

    /// <summary>仮IDか。BOOTHへ問い合わせてよいかの判断はすべてここを通す。</summary>
    public static bool IsLocal(string? itemId)
        => itemId is not null && itemId.StartsWith(Prefix, StringComparison.Ordinal);
}
