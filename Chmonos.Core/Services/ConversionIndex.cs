using System.Runtime.CompilerServices;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>変換経由で使えるアバター1体の道筋（吹き出しで「「A」の逆方向と、このアバターの順方向のプロファイルで」と言うため）。</summary>
/// <param name="SourceAvatarItemId">元にできる対応アバター A（1体）。</param>
/// <param name="OtherSources">ほかに元にできる対応アバターの数（「ほか n 通り」）。</param>
/// <param name="Pair">
/// 人が足した組（<see cref="ConversionPair"/>）から来た道筋なら、その組。プロファイルから来た道筋は null。
/// 両方から来たアバターはプロファイルの道筋を言う（組を足す前からの言い方を変えない。<c>conversion.md</c>「人が足す組」）
/// </param>
public sealed record ConversionRoute(string SourceAvatarItemId, int OtherSources, ConversionPair? Pair = null);

/// <summary>
/// 変換経由で使えるアバター（<c>docs/spec/conversion.md</c>「使える、と見なす決まり」）。保存はせず、その場で計算する（素体経由と同じ）。
///
/// 衣装の対応 S（直接対応＋素体経由：<see cref="AvatarCompatibilityIndex.Resolve"/>）のどれか A について、A か A の素体に**逆方向**のプロファイルがあり、
/// アバター B か B の素体に**順方向**のプロファイルがあるとき、B は変換経由で使える（作者の使い方ガイド「衣装元は逆方向、変換先は順方向」）。
/// 素体経由は元の側に1回まで、変換は1回まで。
/// **つなぎ先を共通素体にしたプロファイルは素体の仲間全員に効き、アバターにした物はそのアバターにだけ効く**（ユーザ判断 2026-11-06）。
/// 使うのは手元にプロファイルを持っている商品の、変換扱いのつなぎ先だけ（「プロファイルとして扱わない」にした商品は使わない）。
/// これに、人が足した組（<see cref="ConversionPair"/>。元が S のどれかに当たれば先が使える）の道筋を足す（ユーザ判断 2026-11-20）。
/// </summary>
public sealed class ConversionIndex
{
    private readonly AvatarCompatibilityIndex _compatibility;
    private readonly HashSet<string> _reverseAvatars = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reverseBases = new(AvatarBaseNames.Comparer);
    private readonly HashSet<string> _forwardAvatars = new(StringComparer.Ordinal);
    private readonly HashSet<string> _forwardBases = new(AvatarBaseNames.Comparer);

    /// <summary>
    /// 人が足した組。プロファイルの集合（逆方向のどれか×順方向のどれか）には混ぜない——混ぜると A→B の組が A→順方向の全員に化ける
    /// </summary>
    private readonly List<ConversionPair> _pairs = [];

    /// <summary>
    /// 商品ごとに計算した結果。検索は1打鍵ごとに全商品を数え直すので、同じ記録の答えは使い回す（<see cref="AvatarCompatibilityIndex"/> と同じ）。
    /// 記録が捨てられたら一緒に消える
    /// </summary>
    private readonly ConditionalWeakTable<LocalBlock, IReadOnlyDictionary<string, ConversionRoute>> _resolved = new();

    private readonly ConditionalWeakTable<LocalBlock, IReadOnlyDictionary<string, ConversionRoute>>.CreateValueCallback _resolve;

    private ConversionIndex(AvatarCompatibilityIndex compatibility)
    {
        _compatibility = compatibility;
        _resolve = ResolveCore;
    }

    /// <summary>プロファイルの道筋が組めない（逆方向か順方向のどちらかが1つも無い）。</summary>
    private bool NoProfileRoute => (_reverseAvatars.Count == 0 && _reverseBases.Count == 0) || (_forwardAvatars.Count == 0 && _forwardBases.Count == 0);

    /// <summary>プロファイルの道筋も人が足した組も無い（変換経由は何も出ない）。</summary>
    public bool IsEmpty => NoProfileRoute && _pairs.Count == 0;

    /// <summary>
    /// 手元の商品の変換扱いから組む。
    /// </summary>
    /// <param name="includeUnconfirmed">確認していない（自動で推した）つなぎ先も使うか（設定「確認していない変換も含める」・既定は切）。</param>
    /// <param name="includeDetectedVariations">
    /// 持っていないバリエーションの見分けに、名前から見つけた向き先も使うか（設定「持っていないバリエーションの分を、名前からも見分ける」）。
    /// プロファイルの商品をバリエーションでアバターごとに分けて売っているとき、持っていない分のつなぎ先を使わない（<c>conversion.md</c>「使い道」：広げすぎる2件の直し）
    /// </param>
    /// <param name="pairs">人が足した組（<c>conversion-pairs.json</c>）。人が書いた物なので確認を待たずに使う。</param>
    public static ConversionIndex Build(
        AvatarCompatibilityIndex compatibility,
        IEnumerable<ItemRecord> items,
        bool includeUnconfirmed,
        bool includeDetectedVariations,
        IEnumerable<ConversionPair> pairs)
    {
        var index = new ConversionIndex(compatibility);
        index._pairs.AddRange(pairs.Where(pair => !pair.IsEmpty && !pair.PointsToItself));
        foreach (var item in items)
        {
            if (item.Local.Conversion is not { Mode: not ConversionMode.NotProfile } conversion)
            {
                continue;
            }

            var unowned = compatibility.UnownedVariationAvatars(item.Local, includeDetectedVariations);
            foreach (var target in conversion.Targets)
            {
                if (target.IsEmpty || target.Rejected || (!target.Confirmed && !includeUnconfirmed))
                {
                    continue;
                }

                if (target.AvatarItemId is { Length: > 0 } avatar)
                {
                    if (unowned.Contains(avatar))
                    {
                        continue;
                    }

                    if (target.Forward)
                    {
                        index._forwardAvatars.Add(avatar);
                    }

                    if (target.Reverse)
                    {
                        index._reverseAvatars.Add(avatar);
                    }
                }
                else if (target.BaseName is { Length: > 0 } baseName)
                {
                    if (target.Forward)
                    {
                        index._forwardBases.Add(baseName);
                    }

                    if (target.Reverse)
                    {
                        index._reverseBases.Add(baseName);
                    }
                }
            }
        }

        return index;
    }

    /// <summary>
    /// この衣装が変換経由で使えるアバターと、その道筋。**直接対応・素体経由で既に並ぶアバターは除く**（計算で除く。保存した値に頼らない）。
    /// </summary>
    public IReadOnlyDictionary<string, ConversionRoute> Resolve(LocalBlock local) => _resolved.GetValue(local, _resolve);

    private IReadOnlyDictionary<string, ConversionRoute> ResolveCore(LocalBlock local)
    {
        var empty = (IReadOnlyDictionary<string, ConversionRoute>)new Dictionary<string, ConversionRoute>(StringComparer.Ordinal);
        if (IsEmpty)
        {
            return empty;
        }

        var supported = _compatibility.Resolve(local);
        var result = new Dictionary<string, ConversionRoute>(StringComparer.Ordinal);
        if (!NoProfileRoute)
        {
            AddProfileRoutes(supported, result);
        }

        AddPairRoutes(supported, result);
        return result.Count == 0 ? empty : result;
    }

    /// <summary>元に直接対応を先に、同じならIDの順に並べる（吹き出しで名前を出す1体を決める）。</summary>
    private static List<string> Ordered(IEnumerable<string> avatars, IReadOnlyDictionary<string, AvatarMatch> supported)
        => [.. avatars
            .OrderBy(avatar => supported[avatar] == AvatarMatch.Direct ? 0 : 1)
            .ThenBy(avatar => avatar, StringComparer.Ordinal)];

    private void AddProfileRoutes(IReadOnlyDictionary<string, AvatarMatch> supported, Dictionary<string, ConversionRoute> result)
    {
        var sources = Ordered(
            supported.Keys.Where(avatar => _reverseAvatars.Contains(avatar)
                || (_compatibility.BaseNameOf(avatar) is { } baseName && _reverseBases.Contains(baseName))),
            supported);
        if (sources.Count == 0)
        {
            return;
        }

        var destinations = new HashSet<string>(_forwardAvatars, StringComparer.Ordinal);
        foreach (var baseName in _forwardBases)
        {
            destinations.UnionWith(_compatibility.MembersOf(baseName));
        }

        foreach (var destination in destinations)
        {
            if (supported.ContainsKey(destination))
            {
                continue;
            }

            var from = sources.Where(source => !string.Equals(source, destination, StringComparison.Ordinal)).ToList();
            if (from.Count > 0)
            {
                result[destination] = new ConversionRoute(from[0], from.Count - 1);
            }
        }
    }

    /// <summary>
    /// 人が足した組の道筋。元の端は対応 S（直接対応＋素体経由）の中から選ぶので、素体経由は元の側に1回まで。
    /// 組から組・組からプロファイルへはつながない（変換は1回まで）。プロファイルの道筋が既にあるアバターには足さない
    /// </summary>
    private void AddPairRoutes(IReadOnlyDictionary<string, AvatarMatch> supported, Dictionary<string, ConversionRoute> result)
    {
        if (_pairs.Count == 0)
        {
            return;
        }

        // 先ごとに、届く元と最初に届いた組を集める（ほか n 通りは元のアバターの数で数える。プロファイルと同じ）
        var reached = new Dictionary<string, (HashSet<string> Sources, ConversionPair Pair)>(StringComparer.Ordinal);
        foreach (var pair in _pairs)
        {
            var sources = SourcesOf(pair.From, supported);
            if (sources.Count == 0)
            {
                continue;
            }

            foreach (var destination in DestinationsOf(pair.To))
            {
                if (supported.ContainsKey(destination) || result.ContainsKey(destination))
                {
                    continue;
                }

                var from = sources.Where(source => !string.Equals(source, destination, StringComparison.Ordinal)).ToList();
                if (from.Count == 0)
                {
                    continue;
                }

                if (!reached.TryGetValue(destination, out var known))
                {
                    known = (new HashSet<string>(StringComparer.Ordinal), pair);
                    reached[destination] = known;
                }

                known.Sources.UnionWith(from);
            }
        }

        foreach (var (destination, (sources, pair)) in reached)
        {
            result[destination] = new ConversionRoute(Ordered(sources, supported)[0], sources.Count - 1, pair);
        }
    }

    private List<string> SourcesOf(ConversionEnd end, IReadOnlyDictionary<string, AvatarMatch> supported)
        => end.AvatarItemId is { Length: > 0 } avatar
            ? supported.ContainsKey(avatar) ? [avatar] : []
            : end.BaseName is { Length: > 0 } baseName
                ? [.. supported.Keys.Where(avatar => _compatibility.BaseNameOf(avatar) is { } name && AvatarBaseNames.Comparer.Equals(name, baseName))]
                : [];

    private IReadOnlyList<string> DestinationsOf(ConversionEnd end)
        => end.AvatarItemId is { Length: > 0 } avatar
            ? [avatar]
            : end.BaseName is { Length: > 0 } baseName ? _compatibility.MembersOf(baseName) : [];
}
