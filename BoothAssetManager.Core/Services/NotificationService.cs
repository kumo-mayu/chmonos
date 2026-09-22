using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

public interface INotificationService
{
    IReadOnlyList<NotificationRecord> Load();

    Task<bool> SetReadAsync(string id, bool isRead, CancellationToken cancellationToken = default);

    /// <summary>1件足す。同じIDの未読があれば差し替える。</summary>
    Task AddAsync(NotificationRecord record, CancellationToken cancellationToken = default);

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

    /// <summary>手で直した JSON の食い違いを要確認に出す（J2・L6）。</summary>
    Task<int> DetectHandEditIssuesAsync(CancellationToken cancellationToken = default);
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

    /// <summary>
    /// 1件足す。**同じIDの未読があれば差し替える**（溜めても読む手間が増えるだけ。商品の更新の知らせと同じ作法）。
    /// </summary>
    public Task AddAsync(NotificationRecord record, CancellationToken cancellationToken = default)
        => _store.Notifications.UpdateAsync(
            records =>
            {
                records.RemoveAll(entry => entry.Id == record.Id && !entry.IsRead);
                records.Add(record);
                return Pruned(records);
            },
            cancellationToken);

    public Task<bool> SetReadAsync(string id, bool isRead, CancellationToken cancellationToken = default)
        => _store.Notifications.TryUpdateAsync(
            records =>
            {
                var index = records.FindIndex(record => record.Id == id);
                if (index < 0 || records[index].IsRead == isRead)
                {
                    return null;
                }

                records[index] = records[index] with { IsRead = isRead };
                return Pruned(records);
            },
            cancellationToken);

    public Task<int> MarkAllReadAsync(CancellationToken cancellationToken = default)
        => MarkAsync(
            _ => true,
            record => record.IsRead,
            record => record with { IsRead = true },
            cancellationToken);

    /// <summary>
    /// 印（既読・解消済み）を付ける。3つとも形が同じなので1か所にまとめ、
    /// **読み直してから付ける**ようにする（錠の外で読むと、付けた印が裏の取り直しに消される）。
    /// </summary>
    private async Task<int> MarkAsync(
        Func<NotificationRecord, bool> targets,
        Func<NotificationRecord, bool> alreadyDone,
        Func<NotificationRecord, NotificationRecord> mark,
        CancellationToken cancellationToken)
    {
        var changed = 0;
        await _store.Notifications.TryUpdateAsync(
            records =>
            {
                changed = 0;
                for (var index = 0; index < records.Count; index++)
                {
                    if (!targets(records[index]) || alreadyDone(records[index]))
                    {
                        continue;
                    }

                    records[index] = mark(records[index]);
                    changed++;
                }

                return changed == 0 ? null : Pruned(records);
            },
            cancellationToken);

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

        // 食い違っている物を先に組み立ててから、錠の中で今の一覧に当てる。
        // 全件を読むのに時間がかかるので、その間に人が既読にした印を古い写しで消さないため
        var wanted = new List<NotificationRecord>();

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
            wanted.Add(new NotificationRecord
            {
                Id = $"orphan-tag:{item.Id}",
                Kind = NotificationKind.OrphanTag,
                ItemId = item.Id,
                Title = item.DisplayName,
                Detail = detail,
                CreatedAt = DateTimeOffset.Now,
            });
        }

        // 今も食い違っている物の通知ID。ここに無い分は直したということなので「解消済み」にする
        var stillBroken = wanted.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        var added = 0;

        await _store.Notifications.TryUpdateAsync(
            records =>
            {
                added = 0;
                foreach (var candidate in wanted)
                {
                    var existing = records.FindIndex(record => record.Id == candidate.Id);
                    if (existing >= 0)
                    {
                        if (records[existing].Detail == candidate.Detail)
                        {
                            continue;
                        }

                        // 中身が変わったなら別の話なので、既読を解いて出し直す
                        records.RemoveAt(existing);
                    }

                    records.Add(candidate);
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

                return added > 0 || resolved > 0 ? Pruned(records) : null;
            },
            cancellationToken);

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
            // **②（商品ページ）をまだ取っていない商品は数えない**（ユーザ指摘 2026-09-22）。
            // 説明文は商品JSON（①）から入るが、見出しはHTML（②）からしか入らない。
            // だから①だけ済んだ商品は必ず「説明文はあるのに見出しが0件」に見え、
            // **HTMLを1件も取っていないのに「形式が変わったかもしれません」が出ていた**
            // （取り込みを②の前で止めた保存先で、27件中27件が該当して実際に出た）。
            // ②が済んだかは説明のファイルが在るかで見る——説明の無い商品でも空のファイルを置くので、
            // 在る＝②を通った、になる（取り込みが `withoutPage` を選ぶのと同じ見方）
            .Where(item => File.Exists(_store.Paths.ItemHtmlFile(item.Id)))
            .Where(item => (item.Booth.Description?.Length ?? 0) >= StructureBodyLength)
            .ToList();

        var broken = recent.Count(item => item.Booth.H2Sections.Count == 0);
        var suspect = recent.Count >= StructureSampleMinimum
            && broken >= (int)Math.Ceiling(recent.Count * StructureBrokenRatio);

        const string id = "page-structure";
        var detail = $"最近取り直した{recent.Count}件のうち{broken}件で、説明文の見出しを読み取れませんでした。"
            + "BOOTHから取得できる情報の形式が変化した可能性があります。アプリの更新が必要かもしれません。";

        await _store.Notifications.TryUpdateAsync(
            records =>
            {
                var existing = records.FindIndex(record => record.Id == id);
                if (suspect)
                {
                    if (existing >= 0 && !records[existing].IsResolved && records[existing].Detail == detail)
                    {
                        return null;
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

                    return Pruned(records);
                }

                // 読み取りが戻ったら、帯は自分で消える
                if (existing < 0 || records[existing].IsResolved)
                {
                    return null;
                }

                records[existing] = records[existing] with { IsResolved = true };
                return Pruned(records);
            },
            cancellationToken);

        return suspect;
    }

    public Task<int> MarkReadAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var wanted = ids.ToHashSet(StringComparer.Ordinal);
        return wanted.Count == 0
            ? Task.FromResult(0)
            : MarkAsync(
                record => wanted.Contains(record.Id),
                record => record.IsRead,
                record => record with { IsRead = true },
                cancellationToken);
    }

    public Task<int> ResolveAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var wanted = ids.ToHashSet(StringComparer.Ordinal);
        return wanted.Count == 0
            ? Task.FromResult(0)
            : MarkAsync(
                record => wanted.Contains(record.Id),
                record => record.IsResolved,
                record => record with { IsResolved = true },
                cancellationToken);
    }

    /// <summary>
    /// 上限を超えた分を落とす。
    ///
    /// 足すときは上限を見ていない（足す側は `SaveAsync` を直に呼ぶ）ので、
    /// 放っておくと上限を超えたまま増える。起動時に1回ここを通す（ユーザ判断 2026-09-18）
    /// </summary>
    public async Task<int> PruneAsync(CancellationToken cancellationToken = default)
    {
        var dropped = 0;
        await _store.Notifications.TryUpdateAsync(
            records =>
            {
                var before = records.Count;
                Prune(records);
                dropped = before - records.Count;
                return dropped == 0 ? null : records;
            },
            cancellationToken);

        return dropped;
    }

    /// <summary>
    /// 手で直した JSON の食い違いを要確認に出す（ユーザ判断 2026-09-21・J2/L6）。
    ///
    /// **こちらから直さない。**公開前は、合っていないデータの方を問題にして直し方を聞く決まり
    /// （`docs/spec/data-model.md`）。ここは「どこの何がどう食い違っているか」を伝えるだけ。
    /// 食い違いが無くなったら解消済みにする（直したのに残り続けない）。
    /// </summary>
    public async Task<int> DetectHandEditIssuesAsync(CancellationToken cancellationToken = default)
    {
        var issues = await HandEditCheck.FindAsync(_store, cancellationToken);
        const string id = "hand-edit";
        var changed = 0;

        await _store.Notifications.TryUpdateAsync(
            records =>
            {
                var existing = records.FindIndex(record => record.Id == id);

                if (issues.Count == 0)
                {
                    // 直ったので用は済んだ
                    if (existing < 0 || records[existing].IsResolved)
                    {
                        return null;
                    }

                    records[existing] = records[existing] with { IsResolved = true };
                    return Pruned(records);
                }

                var detail = string.Join(
                    "\n",
                    issues.Select(issue => $"・{issue.Where}：{issue.What}"));

                if (existing >= 0 && !records[existing].IsResolved && records[existing].Detail == detail)
                {
                    return null;
                }

                if (existing >= 0)
                {
                    records.RemoveAt(existing);
                }

                records.Add(new NotificationRecord
                {
                    Id = id,
                    Kind = NotificationKind.HandEditMismatch,
                    Title = "手で直したJSONに食い違いがあります",
                    Detail = detail + "\n\n同じ名前が2つあると、その名前を使う画面が開けません。"
                        + "商品IDとファイル名が違うと、以後その商品への保存が別のファイルに書かれます。"
                        + "どちらも**直し方はこちらで決められない**ので、JSONを開いて片方を消すか、名前を分けてください。",
                    CreatedAt = DateTimeOffset.Now,
                    IsStrong = true,
                });

                changed = issues.Count;
                return Pruned(records);
            },
            cancellationToken);

        return changed;
    }

    private List<NotificationRecord> Pruned(List<NotificationRecord> records)
    {
        Prune(records);
        return records;
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
