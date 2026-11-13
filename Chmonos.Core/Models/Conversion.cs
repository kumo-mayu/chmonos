using System.Text.Json.Serialization;

namespace Chmonos.Core.Models;

/// <summary>
/// 商品を、衣装の変換ツールのプロファイルとして扱うか（<c>docs/spec/conversion.md</c>「データの形」）。
/// 「アバター扱い」（アバターかどうか）とは**別に**決める（ユーザ 2026-11-06）。
/// </summary>
public enum ConversionMode
{
    /// <summary>自動の判定に任せる（既定）。中のファイルにプロファイルがあれば、プロファイルとして扱う。</summary>
    Auto,

    /// <summary>人がプロファイルとして扱うと決めた。</summary>
    Profile,

    /// <summary>人がプロファイルとして扱わないと決めた（中にプロファイルのファイルがあっても）。</summary>
    NotProfile,
}

/// <summary>
/// 商品1つの「変換扱い」（<see cref="LocalBlock.Conversion"/>）。人が決めた値と、自動で推した値（<see cref="ConversionTarget.Confirmed"/> が偽）を持つ。
/// 既定（自動に任せて、つなぎ先も無い）の商品は書き出さない。
/// </summary>
public sealed record ConversionSetting
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ConversionMode Mode { get; init; }

    /// <summary>つなぎ先。中のプロファイル1つ（slug）につき、ふつうは1行。</summary>
    public IReadOnlyList<ConversionTarget> Targets { get; init; } = [];

    /// <summary>何も決めていない（自動に任せて、つなぎ先も無い）。書き出さない値。</summary>
    [JsonIgnore]
    public bool IsDefault => Mode == ConversionMode.Auto && Targets.Count == 0;
}

/// <summary>
/// プロファイルのつなぎ先1つ。アバター（商品 ID）か共通素体（名前）の**どちらか**を持つ。
///
/// つなぎ先を共通素体にした物は、その素体を使うアバター全員に効く（ユーザ判断 2026-11-06「素体が同じであれば動くことがほとんど」）。
/// アバターにした物は、そのアバターにだけ効き、素体の仲間には広げない。
/// </summary>
public sealed record ConversionTarget
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AvatarItemId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseName { get; init; }

    /// <summary>順方向：ほかのアバターの衣装を、つなぎ先に合わせられる。</summary>
    public bool Forward { get; init; }

    /// <summary>逆方向：つなぎ先の衣装を、ほかのアバターに合わせられる。</summary>
    public bool Reverse { get; init; }

    /// <summary>中のプロファイルの名前（ツールが付ける短い名前）。分かれば。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Slug { get; init; }

    /// <summary>
    /// 人が決めたか。自動で推した値は偽で持ち、変換経由の計算には**確認したものだけ**を使う（推定を勝手に入れない）。
    /// 設定「確認していない変換も含める」（既定は切）を入れたときだけ、偽の物も使う
    /// </summary>
    public bool Confirmed { get; init; }

    /// <summary>
    /// 人が外した（ユーザ判断 2026-11-09「ユーザが外したんですから次からも外れているべき」）。行は残し、見回りが同じつなぎ先を
    /// また推さないようにする。変換経由の計算には使わない。対応アバターの <see cref="AvatarLink.Rejected"/> と同じ考え方。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Rejected { get; init; }

    /// <summary>つなぎ先が無い（アバターも素体も持たない）。</summary>
    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(AvatarItemId) && string.IsNullOrWhiteSpace(BaseName);

    /// <summary>
    /// 画面に出した行と、今の値の行が同じ物か。確認済みかと向きは比べない（確かめる・向きを変える操作は、その2つを変えるため）。
    /// データの形の側に置く：別の物を指す欄の表（<see cref="LocalReferences"/>）も使うので、前のようにサービスの側に置くと、データの形がサービスを呼ぶ向きになる
    /// </summary>
    public static bool SameRow(ConversionTarget a, ConversionTarget b)
        => string.Equals(a.AvatarItemId, b.AvatarItemId, StringComparison.Ordinal)
            && SameText(a.BaseName, b.BaseName)
            && SameText(a.Slug, b.Slug);

    /// <summary>
    /// 素体の統合・商品 ID のまとめで同じ行（<see cref="SameRow"/>）が2つ以上になったとき、1行に畳む（Codex の点検 2026-11-13 件1）。
    /// 前は配列の先頭を残していた。<see cref="Services.ConversionEdits.Choose"/> は外した行を残して選んだ行を末尾へ足すので、
    /// 「A を外す→B を選ぶ→A を B へ統合」で、後から人が選んだ B が消え、外した印だけが残った。
    ///
    /// 残す行の決め方（<c>docs/spec/conversion.md</c>「統合で重なった行」）：
    /// 1. 選んだ行（確認済みで外していない）＞外した行＞推した行（確認待ち）。人が選んだ物を、外した印や推定で消さない。
    ///    外した行は確認済みも立つ（<see cref="Services.ConversionEdits.Reject"/>）ので、確認済みだけでは比べない
    /// 2. 同じ順位なら統合先の行（<paramref name="rows"/> の <c>Moved</c> が偽＝書き換えで値が変わらなかった行）。
    ///    向きなど <see cref="SameRow"/> が見ない欄が食い違っても混ぜず、残した行の値をそのまま使う（足し合わせると、人が選んでいない向きが入る）
    /// 3. それでも並ぶなら前の行。
    /// 残した行は、重なった行のうち一番前の位置に置く（ほかの行の並びを動かさない）。
    /// </summary>
    public static List<ConversionTarget> Fold(IEnumerable<(ConversionTarget Row, bool Moved)> rows)
    {
        var groups = new List<List<(ConversionTarget Row, bool Moved)>>();
        foreach (var row in rows)
        {
            var group = groups.FirstOrDefault(kept => SameRow(kept[0].Row, row.Row));
            if (group is null)
            {
                groups.Add([row]);
            }
            else
            {
                group.Add(row);
            }
        }

        // OrderBy は安定なので、順位と統合先かが並んだら前の行が残る（3.）
        return [.. groups.Select(group => group
            .OrderBy(row => Rank(row.Row))
            .ThenBy(row => row.Moved)
            .First().Row)];
    }

    /// <summary>畳むときの順位。小さいほど残す。</summary>
    private static int Rank(ConversionTarget row)
        => row.Rejected ? 1 : row.Confirmed ? 0 : 2;

    /// <summary>素体の名前と中のプロファイルの名前は、大文字小文字を区別せずに比べる（手で書いた JSON の揺れを同じ行として扱う）。</summary>
    internal static bool SameText(string? a, string? b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
