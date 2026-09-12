using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 登録簿の編集。画面からの操作をここに集める。
///
/// 素体グループは名前で参照されているので、改名はitem側の宣言も一緒に書き換える
/// （userTag・属性の改名と同じ扱い）。
///
/// **登録簿は必ず <see cref="Storage.JsonFileStore{T}.UpdateAsync"/> で書く。**
/// 検出が同じファイルを書くので、読んでから書くまでの間に割り込まれると片方の変更が消える（U15）。
/// </summary>
public sealed partial class AvatarService
{
    /// <summary>
    /// 表示名を手で付ける。付けた名前は自動では上書きしない（ユーザ判断 #54）。
    /// **空にすると自動に戻る**（正式名から計算した名前を出す）。
    /// 名前の欄には自動の名前が入っているので、変えずに「名前を保存」を押すと自動の名前が手で付けた名前として固まり、
    /// 付け方を直しても変わらなくなる。自動の名前と同じなら自動のままにする。
    /// </summary>
    public async Task SetDisplayNameAsync(string itemId, string name, CancellationToken cancellationToken = default)
        => await UpdateEntryAsync(
            itemId,
            entry =>
            {
                var typed = name.Trim();
                var automatic = AvatarNames.ShownName(entry with { DisplayName = null, DisplayNameSetAt = null });

                // 自動の名前と同じなら付けていないのと同じ（付け方を直せば一緒に直るよう、書かない）
                return typed.Length == 0 || typed == automatic
                    ? entry with { DisplayName = null, DisplayNameSetAt = null }
                    : entry with { DisplayName = typed, DisplayNameSetAt = DateTimeOffset.Now };
            },
            cancellationToken);

    /// <summary>メモを書く。</summary>
    public async Task SetMemoAsync(string itemId, string? memo, CancellationToken cancellationToken = default)
        => await UpdateEntryAsync(
            itemId,
            entry => entry with { Memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim() },
            cancellationToken);

    /// <summary>
    /// 手動で「所有している」を立てる／外す。
    /// 計算による所有（本体を取り込んでいる）とは和を取るので、ここを外しても消えないことがある。
    /// </summary>
    public async Task SetOwnedManuallyAsync(string itemId, bool owned, CancellationToken cancellationToken = default)
        => await UpdateEntryAsync(itemId, entry => entry with { IsOwnedManually = owned }, cancellationToken);

    /// <summary>
    /// アバターとして扱うかを手で決める。null に戻すと規則の判定に従う。
    /// 販売終了でcategoryを引けないものなど、規則で拾えない例外のために使う。
    /// </summary>
    public async Task SetAvatarOverrideAsync(
        string itemId,
        bool? value,
        CancellationToken cancellationToken = default)
        => await UpdateEntryAsync(itemId, entry => entry with { AvatarOverride = value }, cancellationToken);

    /// <summary>
    /// このアバターの共通素体を決める。まだ無いグループ名なら作る。
    /// null／空を渡すと所属を外す。
    /// </summary>
    public async Task SetBaseAsync(string itemId, string? baseName, CancellationToken cancellationToken = default)
    {
        var trimmed = string.IsNullOrWhiteSpace(baseName) ? null : baseName.Trim();

        await _store.Avatars.UpdateAsync(
            registry =>
            {
                var entries = registry.Entries
                    .Select(entry => entry.ItemId == itemId ? entry with { BaseName = trimmed } : entry);

                var groups = registry.BaseGroups.ToList();

                if (trimmed is not null
                    && !groups.Any(group => string.Equals(group.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
                {
                    groups.Add(new AvatarBaseGroup { Name = trimmed });
                }

                return Sorted(entries, groups);
            },
            cancellationToken);
    }

    /// <summary>
    /// このグループの一致から衣装の互換を推し量ってよいかを切り替える。
    /// 同じ素体を名乗っていても衣装が合わない組は false にする。
    /// </summary>
    public async Task SetInferClothingAsync(
        string name,
        bool infer,
        CancellationToken cancellationToken = default)
        => await UpdateGroupAsync(name, group => group with { InferClothing = infer }, cancellationToken);

    /// <summary>素体グループの配布商品IDを結び付ける。</summary>
    public async Task SetBaseItemIdAsync(
        string name,
        string? itemId,
        CancellationToken cancellationToken = default)
        => await UpdateGroupAsync(
            name,
            group => group with { ItemId = string.IsNullOrWhiteSpace(itemId) ? null : itemId.Trim() },
            cancellationToken);

    /// <summary>
    /// 素体グループを改名する。名前で参照しているので、
    /// レジストリのアバターと、全itemの宣言を一括で書き換える。
    /// </summary>
    /// <returns>書き換えたitem数。</returns>
    public async Task<int> RenameBaseAsync(
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var trimmed = newName.Trim();
        if (trimmed.Length == 0 || string.Equals(oldName, trimmed, StringComparison.CurrentCulture))
        {
            return 0;
        }

        await _store.Avatars.UpdateAsync(
            registry =>
            {
                var entries = registry.Entries
                    .Select(entry => string.Equals(entry.BaseName, oldName, StringComparison.CurrentCultureIgnoreCase)
                        ? entry with { BaseName = trimmed }
                        : entry);

                // 同名のグループが既にあれば統合する
                var groups = registry.BaseGroups
                    .Where(group => !string.Equals(group.Name, oldName, StringComparison.CurrentCultureIgnoreCase))
                    .ToList();

                var renamed = registry.BaseGroups
                    .FirstOrDefault(group => string.Equals(group.Name, oldName, StringComparison.CurrentCultureIgnoreCase));

                if (renamed is not null
                    && !groups.Any(group => string.Equals(group.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
                {
                    groups.Add(renamed with { Name = trimmed });
                }

                return Sorted(entries, groups);
            },
            cancellationToken);

        return await RewriteBaseNameInItemsAsync(oldName, trimmed, cancellationToken);
    }

    /// <summary>
    /// この素体を名指ししている商品の数。消す前の確認に出す。
    ///
    /// <see cref="AvatarBaseSummary.ItemCount"/> は所持している商品しか数えていないのに対し、
    /// 削除は全商品の宣言を書き換えるので、確認に出す数はこちらで数え直す。
    /// 消すのは戻せない操作なので、押す前に規模が見えている必要がある。
    /// </summary>
    public async Task<int> CountItemsUsingBaseAsync(string name, CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        return loaded.Items.Count(item => item.Local.AvatarBases.Any(link =>
            string.Equals(link.BaseName, name, StringComparison.CurrentCultureIgnoreCase)));
    }

    /// <summary>素体グループを消す。所属していたアバターは所属無しに戻る。</summary>
    /// <returns>書き換えたitem数。</returns>
    public async Task<int> DeleteBaseAsync(string name, CancellationToken cancellationToken = default)
    {
        await _store.Avatars.UpdateAsync(
            registry => Sorted(
                registry.Entries.Select(entry => string.Equals(entry.BaseName, name, StringComparison.CurrentCultureIgnoreCase)
                    ? entry with { BaseName = null }
                    : entry),
                registry.BaseGroups.Where(group => !string.Equals(group.Name, name, StringComparison.CurrentCultureIgnoreCase))),
            cancellationToken);

        return await RewriteBaseNameInItemsAsync(name, null, cancellationToken);
    }

    /// <summary>別名を手で足す。照合に効くので、検出が拾えない表記を補える。</summary>
    public async Task AddAliasAsync(string itemId, string text, CancellationToken cancellationToken = default)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 2)
        {
            return;
        }

        await UpdateEntryAsync(
            itemId,
            entry => entry.Aliases.Any(alias => string.Equals(alias.Text, trimmed, StringComparison.CurrentCultureIgnoreCase))
                // 一度消したものを足し直す場合は、印を下ろすだけ
                ? entry with
                {
                    Aliases = entry.Aliases
                        .Select(alias => string.Equals(alias.Text, trimmed, StringComparison.CurrentCultureIgnoreCase)
                            ? alias with { Rejected = false }
                            : alias)
                        .ToList(),
                }
                : entry with
                {
                    Aliases = [.. entry.Aliases, new AvatarAlias
                    {
                        Text = trimmed,
                        Count = 0,
                        Source = nameof(AvatarLinkSource.Manual),
                    }],
                },
            cancellationToken);
    }

    /// <summary>
    /// 別名を消す。誤って覚えた表記を落とすため。
    ///
    /// **自動で覚えた別名は、行を消さずに「消した」印を付ける。**
    /// タグから毎回作り直されるので、行ごと消すと次の検出で復活してしまう
    /// （対応アバターの <c>Rejected</c> と同じ形）。
    /// 手で足した別名は検出が作らないので、そのまま消してよい。
    /// </summary>
    public async Task RemoveAliasAsync(string itemId, string text, CancellationToken cancellationToken = default)
        => await UpdateEntryAsync(
            itemId,
            entry => entry with
            {
                Aliases = entry.Aliases
                    .Where(alias => !IsManualMatch(alias, text))
                    .Select(alias => string.Equals(alias.Text, text, StringComparison.CurrentCultureIgnoreCase)
                        ? alias with { Rejected = true }
                        : alias)
                    .ToList(),
            },
            cancellationToken);

    /// <summary>手で足したものを消す指示か。検出が作らないので、行ごと消してよい。</summary>
    private static bool IsManualMatch(AvatarAlias alias, string text)
        => string.Equals(alias.Text, text, StringComparison.CurrentCultureIgnoreCase)
            && string.Equals(alias.Source, nameof(AvatarLinkSource.Manual), StringComparison.Ordinal);

    /// <summary>
    /// この項目をもう一度BOOTHに問い合わせる。
    /// 販売終了から復活した場合や、こちらの判定を疑うときの逃げ道。
    /// </summary>
    public async Task<bool> RecheckAsync(string itemId, CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            return false;
        }

        var fetched = await _client.GetItemJsonAsync(itemId, cancellationToken);

        if (fetched.Status == Booth.BoothFetchStatus.NotFound)
        {
            await UpdateEntryAsync(
                itemId,
                entry => entry with { Category = null, CheckedAt = DateTimeOffset.Now },
                cancellationToken);
            return true;
        }

        if (!fetched.IsSuccess || fetched.Value is null)
        {
            return false;
        }

        var booth = Booth.BoothItemMapper.Map(fetched.Value, DateTimeOffset.Now);

        await UpdateEntryAsync(
            itemId,
            entry => entry with
            {
                BoothName = booth.Name,
                // 表示名は書かない（手で付けた名前はそのまま、無ければ正式名から計算する #54）
                ShopName = booth.Shop?.Name ?? entry.ShopName,
                Category = booth.Category?.Name,
                CheckedAt = DateTimeOffset.Now,
            },
            cancellationToken);

        return true;
    }

    private async Task UpdateEntryAsync(
        string itemId,
        Func<AvatarRegistryEntry, AvatarRegistryEntry> update,
        CancellationToken cancellationToken)
        => await _store.Avatars.UpdateAsync(
            registry => registry.Entries.Any(entry => entry.ItemId == itemId)
                ? Sorted(
                    registry.Entries.Select(entry => entry.ItemId == itemId ? update(entry) : entry),
                    registry.BaseGroups)
                : registry,
            cancellationToken);

    private async Task UpdateGroupAsync(
        string name,
        Func<AvatarBaseGroup, AvatarBaseGroup> update,
        CancellationToken cancellationToken)
        => await _store.Avatars.UpdateAsync(
            registry => Sorted(
                registry.Entries,
                registry.BaseGroups.Select(group => string.Equals(group.Name, name, StringComparison.CurrentCultureIgnoreCase)
                    ? update(group)
                    : group)),
            cancellationToken);

    /// <summary>保存する形。人が開いて読むファイルなので、並びを毎回そろえる。</summary>
    private static AvatarRegistry Sorted(
        IEnumerable<AvatarRegistryEntry> entries,
        IEnumerable<AvatarBaseGroup> groups)
        => new()
        {
            Entries = entries.OrderBy(entry => entry.ItemId, StringComparer.Ordinal).ToList(),
            BaseGroups = groups.OrderBy(group => group.Name, StringComparer.CurrentCulture).ToList(),
        };

    /// <summary>
    /// 全itemの素体宣言を書き換える。名前で参照しているので改名・削除に追随が要る。
    /// 1284件の小さなJSONなら実測で1秒もかからない（userTagの改名と同じ判断）。
    /// </summary>
    private async Task<int> RewriteBaseNameInItemsAsync(
        string oldName,
        string? newName,
        CancellationToken cancellationToken)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var updated = 0;

        foreach (var item in loaded.Items)
        {
            if (!item.Local.AvatarBases.Any(link =>
                string.Equals(link.BaseName, oldName, StringComparison.CurrentCultureIgnoreCase)))
            {
                continue;
            }

            var bases = newName is null
                ? item.Local.AvatarBases
                    .Where(link => !string.Equals(link.BaseName, oldName, StringComparison.CurrentCultureIgnoreCase))
                    .ToList()
                : item.Local.AvatarBases
                    .Select(link => string.Equals(link.BaseName, oldName, StringComparison.CurrentCultureIgnoreCase)
                        ? link with { BaseName = newName }
                        : link)
                    .DistinctBy(link => link.BaseName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

            // 全件を先に読んでから順に書く。書く頃には写しが古いので、素体の対応だけを名指しする
            await _store.Items.SaveLocalAsync(
                item.Id,
                item.Local with { AvatarBases = bases },
                LocalOwners.AvatarBases,
                cancellationToken: cancellationToken);

            updated++;
        }

        return updated;
    }
}
