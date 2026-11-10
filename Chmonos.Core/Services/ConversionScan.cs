using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>プロファイル1つが入っている所：unitypackage の名前と、それを含む手元のファイル（zip か unitypackage そのもの）の名前。</summary>
public sealed record ProfileSource(string Slug, string PackageName, string FileName);

/// <summary>
/// 手元の商品の中から変換ツールのプロファイルを見つけ、つなぎ先を推して、変換扱いに**確認待ち**として書く（<c>docs/spec/conversion.md</c>「判定」）。
///
/// **対象はプロファイルのファイルを持っている商品だけ**（ユーザ判断 2026-11-06：手元に無いと意味を成さない）。「持っている」は unitypackage の控えがあること
/// （zip を後で動かした・消した物も、控えがあれば数える）。BOOTH のタグ・商品名だけでは判定しない。
/// 中のパスは取り込みの裏で控え（<c>unitypackages/&lt;ハッシュ&gt;.json</c>）に書いてあるので、zip は開かない。
/// 入る先の一番上（<see cref="UnityPackageSummary.Roots"/>）にプロファイルの場所がある物だけ控えを読む（ライブラリ全体の控えは読まない）。
///
/// 人が決めた（確認した）つなぎ先は残し、確認待ちの物だけを書き直す。推せなかった（食い違った）物は書かない（画面で人が選ぶ）。
/// </summary>
public sealed class ConversionScan(DataStore store, UnityPackagePathStore packages)
{
    /// <summary>入る先の一番上から、プロファイルを持つかもしれない商品か（控えを読む前のふるい）。</summary>
    public static bool MayHoldProfiles(ItemRecord item)
        => item.Local.LocalFiles.Any(file => !file.Detached
            && file.UnityPackages is { } summaries
            && ProfileFiles.MayHoldProfiles(summaries.SelectMany(summary => summary.Roots)));

    /// <summary>この商品の手元のファイルの中にあるプロファイル（slug ごと）。控えの無い物は数えない。</summary>
    public IReadOnlyList<ProfileSlug> ProfilesOf(ItemRecord item)
    {
        var paths = new List<string>();
        foreach (var file in item.Local.LocalFiles)
        {
            if (file.Detached
                || file.UnityPackages is not { } summaries
                || !ProfileFiles.MayHoldProfiles(summaries.SelectMany(summary => summary.Roots))
                || packages.Load(file.Hash) is not { } stored)
            {
                continue;
            }

            paths.AddRange(stored.Values.SelectMany(assets => assets).Select(asset => asset.Path));
        }

        return ProfileFiles.Scan(paths);
    }

    /// <summary>
    /// プロファイルの <c>avatar_data</c> の欄を、まだ控えていなければ zip から読んで控える（docs/spec/conversion.md「データの形」）。
    /// 前の版で取り込んだプロファイルにも、見回りのときに1回だけ読む（ユーザ判断 2026-11-06：裏で1回読み直す）。読んで何も無ければ空を控えて、もう読まない。
    /// zip が手元に無い物・開けない物は控えない（次の見回りでまた試す）。
    /// </summary>
    public async Task EnsureProfileDataAsync(ItemRecord item, CancellationToken cancellationToken = default)
    {
        foreach (var file in item.Local.LocalFiles)
        {
            if (file.Detached
                || file.UnityPackages is not { } summaries
                || !ProfileFiles.MayHoldProfiles(summaries.SelectMany(summary => summary.Roots))
                || packages.Load(file.Hash) is not { } stored
                || packages.LoadProfiles(file.Hash) is not null)
            {
                continue;
            }

            if (!ProfileDataReader.HasAvatarData(stored))
            {
                await packages.SaveProfilesAsync(file.Hash, new Dictionary<string, ProfileAvatarData>(), cancellationToken);
                continue;
            }

            var zip = ArchiveFiles.FirstExistingZip(file.Paths);
            if (zip is not null && ProfileDataReader.Read(zip, stored) is { } read)
            {
                await packages.SaveProfilesAsync(file.Hash, read, cancellationToken);
            }
        }
    }

    /// <summary>
    /// FBX の場所 → それを中身に持つアバターの商品 ID（手掛かり (b) の照らし先）。アバターとして扱う登録簿の行のうち、
    /// 手元に商品があり、中身の控えがある物だけ（持っていないアバターは FBX の場所が分からない）。
    /// </summary>
    public IReadOnlyDictionary<string, string> FbxIndex(AvatarRegistry registry, Func<string, ItemRecord?> findItem)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in registry.Entries.Where(AvatarService.IsAvatar))
        {
            if (findItem(entry.ItemId) is not { } avatarItem)
            {
                continue;
            }

            foreach (var file in avatarItem.Local.LocalFiles.Where(file => !file.Detached && file.UnityPackages is { Count: > 0 }))
            {
                if (packages.Load(file.Hash) is not { } stored)
                {
                    continue;
                }

                foreach (var asset in stored.Values.SelectMany(assets => assets))
                {
                    if (asset.Path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    {
                        index.TryAdd(asset.Path.Replace('\\', '/'), entry.ItemId);
                    }
                }
            }
        }

        return index;
    }

    /// <summary>この商品のプロファイル（slug）ごとに、FBX の場所が中身にあるアバター（手掛かり (b)）。控えた欄だけを見る（zip は開かない）。</summary>
    public IReadOnlyDictionary<string, string> FbxAvatarsOf(ItemRecord item, IReadOnlyDictionary<string, string> fbxIndex)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (fbxIndex.Count == 0)
        {
            return result;
        }

        foreach (var file in item.Local.LocalFiles.Where(file => !file.Detached))
        {
            foreach (var (slug, data) in packages.LoadProfiles(file.Hash) ?? new Dictionary<string, ProfileAvatarData>())
            {
                if (data.DefaultFbxPath is { } fbx && fbxIndex.TryGetValue(fbx, out var avatar))
                {
                    result.TryAdd(slug, avatar);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// プロファイル（slug）ごとに、入っている unitypackage と、それを含む手元のファイルの名前（ユーザ指示 2026-11-09：
    /// アバターとプロファイルのセット商品では、アバター扱いと変換扱いのどちらの話か分かりにくいので、どのファイルの物かを添える）。
    /// 同じ slug が複数の unitypackage に入っていれば（FBX の場所だけ違えて配る物）全部返す。
    /// </summary>
    public IReadOnlyList<ProfileSource> ProfileSourcesOf(ItemRecord item)
    {
        var sources = new List<ProfileSource>();
        foreach (var file in item.Local.LocalFiles)
        {
            if (file.Detached
                || file.UnityPackages is not { } summaries
                || !ProfileFiles.MayHoldProfiles(summaries.SelectMany(summary => summary.Roots))
                || packages.Load(file.Hash) is not { } stored)
            {
                continue;
            }

            var fileName = file.Paths.Count > 0 ? Path.GetFileName(file.Paths[0]) : string.Empty;
            foreach (var (entry, assets) in stored)
            {
                var packageName = Path.GetFileName(entry.Replace('\\', '/').TrimEnd('/'));
                foreach (var profile in ProfileFiles.Scan(assets.Select(asset => asset.Path)))
                {
                    sources.Add(new ProfileSource(profile.Slug, packageName, fileName));
                }
            }
        }

        return sources;
    }

    /// <summary>
    /// 手元の全商品を見て、変換扱いの推した値を書き直す。書いた商品の ID を返す
    /// （起動時は検索が先に全件を読んでいるので、呼び手が写しへ知らせる。知らせないと、開いた商品ページに推した値が出ない）。
    /// </summary>
    public async Task<IReadOnlyList<string>> RunAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var registry = store.Avatars.Load();
        var compatibility = AvatarCompatibilityIndex.For(registry);

        // FBX の場所の照らし先は、プロファイルの商品が1つでもあるときだけ作る（アバターの中身の控えを読むので）
        // 同じ ID が2件あると ToDictionary が投げるので、先の1件を使う（改変の画面の組み方と同じ）
        var byId = loaded.Items.GroupBy(item => item.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var fbxIndex = new Lazy<IReadOnlyDictionary<string, string>>(() => FbxIndex(registry, id => byId.GetValueOrDefault(id)));
        var written = new List<string>();
        var rooted = 0;
        var unreadable = 0;

        foreach (var item in loaded.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mayHold = MayHoldProfiles(item);
            var profiles = mayHold ? ProfilesOf(item) : [];
            if (profiles.Count > 0)
            {
                await EnsureProfileDataAsync(item, cancellationToken);
            }

            // 置き場はあるのにプロファイルのファイルが1つも見つからない商品を数える（ファイル名の決まりが変わった疑い）。
            // 中身の控えが無い物は、読めていないだけなので数えない
            if (mayHold && HasStoredPaths(item))
            {
                rooted++;
                if (profiles.Count == 0)
                {
                    unreadable++;
                }
            }

            // プロファイルが無くなった（zip を外した）商品も、前に推した確認待ちの値を消すために通す
            if (profiles.Count == 0 && item.Local.Conversion is not { Targets.Count: > 0 })
            {
                continue;
            }

            var suggestions = ConversionInference.Suggest(item, profiles, registry, compatibility, FbxAvatarsOf(item, fbxIndex.Value));
            if (await store.Items.ChangeLocalAsync(
                item.Id,
                local => Apply(local.Conversion, suggestions) is var next && next != local.Conversion ? local with { Conversion = next } : null,
                LocalOwners.Conversion,
                cancellationToken))
            {
                written.Add(item.Id);
            }
        }

        await UpdateUnreadableAlertAsync(rooted, unreadable, cancellationToken);
        return written;
    }

    /// <summary>
    /// 帯の知らせを出すのは、置き場を含む商品が <see cref="UnreadableMinimum"/> 件以上で、その半分以上でプロファイルが見つからないとき（ユーザ判断 2026-11-09）。
    /// 変換ツールの本体のように、置き場はあってもプロファイルを含まない商品が少しはあるので、1件だけでは鳴らさない。
    /// 前の件数を覚えておかずに済み、初めて取り込んだときにも気付ける
    /// </summary>
    internal const int UnreadableMinimum = 3;

    private const string UnreadableAlertId = "profile-files";

    private bool HasStoredPaths(ItemRecord item)
        => item.Local.LocalFiles.Any(file => !file.Detached && file.UnityPackages is { Count: > 0 } && packages.Load(file.Hash) is not null);

    /// <summary>ナビの「設定」の上の帯の知らせを出す・消す（読めるようになれば自分で消える。BOOTH の作りの変化の知らせと同じ）。</summary>
    private Task UpdateUnreadableAlertAsync(int rooted, int unreadable, CancellationToken cancellationToken)
    {
        var suspect = unreadable >= UnreadableMinimum && unreadable * 2 >= rooted;
        var detail = $"変換ツールのフォルダを含む {rooted} 件のうち {unreadable} 件で、プロファイルのファイルが見つかりませんでした。";
        return store.Notifications.TryUpdateAsync(
            records =>
            {
                var existing = records.FindIndex(record => record.Id == UnreadableAlertId);
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
                        Id = UnreadableAlertId,
                        Kind = NotificationKind.ProfileFilesUnreadable,
                        Title = "変換ツールのプロファイルを読み取れなくなったかもしれません",
                        Detail = detail,
                        CreatedAt = DateTimeOffset.Now,
                        IsStrong = true,
                    });
                    return records;
                }

                if (existing < 0 || records[existing].IsResolved)
                {
                    return null;
                }

                records[existing] = records[existing] with { IsResolved = true };
                return records;
            },
            cancellationToken);
    }

    /// <summary>
    /// 今の変換扱いに、推した値を当てる。人が決めた行（確認した・外した）は残し、確認待ちの物は今回の推した値に置き換える
    /// （同じプロファイル（slug）に確認したつなぎ先があれば、その slug は推さない。人が外したつなぎ先は、同じ物を推さない）。
    /// 何も決めていない形になれば null（書き出さない）。変わらなければ、渡された値をそのまま返す（書かずに済ませるため）。
    /// </summary>
    internal static ConversionSetting? Apply(ConversionSetting? current, IReadOnlyList<ConversionSuggestion> suggestions)
    {
        var decided = current?.Targets.Where(target => target.Confirmed || target.Rejected).ToList() ?? [];
        var confirmedSlugs = decided
            .Where(target => !target.Rejected && target.Slug is not null)
            .Select(target => target.Slug!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rejected = decided.Where(target => target.Rejected).ToList();

        var suggested = suggestions
            .Where(suggestion => suggestion.Target is { } target
                && !confirmedSlugs.Contains(suggestion.Profile.Slug)
                && !rejected.Any(gone => ConversionTarget.SameRow(gone, target)))
            .Select(suggestion => suggestion.Target!)
            .ToList();

        var next = new ConversionSetting { Mode = current?.Mode ?? ConversionMode.Auto, Targets = [.. decided, .. suggested] };
        if (next.IsDefault)
        {
            return current is null ? null : current.IsDefault ? current : null;
        }

        return current is not null && current.Mode == next.Mode && current.Targets.SequenceEqual(next.Targets) ? current : next;
    }
}
