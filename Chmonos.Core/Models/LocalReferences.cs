using Chmonos.Core.Services;

namespace Chmonos.Core.Models;

/// <summary>参照の種類。<see cref="LocalReferences"/> の表の列。</summary>
public enum LocalReferenceKind
{
    /// <summary>別の商品の ID（対応アバター・バリエーションの対応・変換のつなぎ先など）。</summary>
    ItemId,

    /// <summary>共通素体の名前。素体は商品 ID を持たないことがあるので名前で指す。</summary>
    BaseName,
}

/// <summary>
/// <c>local</c> の中で、別の商品 ID か共通素体の名前を持つ欄1つ。
/// </summary>
/// <param name="Path"><see cref="LocalBlock"/> からの欄の道（配列は <c>[]</c>）。表が古くなっていないかを試験で照らすための名前。</param>
/// <param name="Owner">書き換えるときに名乗る持ち主。名乗らないと、錠の中で今の値に重ねるときに書き換えた分が落ちる。</param>
/// <param name="Values">今持っている値。</param>
/// <param name="Rewrite">値を1つずつ置き換える。置き換えの関数が null を返した値は消す（素体の削除）。</param>
public sealed record LocalReferenceField(
    string Path,
    LocalReferenceKind Kind,
    LocalField Owner,
    Func<LocalBlock, IEnumerable<string>> Values,
    Func<LocalBlock, Func<string, string?>, LocalBlock> Rewrite);

/// <summary>
/// **別の商品 ID・共通素体の名前を持つ <c>local</c> の欄の表**（洗い出し 2026-11-10・D2・D3）。
///
/// 素体の改名・削除と、商品 ID の付け替えは、ここに載った欄だけを書き換える。前はどちらも1つの欄（<c>avatarBases</c>・<c>avatars</c>）
/// しか直しておらず、v1.3.0 で足した欄（バリエーションの対応・変換のつなぎ先・新しい版の向き先）が、消えた名前・ID を指したまま残った
/// （素体を改名すると変換経由が黙って効かなくなる）。欄を足したときに表へ載せ忘れないよう、<c>LocalBlock</c> の欄を全部列挙して
/// 「持つ」（<see cref="Fields"/>）か「持たない」（<see cref="HoldsNeither"/>）のどちらかに載っているかを試験で確かめる。
/// </summary>
public static class LocalReferences
{
    /// <summary>「持つ」欄。</summary>
    public static readonly IReadOnlyList<LocalReferenceField> Fields =
    [
        new(
            "Avatars[].AvatarItemId",
            LocalReferenceKind.ItemId,
            LocalField.Avatars,
            local => local.Avatars.Select(link => link.AvatarItemId),

            // 同じアバターへの行が2つになっても1つに畳まない。手で消した印（Rejected）と確認済みのどちらを残すかを、ここでは決められない
            (local, map) => local with
            {
                Avatars = [.. local.Avatars
                    .Select(link => map(link.AvatarItemId) is { } id ? link with { AvatarItemId = id } : null)
                    .OfType<AvatarLink>()],
            }),
        new(
            "AvatarBases[].BaseName",
            LocalReferenceKind.BaseName,
            LocalField.AvatarBases,
            local => local.AvatarBases.Select(link => link.BaseName),
            (local, map) => local with
            {
                AvatarBases = [.. local.AvatarBases
                    .Select(link => map(link.BaseName) is { } name ? link with { BaseName = name } : null)
                    .OfType<AvatarBaseLink>()
                    .DistinctBy(link => link.BaseName, StringComparer.CurrentCultureIgnoreCase)],
            }),
        new(
            "VariationTargets[].Avatars",
            LocalReferenceKind.ItemId,
            LocalField.VariationTargets,
            local => local.VariationTargets.SelectMany(target => target.Avatars),
            (local, map) => local with { VariationTargets = MapTargets(local.VariationTargets, target => target with { Avatars = MapIds(target.Avatars, map) }) }),
        new(
            "VariationTargets[].Bases",
            LocalReferenceKind.BaseName,
            LocalField.VariationTargets,
            local => local.VariationTargets.SelectMany(target => target.Bases),
            (local, map) => local with { VariationTargets = MapTargets(local.VariationTargets, target => target with { Bases = MapNames(target.Bases, map) }) }),
        new(
            "VariationTargets[].RejectedAvatars",
            LocalReferenceKind.ItemId,
            LocalField.VariationTargets,
            local => local.VariationTargets.SelectMany(target => target.RejectedAvatars ?? []),
            (local, map) => local with
            {
                VariationTargets = MapTargets(local.VariationTargets, target => target with { RejectedAvatars = NullIfEmpty(MapIds(target.RejectedAvatars, map)) }),
            }),
        new(
            "VariationTargets[].RejectedBases",
            LocalReferenceKind.BaseName,
            LocalField.VariationTargets,
            local => local.VariationTargets.SelectMany(target => target.RejectedBases ?? []),
            (local, map) => local with
            {
                VariationTargets = MapTargets(local.VariationTargets, target => target with { RejectedBases = NullIfEmpty(MapNames(target.RejectedBases, map)) }),
            }),

        // 検出の値は次の検出で作り直されるが、それまでの間も消えた名前・ID を指したままにしない（検索と相性はこの値も読む）
        new(
            "DetectedVariationTargets[].Avatars",
            LocalReferenceKind.ItemId,
            LocalField.DetectedVariationTargets,
            local => local.DetectedVariationTargets.SelectMany(target => target.Avatars),
            (local, map) => local with
            {
                DetectedVariationTargets = MapTargets(local.DetectedVariationTargets, target => target with { Avatars = MapIds(target.Avatars, map) }),
            }),
        new(
            "DetectedVariationTargets[].Bases",
            LocalReferenceKind.BaseName,
            LocalField.DetectedVariationTargets,
            local => local.DetectedVariationTargets.SelectMany(target => target.Bases),
            (local, map) => local with
            {
                DetectedVariationTargets = MapTargets(local.DetectedVariationTargets, target => target with { Bases = MapNames(target.Bases, map) }),
            }),
        new(
            "DetectedVariationTargets[].RejectedAvatars",
            LocalReferenceKind.ItemId,
            LocalField.DetectedVariationTargets,
            local => local.DetectedVariationTargets.SelectMany(target => target.RejectedAvatars ?? []),
            (local, map) => local with
            {
                DetectedVariationTargets = MapTargets(local.DetectedVariationTargets, target => target with { RejectedAvatars = NullIfEmpty(MapIds(target.RejectedAvatars, map)) }),
            }),
        new(
            "DetectedVariationTargets[].RejectedBases",
            LocalReferenceKind.BaseName,
            LocalField.DetectedVariationTargets,
            local => local.DetectedVariationTargets.SelectMany(target => target.RejectedBases ?? []),
            (local, map) => local with
            {
                DetectedVariationTargets = MapTargets(local.DetectedVariationTargets, target => target with { RejectedBases = NullIfEmpty(MapNames(target.RejectedBases, map)) }),
            }),

        // つなぎ先が消えた行は消す（つなぎ先の無い行は持たない）。改名で同じ行が2つになったら前の方を残す
        new(
            "Conversion.Targets[].AvatarItemId",
            LocalReferenceKind.ItemId,
            LocalField.Conversion,
            local => local.Conversion?.Targets.Select(target => target.AvatarItemId).OfType<string>() ?? [],
            (local, map) => local with
            {
                Conversion = MapConversion(local.Conversion, target => target.AvatarItemId is { } id
                    ? map(id) is { } mapped ? target with { AvatarItemId = mapped } : null
                    : target),
            }),
        new(
            "Conversion.Targets[].BaseName",
            LocalReferenceKind.BaseName,
            LocalField.Conversion,
            local => local.Conversion?.Targets.Select(target => target.BaseName).OfType<string>() ?? [],
            (local, map) => local with
            {
                Conversion = MapConversion(local.Conversion, target => target.BaseName is { } name
                    ? map(name) is { } mapped ? target with { BaseName = mapped } : null
                    : target),
            }),
        new(
            "ContentUpdate.ForAvatars",
            LocalReferenceKind.ItemId,
            LocalField.ContentUpdate,
            local => local.ContentUpdate?.ForAvatars ?? [],
            (local, map) => local.ContentUpdate is { ForAvatars: { } avatars } update
                ? local with { ContentUpdate = update with { ForAvatars = KeepTargeted(avatars, MapIds(avatars, map)) } }
                : local),
        new(
            "ContentUpdate.ForBases",
            LocalReferenceKind.BaseName,
            LocalField.ContentUpdate,
            local => local.ContentUpdate?.ForBases ?? [],
            (local, map) => local.ContentUpdate is { ForBases: { } bases } update
                ? local with { ContentUpdate = update with { ForBases = KeepTargeted(bases, MapNames(bases, map)) } }
                : local),
    ];

    /// <summary>
    /// 「持たない」欄（<see cref="LocalBlock"/> と中の型の、保存する欄のうち <see cref="Fields"/> に無い物）。
    /// 振る舞いには使わない。欄を足したら、どちらに載せるかを決めさせるための控え（試験 <c>LocalReferencesTests</c>）。
    /// </summary>
    public static readonly IReadOnlySet<string> HoldsNeither = new HashSet<string>(StringComparer.Ordinal)
    {
        "DisplayName",
        "Shop.Name",
        "Shop.Subdomain",
        "Shop.Url",
        "Category",
        "UserImages[].FileName",
        "UserImages[].Caption",
        "UserImages[].AddedAt",
        "ThumbnailImage",
        "ImageRoles",
        "UserTags[].Top",
        "UserTags[].Subs",
        "Attributes",
        "Memo",
        "Avatars[].Name",
        "Avatars[].Source",
        "Avatars[].Confirmed",
        "Avatars[].Rejected",
        "AvatarBases[].Source",
        "AvatarBases[].Confirmed",
        "AvatarBases[].Rejected",
        "AvatarsDetectedAt",
        "VariationTargets[].VariationId",
        "VariationTargets[].All",
        "DetectedVariationTargets[].VariationId",
        "DetectedVariationTargets[].All",
        "Conversion.Mode",
        "Conversion.Targets[].Forward",
        "Conversion.Targets[].Reverse",
        "Conversion.Targets[].Slug",
        "Conversion.Targets[].Confirmed",
        "Conversion.Targets[].Rejected",
        "ContentUpdate.FoundAt",
        "ContentUpdate.FilesBefore",
        "ContentUpdate.CheckedAt",
        "Purchases[].VariationId",
        "Purchases[].NameSnapshot",
        "Purchases[].Price",
        "Purchases[].Kind",
        "Purchases[].Note",
        "Purchases[].PurchasedAt",
        "Purchases[].ExistsOnBooth",
        "LocalFiles[].Hash",
        "LocalFiles[].Paths",
        "LocalFiles[].SizeBytes",
        "LocalFiles[].VariationId",
        "LocalFiles[].Contents",
        "LocalFiles[].UnityPackages[].Entry",
        "LocalFiles[].UnityPackages[].Roots",
        "LocalFiles[].Detached",
        "LocalFiles[].ArchiveBroken",
        "LocalFiles[].MissingSince",
        "LocalFiles[].Replaced.Path",
        "LocalFiles[].Replaced.At",
        "LocalFiles[].Volumes",
        "LocalFolders[].Path",
        "LocalFolders[].FileCount",
        "LocalFolders[].TotalBytes",
        "LocalFolders[].RegisteredAt",
        "LocalFolders[].LastSeenAt",
        "LocalFolders[].MissingSince",
        "LocalFolders[].UnityPackages",
        "LocalFolders[].Volume",
        "AcquiredAt",
        "NotifyOnUpdate",
        "IsHidden",
        "IsFavorite",
        "LastFetchedAt",
        "NextFetchDueAt",
        "ConsecutiveNotFoundCount",
        "IsDelisted",
    };

    /// <summary>その種類の欄を書き換えるときに名乗る持ち主。</summary>
    public static IReadOnlyCollection<LocalField> OwnersOf(LocalReferenceKind kind)
        => [.. Fields.Where(field => field.Kind == kind).Select(field => field.Owner).Distinct()];

    /// <summary>その種類の欄のどれかが、当てはまる値を持つか。</summary>
    public static bool Uses(LocalBlock local, LocalReferenceKind kind, Func<string, bool> matches)
        => Fields.Any(field => field.Kind == kind && field.Values(local).Any(matches));

    /// <summary>
    /// その種類の欄の、当てはまる値を <paramref name="replacement"/> に置き換える（null なら消す）。何も変わらなければ null（書かない）。
    /// </summary>
    public static LocalBlock? Rewrite(LocalBlock local, LocalReferenceKind kind, Func<string, bool> matches, string? replacement)
    {
        if (!Uses(local, kind, matches))
        {
            return null;
        }

        string? Map(string value) => matches(value) ? replacement : value;
        var result = Fields.Where(field => field.Kind == kind).Aggregate(local, (current, field) => field.Rewrite(current, Map));
        return LocalFields.Changed(local, result, OwnersOf(kind)).Count > 0 ? result : null;
    }

    /// <summary>商品 ID を比べる決まり。ID は数字の字なので字のまま比べる。</summary>
    public static Func<string, bool> SameItemId(string id) => value => string.Equals(value, id, StringComparison.Ordinal);

    /// <summary>素体の名前を比べる決まり。素体の一覧（<c>AvatarServiceEditing</c>）と同じく大文字・小文字を区別しない。</summary>
    public static Func<string, bool> SameBaseName(string name)
        => value => string.Equals(value, name, StringComparison.CurrentCultureIgnoreCase);

    private static List<string> MapIds(IReadOnlyList<string>? values, Func<string, string?> map)
        => [.. (values ?? []).Select(map).OfType<string>().Distinct(StringComparer.Ordinal)];

    private static List<string> MapNames(IReadOnlyList<string>? values, Func<string, string?> map)
        => [.. (values ?? []).Select(map).OfType<string>().Distinct(StringComparer.CurrentCultureIgnoreCase)];

    private static List<string>? NullIfEmpty(List<string> values) => values.Count == 0 ? null : values;

    /// <summary>
    /// 向き先が全部消えた行は落とす（空の行は持たない）。外した記録が残る行は、人が決めた行として残す（検出の行に勝つ）。
    /// </summary>
    private static List<VariationTarget> MapTargets(IReadOnlyList<VariationTarget> targets, Func<VariationTarget, VariationTarget> change)
        => [.. targets.Select(change).Where(target => !target.IsEmpty || target.HasRejections)];

    private static ConversionSetting? MapConversion(ConversionSetting? setting, Func<ConversionTarget, ConversionTarget?> change)
    {
        if (setting is null)
        {
            return null;
        }

        var targets = new List<ConversionTarget>();
        foreach (var target in setting.Targets.Select(change).OfType<ConversionTarget>())
        {
            if (!targets.Any(kept => ConversionEdits.IsSameRow(kept, target)))
            {
                targets.Add(target);
            }
        }

        var result = setting with { Targets = targets };
        return result.IsDefault ? null : result;
    }

    /// <summary>
    /// 新しい版の向き先（<see cref="ContentUpdate.ForAvatars"/>・<see cref="ContentUpdate.ForBases"/>）。
    /// **消して空になるなら、消さずに元のまま残す**——空（null）は「全部向け」なので、消えた素体だけ向けの更新が、
    /// どのアバターで使っていても促す更新に変わってしまう。消えた素体の名前を指したままなら、どの改変にも当たらない（今と同じ）。
    /// 改名では空にならない。
    /// </summary>
    private static IReadOnlyList<string>? KeepTargeted(IReadOnlyList<string> before, List<string> after)
        => after.Count == 0 ? before : after;
}
