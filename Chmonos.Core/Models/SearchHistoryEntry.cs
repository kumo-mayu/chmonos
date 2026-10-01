using System.Text;
using System.Text.Json.Serialization;

namespace Chmonos.Core.Models;

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
/// 絞り込みはモジュールの状態（<see cref="SearchModuleState"/>）をそのまま持つ（検索画面の刷新 2026-09-16。
/// 前はカテゴリ・所持・タグ…を1つずつ欄にしていて、画面に条件を足すたびにここにも足す必要があった）。
/// </summary>
public sealed record SearchHistoryEntry
{
    /// <summary>
    /// 指紋の項目の区切り。
    ///
    /// 入力に現れない字を使う。区切りに使える字が本文にも入れられると、
    /// 「衣装|夏」と「衣装」＋カテゴリ「夏」が同じ指紋になりうる。
    /// </summary>
    private const char Separator = (char)0x1F;

    /// <summary>検索文字列。</summary>
    public string Text { get; init; } = string.Empty;

    // ---- 文字列で探す対象と切り替え ----

    /// <summary>
    /// 文字列で探した対象（前置きの名前：<c>name</c>・<c>main</c> など）。**既定のままなら空**
    /// （既定の対象を後から変えても、古い履歴が既定に追従する）。
    /// </summary>
    public IReadOnlyList<string> Targets { get; init; } = [];

    public bool CaseSensitive { get; init; }

    public bool WidthSensitive { get; init; }

    /// <summary>ひらがなとカタカナを区別しない。既定（区別する）を false にしておくため、否定の形で持つ。</summary>
    public bool KanaInsensitive { get; init; }

    public bool SearchAlternates { get; init; }

    // ---- 絞り込み ----

    /// <summary>効いていた絞り込みのモジュール（何も絞っていないモジュールは持たない）。</summary>
    public IReadOnlyList<SearchModuleState> Modules { get; init; } = [];

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

    /// <summary>何も絞っていない状態。残す価値が無い（探す対象や区別を変えただけでは絞っていない）。</summary>
    [JsonIgnore]
    public bool IsEmpty => Text.Trim().Length == 0 && Modules.Count == 0;

    /// <summary>
    /// 同じ条件かを見るための指紋。
    ///
    /// **同じ検索を2回しても2行にしない。**時刻と名前は含めない——
    /// 条件が同じなら同じ検索で、上に持ち上げるだけでよい。
    /// 探す対象・区別の切り替え・別表記は結果を変えるので含める。
    /// 表示順は結果の中身を変えないが、**戻したい状態の一部**なので含める。
    /// </summary>
    [JsonIgnore]
    public string Fingerprint
    {
        get
        {
            var text = new StringBuilder();
            text.Append(Text.Trim()).Append(Separator);
            text.Append(CaseSensitive ? '1' : '0');
            text.Append(WidthSensitive ? '1' : '0');
            text.Append(KanaInsensitive ? '1' : '0');
            text.Append(SearchAlternates ? '1' : '0').Append(Separator);
            text.Append(Sort).Append(Separator);
            text.Append(string.Join(',', Targets.OrderBy(name => name, StringComparer.Ordinal))).Append(Separator);

            // 並べ替えてから繋ぐ。積んだ順が違うだけで別物にはしない
            text.Append(string.Join((char)0x1D, Modules.Select(module => module.Fingerprint).OrderBy(print => print, StringComparer.Ordinal)));
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

            parts.AddRange(Modules.Select(module => module.Summary ?? module.Kind));

            // 探す対象と区別は結果を変えるので出す。表示順は出さない（思い出す手掛かりにならない）
            if (Targets.Count > 0)
            {
                parts.Add("対象 " + string.Join(",", Targets));
            }

            if (CaseSensitive || WidthSensitive || KanaInsensitive)
            {
                parts.Add(string.Join("・", new[]
                {
                    CaseSensitive ? "大小を区別" : null,
                    WidthSensitive ? "全角半角を区別" : null,
                    KanaInsensitive ? "かなを区別しない" : null,
                }.OfType<string>()));
            }

            if (SearchAlternates)
            {
                parts.Add("別表記も");
            }

            return parts.Count == 0 ? "条件なし" : string.Join(" / ", parts);
        }
    }
}
