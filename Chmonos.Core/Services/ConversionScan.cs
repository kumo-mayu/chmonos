using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

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

    /// <summary>手元の全商品を見て、変換扱いの推した値を書き直す。書いた商品の数を返す。</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var registry = store.Avatars.Load();
        var compatibility = AvatarCompatibilityIndex.For(registry);
        var written = 0;

        foreach (var item in loaded.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profiles = MayHoldProfiles(item) ? ProfilesOf(item) : [];

            // プロファイルが無くなった（zip を外した）商品も、前に推した確認待ちの値を消すために通す
            if (profiles.Count == 0 && item.Local.Conversion is not { Targets.Count: > 0 })
            {
                continue;
            }

            var suggestions = ConversionInference.Suggest(item, profiles, registry, compatibility);
            if (await store.Items.ChangeLocalAsync(
                item.Id,
                local => Apply(local.Conversion, suggestions) is var next && next != local.Conversion ? local with { Conversion = next } : null,
                LocalOwners.Conversion,
                cancellationToken))
            {
                written++;
            }
        }

        return written;
    }

    /// <summary>
    /// 今の変換扱いに、推した値を当てる。確認したつなぎ先は残し、確認待ちの物は今回の推した値に置き換える
    /// （同じプロファイル（slug）に確認したつなぎ先があれば、その slug は推さない）。何も決めていない形になれば null（書き出さない）。
    /// 変わらなければ、渡された値をそのまま返す（書かずに済ませるため）。
    /// </summary>
    internal static ConversionSetting? Apply(ConversionSetting? current, IReadOnlyList<ConversionSuggestion> suggestions)
    {
        var confirmed = current?.Targets.Where(target => target.Confirmed).ToList() ?? [];
        var confirmedSlugs = confirmed
            .Where(target => target.Slug is not null)
            .Select(target => target.Slug!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var suggested = suggestions
            .Where(suggestion => suggestion.Target is not null && !confirmedSlugs.Contains(suggestion.Profile.Slug))
            .Select(suggestion => suggestion.Target!)
            .ToList();

        var next = new ConversionSetting { Mode = current?.Mode ?? ConversionMode.Auto, Targets = [.. confirmed, .. suggested] };
        if (next.IsDefault)
        {
            return current is null ? null : current.IsDefault ? current : null;
        }

        return current is not null && current.Mode == next.Mode && current.Targets.SequenceEqual(next.Targets) ? current : next;
    }
}
