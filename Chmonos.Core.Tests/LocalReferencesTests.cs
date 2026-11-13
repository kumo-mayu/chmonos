using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 別の商品 ID・共通素体の名前を持つ <c>local</c> の欄の表（<see cref="LocalReferences"/>・洗い出し 2026-11-10 D2・D3）。
/// 欄を足したときに表へ載せ忘れると、素体の改名・削除と商品 ID の付け替えが、その欄だけ古い名前・ID を指したまま残す。
/// </summary>
public sealed class LocalReferencesTests
{
    /// <summary>
    /// <see cref="LocalBlock"/> と中の型の、保存する欄を全部たどり、どの欄も表の「持つ」か「持たない」のどちらかに載っている。
    /// 載っていなければ、その欄が商品 ID か素体の名前を持つかを決めて、どちらかに足す。
    /// </summary>
    [Fact]
    public void EveryStoredFieldIsOnTheTable()
    {
        var stored = StoredPaths(typeof(LocalBlock), prefix: "").ToHashSet(StringComparer.Ordinal);
        var holds = LocalReferences.Fields.Select(field => field.Path).ToHashSet(StringComparer.Ordinal);

        var missing = stored.Where(path => !holds.Contains(path) && !LocalReferences.HoldsNeither.Contains(path)).Order().ToList();
        Assert.True(missing.Count == 0, "表に無い欄：" + string.Join("、", missing));

        // 表に残った古い名前（消した・名前を変えた欄）も見つける
        var stale = holds.Concat(LocalReferences.HoldsNeither).Where(path => !stored.Contains(path)).Order().ToList();
        Assert.True(stale.Count == 0, "もう無い欄：" + string.Join("、", stale));

        var both = holds.Intersect(LocalReferences.HoldsNeither).ToList();
        Assert.True(both.Count == 0, "両方に載った欄：" + string.Join("、", both));
    }

    /// <summary>
    /// 欄を書き換えるときに名乗る持ち主が、その欄の持ち主と合っている。合っていないと、錠の中で今の値に重ねるときに書き換えた分が落ちる。
    /// </summary>
    [Fact]
    public void EachFieldNamesTheOwnerThatWritesIt()
    {
        foreach (var field in LocalReferences.Fields)
        {
            var top = field.Path.Split('.', '[')[0];
            Assert.Equal(top, field.Owner.ToString());
        }
    }

    /// <summary>素体の名前を持つ欄は、改名で全部書き換わる。</summary>
    [Fact]
    public void RenamingABaseRewritesEveryFieldThatHoldsIt()
    {
        var local = Everything(itemId: "9900001", baseName: "素体A");

        var renamed = LocalReferences.Rewrite(local, LocalReferenceKind.BaseName, LocalReferences.SameBaseName("素体a"), "素体B");

        Assert.NotNull(renamed);
        foreach (var field in LocalReferences.Fields.Where(field => field.Kind == LocalReferenceKind.BaseName))
        {
            Assert.Equal(["素体B"], field.Values(renamed!).Distinct());
        }
    }

    /// <summary>商品 ID を持つ欄は、付け替えで全部書き換わる。</summary>
    [Fact]
    public void ChangingAnItemIdRewritesEveryFieldThatHoldsIt()
    {
        var local = Everything(itemId: "9900001", baseName: "素体A");

        var moved = LocalReferences.Rewrite(local, LocalReferenceKind.ItemId, LocalReferences.SameItemId("9900001"), "9900002");

        Assert.NotNull(moved);
        foreach (var field in LocalReferences.Fields.Where(field => field.Kind == LocalReferenceKind.ItemId))
        {
            Assert.Equal(["9900002"], field.Values(moved!).Distinct());
        }
    }

    /// <summary>当てはまる値が無ければ書かない（null）。</summary>
    [Fact]
    public void ReturnsNullWhenNothingMatches()
        => Assert.Null(LocalReferences.Rewrite(
            Everything(itemId: "9900001", baseName: "素体A"), LocalReferenceKind.BaseName, LocalReferences.SameBaseName("素体Z"), "素体B"));

    /// <summary>
    /// 素体を消すと、その素体だけを向き先にしていたバリエーションの行と変換のつなぎ先は消える（空の行は持たない）。
    /// 外した記録が残る行は人が決めた行として残す。新しい版の向き先は、空にすると「全部向け」に変わるので残す。
    /// </summary>
    [Fact]
    public void DeletingABaseDropsRowsLeftEmptyButKeepsTheUpdateTargeted()
    {
        var local = new LocalBlock
        {
            AvatarBases = [new AvatarBaseLink { BaseName = "素体A" }, new AvatarBaseLink { BaseName = "素体B" }],
            VariationTargets =
            [
                new VariationTarget { VariationId = 1, Bases = ["素体A"] },
                new VariationTarget { VariationId = 2, Bases = ["素体A", "素体B"] },
                new VariationTarget { VariationId = 3, Bases = ["素体A"], RejectedAvatars = ["9900009"] },
            ],
            Conversion = new ConversionSetting
            {
                Targets = [new ConversionTarget { BaseName = "素体A", Forward = true, Confirmed = true }],
            },
            ContentUpdate = new ContentUpdate { FoundAt = DateTimeOffset.UnixEpoch, ForBases = ["素体A"] },
        };

        var deleted = LocalReferences.Rewrite(local, LocalReferenceKind.BaseName, LocalReferences.SameBaseName("素体A"), null)!;

        Assert.Equal(["素体B"], deleted.AvatarBases.Select(link => link.BaseName));
        Assert.Equal([2L, 3L], deleted.VariationTargets.Select(target => target.VariationId!.Value));
        Assert.Equal(["素体B"], deleted.VariationTargets[0].Bases);
        Assert.Empty(deleted.VariationTargets[1].Bases);
        Assert.Null(deleted.Conversion);
        Assert.Equal(["素体A"], deleted.ContentUpdate!.ForBases);
    }

    /// <summary>改名で同じ物が2つになったら1つに畳む（素体の統合）。どの行を残すかの決まりは下の Merging… の試験。</summary>
    [Fact]
    public void MergingTwoBasesFoldsTheDuplicates()
    {
        var local = new LocalBlock
        {
            VariationTargets = [new VariationTarget { VariationId = 1, Bases = ["素体A", "素体B"] }],
            Conversion = new ConversionSetting
            {
                Targets =
                [
                    new ConversionTarget { BaseName = "素体B", Slug = "x", Confirmed = true },
                    new ConversionTarget { BaseName = "素体A", Slug = "x" },
                ],
            },
        };

        var merged = LocalReferences.Rewrite(local, LocalReferenceKind.BaseName, LocalReferences.SameBaseName("素体A"), "素体B")!;

        Assert.Equal(["素体B"], merged.VariationTargets[0].Bases);
        Assert.True(Assert.Single(merged.Conversion!.Targets).Confirmed);
    }

    /// <summary>
    /// 「A を外す→B を選ぶ」の後に A を B へ統合すると、後から選んだ B の行が残る（Codex の点検 2026-11-13 件1）。
    /// <see cref="ConversionEdits.Choose"/> は外した行を残して選んだ行を末尾へ足すので、外した行が先に並ぶ。前は先頭を残し、選んだ行が消えて外した印が残った。
    /// 素体の統合と商品 ID のまとめ（ほかの商品が指す ID の書き換え）の両方で見る。
    /// </summary>
    [Theory]
    [InlineData(LocalReferenceKind.BaseName)]
    [InlineData(LocalReferenceKind.ItemId)]
    public void MergingKeepsTheChosenRowOverTheRejectedOneBeforeIt(LocalReferenceKind kind)
    {
        var (a, b) = kind == LocalReferenceKind.BaseName ? ("素体A", "素体B") : ("9900001", "9900002");
        var rowA = Row(kind, a) with { Slug = "x", Forward = true };
        var setting = ConversionEdits.Reject(rowA)(new ConversionSetting { Targets = [rowA] });
        setting = Choose(kind, b, reverse: true)(setting);
        Assert.True(setting!.Targets[0].Rejected); // 筋書きのとおり外した行が先

        var merged = Merge(new LocalBlock { Conversion = setting }, kind, a, b);

        var kept = Assert.Single(merged.Conversion!.Targets);
        Assert.True(kept is { Confirmed: true, Rejected: false, Forward: false, Reverse: true });
        Assert.Equal(b, kind == LocalReferenceKind.BaseName ? kept.BaseName : kept.AvatarItemId);
    }

    /// <summary>逆の順（選んだ行が統合元で、外した行が統合先）でも、選んだ行が勝つ。人が選んだ物を外した印で消さない。</summary>
    [Theory]
    [InlineData(LocalReferenceKind.BaseName)]
    [InlineData(LocalReferenceKind.ItemId)]
    public void MergingKeepsTheChosenRowEvenWhenTheTargetRowWasRejected(LocalReferenceKind kind)
    {
        var (a, b) = kind == LocalReferenceKind.BaseName ? ("素体A", "素体B") : ("9900001", "9900002");
        var local = new LocalBlock
        {
            Conversion = new ConversionSetting
            {
                Targets =
                [
                    Row(kind, b) with { Slug = "x", Forward = true, Confirmed = true, Rejected = true },
                    Row(kind, a) with { Slug = "x", Reverse = true, Confirmed = true },
                ],
            },
        };

        var kept = Assert.Single(Merge(local, kind, a, b).Conversion!.Targets);

        Assert.True(kept is { Confirmed: true, Rejected: false, Forward: false, Reverse: true });
    }

    /// <summary>外した行は推した行（確認待ち）に勝つ。どちらが先に並んでいても同じ。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MergingKeepsTheRejectedRowOverAnEstimate(bool rejectedFirst)
    {
        ConversionTarget estimate = new() { BaseName = "素体A", Slug = "x", Forward = true };
        ConversionTarget rejected = new() { BaseName = "素体B", Slug = "x", Forward = true, Confirmed = true, Rejected = true };
        var local = new LocalBlock
        {
            Conversion = new ConversionSetting { Targets = rejectedFirst ? [rejected, estimate] : [estimate, rejected] },
        };

        var kept = Assert.Single(Merge(local, LocalReferenceKind.BaseName, "素体A", "素体B").Conversion!.Targets);

        Assert.True(kept.Rejected);
    }

    /// <summary>
    /// 両方とも人が選んだ行で向きが食い違うなら、統合先の行をそのまま残す（向きを足し合わせない）。
    /// 残した行は重なった行の一番前の位置に置き、ほかの行の並びは動かさない。
    /// </summary>
    [Fact]
    public void MergingTwoChosenRowsKeepsTheTargetRowAtTheFirstPosition()
    {
        var local = new LocalBlock
        {
            Conversion = new ConversionSetting
            {
                Targets =
                [
                    new ConversionTarget { BaseName = "素体A", Slug = "x", Forward = true, Confirmed = true },
                    new ConversionTarget { BaseName = "素体C", Slug = "y", Forward = true },
                    new ConversionTarget { BaseName = "素体B", Slug = "x", Reverse = true, Confirmed = true },
                ],
            },
        };

        var targets = Merge(local, LocalReferenceKind.BaseName, "素体A", "素体B").Conversion!.Targets;

        Assert.Collection(
            targets,
            row => Assert.True(row is { BaseName: "素体B", Forward: false, Reverse: true, Confirmed: true }),
            row => Assert.Equal("素体C", row.BaseName));
    }

    /// <summary>
    /// 商品をまとめるとき、移した先の推した行（確認待ち）は、移す元で人が選んだ同じ行に負ける。前は移した先の行がいつも勝っていた。
    /// 同じ順位（どちらも選んだ行）なら移した先の行を残す。
    /// </summary>
    [Fact]
    public void MergingTwoItemsKeepsTheChosenRowFromEitherSide()
    {
        var target = new LocalBlock
        {
            Conversion = new ConversionSetting
            {
                Targets =
                [
                    new ConversionTarget { BaseName = "素体A", Slug = "a", Forward = true },
                    new ConversionTarget { BaseName = "素体B", Slug = "b", Forward = true, Confirmed = true },
                ],
            },
        };
        var source = new LocalBlock
        {
            Conversion = new ConversionSetting
            {
                Targets =
                [
                    new ConversionTarget { BaseName = "素体A", Slug = "a", Reverse = true, Confirmed = true },
                    new ConversionTarget { BaseName = "素体B", Slug = "b", Reverse = true, Confirmed = true },
                ],
            },
        };

        var merged = ItemIdChange.Merge(source, target, new HashSet<int>(), "9900001", "9900002");

        Assert.Collection(
            merged.Conversion!.Targets,
            row => Assert.True(row is { BaseName: "素体A", Forward: false, Reverse: true, Confirmed: true }),
            row => Assert.True(row is { BaseName: "素体B", Forward: true, Reverse: false, Confirmed: true }));
    }

    /// <summary>
    /// 商品をまとめるとき、移した先で外した行と、移す元で選んだ同じ行が重なれば、選んだ行が勝つ（素体の統合と同じ決まり）。
    /// 前は移した先の外した印が残り、移す元で選んだ行が消えていた。
    /// </summary>
    [Fact]
    public void MergingTwoItemsKeepsTheSourceChosenRowOverTheTargetRejectedOne()
    {
        var target = new LocalBlock
        {
            Conversion = new ConversionSetting
            {
                Targets = [new ConversionTarget { BaseName = "素体A", Slug = "a", Forward = true, Confirmed = true, Rejected = true }],
            },
        };
        var source = new LocalBlock
        {
            Conversion = new ConversionSetting
            {
                Targets = [new ConversionTarget { BaseName = "素体A", Slug = "a", Reverse = true, Confirmed = true }],
            },
        };

        var kept = Assert.Single(ItemIdChange.Merge(source, target, new HashSet<int>(), "9900001", "9900002").Conversion!.Targets);

        Assert.True(kept is { Confirmed: true, Rejected: false, Forward: false, Reverse: true });
    }

    private static ConversionTarget Row(LocalReferenceKind kind, string value)
        => kind == LocalReferenceKind.BaseName ? new ConversionTarget { BaseName = value } : new ConversionTarget { AvatarItemId = value };

    private static Func<ConversionSetting?, ConversionSetting?> Choose(LocalReferenceKind kind, string value, bool reverse)
        => kind == LocalReferenceKind.BaseName
            ? ConversionEdits.Choose("x", forward: !reverse, reverse: reverse, avatarItemId: null, baseName: value)
            : ConversionEdits.Choose("x", forward: !reverse, reverse: reverse, avatarItemId: value, baseName: null);

    private static LocalBlock Merge(LocalBlock local, LocalReferenceKind kind, string from, string to)
        => LocalReferences.Rewrite(
            local,
            kind,
            kind == LocalReferenceKind.BaseName ? LocalReferences.SameBaseName(from) : LocalReferences.SameItemId(from),
            to)!;

    /// <summary>表のどの欄にも同じ名前・ID が入った <c>local</c>。</summary>
    private static LocalBlock Everything(string itemId, string baseName)
    {
        var target = new VariationTarget
        {
            VariationId = 1,
            Avatars = [itemId],
            Bases = [baseName],
            RejectedAvatars = [itemId],
            RejectedBases = [baseName],
        };

        var local = new LocalBlock
        {
            Avatars = [new AvatarLink { AvatarItemId = itemId }],
            AvatarBases = [new AvatarBaseLink { BaseName = baseName }],
            VariationTargets = [target],
            DetectedVariationTargets = [target],
            Conversion = new ConversionSetting
            {
                Targets =
                [
                    new ConversionTarget { AvatarItemId = itemId, Slug = "a" },
                    new ConversionTarget { BaseName = baseName, Slug = "b" },
                ],
            },
            ContentUpdate = new ContentUpdate { FoundAt = DateTimeOffset.UnixEpoch, ForAvatars = [itemId], ForBases = [baseName] },
        };

        // 表に欄を足したのに、ここで値を入れ忘れると、書き換わったかを確かめられない
        foreach (var field in LocalReferences.Fields)
        {
            Assert.NotEmpty(field.Values(local));
        }

        return local;
    }

    /// <summary>
    /// 保存する欄の道を全部返す。計算で出す欄（<see cref="JsonIgnoreAttribute"/> の条件なし）は書かないので除く。
    /// 文字・数・日時・列挙・辞書はそこで止め、配列は <c>[]</c> を付けて中の型へ、Chmonos の型は中へ降りる。
    /// </summary>
    private static IEnumerable<string> StoredPaths(Type type, string prefix)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0
                || property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
            {
                continue;
            }

            var path = prefix + property.Name;
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var element = ElementOf(propertyType);
            if (element is not null)
            {
                if (IsOurs(element))
                {
                    foreach (var inner in StoredPaths(element, path + "[]."))
                    {
                        yield return inner;
                    }
                }
                else
                {
                    yield return path;
                }
            }
            else if (IsOurs(propertyType))
            {
                foreach (var inner in StoredPaths(propertyType, path + "."))
                {
                    yield return inner;
                }
            }
            else
            {
                yield return path;
            }
        }
    }

    /// <summary>配列の中の型。辞書と文字は配列として扱わない（鍵と値は1つの欄として見る）。</summary>
    private static Type? ElementOf(Type type)
    {
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
        {
            return null;
        }

        if (type.GetInterfaces().Append(type).Any(face => face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
        {
            return null;
        }

        return type.IsArray
            ? type.GetElementType()
            : type.GetInterfaces().Append(type)
                .FirstOrDefault(face => face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];
    }

    private static bool IsOurs(Type type)
        => type.IsClass && type != typeof(string) && type.Namespace?.StartsWith("Chmonos", StringComparison.Ordinal) == true;
}
