using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

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

    /// <summary>
    /// 入り先の要約を書く（書き直す）要るファイルか：まだ書いていないか、**書いた要約が zip の中の unitypackage を全部覆っていない**
    /// （<see cref="UnityHandoff.KnownPackages"/> が使わない）。
    ///
    /// 控えは商品ページなどで包みを1つずつ足しても作られ（<see cref="UnityPackagePathStore.Add"/>）、前はそれを「ある」と見て読み直さず、
    /// 一部の包みしか載っていない要約を書き得た。そのままだと表示の側が毎回 zip を開いて補う（1GB 級の zip が HDD にあると重い）。
    /// </summary>
    public static bool NeedsSummary(LocalFileRecord file)
        => HasPackages(file) && UnityHandoff.KnownPackages(file) is null;

    /// <summary>控えが、中身の一覧（<see cref="LocalFileRecord.Contents"/>）にある unitypackage を全部持っているか。</summary>
    private static bool Covers(LocalFileRecord file, IReadOnlyDictionary<string, IReadOnlyList<UnityPackageAsset>>? stored)
        => stored is not null && UnityHandoff.PackageEntriesIn(file).All(stored.ContainsKey);

    /// <summary>まだ入り先を書いていない・一部しか書いていない手元のファイル（前の取り込みの読み残しも含む）。</summary>
    public async Task<UnityPackagePending> FindPendingAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var pending = loaded.Items
            .SelectMany(item => item.Local.LocalFiles
                .Where(NeedsSummary)
                .Select(file => (ItemId: item.Id, File: file)))
            .ToList();

        return new UnityPackagePending(
            pending.Select(entry => entry.File).ToList(),
            pending.Select(entry => entry.ItemId).Distinct(StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// 控えの無い物・控えが中の unitypackage を全部持っていない物を読む。**小さい zip から1件ずつ、優先度を下げたスレッドで。**
    /// 大きな zip を同時に解くとディスクを取り合う。小さい物から片付けると、多くの商品が早く揃う。
    /// 読んだ数を返す。1件読むごとに控えを書くので、途中で止めても次は続きから読む。
    /// </summary>
    public async Task<int> ReadAsync(IEnumerable<LocalFileRecord> files, CancellationToken cancellationToken = default)
    {
        var targets = files
            .Where(HasPackages)
            .DistinctBy(file => file.Hash, StringComparer.OrdinalIgnoreCase)
            .Select(file => (Record: file, Stored: pathStore.Load(file.Hash)))
            .Where(target => !Covers(target.Record, target.Stored))

            // 新しいバージョンが書いた控えは、読めなくても zip を解き直さない（書けないので、取り込みのたびに解き直すだけになる）
            .Where(target => !pathStore.IsTooNew(target.Record.Hash))
            .Select(target => (target.Record, target.Stored, Zip: target.Record.Paths.FirstOrDefault(path =>
                path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path))))
            .Where(target => target.Zip is not null)
            .OrderBy(target => target.Record.SizeBytes)
            .ToList();

        var read = 0;
        foreach (var (record, stored, zip) in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 4K テクスチャを大量に同梱した物でも、中身は写さずに流して読む（UnityHandoff.ReadAssets）ので、メモリは増えない。
            // 読んだパスは控えのファイルに書くので、画面が使うメモリの表には入れない（手元の全部で表を埋めて、画面の分を押し出していた）。
            // 控えに既にある包みは解き直さない（中身は zip のハッシュで決まる。欠けた分だけ読めば足りる）
            var packages = await RunBelowNormalAsync(() => UnityHandoff.FindPackages(zip!)
                .DistinctBy(package => package.EntryPath, StringComparer.Ordinal)
                .ToDictionary(
                    package => package.EntryPath,
                    package => stored is not null && stored.TryGetValue(package.EntryPath, out var known)
                        ? known
                        : UnityHandoff.ReadAssets(package, remember: false),
                    StringComparer.Ordinal));

            // zip を開けなかった（ほかのアプリが開いている・壊れている）ときは控えない。空の控えを書くと、
            // 入り先の無い要約が書かれて次の取り込みでまた読み直すだけになる。控えなければ次の取り込みでまた開く
            if (packages.Count == 0)
            {
                continue;
            }

            await pathStore.SaveAsync(record.Hash, packages, cancellationToken);
            read++;
        }

        return read;
    }

    /// <summary>
    /// 控えから入り先を item に写す。**まだ書いていない（<see cref="LocalFileRecord.UnityPackages"/> が null の）ファイルと、
    /// 一部しか書いていないファイル（<see cref="NeedsSummary"/>）だけ。**
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

            // 要約は読み取りの控え（ディスク）から先に組んでおき、錠の中では当てるだけにする
            var summaries = new Dictionary<string, List<UnityPackageSummary>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in item.Local.LocalFiles)
            {
                // 一部しか書いていない要約は、控えがそろったときだけ書き直す（そろわない控えで書き直しても同じ欠けた要約になる）
                if (NeedsSummary(file) && pathStore.Load(file.Hash) is { } packages
                    && (file.UnityPackages is null || Covers(file, packages)))
                {
                    summaries[file.Hash] = packages
                        .Select(pair => new UnityPackageSummary
                        {
                            Entry = pair.Key,
                            Roots = UnityHandoff.DestinationRoots(pair.Value.Select(asset => asset.Path)),
                        })
                        .ToList();
                }
            }

            if (summaries.Count == 0)
            {
                continue;
            }

            // 手元のファイルの一覧は書く直前の今の値に当てる。
            // 全件を順に読んで控えを開く間に、取り込みや「この商品から外す」が同じ商品の一覧を書き換えることがあり、
            // 読んだ写しの一覧で書くと、その間に足されたファイルや外した印が消えていた
            if (await store.Items.ChangeLocalAsync(
                    itemId,
                    current =>
                    {
                        var changed = false;
                        var files = current.LocalFiles
                            .Select(file =>
                            {
                                if (!NeedsSummary(file) || !summaries.TryGetValue(file.Hash, out var summary))
                                {
                                    return file;
                                }

                                changed = true;
                                return file with { UnityPackages = summary };
                            })
                            .ToList();

                        return changed ? current with { LocalFiles = files } : null;
                    },
                    [LocalField.LocalFiles],
                    cancellationToken))
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
            Name = "unitypackageの読み取り",
        };
        thread.Start();
        return done.Task;
    }
}
