namespace Chmonos.Core.Services;

/// <summary>展開フォルダをzipへ切り替えた結果。画面の1行を書き分けるために、何が起きたかを返す。</summary>
public enum ArchiveSwapResult
{
    /// <summary>zipを商品に付け、フォルダの登録を外した。</summary>
    Registered,

    /// <summary>zipは既に付いていた。フォルダの登録だけ外した。</summary>
    AlreadyRegistered,

    /// <summary>隣にzipが見つからなかった（移動・改名・外付けを外している）。</summary>
    ArchiveMissing,

    /// <summary>zipが読めなかった（使用中・権限）。</summary>
    ArchiveUnreadable,

    /// <summary>商品がもう無い。</summary>
    ItemMissing,

    /// <summary>
    /// zip を管理対象から除外している。何も書いていない。
    /// 除外も人が決めたことなので、聞かずには解かない（画面が窓で聞いて、解除を頼んで呼び直す。ユーザ判断 2026-10-05）。
    /// </summary>
    Excluded,

    /// <summary>
    /// ほかの商品が zip を持っている（外していない）。何も書いていない。持ち主は <see cref="ArchiveSwapOutcome.Holders"/>。
    /// 持ち主は人が決めたことなので、聞かずには増やしも替えもしない（画面が「その商品を開く」「両方に付ける」「この商品に付け直す」を聞く。
    /// ユーザ判断 2026-10-05・2026-11-11）。
    /// </summary>
    OwnedElsewhere,
}

/// <summary>
/// 付けるファイルをほかの商品が持っていたときにどうするか（<see cref="LocalFileEditor.AttachFileAsync"/>・
/// <see cref="LocalFileEditor.SwapFolderForArchiveAsync"/>）。人が窓で選んだ答え。
/// 2つの真偽で持つと「付け直す」と「残す」を両方立てられてしまうので、1つの値にする。
/// </summary>
public enum OtherHolders
{
    /// <summary>決めていない。ほかの商品が持てば何も書かずに <c>OwnedElsewhere</c> で返す（画面が聞く）。</summary>
    Ask,

    /// <summary>ほかの商品から外して（外した印を付けて）、この商品に付ける（「この商品に付け直す」）。</summary>
    TakeOver,

    /// <summary>
    /// ほかの商品にも残したまま、この商品にも付ける（「両方に付ける」・ユーザ判断 2026-11-11 plan 27）。
    /// セット商品と中身の各商品のように、1つのファイルが2つ以上の商品の物であるときに使う。
    /// 容量の合計は商品をまたいで同じ中身・同じ場所を1回だけ数える（<see cref="LibrarySizes"/>）。
    /// </summary>
    Share,
}

/// <param name="ArchiveName">見せる用のzipのファイル名。分からなければ null。</param>
public sealed record ArchiveSwapOutcome(ArchiveSwapResult Result, string? ArchiveName)
{
    /// <summary><see cref="ArchiveSwapResult.OwnedElsewhere"/> のときの持ち主。ほかの結果では空。</summary>
    public IReadOnlyList<ArchiveHolder> Holders { get; init; } = [];
}

/// <summary>zip を持っているほかの商品。</summary>
/// <param name="LosesLastFile">付け直す（<see cref="OtherHolders.TakeOver"/>）と、この商品の手元の物（外していないファイルとフォルダ）が無くなるか。窓で先に言うため。</param>
public sealed record ArchiveHolder(string ItemId, string Name, bool LosesLastFile);
