namespace Chmonos.Core.Scanning;

/// <summary>
/// 未確定の件数を「登録する回数」で数える（ユーザ指示 2026-09-29：展開元のフォルダでまとまるなら1と数え、
/// 実際にIDを登録する回数を想像できるようにする）。
///
/// 数え方は未確定の画面の登録の単位と同じにする（見出しとナビの札で食い違うと、どちらを信じればよいか分からない）：
/// <list type="bullet">
/// <item>元のzipが分かるファイル（zip自身と、そのzipを展開した中身）は、zipの名前ごとに1件。
///   zipを登録すると中身は一覧から外れ、zipが無ければ中身全件を1つの単位で登録するので、どちらでも1回で済む</item>
/// <item>zipが無い展開物（展開物の根が決まったもの）は、その根ごとに1件</item>
/// <item>それ以外のファイルは1件ずつ。同じフォルダに並んでいても、別々の商品であり得る（ダウンロードのフォルダなど）</item>
/// </list>
/// 束の見出しの鍵（zipの名前）と同じく、zipの名前は大文字小文字を区別しない。
/// </summary>
public static class UnresolvedUnits
{
    /// <summary>1件の未確定が属する登録の単位の鍵。</summary>
    /// <param name="hash">ファイルのハッシュ。ほかにまとまる先が無いときの鍵。</param>
    /// <param name="origin">元のzip（zip自身ならそれ自身）。分からなければ null。</param>
    /// <param name="unpackRoot">zipが無い展開物の根。展開物でなければ null。</param>
    public static string KeyOf(string hash, ArchiveOrigin? origin, string? unpackRoot)
    {
        if (origin is not null)
        {
            return "zip|" + origin.ArchiveName;
        }

        // 頭の印はパスにもハッシュにも入らない形で分け、別の種類の鍵がぶつからないようにする
        return unpackRoot is { Length: > 0 }
            ? "dir|" + unpackRoot
            : "file|" + hash;
    }

    /// <summary>単位の数。</summary>
    public static int Count(IEnumerable<string> keys)
        => keys.Distinct(StringComparer.OrdinalIgnoreCase).Count();
}
