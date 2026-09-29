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

    /// <summary>画面に出す名前。同じ名前の別アバターがあればショップ名まで付けてある（<see cref="AvatarNames.Map"/>）。</summary>
    public string Name { get; init; } = string.Empty;

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

    /// <summary>
    /// この素体に属するアバターの商品ID。<see cref="MemberCount"/> と同じ数え方
    /// （手で決めた所属に、名前から推した仲間を足したもの）。
    /// 画面で「この素体を使っているアバター」を並べるのに使う。所属を別の数え方で引くと、人数と一覧が食い違う
    /// </summary>
    public IReadOnlyList<string> MemberIds { get; init; } = [];

    public required int OwnedMemberCount { get; init; }

    /// <summary>この素体を名指ししている所持商品数。</summary>
    public required int ItemCount { get; init; }
}

/// <summary>共通素体を手で足した結果。画面が何と知らせるかを分ける。</summary>
public enum AvatarBaseAddOutcome
{
    /// <summary>新しく足した。</summary>
    Added,

    /// <summary>消していた素体を戻した。</summary>
    Restored,

    /// <summary>もう一覧にあった。何も書いていない。</summary>
    AlreadyThere,
}

public interface IAvatarService
{
    Task<AvatarDetectResult> DetectAsync(
        IProgress<AvatarDetectProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>裏での検出をお願いする（結果は見ない）。走っている間に来た分は1回にまとめる。</summary>
    Task RequestDetectAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvatarSummary>> LoadAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvatarBaseSummary>> LoadBasesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// アバターの登録簿を人が直す操作（アバター画面）。画面からは <see cref="Commands.UiCommand"/> で呼ぶ（技術的負債 3-1）。
/// 検出（<see cref="IAvatarService"/>）と口を分けるのは、検出だけを差し替える試験の作り物に、使わない操作まで持たせないため。
/// </summary>
public interface IAvatarRegistryEditor
{
    Task SetDisplayNameAsync(string itemId, string name, CancellationToken cancellationToken = default);

    Task SetMemoAsync(string itemId, string? memo, CancellationToken cancellationToken = default);

    Task SetOwnedManuallyAsync(string itemId, bool owned, CancellationToken cancellationToken = default);

    Task SetAvatarOverrideAsync(string itemId, bool? value, CancellationToken cancellationToken = default);

    Task SetBaseAsync(string itemId, string? baseName, CancellationToken cancellationToken = default);

    Task SetInferClothingAsync(string name, bool infer, CancellationToken cancellationToken = default);

    Task SetBaseItemIdAsync(string name, string? itemId, CancellationToken cancellationToken = default);

    /// <returns>書き換えた商品の数。</returns>
    Task<int> RenameBaseAsync(string oldName, string newName, CancellationToken cancellationToken = default);

    /// <returns>書き換えた商品の数。</returns>
    Task<int> DeleteBaseAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>共通素体を手で足す（どのアバターにもまだ結ばない）。</summary>
    Task<AvatarBaseAddOutcome> AddBaseAsync(string name, CancellationToken cancellationToken = default);

    Task AddAliasAsync(string itemId, string text, CancellationToken cancellationToken = default);

    Task RemoveAliasAsync(string itemId, string text, CancellationToken cancellationToken = default);

    /// <returns>BOOTH に確かめられたか。</returns>
    /// <summary>もう一度BOOTHに問い合わせる。**失敗の種類をそのまま返す**（E3）。404 は「非公開になっていた」として書き込む。</summary>
    Task<Booth.BoothFetchStatus> RecheckAsync(string itemId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 対応アバターの検出と、登録簿の編集。
///
/// 検出の材料は実データの計測で決めた（詳細は docs/history/avatars.md）：
/// ・「対応アバター」節のID … 45%の商品にあり、単独で最も多く取れる
/// ・タグ／variation名 … 節が無い商品を補う。組み合わせると宣言の78%に届く
/// ・クレジット・使用素材などの節は読まない（34件中0件しかアバターではなかった）
///
/// 保存済みの h2.html を読むので、取り込み済みの商品は通信ゼロで走る。
/// BOOTHへ問い合わせるのは、まだcategoryを知らない商品IDだけ。
/// </summary>
public sealed partial class AvatarService : IAvatarService, IAvatarRegistryEditor
{
    private readonly DataStore _store;
    private readonly Func<AppSettings> _currentSettings;
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
        : this(store, SettingsSource.Fixed(settings), client)
    {
    }

    /// <param name="currentSettings">使うたびに今の設定を返すもの（<see cref="SettingsSource"/>）。</param>
    public AvatarService(DataStore store, Func<AppSettings> currentSettings, IBoothClient? client = null)
    {
        _store = store;
        _currentSettings = currentSettings;
        _client = client;
    }

    /// <summary>今の設定。**抱えずに毎回読む。**</summary>
    private AppSettings _settings => _currentSettings();

    /// <summary>
    /// 起動時に、ライブラリ全体を検出し直す時期か。
    ///
    /// 検出は取り込み・アバター画面のボタン・手で紐付けた後にしか走らない。取り込まない間も、⑦が取り直した説明文や、
    /// 手で直した別名・素体の名前で拾える物は増えるので、間隔を過ぎたら裏で1度回す（設定「対応アバターを検出し直す間隔」）。
    /// </summary>
    public static bool IsRedetectDue(DateTimeOffset? detectedAt, int intervalDays, DateTimeOffset now)
        => detectedAt is not { } last || now - last >= TimeSpan.FromDays(Math.Max(1, intervalDays));

    /// <inheritdoc cref="IsRedetectDue(DateTimeOffset?, int, DateTimeOffset)"/>
    public bool IsRedetectDue() => IsRedetectDue(_store.Avatars.Load().DetectedAt, _settings.AvatarDetectRecheckDays, DateTimeOffset.Now);

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
            .Where(item => item.Local.OwnedFiles.Count > 0 || item.Local.LocalFolders.Count > 0)
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

        var shownNames = AvatarNames.Map(registry.Entries);
        return registry.Entries
            .Select(entry => new AvatarSummary
            {
                Entry = entry,
                Name = shownNames[entry.ItemId],
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
            .ThenBy(summary => summary.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    public async Task<IReadOnlyList<AvatarBaseSummary>> LoadBasesAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var registry = _store.Avatars.Load();

        var owned = loaded.Items
            .Where(item => item.Local.OwnedFiles.Count > 0 || item.Local.LocalFolders.Count > 0)
            .ToList();

        var ownedIds = owned.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        // 人数は検索と同じ数え方にする。手で決めた所属だけで数えると、名前から推した仲間が
        // 検索では素体経由として出るのに、ここでは「属しているのはこのアバターだけ」と出てしまう
        var compatibility = AvatarCompatibilityIndex.Build(registry);

        var declared = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var link in owned.SelectMany(item => item.Local.AvatarBases).Where(link => !link.Rejected))
        {
            declared[link.BaseName] = declared.TryGetValue(link.BaseName, out var current) ? current + 1 : 1;
        }

        // 消した印の付いたグループは一覧に出さない（行は「戻さない」ための記録として残っているだけ・X1）
        return registry.BaseGroups
            .Where(group => !group.Rejected)
            .Select(group =>
            {
                var memberIds = compatibility.MembersOf(group.Name).ToHashSet(StringComparer.Ordinal);
                var members = registry.Entries
                    .Where(entry => memberIds.Contains(entry.ItemId))
                    .ToList();

                return new AvatarBaseSummary
                {
                    Group = group,
                    MemberCount = members.Count,
                    MemberIds = members.Select(entry => entry.ItemId).ToList(),
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
        // **1本ずつ走らせる。**取り込みの③、アバター画面のボタン、手で紐付けた後の検出が重なりうる。
        // どれもライブラリ全体を読み書きするので、重なると後から書いた方が先の結果を消す
        await _detectGate.WaitAsync(cancellationToken);

        try
        {
            // **画面のスレッドの外で回す。**Core は続きを元の文脈へ戻すので、画面から押した検出・取り込みの③は
            // 全商品の走査（正規表現と名前の照合）を画面のスレッドで回していた（205件で1回0.37秒、2000件で数秒の固まり）。
            // 進み具合は画面の Progress が画面のスレッドへ運ぶ。優先度（Prioritize）は AsyncLocal なので中へ引き継がれる。
            // 取り消しの印は中で見る（Task.Run に渡すと、始まる前の取り消しで例外の種類が変わる）
            return await Task.Run(() => DetectUnguardedAsync(progress, cancellationToken));
        }
        finally
        {
            _detectGate.Release();
        }
    }

    private readonly SemaphoreSlim _detectGate = new(1, 1);

    private CoalescedRun? _detectRequests;

    /// <summary>
    /// 裏での検出をお願いする（ユーザ判断 2026-09-21・N4）。**走っている間に来た分は1回にまとめる。**
    ///
    /// 未確定で確定するたびに投げていたので、10件まとめて確定すると
    /// 全件走査が10回直列に並んでいた（錠があるので壊れはしないが、待たせるだけ）。
    /// 検出は毎回ライブラリ全体を見るので、10回やっても結果は最後の1回と同じ。
    /// まとめ方と取りこぼさない理由は <see cref="CoalescedRun"/>。
    /// </summary>
    public Task RequestDetectAsync(CancellationToken cancellationToken = default)
        => LazyInitializer.EnsureInitialized(
                ref _detectRequests,
                () => new CoalescedRun(token => DetectAsync(cancellationToken: token)))
            .RequestAsync(cancellationToken);

    private async Task<AvatarDetectResult> DetectUnguardedAsync(
        IProgress<AvatarDetectProgress>? progress,
        CancellationToken cancellationToken)
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

    private async Task<AvatarDetectResult> DetectOnceAsync(
        IProgress<AvatarDetectProgress>? progress,
        CancellationToken cancellationToken)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var registry = _store.Avatars.Load();

        var entries = FirstWins.Map(registry.Entries, entry => entry.ItemId, StringComparer.Ordinal);
        var entriesBefore = entries.Count;
        // 名前が定着している共通素体を最初だけ足す。既にある名前には触らない。
        // 素体の関係はBOOTHのデータからは取れないので、空から始めると何も出ない
        var seeded = registry.BaseGroups.ToList();
        AvatarBaseSeed.Merge(seeded);

        // 消した印の付いたグループは照合に使わない（行は「戻さない」ための記録・X1）
        var groups = FirstWins.Map(
            seeded.Where(group => !group.Rejected),
            group => group.Name,
            StringComparer.CurrentCultureIgnoreCase);

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

            // 表示名は書かない。正式名から計算する（AvatarNames・#54）
            entries[item.Id] = new AvatarRegistryEntry
            {
                ItemId = item.Id,
                BoothName = item.Booth.Name,
                ShopName = item.Booth.Shop?.Name,
                Category = item.Booth.Category?.Name,
                CheckedAt = item.Booth.FetchedAt,
                Aliases = BuildAliasesFromTags(item.Booth),
            };
        }

        // 以前の版で入れたアバターにはショップ名が無い。手元に持っている物だけは通信せずに埋められる
        // （同じ名前のアバターを見分けるのに使う）
        foreach (var item in loaded.Items)
        {
            if (entries.TryGetValue(item.Id, out var known) && known.ShopName is null && item.Booth.Shop?.Name is { Length: > 0 } shop)
            {
                entries[item.Id] = known with { ShopName = shop };
            }
        }

        // 索引はライブラリのアバターを入れた後に組む。先に組むと、
        // 自分のアバターの別名が1回目の照合に効かない
        var index = AvatarNameIndex.Build(
            new AvatarRegistry { Entries = entries.Values.ToList(), BaseGroups = seeded },
            IsAvatar);

        // 走査の控えの鍵のうち、全商品に共通の分（索引・照合に使う素体・見出しの設定）
        var scanContext = ScanContextOf(index, groups.Values);
        var htmlStamps = HtmlStamps();
        PruneScans(loaded.Items);

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
                Phase = "説明文とタグを読んでいます",
                Done = ++done,
                Total = loaded.Items.Count,
                Current = item.Booth.Name,
            });

            var scan = ScanItemCached(item, index, groups.Values, scanContext, htmlStamps);
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

            foreach (var id in scan.SupportLists)
            {
                Bump(seenAs, id, nameof(AvatarLinkSource.SupportList));
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

        // ── ② 未知のIDと、確かめ直す時期の来た404の項目だけBOOTHへ問い合わせる ──
        var libraryIds = loaded.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var now = DateTimeOffset.Now;
        var unknown = seenAs.Keys
            .Where(id => !entries.TryGetValue(id, out var known)
                         || IsNotFoundRecheckDue(known, libraryIds.Contains(id), _settings.DelistedRecheckDays, now))
            .ToList();

        var requests = 0;
        var unresolved = 0;
        done = 0;

        // **問い合わせて分かった項目は、途中でも登録簿へ書いておく**（ユーザ判断 2026-09-29）。
        // 最後に1回だけ書いていたので、中止・閉じる・例外で抜けると、それまでの問い合わせ（友人データの初回で約37分）が
        // 全部捨てられ、次の検出がまた最初から問い合わせ直していた。
        // 途中で書くのは問い合わせの結果だけ。seenAs・別名の数え直し・素体は全商品を読み終えた値なので、最後にまとめて書く
        var pending = new List<string>();
        var requestsSinceFlush = 0;

        async Task FlushAsync()
        {
            if (pending.Count == 0)
            {
                return;
            }

            var batch = pending.ToDictionary(id => id, id => entries[id], StringComparer.Ordinal);
            // 中止で抜けるときにも書くので、取り消しの印は渡さない（書き込みは一瞬で終わる）
            await _store.Avatars.UpdateAsync(latest => MergeFetched(latest, registry, batch), CancellationToken.None);
            pending.Clear();
        }

        try
        {
            foreach (var id in unknown)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 商品IDは画面に出さない（内部の言葉）。名前は問い合わせるまで分からないので空で渡す
                progress?.Report(new AvatarDetectProgress
                {
                    Phase = "見つかった商品を確かめています",
                    Done = ++done,
                    Total = unknown.Count,
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
                        ShopName = owned.Booth.Shop?.Name,
                        Category = owned.Booth.Category?.Name,
                        CheckedAt = DateTimeOffset.Now,
                    };
                    pending.Add(id);
                    continue;
                }

                if (_client is null)
                {
                    unresolved++;
                    continue;
                }

                requests++;
                var fetched = await _client.GetItemJsonAsync(id, cancellationToken);
                var answered = Answered(id, fetched, entries.GetValueOrDefault(id));
                if (answered is null)
                {
                    unresolved++;
                }
                else
                {
                    entries[id] = answered;
                    pending.Add(id);
                }

                // 1件の問い合わせは1.5秒以上かかるので、20件で約30秒ぶん。
                // 強制終了（finally も走らない）で失うのをそれまでに抑える。登録簿の書き込みは一瞬なので、
                // 30秒に1回なら問い合わせの間隔に比べて無視できる。1件ごとに書くほど細かくする理由は無い
                if (++requestsSinceFlush >= FlushEveryRequests)
                {
                    requestsSinceFlush = 0;
                    await FlushAsync();
                }
            }
        }
        catch
        {
            // 中止・例外で抜けるときも、そこまでの問い合わせの結果は残す。
            // 書けなかったときは元の例外を隠さない（ログに残して元の例外を投げ直す）
            try
            {
                await FlushAsync();
            }
            catch (Exception flushFailure)
            {
                Diagnostics.AppLog.Error("対応アバターの検出：途中で止まったときの登録簿の書き込み", flushFailure);
            }

            throw;
        }

        // ③で商品を書いている間に止まっても、問い合わせの結果は残るように、ここで一度書く
        await FlushAsync();

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
                Phase = "対応アバターを書き込んでいます",
                Done = ++done,
                Total = loaded.Items.Count,
            });

            var scan = candidates[item.Id];
            var links = BuildLinks(scan, entries);
            var bases = BuildBaseLinks(scan);

            // 全件を先に読んでから順に書くので、書く頃には手元の写しが古い（友人データの初回で約37分）。
            // 名指し（LocalOwners.Detection）が守るのは**他の**項目だけなので、
            // **重ね合わせそのものを書く直前の値に当てる**。
            // 古い写しに当てていたため、検出の間に人が手で足した／「違う」と消した対応アバターが元へ戻っていた
            var written = await _store.Items.ChangeLocalAsync(
                item.Id,
                current =>
                {
                    var merged = MergeLinks(current.Avatars, links);
                    var mergedBases = MergeBaseLinks(current.AvatarBases, bases);

                    return SameLinks(current.Avatars, merged) && SameBaseLinks(current.AvatarBases, mergedBases)
                        ? null
                        : current with
                        {
                            Avatars = merged,
                            AvatarBases = mergedBases,
                            AvatarsDetectedAt = DateTimeOffset.Now,
                        };
                },
                LocalOwners.Detection,
                cancellationToken);

            if (written)
            {
                updated++;
            }
        }

        // 始めに読んだ写しで丸ごと書くと、検出の間に人が保存した名前・メモ・素体などが消える（U15）。
        // 書く直前の最新に、検出が受け持つ項目だけを重ねる
        var finalRegistry = await _store.Avatars.UpdateAsync(
            latest => MergeDetected(latest, registry, entries, groups),
            cancellationToken);

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

    /// <summary>途中で登録簿へ書く間隔（問い合わせの件数）。根拠は書く所のコメント。</summary>
    private const int FlushEveryRequests = 20;

    /// <summary>
    /// 問い合わせの答えを登録簿の項目にする。**何も決められない答え（通信の失敗・読めない応答）は null**
    /// ——次回また試す（投げると検出全体が止まる）。
    /// </summary>
    private static AvatarRegistryEntry? Answered(string id, BoothFetchResult<string> fetched, AvatarRegistryEntry? known)
    {
        var fresh = AnsweredFresh(id, fetched);
        if (known is null || fresh is null)
        {
            return fresh;
        }

        // 確かめ直した404の項目（IsNotFoundRecheckDue）。まだ404なら確かめた日だけ進める。
        // 人が付けた名前・メモ・素体は項目ごと引き継ぐ（作り直すと、最後に重ねるまでの間に消えて見える）
        if (fetched.Status == BoothFetchStatus.NotFound)
        {
            return known with { CheckedAt = fresh.CheckedAt };
        }

        // 再公開されていた。名前・カテゴリを埋める
        return known with
        {
            BoothName = fresh.BoothName,
            ShopName = fresh.ShopName ?? known.ShopName,
            Category = fresh.Category,
            CheckedAt = fresh.CheckedAt,
            Aliases = MergeDetectedAliases(known.Aliases, fresh.Aliases),
            ImageUrl = string.IsNullOrEmpty(fresh.ImageUrl) ? known.ImageUrl : fresh.ImageUrl,
        };
    }

    /// <summary>
    /// 404 だった項目を、次の検出で問い合わせ直す時期か（ユーザ判断 2026-09-29）。
    ///
    /// 404 の項目は一度入ると二度と問い合わせず、再公開されても名前もカテゴリも埋まらなかった。
    /// 間隔は販売終了と見なした商品の確かめ直し（<see cref="AppSettings.DelistedRecheckDays"/>）と同じ：
    /// 確かめる間隔が再公開されている期間より長いと原理的に取り逃し、季節物は1ヶ月ほどしか公開されない。
    /// **問い合わせを増やす向き**なので、対象は問い合わせで作った404の項目だけにする。
    /// 手元に持っている商品の項目はカテゴリが無くても含めない（その商品は⑦が取り直す）。
    /// </summary>
    public static bool IsNotFoundRecheckDue(AvatarRegistryEntry entry, bool inLibrary, int intervalDays, DateTimeOffset now)
        => entry.Category is null
           && !inLibrary
           && (entry.CheckedAt is not { } checkedAt || now - checkedAt >= TimeSpan.FromDays(Math.Max(1, intervalDays)));

    private static AvatarRegistryEntry? AnsweredFresh(string id, BoothFetchResult<string> fetched)
    {
        if (fetched.Status == BoothFetchStatus.NotFound)
        {
            // 販売終了。categoryを観測できないだけで、アバターではないとは限らない
            return new AvatarRegistryEntry
            {
                ItemId = id,
                Category = null,
                CheckedAt = DateTimeOffset.Now,
            };
        }

        if (!fetched.IsSuccess || fetched.Value is null
            || BoothItemMapper.TryMap(fetched.Value, DateTimeOffset.Now, itemId: id) is not { } booth)
        {
            return null;
        }

        var aliases = string.Equals(booth.Category?.Name, AvatarCategory, StringComparison.Ordinal)
            ? BuildAliasesFromTags(booth)
            : [];

        return new AvatarRegistryEntry
        {
            ItemId = id,
            BoothName = booth.Name,
            ShopName = booth.Shop?.Name,
            Category = booth.Category?.Name,
            CheckedAt = DateTimeOffset.Now,
            // 別名はアバターにだけ持たせる。依存ツールの名前で照合しても意味が無い
            Aliases = aliases,
            // 1枚目のURLは、ここで取ったJSONに入っている。控えておけば絵を取るときに問い合わせ直さずに済む（U18）
            ImageUrl = booth.Images.FirstOrDefault()?.OriginalUrl ?? string.Empty,
        };
    }

    private sealed record ItemScan
    {
        public required DescriptionScan Description { get; init; }

        public required IReadOnlyCollection<string> FromTags { get; init; }

        public required IReadOnlyCollection<string> FromVariations { get; init; }

        public required IReadOnlyList<string> BaseNames { get; init; }

        /// <summary>h2 を使わずに書かれた対応の一覧（本文中の対応行・平文・長い一覧）から拾ったID。</summary>
        public IReadOnlyList<string> SupportLists { get; init; } = [];
    }

    /// <summary>
    /// 商品ごとの走査の結果の控え。**検出を画面・取り込み・起動時の見直しから何度走らせても、変わっていない商品は走査し直さない。**
    ///
    /// 走査（説明の正規表現・名前の照合）は検出の時間の大半で、2000件（stress-realcat・説明は h2 から作った物）で
    /// 1回約2秒・1.1GB を割り当てていた。取り込みの③は新しい商品が1件でも全商品を走査し直していた。
    ///
    /// 鍵は「その商品が走査に渡す物が全部同じか」：
    /// <list type="bullet">
    /// <item>booth（タグ・説明・種類の名前）は**参照で**比べる。商品の写し（<see cref="ItemRepository"/>）は変わっていないファイルに同じ物を返し、
    /// local だけを書いたときも <c>with</c> で booth の参照を引き継ぐ。取り直した・手で直した booth は別の物になる。</item>
    /// <item>購入した種類の名前（local の側）は中身で比べる。</item>
    /// <item>説明HTMLはファイルの大きさと更新日時。</item>
    /// <item>全商品に共通の分（索引の中身・素体・見出しの設定）は <see cref="ScanContextOf"/>。</item>
    /// </list>
    /// 検出の錠（<see cref="_detectGate"/>）の中でだけ触る。
    /// </summary>
    private readonly Dictionary<string, KeptScan> _scans = new(StringComparer.Ordinal);

    private sealed record KeptScan(
        BoothBlock Booth,
        IReadOnlyList<string> VariationNames,
        (long Length, DateTime LastWriteUtc)? Html,
        string Context,
        ItemScan Scan);

    private ItemScan ScanItemCached(
        ItemRecord item,
        AvatarNameIndex index,
        IEnumerable<AvatarBaseGroup> groups,
        string context,
        IReadOnlyDictionary<string, (long Length, DateTime LastWriteUtc)> htmlStamps)
    {
        var variationNames = VariationNamesOf(item);
        (long, DateTime)? html = htmlStamps.TryGetValue(item.Id, out var stamp) ? stamp : null;

        if (_scans.TryGetValue(item.Id, out var kept)
            && ReferenceEquals(kept.Booth, item.Booth)
            && kept.Html == html
            && string.Equals(kept.Context, context, StringComparison.Ordinal)
            && kept.VariationNames.SequenceEqual(variationNames, StringComparer.Ordinal))
        {
            return kept.Scan;
        }

        var scan = ScanItem(item, variationNames, index, groups);
        _scans[item.Id] = new KeptScan(item.Booth, variationNames, html, context, scan);
        return scan;
    }

    /// <summary>
    /// 全商品に共通の鍵。索引は中身（<see cref="AvatarNameIndex.Fingerprint"/>）、素体と見出しの設定は書いたままの形で比べる。
    /// 素体は照合（<see cref="AvatarDetector.ScanBaseDeclarations(AvatarDetector.ParsedDescription, IEnumerable{string}, IEnumerable{string}, IEnumerable{AvatarBaseGroup}, IReadOnlyList{string})"/>）に渡す物そのものを JSON にして比べる。
    /// </summary>
    private string ScanContextOf(AvatarNameIndex index, IEnumerable<AvatarBaseGroup> groups)
    {
        var text = new System.Text.StringBuilder()
            .Append(index.Fingerprint).Append('\u0001')
            .Append(System.Text.Json.JsonSerializer.Serialize(groups.ToList(), JsonStore.Options)).Append('\u0001')
            .AppendJoin('\u0002', SupportHeadings).Append('\u0001')
            .AppendJoin('\u0002', _settings.AvatarIgnoredHeadings)
            .ToString();
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>説明HTMLの大きさと更新日時を、1回の列挙でまとめて取る（1件ずつ問い合わせない）。</summary>
    private Dictionary<string, (long Length, DateTime LastWriteUtc)> HtmlStamps()
    {
        const string suffix = ".h2.html";
        var stamps = new Dictionary<string, (long, DateTime)>(StringComparer.Ordinal);
        var directory = _store.Paths.ItemsDir;
        if (!Directory.Exists(directory))
        {
            return stamps;
        }

        try
        {
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*" + suffix))
            {
                stamps[file.Name[..^suffix.Length]] = (file.Length, file.LastWriteTimeUtc);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 列挙できなければ控えは使わずに走査する（空のままなら全件が「HTML無し」と比べられ、読み直しになる）
            stamps.Clear();
        }

        return stamps;
    }

    /// <summary>もう無い商品の控えを捨てる。</summary>
    private void PruneScans(IReadOnlyList<ItemRecord> items)
    {
        var present = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in _scans.Keys.Where(id => !present.Contains(id)).ToList())
        {
            _scans.Remove(gone);
        }
    }

    // 購入したvariationがあればそれを先に見る。買った版がそのままアバター名になっている
    private static List<string> VariationNamesOf(ItemRecord item)
        => item.Local.Purchases
            .Select(purchase => purchase.NameSnapshot)
            .Concat(item.Booth.Variations.Select(variation => variation.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();

    private ItemScan ScanItem(
        ItemRecord item,
        IReadOnlyList<string> variationNames,
        AvatarNameIndex index,
        IEnumerable<AvatarBaseGroup> groups)
    {
        var html = ReadHtml(item.Id);

        var description = AvatarDetector.ScanDescription(
            html, item.Id, SupportHeadings, _settings.AvatarIgnoredHeadings);

        var (fromTags, fromVariations) = AvatarDetector.ScanNames(index, item.Booth.Tags, variationNames);

        // 見出しで区切るのは1回だけにして、素体の宣言と対応の一覧の両方で使う
        var parsed = AvatarDetector.Parse(html, item.Booth.Description);

        return new ItemScan
        {
            Description = description,
            FromTags = fromTags,
            FromVariations = fromVariations,
            BaseNames = AvatarDetector.ScanBaseTags(item.Booth.Tags)
                .Concat(AvatarDetector.ScanBaseDeclarations(
                    parsed, item.Booth.Tags, variationNames, groups, SupportHeadings))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            SupportLists = AvatarDetector.ScanLists(
                parsed, item.Id, index, SupportHeadings, _settings.AvatarIgnoredHeadings),
        };
    }

    /// <summary>
    /// 対応を宣言する見出し。**設定に書かれている物をそのまま使う**（ユーザ判断 2026-09-21・G13）。
    ///
    /// 前は既定の語を必ず混ぜていたので、`settings.json` から消しても次の検出で戻り、
    /// **足すことしかできなかった**（対になる「読まない見出し」は消せるので、作りが揃っていなかった）。
    /// 混ぜていたのは「既定に語を足したときに今の利用者へ届ける」ためだが、
    /// 公開前なので古い設定に合わせる必要は無い（`docs/spec/data-model.md`）。
    /// 欄ごと消した・壊れた場合は、読むときに空として受けるので既定に戻す。
    /// </summary>
    private IReadOnlyList<string> SupportHeadings
        => _settings.AvatarSupportHeadings.Count > 0
            ? _settings.AvatarSupportHeadings
            : AppSettings.DefaultAvatarSupportHeadings;

    private string? ReadHtml(string itemId)
    {
        try
        {
            var path = _store.Paths.ItemHtmlFile(itemId);
            return File.Exists(path) ? Storage.JsonStore.ReadText(path) : null;
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
                Name = AvatarNames.ShownName(entry),
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

        // 説明文のリンク（要確認）より先に入れる。同じアバターが両方に出たら、確定の方を残す
        foreach (var id in scan.SupportLists)
        {
            Add(id, AvatarLinkSource.SupportList, confirmed: true);
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
        var byText = FirstWins.Map(existing, alias => alias.Text, StringComparer.CurrentCultureIgnoreCase);

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

    /// <summary>
    /// 検出の結果を、書く直前の登録簿に重ねる（U15）。
    ///
    /// 検出は始めに登録簿を読み（<paramref name="snapshot"/>）、数十分かけて結果を組み立てる
    /// （友人データの初回で約37分）。その間に人が保存した表示名・メモ・所有・素体・判定の上書き・
    /// 別名の印は <paramref name="latest"/> にしか無い。**検出が受け持つのは BOOTH から観測した事実と、
    /// 毎回数え直す値だけ**なので、それだけを重ねる。
    /// <see cref="IsAvatar"/> と同じく規則だけの関数なので公開している。
    /// </summary>
    public static AvatarRegistry MergeDetected(
        AvatarRegistry latest,
        AvatarRegistry snapshot,
        IReadOnlyDictionary<string, AvatarRegistryEntry> detected,
        IReadOnlyDictionary<string, AvatarBaseGroup> detectedGroups)
    {
        var before = new Dictionary<string, AvatarRegistryEntry>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Entries)
        {
            before.TryAdd(entry.ItemId, entry);
        }

        var entries = new Dictionary<string, AvatarRegistryEntry>(StringComparer.Ordinal);
        foreach (var entry in latest.Entries)
        {
            entries.TryAdd(entry.ItemId, entry);
        }

        foreach (var (id, found) in detected)
        {
            if (!entries.TryGetValue(id, out var current))
            {
                entries[id] = found;
                continue;
            }

            entries[id] = Overlay(current, before.GetValueOrDefault(id), found) with { SeenAs = found.SeenAs };
        }

        // 素体のグループは、検出が新しく作ったものだけを足す。
        // 写しにあって最新に無いものは、その間に人が消したか改名したもの
        var beforeGroups = snapshot.BaseGroups.Select(group => group.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var groups = new Dictionary<string, AvatarBaseGroup>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var group in latest.BaseGroups)
        {
            groups.TryAdd(group.Name, group);
        }

        foreach (var (name, group) in detectedGroups)
        {
            if (!groups.ContainsKey(name) && !beforeGroups.Contains(name))
            {
                groups[name] = group;
            }
        }

        return new AvatarRegistry
        {
            DetectedAt = DateTimeOffset.Now,
            Entries = entries.Values.OrderBy(entry => entry.ItemId, StringComparer.Ordinal).ToList(),
            BaseGroups = groups.Values.OrderBy(group => group.Name, StringComparer.CurrentCulture).ToList(),
        };
    }

    /// <summary>
    /// 検出の途中で、問い合わせて分かった項目だけを書く直前の登録簿に重ねる（ユーザ判断 2026-09-29）。
    ///
    /// <see cref="MergeDetected"/> と同じ重ね方（人が付けた名前・メモ・素体は最新のまま）で、
    /// seenAs・素体・<see cref="AvatarRegistry.DetectedAt"/> には触れない。それらは全商品を読み終えた値なので、
    /// 途中で書くと中途半端な数が残り、検出を終えたように見えて起動時の検出し直しも遅れる。
    /// </summary>
    public static AvatarRegistry MergeFetched(
        AvatarRegistry latest,
        AvatarRegistry snapshot,
        IReadOnlyDictionary<string, AvatarRegistryEntry> fetched)
    {
        var before = new Dictionary<string, AvatarRegistryEntry>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Entries)
        {
            before.TryAdd(entry.ItemId, entry);
        }

        var entries = new Dictionary<string, AvatarRegistryEntry>(StringComparer.Ordinal);
        foreach (var entry in latest.Entries)
        {
            entries.TryAdd(entry.ItemId, entry);
        }

        foreach (var (id, found) in fetched)
        {
            entries[id] = entries.TryGetValue(id, out var current)
                ? Overlay(current, before.GetValueOrDefault(id), found)
                : found;
        }

        return new AvatarRegistry
        {
            DetectedAt = latest.DetectedAt,
            Entries = entries.Values.OrderBy(entry => entry.ItemId, StringComparer.Ordinal).ToList(),
            BaseGroups = latest.BaseGroups,
        };
    }

    /// <summary>BOOTH から観測した値だけを、最新の項目に重ねる。</summary>
    private static AvatarRegistryEntry Overlay(AvatarRegistryEntry current, AvatarRegistryEntry? old, AvatarRegistryEntry found)
    {
        // 検出が値を変えたときだけ重ねる。変えていなければ、その間に
        // 「BOOTHに確認し直す」で入った値かもしれないので最新を残す
        T Observed<T>(Func<AvatarRegistryEntry, T> field)
            => old is not null && EqualityComparer<T>.Default.Equals(field(found), field(old))
                ? field(current)
                : field(found);

        return current with
        {
            BoothName = Observed(entry => entry.BoothName),
            ShopName = Observed(entry => entry.ShopName),
            Category = Observed(entry => entry.Category),
            CheckedAt = Observed(entry => entry.CheckedAt),
            // 検出の途中で裏の取得（AvatarImageSync）が書いたURLを消さない
            ImageUrl = Observed(entry => entry.ImageUrl),
            Aliases = MergeDetectedAliases(current.Aliases, found.Aliases),
        };
    }

    /// <summary>
    /// 別名を重ねる。数は検出が数え直した値、消した印と出所は人が触った最新の方を使う。
    /// 手で足した別名は検出が作らないので、最新に無ければ、その間に人が消したものとして戻さない。
    /// </summary>
    private static IReadOnlyList<AvatarAlias> MergeDetectedAliases(
        IReadOnlyList<AvatarAlias> current,
        IReadOnlyList<AvatarAlias> found)
    {
        var now = new Dictionary<string, AvatarAlias>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var alias in current)
        {
            now.TryAdd(alias.Text, alias);
        }

        var merged = new Dictionary<string, AvatarAlias>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var alias in found)
        {
            if (now.TryGetValue(alias.Text, out var kept))
            {
                merged[alias.Text] = alias with { Rejected = kept.Rejected, Source = kept.Source };
            }
            else if (!string.Equals(alias.Source, nameof(AvatarLinkSource.Manual), StringComparison.Ordinal))
            {
                merged[alias.Text] = alias;
            }
        }

        foreach (var alias in current)
        {
            merged.TryAdd(alias.Text, alias);
        }

        return merged.Values.OrderByDescending(alias => alias.Count).ToList();
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

