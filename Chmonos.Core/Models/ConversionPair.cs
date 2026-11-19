using System.Text.Json.Serialization;

namespace Chmonos.Core.Models;

/// <summary>
/// 人が足した変換の組の一覧（<c>conversion-pairs.json</c>。<c>docs/spec/conversion.md</c>「人が足す組」）。
/// プロファイルの商品を持っていない・プロファイルの無いツールで変換できる組を、人が自分で書く（ユーザ判断 2026-11-20）。
/// </summary>
public sealed record ConversionPairFile
{
    /// <summary>組。並びは人が足した順。欠けた配列（null）は空として受ける。</summary>
    public IReadOnlyList<ConversionPair> Pairs
    {
        get => _pairs;
        init => _pairs = value ?? [];
    }

    private readonly IReadOnlyList<ConversionPair> _pairs = [];
}

/// <summary>
/// 「元の衣装を、先に着せられる」の組1つ。向きは元→先の1方向（逆向きはもう1つの組として足す。「逆も」は作らない・ユーザ判断 2026-11-20）。
/// 人が書いた物なので確認は要らず、変換経由の計算にそのまま使う（推定ではない）。
/// </summary>
public sealed record ConversionPair
{
    /// <summary>元（この衣装の対応アバターの側）。手で直したファイルの null は何も指さない端として受ける。</summary>
    public ConversionEnd From
    {
        get => _from;
        init => _from = value ?? new();
    }

    /// <summary>先（変換して着せられるアバターの側）。</summary>
    public ConversionEnd To
    {
        get => _to;
        init => _to = value ?? new();
    }

    private readonly ConversionEnd _from = new();
    private readonly ConversionEnd _to = new();

    /// <summary>使うツールの名前（任意）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tool { get; init; }

    /// <summary>人が書くメモ（任意・アバターの管理の「変換」の組の行で直す）。無い前の形のファイルは null として読む。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Memo { get; init; }

    /// <summary>どちらかの端が何も指していない（読み替えで消えた・壊れた行）。計算には使わない。</summary>
    [JsonIgnore]
    public bool IsEmpty => From.IsEmpty || To.IsEmpty;

    /// <summary>元と先が同じ物を指す（統合で重なった）。意味が無いので持たない。</summary>
    [JsonIgnore]
    public bool PointsToItself => From.Same(To);

    /// <summary>同じ元と先の組か（ツールとメモは比べない。重ねて足さないため）。</summary>
    public bool SameEnds(ConversionPair other) => From.Same(other.From) && To.Same(other.To);
}

/// <summary>組の端。アバター（商品 ID）か共通素体（名前）の**どちらか**を持つ（<see cref="ConversionTarget"/> と同じ持ち方）。</summary>
public sealed record ConversionEnd
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AvatarItemId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseName { get; init; }

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(AvatarItemId) && string.IsNullOrWhiteSpace(BaseName);

    public static ConversionEnd Avatar(string itemId) => new() { AvatarItemId = itemId };

    public static ConversionEnd Base(string name) => new() { BaseName = name };

    /// <summary>同じ物を指すか。素体の名前は素体の名前の比べ方（大文字小文字を見ない）で比べる。</summary>
    public bool Same(ConversionEnd other)
        => string.Equals(AvatarItemId, other.AvatarItemId, StringComparison.Ordinal)
            && (BaseName is null ? other.BaseName is null : other.BaseName is not null && Services.AvatarBaseNames.Comparer.Equals(BaseName, other.BaseName));
}
