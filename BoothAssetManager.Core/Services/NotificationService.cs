using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

public interface INotificationService
{
    IReadOnlyList<NotificationRecord> Load();

    Task<bool> SetReadAsync(string id, bool isRead, CancellationToken cancellationToken = default);

    Task<int> MarkAllReadAsync(CancellationToken cancellationToken = default);

    Task<int> DetectOrphanReferencesAsync(CancellationToken cancellationToken = default);

    /// <summary>指したものだけをまとめて既読にする。</summary>
    Task<int> MarkReadAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);

    /// <summary>用が済んだ通知に「解消済み」の印を付ける。</summary>
    Task<int> ResolveAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);

    /// <summary>説明文の見出しが取れなくなっていないかを調べる。取れていれば前の知らせを解消済みにする。</summary>
    Task<bool> DetectPageStructureAsync(CancellationToken cancellationToken = default);

    /// <summary>上限を超えた分を落とす。起動時に1回呼ぶ（足すときは上限を見ていない）。</summary>
    Task<int> PruneAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 要確認（<c>notifications.json</c>）の読み書き。
///
/// 確認しても消さずに既読にするのは、「見た」ことと「無かったこと」を分けるため。
/// 溜まり続けないよう、上限を超えたら古い既読から捨てる。未読は捨てない
/// （未読を落とすと、気付かないまま消えたことにも気付けない）。
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly DataStore _store;
    private readonly Func<AppSettings> _currentSettings;

    public NotificationService(DataStore store, AppSettings? settings = null)
        : this(store, SettingsSource.Fixed(settings))
    {
    }

    /// <param name="currentSettings">使うたびに今の設定を返すもの（<see cref="SettingsSource"/>）。</param>
    public NotificationService(DataStore store, Func<AppSettings> currentSettings)
    {
        _store = store;
        _currentSettings = currentSettings;
    }

    /// <summary>今の設定。**抱えずに毎回読む。**</summary>
    private AppSettings _settings => _currentSettings();

    public IReadOnlyList<NotificationRecord> Load() => _store.Notifications.Load();

    public async Task<bool> SetReadAsync(string id, bool isRead, CancellationToken cancellationToken = default)
    {
        var records = _store.Notifications.Load();
        var index = records.FindIndex(record => record.Id == id);
        if (index < 0 || records[index].IsRead == isRead)
        {
            return false;
        }

        records[index] = records[index] with { IsRead = isRead };
        await SaveWithPruneAsync(records, cancellationToken);
        return true;
    }

    public async Task<int> MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        var records = _store.Notifications.Load();
        var changed = 0;

        for (var index = 0; index < records.Count; index++)
        {
            if (!records[index].IsRead)
            {
                records[index] = records[index] with { IsRead = true };
                changed++;
            }
        }

        if (changed > 0)
        {
            await SaveWithPruneAsync(records, cancellationToken);
        }

        return changed;
    }

    /// <summary>
    /// マスタに無いuserTag・属性を参照しているitemを探して知らせる。
    ///
    /// マスタのJSONを手で書き換えることを許している以上、item側だけが古い名前を
    /// 指したままになり得る。そのままだと絞り込みに出てこないのに気付けない。
    /// </summary>
    public async Task<int> DetectOrphanReferencesAsync(CancellationToken cancellationToken = default)
    {
        var master = _store.UserTags.Load();
        var attributes = _store.Attributes.Load().Attributes
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var records = _store.Notifications.Load();
        var added = 0;

        // 今も食い違っている物の通知ID。ここに無い分は直したということなので「解消済み」にする
        var stillBroken = new HashSet<string>(StringComparer.Ordinal);

        // フォルダ登録が残っている場所。登録を解除したら「zipを入手した」の用は済んでいる
        var registeredFolders = loaded.Items
            .SelectMany(item => item.Local.LocalFolders.Select(folder => folder.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in loaded.Items)
        {
            // トップだけでなくサブも見る。サブ名がずれると、画面はマスタにある分しか
            // 出さないので、どこにも表示されないまま絞り込みから消える
            var missingTags = UserTagService.FindOrphans(master, [item])
                .Select(orphan => orphan.IsSub ? $"{orphan.Top}／{orphan.Sub}" : orphan.Top)
                .ToList();

            var missingAttributes = item.Local.Attributes.Keys
                .Where(name => !attributes.Contains(name))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (missingTags.Count == 0 && missingAttributes.Count == 0)
            {
                continue;
            }

            var parts = new List<string>();
            if (missingTags.Count > 0)
            {
                parts.Add($"ユーザータグ：{string.Join("・", missingTags)}");
            }

            if (missingAttributes.Count > 0)
            {
                parts.Add($"属性：{string.Join("・", missingAttributes)}");
            }

            // 「どの分類か」だけを書く。何が起きているかの説明は束の見出しに出る（ユーザ指示 2026-09-18）
            var detail = string.Join(" / ", parts);

            // itemごとに1件だけ持つ。既読でも作り直さないのは、
            // 直さないまま画面を開くたびに同じ話が積み上がるのを避けるため
            // （既読は「この食い違いは見た」という意思表示として扱う）。
            var id = $"orphan-tag:{item.Id}";
            stillBroken.Add(id);
            var existing = records.FindIndex(record => record.Id == id);
            if (existing >= 0)
            {
                if (records[existing].Detail == detail)
                {
                    continue;
                }

                // 中身が変わったなら別の話なので、既読を解いて出し直す
                records.RemoveAt(existing);
            }

            records.Add(new NotificationRecord
            {
                Id = id,
                Kind = NotificationKind.OrphanTag,
                ItemId = item.Id,
                Title = item.DisplayName,
                Detail = detail,
                CreatedAt = DateTimeOffset.Now,
            });

            added++;
        }

        // 用が済んだ物に「解消済み」を付ける（ユーザ判断 2026-09-18）。
        // 消さないのは、何が起きていたかを後から辿れるようにするため
        var resolved = 0;
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.IsResolved)
            {
                continue;
            }

            var isDone = record.Kind switch
            {
                NotificationKind.OrphanTag => !stillBroken.Contains(record.Id),
                NotificationKind.ArchiveFoundForFolder =>
                    record.Id.StartsWith("archive-found:", StringComparison.Ordinal)
                        && !registeredFolders.Contains(record.Id["archive-found:".Length..]),
                _ => false,
            };

            if (isDone)
            {
                records[index] = record with { IsResolved = true };
                resolved++;
            }
        }

        if (added > 0 || resolved > 0)
        {
            await SaveWithPruneAsync(records, cancellationToken);
        }

        return added;
    }

    /// <summary>見出しが取れているかを判断するのに必要な、最近取り直した商品の数。これ未満なら何も言わない。</summary>
    private const int StructureSampleMinimum = 5;

    /// <summary>説明文はあるのに見出しが0件の割合。これを超えたら、BOOTH側の作りが変わった疑い。</summary>
    private const double StructureBrokenRatio = 0.8;

    /// <summary>「本文がある」とみなす説明文の長さ。短い一言だけの商品は見出しを持たないのが普通。</summary>
    private const int StructureBodyLength = 200;

    /// <summary>最近取り直したとみなす日数。⑦の間隔（7日±3日）より少し長く取る。</summary>
    private const int StructureRecentDays = 14;

    /// <summary>
    /// 説明文の見出しが取れなくなっていないかを調べる。
    ///
    /// BOOTH側のHTMLの作りが変わると、読み取りが静かに全滅する（対応アバターの検出も痩せる）。
    /// **1商品の話ではなくアプリの話**なので、要確認の束には出さず、ナビの「設定」の上の帯で知らせる
    /// （ユーザ判断 2026-09-18）。人が打てる手はアプリの更新を待つことなので、そう読める文にする。
    /// </summary>
    public async Task<bool> DetectPageStructureAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var since = DateTimeOffset.Now.AddDays(-StructureRecentDays);

        var recent = loaded.Items
            .Where(item => item.Booth.FetchedAt is { } at && at >= since)
            .Where(item => (item.Booth.Description?.Length ?? 0) >= StructureBodyLength)
            .ToList();

        var broken = recent.Count(item => item.Booth.H2Sections.Count == 0);
        var suspect = recent.Count >= StructureSampleMinimum
            && broken >= (int)Math.Ceiling(recent.Count * StructureBrokenRatio);

        var records = _store.Notifications.Load();
        const string id = "page-structure";
        var existing = records.FindIndex(record => record.Id == id);

        if (suspect)
        {
            var detail = $"最近取り直した{recent.Count}件のうち{broken}件で、説明文の見出しを読み取れませんでした。"
                + "BOOTHから取得できる情報の形式が変化した可能性があります。アプリの更新が必要かもしれません。";

            if (existing >= 0 && !records[existing].IsResolved && records[existing].Detail == detail)
            {
                return true;
            }

            if (existing >= 0)
            {
                records.RemoveAt(existing);
            }

            records.Add(new NotificationRecord
            {
                Id = id,
                Kind = NotificationKind.PageStructureChanged,
                Title = "BOOTHから取得できる情報の形式が変化した可能性があります",
                Detail = detail,
                CreatedAt = DateTimeOffset.Now,
                IsStrong = true,
            });

            await SaveWithPruneAsync(records, cancellationToken);
            return true;
        }

        // 読み取りが戻ったら、帯は自分で消える
        if (existing >= 0 && !records[existing].IsResolved)
        {
            records[existing] = records[existing] with { IsResolved = true };
            await SaveWithPruneAsync(records, cancellationToken);
        }

        return false;
    }

    public async Task<int> MarkReadAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var wanted = ids.ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return 0;
        }

        var records = _store.Notifications.Load();
        var changed = 0;

        for (var index = 0; index < records.Count; index++)
        {
            if (wanted.Contains(records[index].Id) && !records[index].IsRead)
            {
                records[index] = records[index] with { IsRead = true };
                changed++;
            }
        }

        if (changed > 0)
        {
            await SaveWithPruneAsync(records, cancellationToken);
        }

        return changed;
    }

    public async Task<int> ResolveAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var wanted = ids.ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return 0;
        }

        var records = _store.Notifications.Load();
        var changed = 0;

        for (var index = 0; index < records.Count; index++)
        {
            if (wanted.Contains(records[index].Id) && !records[index].IsResolved)
            {
                records[index] = records[index] with { IsResolved = true };
                changed++;
            }
        }

        if (changed > 0)
        {
            await SaveWithPruneAsync(records, cancellationToken);
        }

        return changed;
    }

    /// <summary>
    /// 上限を超えた分を落とす。
    ///
    /// 足すときは上限を見ていない（足す側は `SaveAsync` を直に呼ぶ）ので、
    /// 放っておくと上限を超えたまま増える。起動時に1回ここを通す（ユーザ判断 2026-09-18）
    /// </summary>
    public async Task<int> PruneAsync(CancellationToken cancellationToken = default)
    {
        var records = _store.Notifications.Load();
        var before = records.Count;

        Prune(records);
        if (records.Count == before)
        {
            return 0;
        }

        await _store.Notifications.SaveAsync(records, cancellationToken);
        return before - records.Count;
    }

    private async Task SaveWithPruneAsync(List<NotificationRecord> records, CancellationToken cancellationToken)
    {
        Prune(records);
        await _store.Notifications.SaveAsync(records, cancellationToken);
    }

    /// <summary>
    /// 上限を超えた分を落とす。
    /// 捨てるのは既読か解消済みの古い方だけ。未読の宿題を落とすと、気付かないまま消えたことにも気付けない。
    /// </summary>
    private void Prune(List<NotificationRecord> records)
    {
        var limit = Math.Max(1, _settings.NotificationRetentionCount);
        if (records.Count <= limit)
        {
            return;
        }

        var excess = records.Count - limit;
        var droppable = records
            .Where(record => record.IsRead || record.IsResolved)
            .OrderBy(record => record.CreatedAt)
            .Take(excess)
            .ToList();

        foreach (var record in droppable)
        {
            records.Remove(record);
        }
    }
}
