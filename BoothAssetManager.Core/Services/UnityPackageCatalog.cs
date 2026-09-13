using System.Text.Json;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

/// <summary>前の取り込みで読み残した物（中断したときなど）。</summary>
public sealed record UnityPackagePending(IReadOnlyList<LocalFileRecord> Files, IReadOnlyList<string> ItemIds);

/// <summary>
/// 手元の zip の中の unitypackage を1度だけ読み、Unity のどこに入るかを記録する（2026-09-13 ユーザ判断）。
///
/// 前は商品ページ・改変の画面・「Unityで選択」・プロジェクトの中を調べる・連続送りの前と、使うたびに zip を最後まで解いていた
/// （手元の13件で合計2.1秒、1GB の物で約3秒）。中身はハッシュが同じなら変わらないので、取り込みの裏で読んで残す。
///
/// **2つに分けてある。**
/// <list type="bullet">
/// <item>読む（<see cref="ReadAsync"/>）：zip を解き、全部のパスを控え（ハッシュごと）に書く。item には触らないので、取り込みの問い合わせの段と同時に進めてよい</item>
/// <item>書き込む（<see cref="ApplyAsync"/>）：控えから入り先を item の手元のファイルの欄に写す。item の手元のファイルは取り込みの①も書き、錠が無い
///   （<see cref="ItemRepository.SaveLocalAsync"/> は読み直して書くだけ）ので、取り込みでは①が終わってから書く</item>
/// </list>
/// </summary>
public sealed class UnityPackageCatalog(DataStore store, UnityPackagePathStore pathStore)
{
    /// <summary>読む対象になり得るか：外していない zip で、中身の一覧に unitypackage がある。</summary>
    public static bool HasPackages(LocalFileRecord file)
        => !file.Detached && file.Contents.Any(name => name.EndsWith(UnityHandoff.PackageExtension, StringComparison.OrdinalIgnoreCase));

    /// <summary>まだ入り先を書いていない手元のファイル（前の取り込みの読み残しも含む）。</summary>
    public async Task<UnityPackagePending> FindPendingAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var pending = loaded.Items
            .SelectMany(item => item.Local.LocalFiles
                .Where(file => file.UnityPackages is null && HasPackages(file))
                .Select(file => (ItemId: item.Id, File: file)))
            .ToList();

        return new UnityPackagePending(
            pending.Select(entry => entry.File).ToList(),
            pending.Select(entry => entry.ItemId).Distinct(StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// 控えの無い物を読む。**小さい zip から1件ずつ、優先度を下げたスレッドで。**
    /// 大きな zip を同時に解くとディスクを取り合う。小さい物から片付けると、多くの商品が早く揃う。
    /// 読んだ数を返す。1件読むごとに控えを書くので、途中で止めても次は続きから読む。
    /// </summary>
    public async Task<int> ReadAsync(IEnumerable<LocalFileRecord> files, CancellationToken cancellationToken = default)
    {
        var targets = files
            .Where(HasPackages)
            .DistinctBy(file => file.Hash, StringComparer.OrdinalIgnoreCase)
            .Where(file => !pathStore.Has(file.Hash))
            .Select(file => (Record: file, Zip: file.Paths.FirstOrDefault(path =>
                path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path))))
            .Where(target => target.Zip is not null)
            .OrderBy(target => target.Record.SizeBytes)
            .ToList();

        var read = 0;
        foreach (var (record, zip) in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 4K テクスチャを大量に同梱した物でも、中身は写さずに流して読む（UnityHandoff.ReadAssetPaths）ので、メモリは増えない
            var packages = await RunBelowNormalAsync(() => UnityHandoff.FindPackages(zip!)
                .DistinctBy(package => package.EntryPath, StringComparer.Ordinal)
                .ToDictionary(
                    package => package.EntryPath,
                    package => UnityHandoff.ReadAssetPaths(package),
                    StringComparer.Ordinal));

            pathStore.Save(record.Hash, packages);
            read++;
        }

        return read;
    }

    /// <summary>
    /// 控えから入り先を item に写す。**まだ書いていない（<see cref="LocalFileRecord.UnityPackages"/> が null の）ファイルだけ。**
    /// 書き込んだ商品の数を返す。
    /// </summary>
    public async Task<int> ApplyAsync(IEnumerable<string> itemIds, CancellationToken cancellationToken = default)
    {
        var written = 0;
        foreach (var itemId in itemIds.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            ItemRecord? item;
            try
            {
                item = await store.Items.LoadAsync(itemId, cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                continue;
            }

            if (item is null)
            {
                continue;
            }

            var changed = false;
            var files = item.Local.LocalFiles
                .Select(file =>
                {
                    if (file.UnityPackages is not null || !HasPackages(file) || pathStore.Load(file.Hash) is not { } packages)
                    {
                        return file;
                    }

                    changed = true;
                    return file with
                    {
                        UnityPackages = packages
                            .Select(pair => new UnityPackageSummary
                            {
                                Entry = pair.Key,
                                Roots = UnityHandoff.DestinationRoots(pair.Value),
                            })
                            .ToList(),
                    };
                })
                .ToList();

            if (!changed)
            {
                continue;
            }

            // 手元のファイルの欄だけを書く。書く直前に読み直すので、ほかの欄は今の値が残る
            if (await store.Items.SaveLocalAsync(
                    itemId, item.Local with { LocalFiles = files }, [LocalField.LocalFiles], cancellationToken: cancellationToken))
            {
                written++;
            }
        }

        return written;
    }

    /// <summary>手でファイルを付けた後（未確定での確定など）。その商品のまだ読んでいないファイルを読み、書き込む。</summary>
    public async Task FillItemAsync(string itemId, CancellationToken cancellationToken = default)
    {
        if (await store.Items.LoadAsync(itemId, cancellationToken) is not { } item)
        {
            return;
        }

        await ReadAsync(item.Local.LocalFiles, cancellationToken);
        await ApplyAsync([itemId], cancellationToken);
    }

    /// <summary>優先度を下げたスレッドで動かす。解くのは CPU を使うので、画面と問い合わせの邪魔をしない。</summary>
    private static Task<T> RunBelowNormalAsync<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "unitypackage の読み取り",
        };
        thread.Start();
        return done.Task;
    }
}
