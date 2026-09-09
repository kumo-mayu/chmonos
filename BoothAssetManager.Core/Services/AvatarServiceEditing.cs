using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 登録簿の編集。画面からの操作をここに集める。
///
/// 素体グループは名前で参照されているので、改名はitem側の宣言も一緒に書き換える
/// （userTag・属性の改名と同じ扱い）。
/// </summary>
public sealed partial class AvatarService
{
    /// <summary>表示名を変える。BOOTHの正式名が長いときに短くするための操作。</summary>
    public async Task SetDisplayNameAsync(string itemId, string name, CancellationToken cancellationToken = default)
        => await UpdateEntryAsync(itemId, entry => entry with { DisplayName = name.Trim() }, cancellationToken);

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
        var registry = _store.Avatars.Load();
        var trimmed = string.IsNullOrWhiteSpace(baseName) ? null : baseName.Trim();

        var entries = registry.Entries
            .Select(entry => entry.ItemId == itemId ? entry with { BaseName = trimmed } : entry)
            .ToList();

        var groups = registry.BaseGroups.ToList();

        if (trimmed is not null
            && !groups.Any(group => string.Equals(group.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
        {
            groups.Add(new AvatarBaseGroup { Name = trimmed });
        }

        await SaveAsync(registry, entries, groups, cancellationToken);
    }

    /// <summary>
    /// このグループの一致から衣装の互換を推し量ってよいかを切り替える。
    /// +Head のような部位規格は false にする（一致しても衣装は合わない）。
    /// </summary>
    public async Task SetInferClothingAsync(
        string name,
        bool infer,
        CancellationToken cancellationToken = default)
    {
        var registry = _store.Avatars.Load();

        var groups = registry.BaseGroups
            .Select(group => string.Equals(group.Name, name, StringComparison.CurrentCultureIgnoreCase)
                ? group with { InferClothing = infer }
                : group)
            .ToList();

        await SaveAsync(registry, registry.Entries.ToList(), groups, cancellationToken);
    }

    /// <summary>素体グループの配布商品IDを結び付ける。</summary>
    public async Task SetBaseItemIdAsync(
        string name,
        string? itemId,
        CancellationToken cancellationToken = default)
    {
        var registry = _store.Avatars.Load();

        var groups = registry.BaseGroups
            .Select(group => string.Equals(group.Name, name, StringComparison.CurrentCultureIgnoreCase)
                ? group with { ItemId = string.IsNullOrWhiteSpace(itemId) ? null : itemId.Trim() }
                : group)
            .ToList();

        await SaveAsync(registry, registry.Entries.ToList(), groups, cancellationToken);
    }

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

        var registry = _store.Avatars.Load();

        var entries = registry.Entries
            .Select(entry => string.Equals(entry.BaseName, oldName, StringComparison.CurrentCultureIgnoreCase)
                ? entry with { BaseName = trimmed }
                : entry)
            .ToList();

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

        await SaveAsync(registry, entries, groups, cancellationToken);

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
        var registry = _store.Avatars.Load();

        var entries = registry.Entries
            .Select(entry => string.Equals(entry.BaseName, name, StringComparison.CurrentCultureIgnoreCase)
                ? entry with { BaseName = null }
                : entry)
            .ToList();

        var groups = registry.BaseGroups
            .Where(group => !string.Equals(group.Name, name, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        await SaveAsync(registry, entries, groups, cancellationToken);

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
                ? entry
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

    /// <summary>別名を消す。誤って覚えた表記を落とすため。</summary>
    public async Task RemoveAliasAsync(string itemId, string text, CancellationToken cancellationToken = default)
        => await UpdateEntryAsync(
            itemId,
            entry => entry with
            {
                Aliases = entry.Aliases
                    .Where(alias => !string.Equals(alias.Text, text, StringComparison.CurrentCultureIgnoreCase))
                    .ToList(),
            },
            cancellationToken);

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
                // 表示名はユーザが変えている可能性があるので、空のときだけ埋める
                DisplayName = entry.DisplayName ?? AvatarText.ShortenName(booth.Name),
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
    {
        var registry = _store.Avatars.Load();

        var entries = registry.Entries.ToList();
        var index = entries.FindIndex(entry => entry.ItemId == itemId);

        if (index < 0)
        {
            return;
        }

        entries[index] = update(entries[index]);

        await SaveAsync(registry, entries, registry.BaseGroups.ToList(), cancellationToken);
    }

    private Task SaveAsync(
        AvatarRegistry registry,
        List<AvatarRegistryEntry> entries,
        List<AvatarBaseGroup> groups,
        CancellationToken cancellationToken)
        => _store.Avatars.SaveAsync(
            new AvatarRegistry
            {
                Entries = entries.OrderBy(entry => entry.ItemId, StringComparer.Ordinal).ToList(),
                BaseGroups = groups.OrderBy(group => group.Name, StringComparer.CurrentCulture).ToList(),
            },
            cancellationToken);

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
