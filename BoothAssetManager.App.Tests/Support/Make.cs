using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.Tests.Support;

/// <summary>
/// 試験の作り物のデータ。**名前・ID・ファイル名は作り物にする**（友人のデータの物を書かない。CLAUDE.md）。
/// 要る欄だけを引数にし、残りは <c>with</c> で足す。
/// </summary>
internal static class Make
{
    /// <summary>BOOTH から取り終えた姿の商品（名前とショップがある）。ファイルは持たない。</summary>
    public static ItemRecord Item(string id, string name, string shop = "sample-shop") => new()
    {
        Id = id,
        Booth = new BoothBlock
        {
            Name = name,
            Url = $"https://{shop}.booth.pm/items/{id}",
            Shop = new BoothShop { Name = shop, Subdomain = shop, Url = $"https://{shop}.booth.pm/" },
        },
    };

    /// <summary>手元のファイルの記録。ハッシュは名前から作る（中身は見ない。同じ名前なら同じファイル）。</summary>
    public static LocalFileRecord File(string path, bool archiveBroken = false, bool detached = false) => new()
    {
        Hash = HashOf(path),
        Paths = [path],
        SizeBytes = 3,
        ArchiveBroken = archiveBroken,
        Detached = detached,
    };

    /// <summary>記録の場所が1つも無いファイル（場所を全部外した物）。</summary>
    public static LocalFileRecord FileWithoutPath(string name) => new()
    {
        Hash = HashOf(name),
        Paths = [],
        SizeBytes = 3,
    };

    public static ItemRecord WithFiles(this ItemRecord item, params LocalFileRecord[] files)
        => item with { Local = item.Local with { LocalFiles = files } };

    /// <summary>64桁の16進（SHA-256 の形）。</summary>
    public static string HashOf(string text)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
}
