using Chmonos.Core.Models;

namespace Chmonos.Core.Storage;

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

    /// <summary>
    /// 読んだ改変の写し。鍵は改変のID、値は「読んだときのファイルの大きさと更新日時」と読んだ中身。
    ///
    /// 全件の読み込みは商品ページを開くたび（その商品を使った改変を探す）・改変の画面・「改変に追加」・
    /// 検索の改変の条件から呼ばれ、1改変1ファイルなので300件なら毎回300ファイルを開いていた
    /// （stress-manage の300件で1回 約39ms・割り当て 1.2MB。2026-09-29）。
    /// **大きさと更新日時が同じなら前に読んだ中身を返す**（<see cref="ItemRepository"/> の写しと同じ作り）。
    ///
    /// 中身（<see cref="ModificationRecord"/>）は init だけのレコードと読むだけの一覧なので、呼んだ所どうしで共有してよい。
    /// 変えるときは <c>with</c> で写しを作る。
    ///
    /// 持つ量は300件で約380KB（stress-manage で測った）。上限は付けない——
    /// 改変は人が1つずつ作る物で、数千件になることは見込んでいない。消した改変は <see cref="Delete"/> と全件の読み込みで落とす。
    ///
    /// 新しさ：自分の書き込みは改変の錠の中で写しも差し替える。アプリの外で書き換えた物（手で直した JSON）は
    /// 大きさか日時が変わるので読み直す。見逃すのは「同じ大きさで、時刻の刻み（NTFS で最大約16ms）の中に外から2回書かれた」ときだけ。
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedRecord> _cache = new(StringComparer.Ordinal);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <summary>控えの1件。参照で比べるためにクラスにしている（<see cref="ReadThroughAsync"/> の <c>TryUpdate</c>）。</summary>
    private sealed class CachedRecord(long length, DateTime lastWriteUtc, ModificationRecord record)
    {
        public ModificationRecord Record { get; } = record;

        public bool Matches(long otherLength, DateTime otherLastWriteUtc)
            => length == otherLength && lastWriteUtc == otherLastWriteUtc;
    }

    public ModificationRepository(AppPaths paths)
    {
        _paths = paths;
    }

    public bool Exists(string id) => File.Exists(_paths.ModificationFile(id));

    /// <summary>1件を読む。ファイルが前に読んだときと同じ（大きさと更新日時）なら写しを返す。</summary>
    public Task<ModificationRecord?> LoadAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var path = _paths.ModificationFile(id);
        var info = new FileInfo(path);
        return info.Exists
            ? ReadThroughAsync(id, path, info.Length, info.LastWriteTimeUtc, cancellationToken)
            : Task.FromResult<ModificationRecord?>(null);
    }

    /// <summary>
    /// 控えが今のファイルと合えば控えを、合わなければ読んで控える。
    ///
    /// **大きさと日時は読む前に取った物を渡す**（読んだ後に取ると、読んだ後に書かれた新しい日時に古い中身を結び付ける）。
    /// 控えを入れるのは、見たときから誰も差し替えていないときだけ（書き手が錠の中で入れた新しい控えを古い中身で潰さない）。
    /// </summary>
    private async Task<ModificationRecord?> ReadThroughAsync(
        string id,
        string path,
        long length,
        DateTime lastWriteUtc,
        CancellationToken cancellationToken)
    {
        _cache.TryGetValue(id, out var seen);
        if (seen is not null && seen.Matches(length, lastWriteUtc))
        {
            return seen.Record;
        }

        var record = await JsonStore.ReadAsync<ModificationRecord>(path, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var entry = new CachedRecord(length, lastWriteUtc, record);
        if (seen is null)
        {
            _cache.TryAdd(id, entry);
        }
        else
        {
            _cache.TryUpdate(id, entry, seen);
        }

        return record;
    }

    /// <summary>書く。改変の錠の中で書いて、写しも書いた物に差し替える（差し替えを書いた順に並べるため）。</summary>
    public async Task SaveAsync(ModificationRecord record, CancellationToken cancellationToken = default)
    {
        var gate = LockFor(record.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await WriteAsync(record, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>書いた後の大きさと日時で控えるので、直後の読み込みは読み直さずに書いた物を返す。**錠を持った所から呼ぶ。**</summary>
    private async Task WriteAsync(ModificationRecord record, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.ModificationsDir);
        var path = _paths.ModificationFile(record.Id);
        try
        {
            await JsonStore.WriteAsync(path, record, cancellationToken);
        }
        catch
        {
            // 置き換えの途中で失敗すると、本体が新旧どちらか分からない。控えは捨てて次に読み直す
            _cache.TryRemove(record.Id, out _);
            throw;
        }

        var info = new FileInfo(path);
        if (info.Exists)
        {
            _cache[record.Id] = new CachedRecord(info.Length, info.LastWriteTimeUtc, record);
        }
        else
        {
            _cache.TryRemove(record.Id, out _);
        }
    }

    /// <summary>
    /// 1件を読み直して書き換える。**読んでから書くまでを錠の中に入れる。**
    ///
    /// 書き手は画面（名前・メモ・並べ替え）と Unity からの受け取り（構成物）の2つあり、
    /// 本文を打っている最中に構成物が入ると、錠が無ければ片方が消える。
    /// 錠の中では写しを使わずディスクから読む（古い写しに変更を当てて書くと、外で直した分を消すため）。
    /// </summary>
    /// <returns>その改変が無ければ false。</returns>
    public async Task<bool> UpdateAsync(
        string id,
        Func<ModificationRecord, ModificationRecord> change,
        CancellationToken cancellationToken = default)
    {
        var gate = LockFor(id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = _paths.ModificationFile(id);
            if (!File.Exists(path)
                || await JsonStore.ReadAsync<ModificationRecord>(path, cancellationToken) is not { } record)
            {
                return false;
            }

            await WriteAsync(change(record), cancellationToken);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim LockFor(string id) => _locks.GetOrAdd(id, static _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// 全部読む。**新しく作った順（作成日時の降順）**で返す。
    ///
    /// 同じアバターに同じ名前の改変を作れるようにしてあるので、
    /// 一覧では日付が見分けの手掛かりになる。
    ///
    /// 写しと合うファイルは読まない（<see cref="_cache"/>）。一覧に無くなったファイルの写しは落とす。
    /// </summary>
    public async Task<ModificationLoadResult> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        var loaded = new List<ModificationRecord>();
        var failed = new List<string>();

        // 大きさと日時は列挙で得た物を使う（1ファイルずつ問い合わせると、写しが当たっても300件で数ms かかる）
        var files = Directory.Exists(_paths.ModificationsDir)
            ? new DirectoryInfo(_paths.ModificationsDir).EnumerateFiles(ModificationId.Prefix + "*.json").ToList()
            : [];
        var ids = new List<string>(files.Count);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileNameWithoutExtension(file.Name);
            ids.Add(id);

            try
            {
                if (await ReadThroughAsync(id, file.FullName, file.Length, file.LastWriteTimeUtc, cancellationToken) is { } record)
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

        var listed = new HashSet<string>(ids, StringComparer.Ordinal);
        foreach (var gone in _cache.Keys.Where(id => !listed.Contains(id)).ToList())
        {
            _cache.TryRemove(gone, out _);
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

        // 消えたファイルの写しは読み込みで返らない（在るかを先に見る）が、持ち続ける理由も無い
        _cache.TryRemove(id, out _);

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
