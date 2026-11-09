using System.Runtime.CompilerServices;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>変換経由で使えるアバター1体の道筋（吹き出しで「「A」の逆方向と、このアバターの順方向のプロファイルで」と言うため）。</summary>
/// <param name="SourceAvatarItemId">元にできる対応アバター A（1体）。</param>
/// <param name="OtherSources">ほかに元にできる対応アバターの数（「ほか n 通り」）。</param>
public sealed record ConversionRoute(string SourceAvatarItemId, int OtherSources);

/// <summary>
/// 変換経由で使えるアバター（<c>docs/spec/conversion.md</c>「使える、と見なす決まり」）。保存はせず、その場で計算する（素体経由と同じ）。
///
/// 衣装の対応 S（直接対応＋素体経由：<see cref="AvatarCompatibilityIndex.Resolve"/>）のどれか A について、A か A の素体に**逆方向**のプロファイルがあり、
/// アバター B か B の素体に**順方向**のプロファイルがあるとき、B は変換経由で使える（作者の使い方ガイド「衣装元は逆方向、変換先は順方向」）。
/// 素体経由は元の側に1回まで、変換は1回まで。
/// **つなぎ先を共通素体にしたプロファイルは素体の仲間全員に効き、アバターにした物はそのアバターにだけ効く**（ユーザ判断 2026-11-06）。
/// 使うのは手元にプロファイルを持っている商品の、変換扱いのつなぎ先だけ（「プロファイルとして扱わない」にした商品は使わない）。
/// </summary>
public sealed class ConversionIndex
{
    private readonly AvatarCompatibilityIndex _compatibility;
    private readonly HashSet<string> _reverseAvatars = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reverseBases = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly HashSet<string> _forwardAvatars = new(StringComparer.Ordinal);
    private readonly HashSet<string> _forwardBases = new(StringComparer.CurrentCultureIgnoreCase);

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

    /// <summary>プロファイルが1つも無い（変換経由は何も出ない）。</summary>
    public bool IsEmpty => (_reverseAvatars.Count == 0 && _reverseBases.Count == 0) || (_forwardAvatars.Count == 0 && _forwardBases.Count == 0);

    /// <summary>
    /// 手元の商品の変換扱いから組む。
    /// </summary>
    /// <param name="includeUnconfirmed">確認していない（自動で推した）つなぎ先も使うか（設定「確認していない変換も含める」・既定は切）。</param>
    /// <param name="includeDetectedVariations">
    /// 持っていないバリエーションの見分けに、名前から見つけた向き先も使うか（設定「持っていないバリエーションの分を、名前からも見分ける」）。
    /// プロファイルの商品をバリエーションでアバターごとに分けて売っているとき、持っていない分のつなぎ先を使わない（<c>conversion.md</c>「使い道」：広げすぎる2件の直し）
    /// </param>
    public static ConversionIndex Build(
        AvatarCompatibilityIndex compatibility,
        IEnumerable<ItemRecord> items,
        bool includeUnconfirmed,
        bool includeDetectedVariations)
    {
        var index = new ConversionIndex(compatibility);
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
        var sources = supported.Keys
            .Where(avatar => _reverseAvatars.Contains(avatar)
                || (_compatibility.BaseNameOf(avatar) is { } baseName && _reverseBases.Contains(baseName)))
            .OrderBy(avatar => supported[avatar] == AvatarMatch.Direct ? 0 : 1)
            .ThenBy(avatar => avatar, StringComparer.Ordinal)
            .ToList();
        if (sources.Count == 0)
        {
            return empty;
        }

        var destinations = new HashSet<string>(_forwardAvatars, StringComparer.Ordinal);
        foreach (var baseName in _forwardBases)
        {
            destinations.UnionWith(_compatibility.MembersOf(baseName));
        }

        var result = new Dictionary<string, ConversionRoute>(StringComparer.Ordinal);
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

        return result.Count == 0 ? empty : result;
    }
}
