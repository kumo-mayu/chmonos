using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

public interface INotificationService
{
    IReadOnlyList<NotificationRecord> Load();

    Task<bool> SetReadAsync(string id, bool isRead, CancellationToken cancellationToken = default);

    Task<int> MarkAllReadAsync(CancellationToken cancellationToken = default);

    Task<int> DetectOrphanReferencesAsync(CancellationToken cancellationToken = default);
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
    private readonly AppSettings _settings;

    public NotificationService(DataStore store, AppSettings? settings = null)
    {
        _store = store;
        _settings = settings ?? new AppSettings();
    }

    public IReadOnlyList<NotificationRecord> Load() => _store.Notifications.Load();

    public async Task<bool> SetReadAsync(string id, bool isRead, CancellationToken cancellationToken = default)
    {
        var records = _store.Notifications.Load();
        var index = records.FindIndex(record => record.Id == id);
        if (index < 0 || records[index].IsRead == isRead)
        {
            return false;
        }

        records[index] = Copy(records[index], isRead);
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
                records[index] = Copy(records[index], isRead: true);
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
    /// マスタに無いappTag・属性を参照しているitemを探して知らせる。
    ///
    /// マスタのJSONを手で書き換えることを許している以上、item側だけが古い名前を
    /// 指したままになり得る。そのままだと絞り込みに出てこないのに気付けない。
    /// </summary>
    public async Task<int> DetectOrphanReferencesAsync(CancellationToken cancellationToken = default)
    {
        var master = _store.AppTags.Load();
        var attributes = _store.Attributes.Load().Attributes
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var records = _store.Notifications.Load();
        var added = 0;

        foreach (var item in loaded.Items)
        {
            // トップだけでなくサブも見る。サブ名がずれると、画面はマスタにある分しか
            // 出さないので、どこにも表示されないまま絞り込みから消える
            var missingTags = AppTagService.FindOrphans(master, [item])
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
                parts.Add($"appTag: {string.Join("・", missingTags)}");
            }

            if (missingAttributes.Count > 0)
            {
                parts.Add($"属性: {string.Join("・", missingAttributes)}");
            }

            var detail = $"{string.Join(" / ", parts)}。"
                + "マスタから消えたか、名前が変わった可能性があります。このままでは絞り込みに出てきません。";

            // itemごとに1件だけ持つ。既読でも作り直さないのは、
            // 直さないまま画面を開くたびに同じ話が積み上がるのを避けるため
            // （既読は「この食い違いは見た」という意思表示として扱う）。
            var id = $"orphan-tag:{item.Id}";
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
                Title = $"{item.Booth.Name ?? item.Id}：マスタに無い分類を参照しています",
                Detail = detail,
                CreatedAt = DateTimeOffset.Now,
            });

            added++;
        }

        if (added > 0)
        {
            await SaveWithPruneAsync(records, cancellationToken);
        }

        return added;
    }

    /// <summary>
    /// 上限を超えた分を落としてから保存する。
    /// 捨てるのは既読の古い方だけ。未読を落とすと、気付かないまま消えたことにも気付けない。
    /// </summary>
    private async Task SaveWithPruneAsync(List<NotificationRecord> records, CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, _settings.NotificationRetentionCount);

        if (records.Count > limit)
        {
            var excess = records.Count - limit;
            var droppable = records
                .Where(record => record.IsRead)
                .OrderBy(record => record.CreatedAt)
                .Take(excess)
                .ToList();

            foreach (var record in droppable)
            {
                records.Remove(record);
            }
        }

        await _store.Notifications.SaveAsync(records, cancellationToken);
    }

    private static NotificationRecord Copy(NotificationRecord record, bool isRead) => new()
    {
        Id = record.Id,
        Kind = record.Kind,
        ItemId = record.ItemId,
        Title = record.Title,
        Detail = record.Detail,
        Diffs = record.Diffs,
        CreatedAt = record.CreatedAt,
        IsRead = isRead,
        IsStrong = record.IsStrong,
    };
}
