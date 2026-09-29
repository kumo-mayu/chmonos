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
                var automatic = AvatarNames.ShownName(entry with { DisplayName = null });

                // 自動の名前と同じなら付けていないのと同じ（付け方を直せば一緒に直るよう、書かない）
                return entry with { DisplayName = typed.Length == 0 || typed == automatic ? null : typed };
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

                return Sorted(registry, entries, trimmed is null ? registry.BaseGroups : WithManualBase(registry.BaseGroups, trimmed));
            },
            cancellationToken);
    }

    /// <summary>
    /// 共通素体を手で足す（アバターの管理の「共通素体」の一覧の上の欄・ユーザ判断 2026-09-28）。
    /// どのアバターにもまだ結ばない。足した素体は検出の照合にも使われ、検出し直しでは消えない。
    /// </summary>
    public async Task<AvatarBaseAddOutcome> AddBaseAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = NameText.Normalize(name);
        if (trimmed.Length == 0)
        {
            return AvatarBaseAddOutcome.AlreadyThere;
        }

        var outcome = AvatarBaseAddOutcome.Added;

        await _store.Avatars.UpdateAsync(
            registry =>
            {
                var existing = registry.BaseGroups.FirstOrDefault(group =>
                    string.Equals(group.Name, trimmed, StringComparison.CurrentCultureIgnoreCase));

                outcome = existing switch
                {
                    null => AvatarBaseAddOutcome.Added,
                    { Rejected: true } => AvatarBaseAddOutcome.Restored,
                    _ => AvatarBaseAddOutcome.AlreadyThere,
                };

                // 既に一覧にある素体は、書き換えない（手で足した印を後から立てると、検出が作った物と見分けが付かなくなる）
                return outcome == AvatarBaseAddOutcome.AlreadyThere
                    ? registry
                    : Sorted(registry, registry.Entries, WithManualBase(registry.BaseGroups, trimmed));
            },
            cancellationToken);

        return outcome;
    }

    /// <summary>
    /// 名前の素体を「人が決めた物」として一覧に入れる。
    /// 無ければ手で足した印を付けて作る。消した印が付いていれば、印を下ろして手で足した物にする
    /// （人が同じ名前をもう一度選んだので、別名の付け直しと同じく印を下ろす・X1）。
    /// 既にある素体はそのまま（検出が作った物に印を立てない）。
    /// </summary>
    private static List<AvatarBaseGroup> WithManualBase(IEnumerable<AvatarBaseGroup> groups, string name)
    {
        var list = groups
            .Select(group => group.Rejected && string.Equals(group.Name, name, StringComparison.CurrentCultureIgnoreCase)
                ? group with { Rejected = false, IsManual = true }
                : group)
            .ToList();

        if (!list.Any(group => string.Equals(group.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            list.Add(new AvatarBaseGroup { Name = name, IsManual = true });
        }

        return list;
    }

    /// <summary>
    /// 共通素体を足す欄の候補。今ある物から出す：一覧にある素体（選ぶとその素体へ移る）、
    /// 消した素体（選ぶと戻る）、同梱の初期辞書でまだ入っていない素体。
    ///
    /// 商品の対応素体と、アバターの所属の名前は、どちらもグループの名前から来るので登録簿の名前に含まれる
    /// （グループを消すと商品の宣言も消し、改名すると一緒に書き換える）。
    /// 別名は入れない：別名を素体として足すと、同じ素体が2つに割れる。
    /// </summary>
    public static IReadOnlyList<string> BaseNameCandidates(AvatarRegistry registry)
        => registry.BaseGroups.Select(group => group.Name)
            .Concat(registry.Entries.Select(entry => entry.BaseName).OfType<string>())
            .Concat(AvatarBaseSeed.Groups.Select(group => group.Name))
            .Where(name => name.Trim().Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

    /// <summary>
    /// 商品ページの「＋ 追加」に混ぜる共通素体の候補（ユーザ判断 2026-09-28）。
    /// 登録簿の素体のうち、削除していない物で、この商品にまだ付いていない物。
    ///
    /// 足す欄（<see cref="BaseNameCandidates"/>）と違い、消した素体と初期辞書の素体は出さない：
    /// 商品の対応素体は検索で素体の兄弟をつなぐためにあり、登録簿に無い素体を付けてもつなぐ先が無い。
    /// 商品から外した（Rejected の）素体は候補に残す——選び直すと戻る（「消したもの」の［戻す］と同じ）。
    /// </summary>
    public static IReadOnlyList<string> ItemBaseCandidates(AvatarRegistry registry, IEnumerable<AvatarBaseLink> links)
    {
        var attached = links
            .Where(link => !link.Rejected)
            .Select(link => link.BaseName)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        return registry.BaseGroups
            .Where(group => !group.Rejected && group.Name.Trim().Length > 0 && !attached.Contains(group.Name))
            .Select(group => group.Name)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// 商品の対応素体に、人が選んだ素体を足す。手で足した物は <see cref="AvatarLinkSource.Manual"/>
    /// （検出し直しは Manual 以外を作り直すので、出どころを変えないと次の検出で消える）。
    /// 外した素体を選び直したときは、行を増やさず消した印を下ろす（「消したもの」の［戻す］と同じ形）。
    /// </summary>
    public static IReadOnlyList<AvatarBaseLink> WithManualBaseLink(IReadOnlyList<AvatarBaseLink> links, string baseName)
    {
        if (links.Any(link => string.Equals(link.BaseName, baseName, StringComparison.CurrentCultureIgnoreCase)))
        {
            return links
                .Select(link => string.Equals(link.BaseName, baseName, StringComparison.CurrentCultureIgnoreCase)
                    ? link with { Source = AvatarLinkSource.Manual, Rejected = false, Confirmed = true }
                    : link)
                .ToList();
        }

        return [.. links, new AvatarBaseLink { BaseName = baseName, Source = AvatarLinkSource.Manual, Confirmed = true }];
    }

    /// <summary>
    /// 説明文の素体の候補を「違う」として消す（ユーザ判断 2026-09-29：対応アバターの消し方と同じ作法）。
    /// 商品に消した印付きの手入力の行を残す——候補は商品に付いている素体（消した物も）を出さないので二度と出ず、
    /// 「消したもの」の欄に並んで戻せる。既に行があれば何もしない（付いている物・消した物を上書きしない）
    /// </summary>
    public static IReadOnlyList<AvatarBaseLink> WithDismissedBaseMention(IReadOnlyList<AvatarBaseLink> links, string baseName)
    {
        if (links.Any(link => string.Equals(link.BaseName, baseName, StringComparison.CurrentCultureIgnoreCase)))
        {
            return links;
        }

        return [.. links, new AvatarBaseLink { BaseName = baseName, Source = AvatarLinkSource.Manual, Confirmed = true, Rejected = true }];
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

                // 同名のグループが既にあれば統合する。
                // **ただし消した印の付いた同名の行は統合先にしない**（ユーザ判断 2026-09-29）：統合すると、改名した素体が
                // 一覧に出ない行へ吸い込まれ、「改名したら消えた」ように見えた。人が今その名前を付け直したのだから、
                // 消した意思より今の操作を優先し、消した行を片付けて名前を引き継がせる（手で付け直したら印を下ろす今の作法と同じ）
                var groups = registry.BaseGroups
                    .Where(group => !string.Equals(group.Name, oldName, StringComparison.CurrentCultureIgnoreCase))
                    .Where(group => !(group.Rejected && string.Equals(group.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
                    .ToList();

                var renamed = registry.BaseGroups
                    .FirstOrDefault(group => string.Equals(group.Name, oldName, StringComparison.CurrentCultureIgnoreCase));

                if (renamed is not null
                    && !groups.Any(group => string.Equals(group.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
                {
                    groups.Add(renamed with { Name = trimmed });
                }
                else if (renamed is { IsManual: true })
                {
                    // 統合先へ手で足した印を移す。落とすと、人が足した素体が検出の作った物として扱われる
                    groups = groups
                        .Select(group => string.Equals(group.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)
                            ? group with { IsManual = true }
                            : group)
                        .ToList();
                }

                return Sorted(registry, entries, groups);
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

    /// <summary>
    /// 素体グループを消す。所属していたアバターは所属無しに戻る。
    ///
    /// **行は残して消した印を立てる**（ユーザ判断 2026-09-21・X1）。
    /// 行ごと消すと、初期辞書が足し直し、検出が作り直すので、次の検出で丸ごと戻っていた。
    /// </summary>
    /// <returns>書き換えたitem数。</returns>
    public async Task<int> DeleteBaseAsync(string name, CancellationToken cancellationToken = default)
    {
        await _store.Avatars.UpdateAsync(
            registry => Sorted(
                registry,
                registry.Entries.Select(entry => string.Equals(entry.BaseName, name, StringComparison.CurrentCultureIgnoreCase)
                    ? entry with { BaseName = null }
                    : entry),
                registry.BaseGroups.Select(group => string.Equals(group.Name, name, StringComparison.CurrentCultureIgnoreCase)
                    ? group with { Rejected = true }
                    : group)),
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
    public async Task<Booth.BoothFetchStatus> RecheckAsync(string itemId, CancellationToken cancellationToken = default)
    {
        // 問い合わせ先が無いのは組み立ての話（画面からは起こらない）。この型はテストでも作るので落とさず、届かなかった扱いにする
        if (_client is null)
        {
            return Booth.BoothFetchStatus.TemporaryFailure;
        }

        var fetched = await _client.GetItemJsonAsync(itemId, cancellationToken);

        if (fetched.Status == Booth.BoothFetchStatus.NotFound)
        {
            await UpdateEntryAsync(
                itemId,
                entry => entry with { Category = null, CheckedAt = DateTimeOffset.Now },
                cancellationToken);
            return Booth.BoothFetchStatus.NotFound;
        }

        if (!fetched.IsSuccess || fetched.Value is null)
        {
            // **届かなかったことをそのまま返す**（E3）。画面は「確認できませんでした」の一言で片付けていた
            return Booth.BoothFetchStatus.TemporaryFailure;
        }

        // 読めない応答も「届かなかった」と同じ扱い（E3 と同じく画面は確認できなかったと出す）
        if (Booth.BoothItemMapper.TryMap(fetched.Value, DateTimeOffset.Now, itemId: itemId) is not { } booth)
        {
            return Booth.BoothFetchStatus.TemporaryFailure;
        }

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

        return Booth.BoothFetchStatus.Success;
    }

    private async Task UpdateEntryAsync(
        string itemId,
        Func<AvatarRegistryEntry, AvatarRegistryEntry> update,
        CancellationToken cancellationToken)
        => await _store.Avatars.UpdateAsync(
            registry => registry.Entries.Any(entry => entry.ItemId == itemId)
                ? Sorted(
                    registry,
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
                registry,
                registry.Entries,
                registry.BaseGroups.Select(group => string.Equals(group.Name, name, StringComparison.CurrentCultureIgnoreCase)
                    ? update(group)
                    : group)),
            cancellationToken);

    /// <summary>保存する形。人が開いて読むファイルなので、並びを毎回そろえる。</summary>
    private static AvatarRegistry Sorted(
        AvatarRegistry registry,
        IEnumerable<AvatarRegistryEntry> entries,
        IEnumerable<AvatarBaseGroup> groups)
        => new()
        {
            DetectedAt = registry.DetectedAt,
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
            // 全件を先に読んでから順に書く。書く頃には写しが古いので、**書き換えを書く直前の値に当てる**
            // （名指しは他の項目を守るだけで、素体の対応そのものは守らない）。持っていなければ書かない
            var written = await _store.Items.ChangeLocalAsync(
                item.Id,
                current => current.AvatarBases.Any(link =>
                    string.Equals(link.BaseName, oldName, StringComparison.CurrentCultureIgnoreCase))
                        ? current with { AvatarBases = Rename(current.AvatarBases) }
                        : null,
                LocalOwners.AvatarBases,
                cancellationToken);

            if (written)
            {
                updated++;
            }
        }

        return updated;

        IReadOnlyList<AvatarBaseLink> Rename(IReadOnlyList<AvatarBaseLink> links)
            => newName is null
                ? [.. links.Where(link => !string.Equals(link.BaseName, oldName, StringComparison.CurrentCultureIgnoreCase))]
                : [.. links
                    .Select(link => string.Equals(link.BaseName, oldName, StringComparison.CurrentCultureIgnoreCase)
                        ? link with { BaseName = newName }
                        : link)
                    .DistinctBy(link => link.BaseName, StringComparer.CurrentCultureIgnoreCase)];
    }
}
