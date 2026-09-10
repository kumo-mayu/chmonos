using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Storage;

/// <summary>
/// 改変の記録の読み書き（<c>modifications/mod-{hash8}.json</c>）。
///
/// **1改変1ファイル。**1つにまとめれば一覧として読めるが、
/// 壊れたときに全部失う方が重い（商品と同じ作法にする）。
///
/// 読めないファイルは飛ばして、飛ばしたことを返す。
/// 1件壊れたせいで一覧が空になるのは困る——
/// 商品の <see cref="ItemRepository.LoadAllAsync"/> と同じ考え方。
/// </summary>
public sealed class ModificationRepository
{
    private readonly AppPaths _paths;

    public ModificationRepository(AppPaths paths)
    {
        _paths = paths;
    }

    public bool Exists(string id) => File.Exists(_paths.ModificationFile(id));

    public async Task<ModificationRecord?> LoadAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var path = _paths.ModificationFile(id);
        return File.Exists(path)
            ? await JsonStore.ReadAsync<ModificationRecord>(path, cancellationToken)
            : null;
    }

    public Task SaveAsync(ModificationRecord record, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.ModificationsDir);
        return JsonStore.WriteAsync(_paths.ModificationFile(record.Id), record, cancellationToken);
    }

    /// <summary>
    /// 全部読む。**新しく作った順（作成日時の降順）**で返す。
    ///
    /// 同じアバターに同じ名前の改変を作れるようにしてあるので、
    /// 一覧では日付が見分けの手掛かりになる。
    /// </summary>
    public async Task<ModificationLoadResult> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        var loaded = new List<ModificationRecord>();
        var failed = new List<string>();

        foreach (var id in EnumerateIds())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await LoadAsync(id, cancellationToken) is { } record)
                {
                    loaded.Add(record);
                    continue;
                }
            }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
            {
                // 読めなかったことは隠さない。呼ぶ側が件数を出す
            }

            failed.Add(id);
        }

        return new ModificationLoadResult
        {
            Modifications = loaded
                .OrderByDescending(record => record.CreatedAt)
                .ThenBy(record => record.Name, StringComparer.CurrentCulture)
                .ToList(),
            FailedIds = failed,
        };
    }

    public IReadOnlyList<string> EnumerateIds()
    {
        if (!Directory.Exists(_paths.ModificationsDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(_paths.ModificationsDir, ModificationId.Prefix + "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToList();
    }

    /// <summary>
    /// 消す。**貼った画像も一緒に消える。**
    ///
    /// 改変専用の画像なので、記録を消して画像だけ残すと
    /// 行き場の無いファイルになって誰も辿れない。
    /// 取り返しがつかないので、聞くのは呼ぶ側の仕事。
    /// </summary>
    public void Delete(string id)
    {
        var file = _paths.ModificationFile(id);
        if (File.Exists(file))
        {
            File.Delete(file);
        }

        var images = _paths.ModificationImagesDir(id);
        if (Directory.Exists(images))
        {
            Directory.Delete(images, recursive: true);
        }
    }
}

public sealed class ModificationLoadResult
{
    public required IReadOnlyList<ModificationRecord> Modifications { get; init; }

    /// <summary>読めなかったファイル。黙って減らさないために返す。</summary>
    public required IReadOnlyList<string> FailedIds { get; init; }
}
