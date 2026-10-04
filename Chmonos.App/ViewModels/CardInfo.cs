using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// カードの札と1行・乗せたときの重ね・リストの列に、どの属性をどの順で出すか（ユーザ判断 2026-10-04）。
/// </summary>
/// <param name="ChipAttributes">
/// カードの札とリストの「属性」の列に出す順。設定で選んだ属性（選んでいなければ属性の管理の並び）で、
/// 並べ替えに属性を使っているときはその属性を先頭に出す。
/// </param>
/// <param name="AllAttributes">乗せたときの重ねに出す順。<paramref name="ChipAttributes"/> の後に、残りを属性の管理の並びで。</param>
/// <param name="WithSubs">ユーザータグの札に小分類も出すか（設定「一覧のカードに小分類のタグも表示」）。</param>
public sealed record CardInfoOptions(IReadOnlyList<string> ChipAttributes, IReadOnlyList<string> AllAttributes, bool WithSubs)
{
    public static CardInfoOptions Empty { get; } = new([], [], false);

    /// <summary>
    /// 設定と属性の管理の並びから組む。
    /// </summary>
    /// <param name="chosen">設定で選んだ属性（<see cref="AppSettings.CardAttributes"/>）。空なら属性の管理の並びの上から。</param>
    /// <param name="masterOrder">属性の管理の並び。</param>
    /// <param name="sortAttribute">並べ替えに使っている属性。並べた値がカードで見えないと、なぜその順なのかが読めないので先に出す。</param>
    public static CardInfoOptions Build(IReadOnlyList<string>? chosen, IReadOnlyList<string> masterOrder, string? sortAttribute, bool withSubs)
    {
        var chips = new List<string>();
        if (!string.IsNullOrEmpty(sortAttribute))
        {
            chips.Add(sortAttribute);
        }

        foreach (var name in chosen is { Count: > 0 } ? chosen : masterOrder)
        {
            if (!chips.Contains(name, StringComparer.Ordinal))
            {
                chips.Add(name);
            }
        }

        var all = new List<string>(chips);
        all.AddRange(masterOrder.Where(name => !chips.Contains(name, StringComparer.Ordinal)));
        return new CardInfoOptions(chips, all, withSubs);
    }

    public bool Equals(CardInfoOptions? other)
        => other is not null
           && WithSubs == other.WithSubs
           && ChipAttributes.SequenceEqual(other.ChipAttributes, StringComparer.Ordinal)
           && AllAttributes.SequenceEqual(other.AllAttributes, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(WithSubs, ChipAttributes.Count, AllAttributes.Count);
}

/// <summary>
/// カードとリストの行が共有する、札に出す物の決め方。画面ごとに1つを持ち、カードはこれを見て札を組む。
/// 設定・属性の並び・並べ替えが変わったら <see cref="Update"/> で替え、画面がカードに知らせる（<see cref="ItemCardViewModel.NoteInfoChanged"/>）。
/// カードは数千枚あるので、カードごとに設定を写さず、この1つを指す
/// </summary>
public sealed class CardInfoContext
{
    public CardInfoOptions Options { get; private set; } = CardInfoOptions.Empty;

    /// <summary>替わった回数。カードはこれで作り置きの札が古いかを見る。</summary>
    public int Version { get; private set; }

    /// <summary>替えた。前と同じなら false（カードに知らせなくてよい）。</summary>
    public bool Update(CardInfoOptions options)
    {
        if (Options.Equals(options))
        {
            return false;
        }

        Options = options;
        Version++;
        return true;
    }
}

/// <summary>カードの属性の札1つ（「質感 63」）。</summary>
public sealed record CardAttributeChip(string Name, int Value)
{
    public string ValueText => Value.ToString(CultureInfo.InvariantCulture);

    public string Text => $"{Name} {ValueText}";
}

/// <summary>乗せたときの重ねの、属性の棒1本。</summary>
/// <param name="TrackWidth">棒の枠の幅。狭いカードでは縮める（名前が「かっこ…」まで切れて読めなかった）。</param>
public sealed record CardAttributeBar(string Name, int Value, double TrackWidth)
{
    public double BarWidth => TrackWidth * Math.Clamp(Value, 0, 100) / 100.0;

    public string ValueText => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// カードの名前の下に出す札と1行（案A）と、リストの列（案C）の中身。カードの幅で中身が変わる（幅200未満は札1枚・短い1行）。
/// **値の無い札は出さない**（評価していない属性・ユーザータグの無い商品）。札の欄の高さはカードの側で固定し、値が無くても一覧の段は揃う
/// </summary>
public sealed record CardInfo
{
    /// <summary>この幅（DIP）より狭いカードでは札を1枚に減らす。札が2枚並ぶと、狭いカードでは2枚目が切れて読めない。</summary>
    public const double NarrowBelow = 200;

    public string? Tag1 { get; init; }

    public string? Tag2 { get; init; }

    /// <summary>出しきれなかったユーザータグの数（「+2」）。無ければ空。</summary>
    public string TagMore { get; init; } = string.Empty;

    public CardAttributeChip? Attribute1 { get; init; }

    public CardAttributeChip? Attribute2 { get; init; }

    /// <summary>「¥3,000・対応 24体」。分からない物は書かない（額を入れていない・対応アバターが無い）。</summary>
    public string MetaLine { get; init; } = string.Empty;

    /// <summary>未所持の襷（カードの左下）に掛からないよう、1行を右へ寄せる。</summary>
    public Thickness MetaMargin { get; init; }

    public bool HasTag1 => Tag1 is not null;

    public bool HasTag2 => Tag2 is not null;

    public bool HasTagMore => TagMore.Length > 0;

    public bool HasAttribute1 => Attribute1 is not null;

    public bool HasAttribute2 => Attribute2 is not null;

    // ---- リストの列（幅で変えない） ----

    /// <summary>ユーザータグの列（全部。カードの札と同じ書き方を並べる）。</summary>
    public string TagsLine { get; init; } = string.Empty;

    /// <summary>属性の列（選んだ属性のうち評価した物を全部。「質感 63　かわいい 66」）。</summary>
    public string AttributesLine { get; init; } = string.Empty;

    /// <summary>払った額の列（「¥3,000」「無料」。額を入れていなければ空）。</summary>
    public string PaidText { get; init; } = string.Empty;

    /// <summary>対応の列（「24体」。無ければ空）。</summary>
    public string AvatarText { get; init; } = string.Empty;

    /// <summary>未所持の襷のぶん、カードの1行を右へ寄せる幅（今までのユーザータグの札と同じ）。</summary>
    private const double SashInset = 46;

    public static CardInfo Build(ItemRecord item, CardInfoOptions options, bool narrow, bool owned)
    {
        var chipCount = narrow ? 1 : 2;

        var tags = item.Local.UserTags.Select(tag => TagText(tag, options.WithSubs)).ToList();
        var rated = RatedInOrder(item, options.ChipAttributes).ToList();

        var paid = PaidTextOf(item);
        var avatars = AvatarCountOf(item);
        var meta = new List<string>(2);
        if (paid.Length > 0)
        {
            meta.Add(paid);
        }

        if (avatars > 0)
        {
            meta.Add(narrow ? $"{avatars}体" : $"対応 {avatars}体");
        }

        return new CardInfo
        {
            Tag1 = tags.Count > 0 ? tags[0] : null,
            Tag2 = chipCount > 1 && tags.Count > 1 ? tags[1] : null,
            TagMore = tags.Count > chipCount ? $"+{tags.Count - chipCount}" : string.Empty,
            Attribute1 = rated.Count > 0 ? rated[0] : null,
            Attribute2 = chipCount > 1 && rated.Count > 1 ? rated[1] : null,
            MetaLine = string.Join("・", meta),
            MetaMargin = owned ? new Thickness(0, 4, 0, 0) : new Thickness(SashInset, 4, 0, 0),
            TagsLine = string.Join("　", tags),
            AttributesLine = string.Join("　", rated.Select(chip => $"{chip.Name} {chip.ValueText}")),
            PaidText = paid,
            AvatarText = avatars > 0 ? $"{avatars}体" : string.Empty,
        };
    }

    /// <summary>ユーザータグの札1枚の文字。小分類を出すときは「衣装：夏・冬」（カードの今までの1行と同じ書き方）。</summary>
    internal static string TagText(UserTagAssignment tag, bool withSubs)
        => withSubs && tag.Subs.Count > 0 ? $"{tag.Top}：{string.Join("・", tag.Subs)}" : tag.Top;

    /// <summary>並べた順のうち、評価してある属性だけ。評価していない属性は札を空けない。</summary>
    internal static IEnumerable<CardAttributeChip> RatedInOrder(ItemRecord item, IEnumerable<string> order)
    {
        foreach (var name in order)
        {
            if (item.Local.Attributes.TryGetValue(name, out var value))
            {
                yield return new CardAttributeChip(name, value);
            }
        }
    }

    /// <summary>
    /// 払った額。自分用の額の合計（並べ替えの「払った額」と同じ <see cref="Core.Services.Purchases.SelfPaidOrNull"/>）。
    /// 額を1つも入れていなければ空（「0円」と書くと無料と読める）
    /// </summary>
    internal static string PaidTextOf(ItemRecord item)
        => Core.Services.Purchases.SelfPaidOrNull(item) switch
        {
            null => string.Empty,
            0 => "無料",
            { } yen => $"¥{yen:N0}",
        };

    /// <summary>対応アバターの数。商品ページの対応アバターの欄と同じく、消した物は数えず、確かめていない候補は数える。</summary>
    internal static int AvatarCountOf(ItemRecord item) => item.Local.Avatars.Count(link => !link.Rejected);
}

/// <summary>
/// 乗せたとき・キーボードで止まったときに絵の上に重ねる中身（案C）。出すときに作る（数千枚のカードに前もって持たせない）。
/// 絵の高さに収まる数だけ属性の棒を出し、入らない分は「ほか n 件」にする
/// </summary>
public sealed record CardPeek
{
    public string TagsText { get; init; } = string.Empty;

    public IReadOnlyList<CardAttributeBar> Bars { get; init; } = [];

    public string MoreText { get; init; } = string.Empty;

    public string PaidText { get; init; } = string.Empty;

    public string AvatarText { get; init; } = string.Empty;

    public bool HasTags => TagsText.Length > 0;

    public bool HasBars => Bars.Count > 0;

    public bool HasMore => MoreText.Length > 0;

    public bool HasPaid => PaidText.Length > 0;

    public bool HasAvatars => AvatarText.Length > 0;

    public bool HasMeta => HasPaid || HasAvatars;

    public bool IsEmpty => !HasTags && !HasBars && !HasMeta;

    // 重ねの縦の大きさ（XAML の高さと揃える）。ここで収まる行の数を数えるので、XAML の側も行の高さを決め打ちにしてある
    internal const double Padding = 16;
    internal const double LabelHeight = 14;
    internal const double TagsHeight = 30;
    internal const double SectionGap = 6;
    internal const double RowHeight = 15;
    internal const double MetaHeight = 15;

    /// <summary>棒の枠の幅。狭いカード（<see cref="CardInfo.NarrowBelow"/> 未満）では名前に幅を譲る。</summary>
    internal const double TrackWidth = 72;
    internal const double NarrowTrackWidth = 40;

    /// <param name="imageHeight">絵の枠の高さ（DIP）。ここに収まる分だけ棒を出す。</param>
    /// <param name="cardWidth">カードの幅（DIP）。狭いときは棒を短くする。</param>
    public static CardPeek Build(ItemRecord item, CardInfoOptions options, double imageHeight, double cardWidth)
    {
        var track = cardWidth < CardInfo.NarrowBelow ? NarrowTrackWidth : TrackWidth;
        var tags = ItemCardViewModel.UserTagLine(item.Local.UserTags, withSubs: true);
        var order = options.AllAttributes
            .Concat(item.Local.Attributes.Keys
                .Where(name => !options.AllAttributes.Contains(name, StringComparer.Ordinal))
                .Order(StringComparer.CurrentCulture));
        var bars = CardInfo.RatedInOrder(item, order).Select(chip => new CardAttributeBar(chip.Name, chip.Value, track)).ToList();
        var paid = CardInfo.PaidTextOf(item);
        var avatars = CardInfo.AvatarCountOf(item);

        var fixedHeight = Padding
                          + (tags.Length > 0 ? LabelHeight + TagsHeight : 0)
                          + (bars.Count > 0 ? SectionGap + LabelHeight : 0)
                          + (paid.Length > 0 || avatars > 0 ? SectionGap + MetaHeight : 0);
        var fits = Math.Max(0, (int)Math.Floor((imageHeight - fixedHeight) / RowHeight));

        // 入りきらないときは、最後の1行を「ほか n 件」にする（何件隠れたかが分からないと、無いのと見分けられない）
        var shown = bars.Count <= fits ? bars.Count : Math.Max(0, fits - 1);
        return new CardPeek
        {
            TagsText = tags,
            Bars = bars.Take(shown).ToList(),
            MoreText = shown < bars.Count ? $"ほか {bars.Count - shown} 件" : string.Empty,
            PaidText = paid,
            AvatarText = avatars > 0 ? $"{avatars}体" : string.Empty,
        };
    }
}

/// <summary>
/// カードの札を、カードの今の幅で組む。幅はスライダーで変わり（DynamicResource）、札の数は 200 を境に変わるので、
/// カードの幅と札の作り直しの印（<see cref="ItemCardViewModel.InfoVersion"/>）の両方に結んで受ける。
/// 画面ごとに幅の知らせを配らずに済む（カードはショップ・フォルダ・管理の画面にも並ぶ）
/// </summary>
public sealed class CardInfoForWidthConverter : IMultiValueConverter
{
    public static CardInfoForWidthConverter Instance { get; } = new();

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length > 0 && values[0] is ItemCardViewModel card
            ? card.InfoFor(narrow: values.Length > 2 && values[2] is double width && width < CardInfo.NarrowBelow)
            : null;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
