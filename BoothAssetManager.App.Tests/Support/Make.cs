using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.Tests.Support;

/// <summary>
/// 試験の作り物のデータ。**名前・ID・ファイル名は作り物にする**（友人のデータの物を書かない。CLAUDE.md）。
/// 要る欄だけを引数にし、残りは <c>with</c> で足す。
/// </summary>
internal static class Make
{
    /// <summary>
    /// BOOTH から取り終えた姿の、持っている商品（名前とショップがあり、zip を1つ持つ）。
    /// **ファイルを持たせてあるのは、所持＝ファイルかフォルダを1つ以上持つこと、だから**——統計・ショップの所持の数・
    /// フォルダの表示は持っている商品しか数えず、持たせないと関係の無い試験で空の表示になる。
    /// 持っていない商品が要るときは <c>.WithFiles()</c> で空にする
    /// </summary>
    public static ItemRecord Item(string id, string name, string shop = "sample-shop") => new()
    {
        Id = id,
        Booth = new BoothBlock
        {
            Name = name,
            Url = $"https://{shop}.booth.pm/items/{id}",
            Shop = new BoothShop { Name = shop, Subdomain = shop, Url = $"https://{shop}.booth.pm/" },
        },
        Local = new LocalBlock { LocalFiles = [File($@"D:\files\{id}.zip")] },
    };

    /// <summary>手元のファイルの記録。ハッシュは場所から作る（中身は見ない。同じ場所なら同じファイル）。</summary>
    public static LocalFileRecord File(string path, bool archiveBroken = false, bool detached = false) => new()
    {
        Hash = HashOf(path),
        Paths = [path],
        SizeBytes = 3,
        ArchiveBroken = archiveBroken,
        Detached = detached,
    };

    /// <summary>手元のファイルを入れ替える（引数なしなら、ファイルを持たない商品になる）。</summary>
    public static ItemRecord WithFiles(this ItemRecord item, params LocalFileRecord[] files)
        => item with { Local = item.Local with { LocalFiles = files } };

    /// <summary>64桁の16進（SHA-256 の形）。</summary>
    public static string HashOf(string text)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
}
