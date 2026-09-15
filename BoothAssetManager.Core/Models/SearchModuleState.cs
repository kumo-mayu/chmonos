using System.Text;
using System.Text.Json.Serialization;

namespace BoothAssetManager.Core.Models;

/// <summary>
/// 検索の絞り込みのモジュール1つの状態（ユーザ案 2026-09-15・`docs/history/search-redesign.md`）。
///
/// 画面の状態（<c>ui-state.json</c>）と検索の履歴の両方に同じ形で書く。**値まで覚える**（ユーザ判断 2026-09-16。
/// 前は種類だけ覚えて値は覚えなかった）。種類ごとに使う欄だけを埋め、使わない欄は既定のままにする。
/// </summary>
public sealed record SearchModuleState
{
    private const char Separator = (char)0x1F;

    /// <summary>モジュールの種類（画面の SearchModuleKind の名前）。</summary>
    public required string Kind { get; init; }

    /// <summary>追加したまま効かせているか（ユーザ案：トグルで追加状態を保ったまま無効化できる）。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>積んだ値（リストの形の条件）。</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>積んだ値を全部満たす（AND）か。false はいずれか（OR）。</summary>
    public bool MatchAll { get; init; }

    /// <summary>三択などで選んだもの・価格の元・最近の種類。</summary>
    public string? Choice { get; init; }

    /// <summary>下限（数値か日付の文字）。空は「制限しない」。</summary>
    public string? Min { get; init; }

    /// <summary>上限（数値か日付の文字）。空は「制限しない」。</summary>
    public string? Max { get; init; }

    /// <summary>補助の切り替え（販売終了の「非公開も表示する」・対応アバターの「素体経由の対応も含める」）。</summary>
    public bool Flag { get; init; }

    /// <summary>属性の幅（属性だけ）。</summary>
    public IReadOnlyList<AttributeRange> Ranges { get; init; } = [];

    /// <summary>一覧に出す1行（検索の履歴の要約に使う・見るだけ）。</summary>
    public string? Summary { get; init; }

    /// <summary>同じ条件かを見るための指紋。要約は含めない（同じ条件なら言い方が違っても同じ）。</summary>
    [JsonIgnore]
    public string Fingerprint
    {
        get
        {
            var text = new StringBuilder();
            text.Append(Kind).Append(Separator);
            text.Append(Enabled ? '1' : '0').Append(MatchAll ? '1' : '0').Append(Flag ? '1' : '0').Append(Separator);
            text.Append(string.Join((char)0x1E, Items.OrderBy(item => item, StringComparer.Ordinal))).Append(Separator);
            text.Append(Choice).Append(Separator).Append(Min).Append(Separator).Append(Max).Append(Separator);
            text.Append(string.Join(
                (char)0x1E,
                Ranges.OrderBy(range => range.Name, StringComparer.Ordinal).Select(range => $"{range.Name}:{range.Min}-{range.Max}")));
            return text.ToString();
        }
    }
}
