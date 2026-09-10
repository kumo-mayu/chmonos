using System.Text;
using System.Text.Json.Serialization;

namespace BoothAssetManager.Core.Models;

/// <summary>属性の幅1つ。0〜100で持つ（画面のスライダと同じ単位）。</summary>
public sealed record AttributeRange(string Name, int Min, int Max)
{
    /// <summary>全開ならば条件になっていない。</summary>
    [JsonIgnore]
    public bool IsOpen => Min <= 0 && Max >= 100;
}

/// <summary>
/// 検索1回ぶんの条件。**絞り込みと文字列を1セットで持つ。**
///
/// 別々に覚えても復元できない——「衣装」と打ったときに
/// カテゴリを絞っていたのか、所持だけに限っていたのかで結果が変わる。
///
/// 画面の状態をそのまま写すので、項目は
/// <c>SearchViewModel.ClearFilters</c> が戻すものと1対1に対応する。
/// **片方に足してもう片方に足し忘れると、復元できない条件が静かに増える。**
/// </summary>
public sealed record SearchHistoryEntry
{
    /// <summary>
    /// 指紋の項目の区切り。
    ///
    /// 入力に現れない字を使う。区切りに使える字が本文にも入れられると、
    /// 「衣装|夏」と「衣装」＋カテゴリ「夏」が同じ指紋になりうる。
    /// </summary>
    private const char Separator = '\u001f';

    /// <summary>検索文字列。</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>カテゴリ。すべてなら null。</summary>
    public string? Category { get; init; }

    public bool OwnedOnly { get; init; }

    public bool MissingOnly { get; init; }

    public bool GivenOnly { get; init; }

    public bool ReceivedOnly { get; init; }

    // ---- 探す範囲 ----

    public bool SearchBody { get; init; }

    public bool SearchPaths { get; init; }

    public bool SearchAlternates { get; init; }

    // ---- アバター ----

    /// <summary>対応アバターの絞り込み。名前で持つ（IDだけだと後から読めない）。</summary>
    public string? AvatarName { get; init; }

    public long? AvatarId { get; init; }

    public bool AvatarHasBase { get; init; }

    // ---- 積んだ条件 ----

    /// <summary>選んだユーザータグ。入れ子は <c>親/子</c> で持つ。</summary>
    public IReadOnlyList<string> UserTags { get; init; } = [];

    public IReadOnlyList<string> BoothTags { get; init; } = [];

    /// <summary>幅を狭めた属性だけ。全開のものは条件ではないので持たない。</summary>
    public IReadOnlyList<AttributeRange> Attributes { get; init; } = [];

    /// <summary>表示順。既定なら null。</summary>
    public string? Sort { get; init; }

    /// <summary>最後に使った時刻。並びと、古いものを落とす判断に使う。</summary>
    public DateTimeOffset UsedAt { get; init; }

    /// <summary>
    /// 人が付けた名前。
    ///
    /// 名前があるものは**古くなっても落とさない**。
    /// 自動で溜まる履歴と、意図して残したものを同じ扱いにすると、
    /// 残したはずのものが押し出されて消える。
    /// </summary>
    public string? Name { get; init; }

    [JsonIgnore]
    public bool IsNamed => !string.IsNullOrWhiteSpace(Name);

    /// <summary>何も絞っていない状態。残す価値が無い。</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        Text.Trim().Length == 0
        && Category is null
        && !OwnedOnly && !MissingOnly && !GivenOnly && !ReceivedOnly
        && AvatarName is null
        && UserTags.Count == 0
        && BoothTags.Count == 0
        && Attributes.Count == 0;

    /// <summary>
    /// 同じ条件かを見るための指紋。
    ///
    /// **同じ検索を2回しても2行にしない。**時刻と名前は含めない——
    /// 条件が同じなら同じ検索で、上に持ち上げるだけでよい。
    /// 探す範囲（本文・パス・別表記）は結果を変えるので含める。
    /// 表示順は結果の中身を変えないが、**戻したい状態の一部**なので含める。
    /// </summary>
    [JsonIgnore]
    public string Fingerprint
    {
        get
        {
            var text = new StringBuilder();
            text.Append(Text.Trim()).Append(Separator);
            text.Append(Category).Append(Separator);
            text.Append(OwnedOnly ? '1' : '0');
            text.Append(MissingOnly ? '1' : '0');
            text.Append(GivenOnly ? '1' : '0');
            text.Append(ReceivedOnly ? '1' : '0');
            text.Append(SearchBody ? '1' : '0');
            text.Append(SearchPaths ? '1' : '0');
            text.Append(SearchAlternates ? '1' : '0');
            text.Append(AvatarHasBase ? '1' : '0').Append(Separator);
            text.Append(AvatarName).Append(Separator);
            text.Append(Sort).Append(Separator);

            // 並べ替えてから繋ぐ。積んだ順が違うだけで別物にはしない
            text.Append(string.Join(',', UserTags.OrderBy(tag => tag, StringComparer.Ordinal))).Append(Separator);
            text.Append(string.Join(',', BoothTags.OrderBy(tag => tag, StringComparer.Ordinal))).Append(Separator);
            text.Append(string.Join(
                ',',
                Attributes
                    .OrderBy(range => range.Name, StringComparer.Ordinal)
                    .Select(range => $"{range.Name}:{range.Min}-{range.Max}")));

            return text.ToString();
        }
    }

    /// <summary>
    /// 一覧に出す1行。**条件を思い出せる形にする。**
    ///
    /// 「衣装」だけでは、そのとき何で絞っていたか分からない。
    /// 名前を付けてあればそれを使う。
    /// </summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            if (IsNamed)
            {
                return Name!;
            }

            var parts = new List<string>();

            if (Text.Trim().Length > 0)
            {
                parts.Add(Text.Trim());
            }

            if (Category is not null)
            {
                parts.Add(Category);
            }

            if (OwnedOnly)
            {
                parts.Add("所持のみ");
            }

            if (MissingOnly)
            {
                parts.Add("ファイルなし");
            }

            if (GivenOnly)
            {
                parts.Add("贈った");
            }

            if (ReceivedOnly)
            {
                parts.Add("貰った");
            }

            if (AvatarName is not null)
            {
                parts.Add(AvatarHasBase ? $"{AvatarName}（素体を含む）" : AvatarName);
            }

            parts.AddRange(UserTags);
            parts.AddRange(BoothTags.Select(tag => $"#{tag}"));
            parts.AddRange(Attributes.Select(range => $"{range.Name} {range.Min}〜{range.Max}"));

            // 探す範囲は結果を変えるので出す。表示順は出さない（思い出す手掛かりにならない）
            if (SearchBody)
            {
                parts.Add("本文も");
            }

            if (SearchPaths)
            {
                parts.Add("パスも");
            }

            if (SearchAlternates)
            {
                parts.Add("別表記も");
            }

            return parts.Count == 0 ? "条件なし" : string.Join(" / ", parts);
        }
    }
}
