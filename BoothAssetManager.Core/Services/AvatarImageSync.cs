using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 持っていないアバターの1枚目を取って置く（U18）。
///
/// 対応アバターの候補や一覧を名前だけで選ぶと、似た名前のアバターを見分けられない。
/// 絵が1枚あれば一目で分かる。**保存するのは1枚目だけ**（ユーザ判断）：一覧・候補・ツールチップに
/// 出すには1枚で足り、友人のデータ（登録簿の約400体）で1枚あたり15〜25KB、全部で10MB前後に収まる。
///
/// 持っているアバター（商品として取り込んである）は、商品の1枚目をそのまま使う。
/// 後で買った場合は、ここに置いた1枚を消す（同じ絵を2か所に持たない・ユーザ判断）。
///
/// 起動時の裏の取得（<see cref="ImageBacklog"/> の後）で回す。優先度は <see cref="BoothPriority.Gallery"/>——
/// 人が押した通信や取り込みの①②③はこれより上なので、自然に割り込む。
/// 画像を保存しない設定なら何もしない。
/// </summary>
public sealed class AvatarImageSync
{
    /// <summary>登録簿へURLを書き戻す間隔（件）。1件ごとに登録簿全体を書き直すと、400体で400回書くことになる。</summary>
    private const int FlushEvery = 20;

    private readonly DataStore _store;
    private readonly IBoothClient _client;
    private readonly ImagePipeline _images;

    public AvatarImageSync(DataStore store, IBoothClient client, ImagePipeline images)
    {
        _store = store;
        _client = client;
        _images = images;
    }

    /// <summary>1枚保存した。引数はアバターの商品ID。**取得した側のスレッドで呼ばれる。**</summary>
    public event Action<string>? AvatarImageSaved;

    /// <returns>この呼び出しで保存した枚数。</returns>
    public async Task<int> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!_images.SavesImages)
        {
            return 0;
        }

        var registry = _store.Avatars.Load();
        var observed = new Dictionary<string, string>(StringComparer.Ordinal);
        var saved = 0;

        using var priority = BoothClient.Prioritize(BoothPriority.Gallery);

        try
        {
            foreach (var entry in registry.Entries.Where(AvatarService.IsAvatar))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var directory = _store.Paths.AvatarImagesDir(entry.ItemId);

                // 持っているなら商品の1枚目を使う。こちらに置いた1枚は要らなくなった
                if (File.Exists(_store.Paths.ItemFile(entry.ItemId)))
                {
                    DeleteQuietly(directory);
                    continue;
                }

                if (FirstImage(directory) is not null)
                {
                    continue;
                }

                var url = entry.ImageUrl;
                if (url is null)
                {
                    var fetched = await _client.GetItemJsonAsync(entry.ItemId, cancellationToken);
                    if (fetched.Status == BoothFetchStatus.NotFound)
                    {
                        url = string.Empty;
                    }
                    else if (!fetched.IsSuccess || fetched.Value is null)
                    {
                        // 通信の失敗では何も決めない。次の起動でまた試す
                        continue;
                    }
                    else
                    {
                        url = BoothItemMapper.Map(fetched.Value, DateTimeOffset.Now).Images.FirstOrDefault()?.OriginalUrl ?? string.Empty;
                    }

                    observed[entry.ItemId] = url;
                    if (observed.Count >= FlushEvery)
                    {
                        await FlushAsync(observed, cancellationToken);
                    }
                }

                if (url.Length == 0)
                {
                    continue;
                }

                if (await _images.SyncOneToAsync(directory, url, cancellationToken) && FirstImage(directory) is not null)
                {
                    saved++;
                    AvatarImageSaved?.Invoke(entry.ItemId);
                }
            }
        }
        finally
        {
            // 途中で止めても、見たURLは残す。次の起動で問い合わせ直さずに済む
            await FlushAsync(observed, CancellationToken.None);
        }

        return saved;
    }

    /// <summary>
    /// 画面に出す絵の場所。持っている（商品として取り込んである）なら商品の1枚目、
    /// そうでなければ <c>images/_avatars</c> の1枚。どちらも無ければ null。
    /// </summary>
    public static string? IconPath(AppPaths paths, string avatarItemId, ItemRecord? item)
    {
        if (item is not null)
        {
            var directory = paths.ItemImagesDir(item.Id);
            var files = ListImages(directory);
            if (files.Count > 0
                && ItemImageOrder.Paths(directory, item.Booth.Images, files, item.Local.UserImages).FirstOrDefault() is { } first)
            {
                return first;
            }
        }

        return FirstImage(paths.AvatarImagesDir(avatarItemId));
    }

    private async Task FlushAsync(Dictionary<string, string> observed, CancellationToken cancellationToken)
    {
        if (observed.Count == 0)
        {
            return;
        }

        var pending = new Dictionary<string, string>(observed, StringComparer.Ordinal);
        observed.Clear();

        await _store.Avatars.UpdateAsync(
            latest => new AvatarRegistry
            {
                Entries = latest.Entries
                    .Select(entry => pending.TryGetValue(entry.ItemId, out var url) ? entry with { ImageUrl = url } : entry)
                    .ToList(),
                BaseGroups = latest.BaseGroups,
            },
            cancellationToken);
    }

    private static string? FirstImage(string directory) => ListImages(directory).FirstOrDefault();

    private static IReadOnlyList<string> ListImages(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.webp").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList()
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 消せなくても表示は商品の1枚目を使うので困らない。次の起動でまた消す
        }
    }
}
