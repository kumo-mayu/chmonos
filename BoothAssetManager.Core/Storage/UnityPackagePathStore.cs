using System.Text.Json;

namespace BoothAssetManager.Core.Storage;

/// <summary><c>unitypackages/&lt;ハッシュ&gt;.json</c> の中身。</summary>
public sealed class UnityPackagePathsFile
{
    /// <summary>zip の中の場所 → unitypackage の中身のパス（<c>Assets/FUKA/…</c> のまま）。</summary>
    public Dictionary<string, List<string>> Packages { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// unitypackage の中身の全部のパスの控え（2026-09-13 ユーザ判断）。**鍵は zip のハッシュ**なので、控えが古くなることは無い
/// （中身が変われば別のハッシュ＝別の手元のファイル）。消しても、取り込みの裏か、使うときに zip を解き直すだけで壊れない。
/// </summary>
public sealed class UnityPackagePathStore(AppPaths paths)
{
    public bool Has(string hash) => File.Exists(paths.UnityPackageFile(hash));

    /// <summary>読めなければ null（無い・壊れている）。</summary>
    public IReadOnlyDictionary<string, List<string>>? Load(string hash)
    {
        try
        {
            return JsonStore.Read<UnityPackagePathsFile>(paths.UnityPackageFile(hash))?.Packages;
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
        IReadOnlyDictionary<string, IReadOnlyList<string>> packages,
        CancellationToken cancellationToken = default)
    {
        var gate = LockFor(hash);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await JsonStore.WriteAsync(paths.UnityPackageFile(hash), ToFile(packages), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 1つだけ足す（取り込みの裏より先に、商品ページなどで読んだとき）。
    /// 同期なのは、呼び手（<see cref="Services.UnityHandoff.ReadAssetPaths"/>）が zip を解く同期の処理で、
    /// 必ず裏のスレッドで呼ばれるため（画面のスレッドでは止まり得る。<see cref="StoreWriteGate.Enter"/>）。
    /// </summary>
    public void Add(string hash, string entry, IReadOnlyList<string> assetPaths)
    {
        // 取り込みの裏と商品ページが同じ zip を同時に開くので、読み直してから足すまでを1本にする
        var gate = LockFor(hash);
        gate.Wait();
        try
        {
            var packages = Load(hash)?.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            packages[entry] = assetPaths;
            JsonStore.Write(paths.UnityPackageFile(hash), ToFile(packages));
        }
        finally
        {
            gate.Release();
        }
    }

    private static UnityPackagePathsFile ToFile(IReadOnlyDictionary<string, IReadOnlyList<string>> packages)
        => new()
        {
            Packages = packages.ToDictionary(pair => pair.Key, pair => pair.Value.ToList(), StringComparer.Ordinal),
        };

    // 丸ごと書く（非同期）と1つ足す（同期）が同じ錠を取るので、どちらからも使える SemaphoreSlim にする
    private static SemaphoreSlim LockFor(string hash) => s_locks.GetOrAdd(hash, static _ => new SemaphoreSlim(1, 1));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> s_locks
        = new(StringComparer.OrdinalIgnoreCase);
}
