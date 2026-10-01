using System.Text.Json.Serialization;

namespace Chmonos.Core.Models;

/// <summary>
/// 編集キューの控え（<c>edit-session.json</c>）。
///
/// 編集は主観的な判断の連続で、途中で切り上げたくなることが多い。
/// どこまで進んだかを残しておき、次に開いたときに同じ場所から続けられるようにする。
/// itemの内容そのものは持たず、IDの並びと位置だけを持つ。
/// キューを積んだ後にitemが消えていることもあるので、読み出し側で存在を確かめる。
/// </summary>
public sealed record EditSession
{
    public IReadOnlyList<string> ItemIds { get; init; } = [];

    /// <summary>次に編集する位置。<see cref="ItemIds"/> の件数に達していれば完了。</summary>
    public int Index { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// この回で「保存して次へ」を押した商品。編集画面の上の帯で、保存した物と飛ばした物を見分けるため（ユーザ指示）。
    /// 押した事実の記録で、位置からは分からない（飛ばした物も同じ位置を通り過ぎる）。
    /// </summary>
    public IReadOnlyList<string> SavedItemIds { get; init; } = [];

    /// <summary>
    /// 位置から導かれる値なので、ファイルには書かない。
    /// 手で開いたときに、書き換えても効かない項目が並んでいると迷わせるため。
    /// </summary>
    [JsonIgnore]
    public bool IsFinished => Index >= ItemIds.Count;
}
