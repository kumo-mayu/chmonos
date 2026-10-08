using System.Text.Json;
using Chmonos.Core.Services;

namespace Chmonos.Core.Storage;

/// <summary><c>unitypackages/&lt;ハッシュ&gt;.json</c> の中身。</summary>
public sealed class UnityPackagePathsFile
{
    /// <summary>
    /// zip の中の場所 → その unitypackage の中身（GUID → Unity 上のパス。<c>Assets/FUKA/…</c> のまま）。
    ///
    /// **GUID を鍵にする。**unitypackage の tar はアセットごとに <c>&lt;GUID&gt;/pathname</c> を持つので、1つの物の中で GUID は重ならない
    /// （パスは壊れた物では重なり得る）。1行が「GUID: パス」になり、千本を超える物でも開いて読める長さに収まる。
    /// GUID は、利用者がプロジェクトの中でフォルダを移した・名前を変えた物を見つけるのに使う（<see cref="UnityProjectGuids"/>）。
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> Packages { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// unitypackage の中身の全部のパスと GUID の控え（2026-09-13 ユーザ判断。GUID は 2026-09-29 に足した）。**鍵は zip のハッシュ**なので、控えが古くなることは無い
/// （中身が変われば別のハッシュ＝別の手元のファイル）。消しても、取り込みの裏か、使うときに zip を解き直すだけで壊れない。
///
/// 読めない控え（無い・壊れている）は、使うときに zip を解いて書き直す。控えは作り直せる写しなので、読めなくても壊れない。
/// </summary>
public sealed class UnityPackagePathStore(AppPaths paths)
{
    /// <summary>読める控えがあるか。読めない控えは無いのと同じ（読み直して書き直す）。</summary>
    /// <remarks>
    /// 鍵は商品の記録（手で直せる JSON）に書かれたハッシュからも来る。**ハッシュの形でない鍵では場所を組まない**
    /// （<see cref="StoreIds"/>）：読むは「無い」、書くは何もしない。控えは作り直せる写しなので、書かなくても壊れない
    /// </remarks>
    public bool Has(string hash) => StoreIds.IsPackageHash(hash) && File.Exists(paths.UnityPackageFile(hash)) && Load(hash) is not null;

    /// <summary>
    /// 新しいバージョンの Chmonos が書いた控えか（点検26）。読めない控えは捨てて書き直すが、新しすぎる控えは
    /// 壊れているのではなく、このバージョンが知らない形なので、書き直すと新しいバージョンが入れた中身を消す。書かず、読み直しもしない
    /// </summary>
    public bool IsTooNew(string hash)
    {
        if (!StoreIds.IsPackageHash(hash))
        {
            return false;
        }

        var path = paths.UnityPackageFile(hash);
        try
        {
            return File.Exists(path) && StoreFormat.VersionOf(File.ReadAllBytes(path)) > StoreFormat.Current;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>読めなければ null（無い・壊れている・新しすぎる）。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<UnityPackageAsset>>? Load(string hash)
    {
        if (!StoreIds.IsPackageHash(hash))
        {
            return null;
        }

        try
        {
            return JsonStore.Read<UnityPackagePathsFile>(paths.UnityPackageFile(hash))?.Packages
                .ToDictionary(
                    pair => pair.Key,
                    // 手で直した控えの欠けた一覧（null）は空として受ける
                    pair => (IReadOnlyList<UnityPackageAsset>)(pair.Value ?? [])
                        .Where(asset => !string.IsNullOrWhiteSpace(asset.Value))
                        .Select(asset => new UnityPackageAsset(asset.Key, asset.Value))
                        .ToList(),
                    StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 1つの zip の分を丸ごと書く（取り込みの裏で読んだとき）。
    ///
    /// **非同期で書く。**取り込みは画面のスレッドの文脈で進むので、ここが同期だと保存先を運んでいる間に
    /// 画面のスレッドが門を待って止まり、門を開ける側も画面のスレッドを待って、互いに待ち合って固まっていた。
    /// </summary>
    public async Task SaveAsync(
        string hash,
        IReadOnlyDictionary<string, IReadOnlyList<UnityPackageAsset>> packages,
        CancellationToken cancellationToken = default)
    {
        if (!StoreIds.IsPackageHash(hash))
        {
            return;
        }

        var gate = LockFor(hash);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (IsTooNew(hash))
            {
                return;
            }

            await JsonStore.WriteAsync(paths.UnityPackageFile(hash), ToFile(packages), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 1つだけ足す（取り込みの裏より先に、商品ページなどで読んだとき）。
    /// 同期なのは、呼び手（<see cref="UnityHandoff.ReadAssets"/>）が zip を解く同期の処理で、
    /// 必ず裏のスレッドで呼ばれるため（画面のスレッドでは止まり得る。<see cref="StoreWriteGate.Enter"/>）。
    /// </summary>
    public void Add(string hash, string entry, IReadOnlyList<UnityPackageAsset> assets)
    {
        if (!StoreIds.IsPackageHash(hash))
        {
            return;
        }

        // 取り込みの裏と商品ページが同じ zip を同時に開くので、読み直してから足すまでを1本にする
        var gate = LockFor(hash);
        gate.Wait();
        try
        {
            if (IsTooNew(hash))
            {
                return;
            }

            // 読めない控えは捨てて、今読んだ分から書き直す。ほかの物は使うときに読み直して足す
            var packages = Load(hash)?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, IReadOnlyList<UnityPackageAsset>>(StringComparer.Ordinal);
            packages[entry] = assets;
            JsonStore.Write(paths.UnityPackageFile(hash), ToFile(packages));
        }
        finally
        {
            gate.Release();
        }
    }

    private static UnityPackagePathsFile ToFile(IReadOnlyDictionary<string, IReadOnlyList<UnityPackageAsset>> packages)
        => new()
        {
            Packages = packages.ToDictionary(
                pair => pair.Key,
                // 同じ GUID は1つの物の中で重ならないが、壊れた物を掴んでも書けるよう先の方を残す
                pair => pair.Value
                    .GroupBy(asset => asset.Guid, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().Path, StringComparer.OrdinalIgnoreCase),
                StringComparer.Ordinal),
        };

    // 丸ごと書く（非同期）と1つ足す（同期）が同じ錠を取るので、どちらからも使える SemaphoreSlim にする
    private static SemaphoreSlim LockFor(string hash) => s_locks.GetOrAdd(hash, static _ => new SemaphoreSlim(1, 1));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> s_locks
        = new(StringComparer.OrdinalIgnoreCase);
}
