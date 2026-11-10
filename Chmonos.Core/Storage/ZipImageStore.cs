using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Storage;

/// <summary>zip の中の画像1つ（<c>zipimages/&lt;ハッシュ&gt;.json</c> の1行）。</summary>
public sealed record ZipImageRow
{
    /// <summary>zip の中の場所。</summary>
    public required string Path { get; init; }

    public required long Size { get; init; }

    /// <summary>zip が持つ CRC-32（16進8桁）。</summary>
    public required string Crc { get; init; }

    /// <summary>CRC を数にする。手で壊した欄は null（「名前が同じ」としてだけ使う）。</summary>
    public uint? CrcValue => uint.TryParse(Crc, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : null;
}

/// <summary><c>zipimages/&lt;ハッシュ&gt;.json</c> の中身。</summary>
public sealed class ZipImagesFile
{
    public IReadOnlyList<ZipImageRow>? Images { get; init; }
}

/// <summary>
/// zip の中の画像の大きさと CRC の控え（ユーザ判断 2026-11-01）。手で Unity へ置いた画像を、名前によらず中身で見つけるために使う。
///
/// **目次だけを読む。**大きさと CRC は zip の目次にあるので、解かなくてよい。
/// **控えるのは、zip が無くなった後も見つけるため。**Unity へ入れた後で zip を動かす・消すのは普通で、そのときも「同じ画像」と言いたい。
/// 鍵は zip のハッシュなので古くならず、消しても zip があれば読み直すだけで壊れない。
/// </summary>
public sealed class ZipImageStore(AppPaths paths)
{
    /// <summary>読めなければ null（無い・壊れている・新しすぎる）。</summary>
    public IReadOnlyList<ZipImageRow>? Load(string hash)
    {
        if (!StoreIds.IsPackageHash(hash))
        {
            return null;
        }

        try
        {
            // 手で直した控えの欠けた行は読まない（欠けた配列を空として受けるのと同じ）
            return JsonStore.Read<ZipImagesFile>(paths.ZipImageFile(hash))?.Images?
                .Where(row => row is { Path.Length: > 0 })
                .ToList();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 控えがあれば返す。無ければ zip の目次を読んで控える（zip が在るとき）。
    /// 同期なのは、呼び手が取り込みの裏とプロジェクトを調べる裏のスレッドだけだから（画面のスレッドから呼ばない）。
    /// </summary>
    public IReadOnlyList<ZipImageRow>? LoadOrRemember(LocalFileRecord file)
    {
        if (Load(file.Hash) is { } known)
        {
            return known;
        }

        if (!HasImages(file) || !StoreIds.IsPackageHash(file.Hash)
            || Services.ArchiveFiles.FirstExistingZip(file.Paths) is not { } zip)
        {
            return null;
        }

        if (ReadTable(zip) is not { } rows)
        {
            return null;
        }

        JsonStore.Write(paths.ZipImageFile(file.Hash), new ZipImagesFile { Images = rows });
        return rows;
    }

    /// <summary>控えのファイルがあるか（取り込みの始めに全部のファイルを見るので、中は読まない）。</summary>
    public bool Has(string hash) => StoreIds.IsPackageHash(hash) && File.Exists(paths.ZipImageFile(hash));

    public static bool HasImages(LocalFileRecord file) => !file.Detached && file.Contents.Any(UnityProjectImages.IsImage);

    /// <summary>目次を読む。開けない zip（ほかのアプリが開いている・壊れている）は null で、控えない。次の機会にまた読む。</summary>
    private static IReadOnlyList<ZipImageRow>? ReadTable(string zip)
    {
        try
        {
            using var archive = ZipFile.Open(zip, ZipArchiveMode.Read, BoothZipInspector.ZipNameEncoding.Instance);
            return archive.Entries
                .Where(entry => UnityProjectImages.IsImage(entry.FullName))
                .Select(entry => new ZipImageRow
                {
                    Path = entry.FullName,
                    Size = entry.Length,
                    Crc = entry.Crc32.ToString("X8", CultureInfo.InvariantCulture),
                })
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
