using System.Text.Json.Serialization;

namespace Chmonos.Core.Models;

/// <summary>
/// BOOTH で見つけた「中身の更新」の控え（使っている商品の更新の促し・<c>docs/spec/used-updates.md</c>）。商品ごとに最後の1件。
///
/// 手元のファイルには「いつ取り込んだか」の記録が無いので、**見つけた時点で持っていたファイル**をここに控える。
/// 控えに無いファイルが後から入れば、新しい版を取り込んだと分かる（<see cref="LocalBlock.HasFilesNewerThanUpdate"/>）。
/// 控えは後から計算で作れない（その時に何を持っていたか）ので記録に書く。
/// </summary>
public sealed record ContentUpdate
{
    /// <summary>見つけた日時（⑦か人が押した取り直しで、促す更新だと分かった時）。</summary>
    public required DateTimeOffset FoundAt { get; init; }

    /// <summary>見つけた時点で持っていたファイルのハッシュ（外した物・古い版は入れない）。</summary>
    public IReadOnlyList<string> FilesBefore { get; init; } = [];

    /// <summary>
    /// 特定のアバター向けの更新なら、その対応アバターの商品ID（更新履歴の行が全部その名前を含むとき・ユーザ判断 2026-11-09）。
    /// 全部向けなら null で、書き出さない。<see cref="ForBases"/> と合わせて見る。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ForAvatars { get; init; }

    /// <summary>特定の共通素体向けの更新なら、その素体の名前。全部向けなら null で、書き出さない。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ForBases { get; init; }

    /// <summary>特定のアバター・素体向けか（計算で出せるので書き出さない）。</summary>
    [JsonIgnore]
    public bool IsTargeted => ForAvatars is { Count: > 0 } || ForBases is { Count: > 0 };

    /// <summary>商品ページの「確認した」を押した日時。押していなければ null で、書き出さない。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CheckedAt { get; init; }
}
