using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

/// <summary>検出の進み具合。数百件を回すので途中経過を出す。</summary>
public sealed record AvatarDetectProgress
{
    public required string Phase { get; init; }

    public required int Done { get; init; }

    public required int Total { get; init; }

    public string? Current { get; init; }
}

/// <summary>検出の結果。何が起きたかを画面にそのまま出せる形にする。</summary>
public sealed record AvatarDetectResult
{
    public required int ItemsScanned { get; init; }

    public required int ItemsUpdated { get; init; }

    /// <summary>新しく分かったアバター。</summary>
    public required int AvatarsFound { get; init; }

    /// <summary>調べた結果アバターではなかったもの（次回から問い合わせない）。</summary>
    public required int NonAvatars { get; init; }

    public required int BaseGroupsFound { get; init; }

    /// <summary>BOOTHへ問い合わせた回数。</summary>
    public required int Requests { get; init; }

    /// <summary>問い合わせに失敗して判断を保留したID。次回もう一度試す。</summary>
    public required int Unresolved { get; init; }

    /// <summary>この回で登録簿に新しく入った件数。商品が変わらなくても、次の回で効いてくる。</summary>
    public int RegistryAdded { get; init; }
}

/// <summary>アバター1体の一覧表示用。</summary>
public sealed record AvatarSummary
{
    public required AvatarRegistryEntry Entry { get; init; }

    public required bool IsAvatar { get; init; }

    public required bool IsOwned { get; init; }

    /// <summary>直接対応している所持商品数。</summary>
    public required int DirectCount { get; init; }

    /// <summary>素体経由で着られる所持商品数。</summary>
    public required int ViaBaseCount { get; init; }

    /// <summary>
    /// このアバターを対応先として挙げている所持商品の名前（直接の対応のみ、先頭から数件）。
    ///
    /// **名前が取れないアバターのために持つ。**BOOTHが404を返す項目は
    /// 商品IDしか出せず、IDと件数だけでは何のことか分からない。
    /// 「『【くうた対応】School sweater』ほか2件が対応先として挙げています」と読めれば分かる。
    /// </summary>
    public IReadOnlyList<string> ReferencedBy { get; init; } = [];
}

/// <summary>素体グループ1件の一覧表示用。</summary>
public sealed record AvatarBaseSummary
{
    public required AvatarBaseGroup Group { get; init; }

    public required int MemberCount { get; init; }

    public required int OwnedMemberCount { get; init; }

    /// <summary>この素体を名指ししている所持商品数。</summary>
    public required int ItemCount { get; init; }
}

public interface IAvatarService
{
    Task<AvatarDetectResult> DetectAsync(
        IProgress<AvatarDetectProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvatarSummary>> LoadAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvatarBaseSummary>> LoadBasesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 対応アバターの検出と、登録簿の編集。
///
/// 検出の材料は実データの計測で決めた（詳細は 設計詳細_grill結果3_アバター.md）：
/// ・「対応アバター」節のID … 45%の商品にあり、単独で最も多く取れる
/// ・タグ／variation名 … 節が無い商品を補う。組み合わせると宣言の78%に届く
/// ・クレジット・使用素材などの節は読まない（34件中0件しかアバターではなかった）
///
/// 保存済みの h2.html を読むので、取り込み済みの商品は通信ゼロで走る。
/// BOOTHへ問い合わせるのは、まだcategoryを知らない商品IDだけ。
/// </summary>
public sealed partial class AvatarService : IAvatarService
{
    private readonly DataStore _store;
    private readonly AppSettings _settings;
    private readonly IBoothClient? _client;

    /// <summary>
    /// 「対応アバター」節から挙がったときだけ受け入れるcategory。
    ///
    /// **3Dモデル（その他）**：素体はここに入ることが多い（+Head など）。
    ///
    /// **VRoid**：実測（一覧の先頭60件）では**アバター本体は3件だけ**で、
    /// 残りはテクスチャ・衣装・アクセサリ・ツールだった。無条件に受け入れると
    /// 95%が誤りになり、衣装がアバターとして一覧に並ぶ。
    /// ただし本体が無いわけではない——『BlueMallow / ブルーマロウ』は
    /// VRMのモデルなのに category が VRoid で、3Dキャラクターではない。
    /// **出品者が「対応アバター」として挙げているなら、それは本体である。**
    /// </summary>
    private static readonly string[] SupportOnlyCategories = ["3Dモデル（その他）", "VRoid"];

    /// <summary>名前の手掛かりに出す参照元の数。並べすぎても読めない。</summary>
    private const int MaxReferenceNames = 4;

    private const string AvatarCategory = "3Dキャラクター";

    public AvatarService(DataStore store, AppSettings? settings = null, IBoothClient? client = null)
    {
        _store = store;
        _settings = settings ?? new AppSettings();
        _client = client;
    }

    /// <summary>
    /// この登録簿の項目をアバターとして扱うか。
    ///
    /// 判定結果は保存せず毎回ここで決める。規則を直したときに、保存済みのJSONだけで
    /// 全件を計算し直せるようにするため（categoryとseenAsという「事実」だけを持っている）。
    /// </summary>
    public static bool IsAvatar(AvatarRegistryEntry entry)
    {
        if (entry.AvatarOverride is { } forced)
        {
            return forced;
        }

        if (string.Equals(entry.Category, AvatarCategory, StringComparison.Ordinal))
        {
            return true;
        }

        // categoryが弱くても、出品者が「対応アバター」節に挙げているなら受け入れる。
        // +Head のように素体が 3Dモデル（その他）で出ている例があるため
        var fromSupport = entry.SeenAs.TryGetValue(nameof(AvatarLinkSource.SupportSection), out var count) && count > 0;
        return fromSupport && SupportOnlyCategories.Contains(entry.Category);
    }

    public async Task<IReadOnlyList<AvatarSummary>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var registry = _store.Avatars.Load();
        var index = AvatarCompatibilityIndex.Build(registry);

        var owned = loaded.Items
            .Where(item => item.Local.LocalFiles.Count > 0 || item.Local.LocalFolders.Count > 0)
            .ToList();

        var ownedIds = owned.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        var direct = new Dictionary<string, int>(StringComparer.Ordinal);
        var viaBase = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var item in owned)
        {
            foreach (var (avatarId, match) in index.Resolve(item.Local))
            {
                var bucket = match == AvatarMatch.Direct ? direct : viaBase;
                bucket[avatarId] = bucket.TryGetValue(avatarId, out var current) ? current + 1 : 1;

                // 名前が取れないアバターの手掛かりになるので、直接の対応だけ名前も控える
                if (match == AvatarMatch.Direct)
                {
                    if (!names.TryGetValue(avatarId, out var list))
                    {
                        list = [];
                        names[avatarId] = list;
                    }

                    if (list.Count < MaxReferenceNames)
                    {
                        list.Add(item.DisplayName);
                    }
                }
            }
        }

        return registry.Entries
            .Select(entry => new AvatarSummary
            {
                Entry = entry,
                IsAvatar = IsAvatar(entry),
                IsOwned = entry.IsOwnedManually || ownedIds.Contains(entry.ItemId),
                DirectCount = direct.TryGetValue(entry.ItemId, out var d) ? d : 0,
                ViaBaseCount = viaBase.TryGetValue(entry.ItemId, out var v) ? v : 0,
                ReferencedBy = names.TryGetValue(entry.ItemId, out var n) ? n : [],
            })
            // 「アバターとして扱わない」にしたものも残す。一覧から消すと選べなくなり、
            // 隣にある「自動判定に戻す」を押す手段が無くなる（JSONを手で直すしかなくなる）
            .Where(summary => summary.IsAvatar || summary.Entry.AvatarOverride == false)
            .OrderByDescending(summary => summary.IsOwned)
            .ThenByDescending(summary => summary.DirectCount + summary.ViaBaseCount)
            .ThenBy(summary => summary.Entry.DisplayName ?? summary.Entry.ItemId, StringComparer.CurrentCulture)
            .ToList();
    }

    public async Task<IReadOnlyList<AvatarBaseSummary>> LoadBasesAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var registry = _store.Avatars.Load();

        var owned = loaded.Items
            .Where(item => item.Local.LocalFiles.Count > 0 || item.Local.LocalFolders.Count > 0)
            .ToList();

        var ownedIds = owned.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        var declared = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var link in owned.SelectMany(item => item.Local.AvatarBases).Where(link => !link.Rejected))
        {
            declared[link.BaseName] = declared.TryGetValue(link.BaseName, out var current) ? current + 1 : 1;
        }

        return registry.BaseGroups
            .Select(group =>
            {
                var members = registry.Entries
                    .Where(entry => string.Equals(entry.BaseName, group.Name, StringComparison.CurrentCultureIgnoreCase))
                    .ToList();

                return new AvatarBaseSummary
                {
                    Group = group,
                    MemberCount = members.Count,
                    OwnedMemberCount = members.Count(entry => entry.IsOwnedManually || ownedIds.Contains(entry.ItemId)),
                    ItemCount = declared.TryGetValue(group.Name, out var count) ? count : 0,
                };
            })
            .OrderByDescending(summary => summary.MemberCount)
            .ThenBy(summary => summary.Group.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// 全itemを走査して対応アバターを検出する。
    ///
    /// 3段階。①手元のHTML・タグ・variationから候補を集める ②未知のIDのcategoryをBOOTHへ問い合わせる
    /// ③規則を当てて item と登録簿を書き戻す。
    /// ②だけが通信を伴い、一度調べたIDは記録するので二度目からは走らない。
    /// </summary>
    /// <summary>
    /// 落ち着くまで検出を繰り返す。
    ///
    /// 1回目で覚えた別名によって2回目に拾えるものが増えるので、
    /// 1回で止めると「もう一度押すと結果が変わる」状態になる。
    /// 2回目以降はBOOTHへの問い合わせがほぼ無く、手元の照合だけなので安い。
    /// </summary>
    public async Task<AvatarDetectResult> DetectAsync(
        IProgress<AvatarDetectProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        AvatarDetectResult? result = null;
        var requests = 0;
        var updated = 0;

        for (var pass = 0; pass < 4; pass++)
        {
            var current = await DetectOnceAsync(progress, cancellationToken);
            requests += current.Requests;
            updated += current.ItemsUpdated;

            // 何回で落ち着いたかではなく、合計で何件書き換えたかを返す
            result = current with { Requests = requests, ItemsUpdated = updated };

            // 商品が変わらなくても、登録簿が増えていれば次の回で拾えるものが増える
            if (current.ItemsUpdated == 0 && current.RegistryAdded == 0)
            {
                break;
            }
        }

        return result!;
    }

    /// <summary>
    /// 以前の初期辞書が配った誤った値を、保存済みの登録簿でも直す。直した組の数を返す。
    ///
    /// 起動時に呼ぶ。初期辞書は検出のときにしか合流しないので、検出を走らせない人の登録簿は
    /// いつまでも古い値のまま残り、検索の素体経由が広がらない。
    /// 直すものが無ければ書かない（毎回の起動で登録簿を書き換えない）。
    /// </summary>
    public int RepairSeededGroups()
    {
        var registry = _store.Avatars.Load();
        var groups = registry.BaseGroups.ToList();

        var repaired = AvatarBaseSeed.RepairLegacy(groups);
        if (repaired > 0)
        {
            JsonStore.Write(_store.Avatars.Path, new AvatarRegistry { Entries = registry.Entries, BaseGroups = groups });
        }

        return repaired;
    }

    private async Task<AvatarDetectResult> DetectOnceAsync(
        IProgress<AvatarDetectProgress>? progress,
        CancellationToken cancellationToken)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var registry = _store.Avatars.Load();

        var entries = registry.Entries.ToDictionary(entry => entry.ItemId, StringComparer.Ordinal);
        var entriesBefore = entries.Count;
        // 名前が定着している共通素体を最初だけ足す。既にある名前には触らない。
        // 素体の関係はBOOTHのデータからは取れないので、空から始めると何も出ない
        var seeded = registry.BaseGroups.ToList();
        AvatarBaseSeed.Merge(seeded);

        var groups = seeded.ToDictionary(group => group.Name, StringComparer.CurrentCultureIgnoreCase);

        // ── ⓪ ライブラリの中のアバターを先に登録する ──
        //
        // 他の商品から参照されたIDだけを候補にすると、対応衣装を持っていない
        // アバターが永久に登録されない。自分のアバターが1体も出ない状態になる。
        // categoryは手元のitemに入っているので、ここは通信ゼロで済む。
        // 先に入れておくと、その別名（＝自分のアバターの呼び名）が最初から
        // 照合に使えるので、衣装側の検出精度も上がる。
        foreach (var item in loaded.Items)
        {
            if (!string.Equals(item.Booth.Category?.Name, AvatarCategory, StringComparison.Ordinal)
                || entries.ContainsKey(item.Id))
            {
                continue;
            }

            entries[item.Id] = new AvatarRegistryEntry
            {
                ItemId = item.Id,
                BoothName = item.Booth.Name,
                DisplayName = AvatarText.ShortenName(
                    item.Booth.Name,
                    BuildAliasesFromTags(item.Booth).Select(alias => alias.Text)),
                Category = item.Booth.Category?.Name,
                CheckedAt = item.Booth.FetchedAt,
                Aliases = BuildAliasesFromTags(item.Booth),
            };
        }

        // 索引はライブラリのアバターを入れた後に組む。先に組むと、
        // 自分のアバターの別名が1回目の照合に効かない
        var index = AvatarNameIndex.Build(
            new AvatarRegistry { Entries = entries.Values.ToList(), BaseGroups = seeded },
            IsAvatar);

        // ── ① 手元の材料から候補を集める ──
        var candidates = new Dictionary<string, ItemScan>(StringComparer.Ordinal);
        var seenAs = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var nameHints = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var aliasCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var baseAliasCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.CurrentCultureIgnoreCase);

        var done = 0;
        foreach (var item in loaded.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AvatarDetectProgress
            {
                Phase = "説明文とタグを読む",
                Done = ++done,
                Total = loaded.Items.Count,
                Current = item.Booth.Name,
            });

            var scan = ScanItem(item, index);
            candidates[item.Id] = scan;

            foreach (var hit in scan.Description.Support)
            {
                Bump(seenAs, hit.ItemId, nameof(AvatarLinkSource.SupportSection));
                if (hit.NameHint is { } hint)
                {
                    Bump(nameHints, hit.ItemId, hint);
                }
            }

            foreach (var id in scan.Description.Other)
            {
                Bump(seenAs, id, nameof(AvatarLinkSource.H2Link));
            }

            foreach (var id in scan.FromTags)
            {
                Bump(seenAs, id, nameof(AvatarLinkSource.Tag));
            }

            foreach (var id in scan.FromVariations)
            {
                Bump(seenAs, id, nameof(AvatarLinkSource.Variation));
            }

            // 別名の出現回数は毎回数え直す（加算しない）。
            // **呼び名そのものに当たったタグだけを数える。**以前は含んでいるかで当たったタグを
            // 覚えていたので、「ミルティナ対応」が「ティナ」の別名になり、次の回から誤りが固定されていた
            foreach (var tag in item.Booth.Tags)
            {
                foreach (var id in index.FindExact(tag))
                {
                    Bump(aliasCounts, id, tag);
                }
            }

            foreach (var name in scan.BaseNames)
            {
                Bump(baseAliasCounts, name, name);
            }
        }

        // ── ② 未知のIDだけBOOTHへ問い合わせる ──
        var unknown = seenAs.Keys
            .Where(id => !entries.ContainsKey(id))
            .ToList();

        var requests = 0;
        var unresolved = 0;
        done = 0;

        foreach (var id in unknown)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AvatarDetectProgress
            {
                Phase = "候補の種類を確かめる",
                Done = ++done,
                Total = unknown.Count,
                Current = id,
            });

            // 手元に持っている商品なら、カテゴリも名前も items/{ID}.json にある。
            // 登録簿に無いというだけで問い合わせに行くと、持っているぶんだけ無駄に通信する
            // （実データでは6体中4体がこれに当たっていた）
            if (await _store.Items.LoadAsync(id, cancellationToken) is { } owned)
            {
                entries[id] = new AvatarRegistryEntry
                {
                    ItemId = id,
                    BoothName = owned.Booth.Name,
                    DisplayName = AvatarText.ShortenName(owned.Booth.Name),
                    Category = owned.Booth.Category?.Name,
                    CheckedAt = DateTimeOffset.Now,
                };
                continue;
            }

            if (_client is null)
            {
                unresolved++;
                continue;
            }

            requests++;
            var fetched = await _client.GetItemJsonAsync(id, cancellationToken);

            if (fetched.Status == BoothFetchStatus.NotFound)
            {
                // 販売終了。categoryを観測できないだけで、アバターではないとは限らない
                entries[id] = new AvatarRegistryEntry
                {
                    ItemId = id,
                    Category = null,
                    CheckedAt = DateTimeOffset.Now,
                };
                continue;
            }

            if (!fetched.IsSuccess || fetched.Value is null)
            {
                // 通信の失敗で何も決めない。次回また試す
                unresolved++;
                continue;
            }

            var booth = BoothItemMapper.Map(fetched.Value, DateTimeOffset.Now);
            var aliases = string.Equals(booth.Category?.Name, AvatarCategory, StringComparison.Ordinal)
                ? BuildAliasesFromTags(booth)
                : [];

            entries[id] = new AvatarRegistryEntry
            {
                ItemId = id,
                BoothName = booth.Name,
                DisplayName = AvatarText.ShortenName(booth.Name, aliases.Select(alias => alias.Text)),
                Category = booth.Category?.Name,
                CheckedAt = DateTimeOffset.Now,
                // 別名はアバターにだけ持たせる。依存ツールの名前で照合しても意味が無い
                Aliases = aliases,
            };
        }

        // ── ③ 規則を当てて書き戻す ──
        foreach (var (id, sources) in seenAs)
        {
            if (entries.TryGetValue(id, out var entry))
            {
                entries[id] = entry with { SeenAs = sources };
            }
        }

        foreach (var (id, hints) in nameHints)
        {
            if (!entries.TryGetValue(id, out var entry))
            {
                continue;
            }

            entries[id] = entry with { Aliases = MergeAliases(entry.Aliases, hints, nameof(AvatarLinkSource.SupportSection)) };
        }

        foreach (var (id, counted) in aliasCounts)
        {
            if (!entries.TryGetValue(id, out var entry) || !IsAvatar(entry))
            {
                continue;
            }

            entries[id] = entry with { Aliases = MergeAliases(entry.Aliases, counted, nameof(AvatarLinkSource.Tag)) };
        }

        foreach (var (name, counted) in baseAliasCounts)
        {
            if (groups.ContainsKey(name))
            {
                continue;
            }

            groups[name] = new AvatarBaseGroup
            {
                Name = name,
                Aliases = counted.Select(pair => new AvatarAlias
                {
                    Text = pair.Key,
                    Count = pair.Value,
                    Source = nameof(AvatarLinkSource.Tag),
                }).ToList(),
            };
        }

        var updated = 0;
        done = 0;

        foreach (var item in loaded.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AvatarDetectProgress
            {
                Phase = "対応アバターを書き込む",
                Done = ++done,
                Total = loaded.Items.Count,
            });

            var scan = candidates[item.Id];
            var links = BuildLinks(scan, entries);
            var bases = BuildBaseLinks(scan);

            var merged = MergeLinks(item.Local.Avatars, links);
            var mergedBases = MergeBaseLinks(item.Local.AvatarBases, bases);

            if (SameLinks(item.Local.Avatars, merged) && SameBaseLinks(item.Local.AvatarBases, mergedBases))
            {
                continue;
            }

            // 全件を先に読んでから順に書くので、書く頃には手元の写しが古い。
            // 検出が持つ3項目だけを名指しして、その間のユーザ入力を潰さない
            var written = await _store.Items.SaveLocalAsync(
                item.Id,
                item.Local with
                {
                    Avatars = merged,
                    AvatarBases = mergedBases,
                    AvatarsDetectedAt = DateTimeOffset.Now,
                },
                LocalOwners.Detection,
                cancellationToken: cancellationToken);

            if (written)
            {
                updated++;
            }
        }

        var finalRegistry = new AvatarRegistry
        {
            Entries = entries.Values.OrderBy(entry => entry.ItemId, StringComparer.Ordinal).ToList(),
            BaseGroups = groups.Values.OrderBy(group => group.Name, StringComparer.CurrentCulture).ToList(),
        };

        await _store.Avatars.SaveAsync(finalRegistry, cancellationToken);

        return new AvatarDetectResult
        {
            ItemsScanned = loaded.Items.Count,
            ItemsUpdated = updated,
            AvatarsFound = finalRegistry.Entries.Count(IsAvatar),
            NonAvatars = finalRegistry.Entries.Count(entry => entry.Category is not null && !IsAvatar(entry)),
            BaseGroupsFound = finalRegistry.BaseGroups.Count,
            Requests = requests,
            Unresolved = unresolved,
            RegistryAdded = entries.Count - entriesBefore,
        };
    }

    private sealed record ItemScan
    {
        public required DescriptionScan Description { get; init; }

        public required IReadOnlyCollection<string> FromTags { get; init; }

        public required IReadOnlyCollection<string> FromVariations { get; init; }

        public required IReadOnlyList<string> BaseNames { get; init; }
    }

    private ItemScan ScanItem(ItemRecord item, AvatarNameIndex index)
    {
        var html = ReadHtml(item.Id);

        var description = AvatarDetector.ScanDescription(
            html, item.Id, _settings.AvatarSupportHeadings, _settings.AvatarIgnoredHeadings);

        // 購入したvariationがあればそれを先に見る。買った版がそのままアバター名になっている
        var variationNames = item.Local.Purchases
            .Select(purchase => purchase.NameSnapshot)
            .Concat(item.Booth.Variations.Select(variation => variation.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();

        var (fromTags, fromVariations) = AvatarDetector.ScanNames(index, item.Booth.Tags, variationNames);

        return new ItemScan
        {
            Description = description,
            FromTags = fromTags,
            FromVariations = fromVariations,
            BaseNames = AvatarDetector.ScanBaseTags(item.Booth.Tags),
        };
    }

    private string? ReadHtml(string itemId)
    {
        try
        {
            var path = _store.Paths.ItemHtmlFile(itemId);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 候補に規則を当てて対応アバターの宣言にする。
    /// 「対応アバター」節から来たものだけ確定にし、その他の見出しのリンクは要確認へ回す。
    /// </summary>
    private static List<AvatarLink> BuildLinks(
        ItemScan scan,
        IReadOnlyDictionary<string, AvatarRegistryEntry> entries)
    {
        var links = new Dictionary<string, AvatarLink>(StringComparer.Ordinal);

        void Add(string id, AvatarLinkSource source, bool confirmed)
        {
            if (!entries.TryGetValue(id, out var entry) || !IsAvatar(entry) || links.ContainsKey(id))
            {
                return;
            }

            links[id] = new AvatarLink
            {
                AvatarItemId = id,
                Name = entry.DisplayName,
                Source = source,
                Confirmed = confirmed,
            };
        }

        foreach (var hit in scan.Description.Support)
        {
            Add(hit.ItemId, AvatarLinkSource.SupportSection, confirmed: true);
        }

        foreach (var id in scan.FromTags)
        {
            Add(id, AvatarLinkSource.Tag, confirmed: true);
        }

        foreach (var id in scan.FromVariations)
        {
            Add(id, AvatarLinkSource.Variation, confirmed: true);
        }

        foreach (var id in scan.Description.Other)
        {
            Add(id, AvatarLinkSource.H2Link, confirmed: false);
        }

        return links.Values.ToList();
    }

    private static List<AvatarBaseLink> BuildBaseLinks(ItemScan scan)
        => scan.BaseNames
            .Select(name => new AvatarBaseLink
            {
                BaseName = name,
                Source = AvatarLinkSource.Tag,
                Confirmed = true,
            })
            .ToList();

    /// <summary>
    /// 再検出のマージ。<c>Manual</c> は残し、それ以外を作り直す。
    ///
    /// これが無いと、ユーザが手で足した対応が再検出で消え、
    /// 手で消した誤検出（Rejected）が復活する。
    /// </summary>
    private static List<AvatarLink> MergeLinks(
        IReadOnlyList<AvatarLink> existing,
        IReadOnlyList<AvatarLink> detected)
    {
        var manual = existing.Where(link => link.Source == AvatarLinkSource.Manual).ToList();
        var manualIds = manual.Select(link => link.AvatarItemId).ToHashSet(StringComparer.Ordinal);

        return manual
            .Concat(detected.Where(link => !manualIds.Contains(link.AvatarItemId)))
            .ToList();
    }

    private static List<AvatarBaseLink> MergeBaseLinks(
        IReadOnlyList<AvatarBaseLink> existing,
        IReadOnlyList<AvatarBaseLink> detected)
    {
        var manual = existing.Where(link => link.Source == AvatarLinkSource.Manual).ToList();
        var manualNames = manual.Select(link => link.BaseName)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        return manual
            .Concat(detected.Where(link => !manualNames.Contains(link.BaseName)))
            .ToList();
    }

    private static bool SameLinks(IReadOnlyList<AvatarLink> a, IReadOnlyList<AvatarLink> b)
        => a.Count == b.Count && a.OrderBy(x => x.AvatarItemId, StringComparer.Ordinal)
            .SequenceEqual(b.OrderBy(x => x.AvatarItemId, StringComparer.Ordinal));

    private static bool SameBaseLinks(IReadOnlyList<AvatarBaseLink> a, IReadOnlyList<AvatarBaseLink> b)
        => a.Count == b.Count && a.OrderBy(x => x.BaseName, StringComparer.Ordinal)
            .SequenceEqual(b.OrderBy(x => x.BaseName, StringComparer.Ordinal));

    /// <summary>
    /// 商品JSONから別名を作る。正式名と、そこに含まれるタグを採る。
    ///
    /// タグをそのまま全部入れると「VRChat」「オリジナル」のような汎用語が混ざるので、
    /// 商品名に現れているものだけに絞る（実測でこの条件が効いた）。
    /// </summary>
    private static List<AvatarAlias> BuildAliasesFromTags(BoothBlock booth)
    {
        var aliases = new List<AvatarAlias>();
        var normalizedName = AvatarText.Normalize(booth.Name);

        foreach (var tag in booth.Tags.Distinct(StringComparer.CurrentCultureIgnoreCase))
        {
            var normalized = AvatarText.Normalize(tag);
            if (normalized.Length >= 2
                && !AvatarText.IsGenericName(tag)
                && normalizedName.Contains(normalized, StringComparison.Ordinal))
            {
                aliases.Add(new AvatarAlias { Text = tag, Count = 0, Source = nameof(AvatarLinkSource.Tag) });
            }
        }

        return aliases;
    }

    /// <summary>
    /// 覚えている別名に、今回数え直したぶんを重ねる。
    ///
    /// 既にある表記は**数だけ**入れ替える。消した印はそのまま残す
    /// （落とすと、消しても次の検出で毎回復活する）。
    /// <see cref="IsAvatar"/> と同じく規則だけの関数なので公開している。
    /// </summary>
    public static IReadOnlyList<AvatarAlias> MergeAliases(
        IReadOnlyList<AvatarAlias> existing,
        IReadOnlyDictionary<string, int> counted,
        string source)
    {
        var byText = existing.ToDictionary(alias => alias.Text, StringComparer.CurrentCultureIgnoreCase);

        foreach (var (text, count) in counted)
        {
            if (AvatarText.IsGenericName(text))
            {
                continue;
            }

            if (byText.TryGetValue(text, out var alias))
            {
                // 消された別名は数だけ数え直し、消されたままにしておく。
                // ここで印を落とすと、次の検出で毎回復活する
                byText[text] = alias with { Count = count };
            }
            else
            {
                byText[text] = new AvatarAlias { Text = text, Count = count, Source = source };
            }
        }

        return byText.Values.OrderByDescending(alias => alias.Count).ToList();
    }

    private static void Bump<TKey>(Dictionary<TKey, Dictionary<string, int>> into, TKey key, string inner)
        where TKey : notnull
    {
        if (!into.TryGetValue(key, out var counts))
        {
            counts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
            into[key] = counts;
        }

        counts[inner] = counts.TryGetValue(inner, out var current) ? current + 1 : 1;
    }
}

