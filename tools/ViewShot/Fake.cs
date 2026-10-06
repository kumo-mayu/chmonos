using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ViewShot;

/// <summary>
/// 場面の作り物のデータ。**名前・ID・ファイル名は全部ここで作った物**で、購入した物にも友人のデータにも由来しない
/// （商品IDは実在の範囲を避けて 990 万台、ショップは「viewshot」で始まる）。
/// </summary>
internal sealed class Fake(DataStore store)
{
    private static readonly DateTimeOffset Day = new(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(9));

    /// <summary>作り物の商品を書く。絵は <paramref name="images"/> 枚（作り物の絵を先に置くので、BOOTH へ取りに行かない）。</summary>
    public async Task<ItemRecord> ItemAsync(
        string id,
        string name,
        Func<ItemRecord, ItemRecord>? change = null,
        int images = 1,
        string shop = "作り物ショップ")
    {
        var urls = Enumerable.Range(1, images).Select(index => $"https://viewshot.invalid/{id}/{index}.png").ToList();
        var item = new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock
            {
                // 取得済みで、期限は遠い先。⑦ 期限の来た商品の取り直しの対象にしない
                FetchedAt = Day,
                Name = name,
                Description = "場面を描くための作り物の商品です。",
                PriceText = "¥ 1,500",
                Url = $"https://viewshot.invalid/items/{id}",
                Tags = ["VRChat", "3Dモデル"],
                Category = new BoothCategory { Id = 208, Name = "3D衣装", ParentName = "3Dモデル" },
                Shop = new BoothShop { Name = shop, Subdomain = "viewshot-" + Hex(shop)[..6] },
                Images = urls.Select(url => new BoothImage { OriginalUrl = url }).ToList(),
                Variations = [new BoothVariation { Id = 1, Name = "フルセット", Price = 1500, Type = "downloadable" }],
            },
            Local = new LocalBlock
            {
                LastFetchedAt = Day,
                NextFetchDueAt = Day.AddYears(10),
                AvatarsDetectedAt = Day,
                AcquiredAt = DateOnly.FromDateTime(Day.Date),
            },
        };

        item = change?.Invoke(item) ?? item;
        await store.Items.SaveAsync(item);

        for (var index = 0; index < urls.Count; index++)
        {
            Image(store.Paths.ItemImagesDir(id), ImagePipeline.FileNameFor(urls[index]), seed: id + index);
        }

        return item;
    }

    /// <summary>
    /// 作り物の絵（色の帯）。アプリが保存する形（長辺384の WebP）で置く。名前から色を決めるので、同じ場面は毎回同じ絵になる
    /// （前後の画像を比べたときに、絵の違いが差として出ない）
    /// </summary>
    public static void Image(string directory, string fileName, string seed, int width = 384, int height = 384)
    {
        Directory.CreateDirectory(directory);

        // 絵は名前と大きさだけで決まるので、1度作った物を写す。作るたびに WebP へ詰めると、
        // 8件の場面で作り物を書くのに 1.5 秒かかっていた（2026-10-05 に測った。プロセスごとに詰める道具を温め直すため）
        var cached = Path.Combine(Isolation.ImageCacheRoot, $"{Hex($"{seed}|{width}x{height}")[..32]}.webp");
        var target = Path.Combine(directory, fileName);
        if (File.Exists(cached))
        {
            File.Copy(cached, target, overwrite: true);
            return;
        }

        Draw(target, seed, width, height);
        try
        {
            // 並んで走る別の回と同じ絵を作ることがある。名前を変えて置いてから移すので、書きかけを写されない
            var partial = cached + "." + Environment.ProcessId + ".tmp";
            File.Copy(target, partial, overwrite: true);
            File.Move(partial, cached, overwrite: true);
        }
        catch (IOException)
        {
            // 控えを置けなくても、絵はもう置いた
        }
    }

    private static void Draw(string path, string seed, int width, int height)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var first = new Rgba32(hash[0], hash[1], hash[2]);
        var second = new Rgba32(hash[3], hash[4], hash[5]);
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    // 斜めの帯。向きと切れ目が見えるので、切り抜き・縮め方の違いが絵で分かる
                    row[x] = ((x + y) / 48) % 2 == 0 ? first : second;
                }
            }
        });
        image.SaveAsWebp(path);
    }

    /// <summary>手元のファイルの記録。</summary>
    public static LocalFileRecord FileRecord(string path, long size = 48_300_000, bool broken = false, long? variation = 1)
        => new()
        {
            Hash = Hex(path),
            Paths = [path],
            SizeBytes = size,
            VariationId = variation,
            ArchiveBroken = broken,
        };

    /// <summary>
    /// 未確定のファイルの記録。<paramref name="originZip"/> は展開元の zip の場所（エクスプローラの「すべて展開」が
    /// 中のファイルに書き残す値）。同じ zip を指すファイルは、一覧で1つの束になる
    /// </summary>
    public static UnresolvedFile Unresolved(
        string path, long size = 48_300_000, bool broken = false, IReadOnlyList<string>? contents = null, string? originZip = null,
        IReadOnlyList<string>? candidates = null)
        => new()
        {
            CandidateItemIds = candidates ?? [],
            Hash = Hex(path),
            Paths = [path],
            SizeBytes = size,
            ModifiedAtUtc = Day.ToUniversalTime(),
            FirstSeenAt = Day,
            Contents = contents ?? [],
            ZoneReferrerUrl = originZip,
            ArchiveBroken = broken,
        };

    /// <summary>中身の要らないファイルを置く（zip でない物：unitypackage・画像・文書）。</summary>
    public static string PlainFile(string relativePath)
    {
        var path = PathFor(relativePath);
        try
        {
            File.WriteAllText(path, "viewshot");
        }
        catch (IOException) when (File.Exists(path))
        {
            // Zip と同じ（別の回が書いている最中）
        }

        return path;
    }

    /// <summary>空のフォルダを置く。</summary>
    public static string Folder(string relativePath)
    {
        var path = Path.Combine(Isolation.FilesRoot, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>本当に開ける zip を置く（「展開して開く」が出る行・中身の一覧）。</summary>
    public static string Zip(string relativePath, params string[] entries)
    {
        var path = PathFor(relativePath);
        try
        {
            using var archive = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
            foreach (var entry in entries.Length > 0 ? entries : ["readme.txt"])
            {
                using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
                writer.Write("viewshot");
            }
        }
        catch (IOException) when (File.Exists(path))
        {
            // 同じ場面を並べて走らせた別の回が、同じ中身を書いている最中。置いてあればよい
        }

        return path;
    }

    /// <summary>zip として開けないファイルを置く（途中で切れたダウンロード）。</summary>
    public static string BrokenZip(string relativePath)
    {
        var path = PathFor(relativePath);
        try
        {
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("PK\u0003\u0004 truncated"));
        }
        catch (IOException) when (File.Exists(path))
        {
            // Zip と同じ（別の回が書いている最中）
        }

        return path;
    }

    /// <summary>置いていないファイルの場所（「見つかりません」）。フォルダは在る。</summary>
    public static string MissingPath(string relativePath)
    {
        var path = Path.Combine(Isolation.FilesRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    /// <summary>つながっていないドライブの上の場所（「取り外しているドライブ」）。この PC に無いドライブ文字を後ろから探す。</summary>
    public static string DetachedDrivePath(string relativePath)
    {
        for (var letter = 'Z'; letter >= 'D'; letter--)
        {
            var root = $@"{letter}:\";
            if (!Directory.Exists(root))
            {
                return Path.Combine(root, relativePath);
            }
        }

        throw new InvalidOperationException("空いているドライブ文字がありません。");
    }

    private static string PathFor(string relativePath)
    {
        var path = Path.Combine(Isolation.FilesRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    public static string Hex(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
