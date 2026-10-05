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
    /// 同じファイルを2つの商品の持ち物にすると容量も二重に数えるので、聞かずには付けない
    /// （画面が「その商品を開く」か「こちらに付け直す」を聞く。ユーザ判断 2026-10-05）。
    /// </summary>
    OwnedElsewhere,
}

/// <param name="ArchiveName">見せる用のzipのファイル名。分からなければ null。</param>
public sealed record ArchiveSwapOutcome(ArchiveSwapResult Result, string? ArchiveName)
{
    /// <summary><see cref="ArchiveSwapResult.OwnedElsewhere"/> のときの持ち主。ほかの結果では空。</summary>
    public IReadOnlyList<ArchiveHolder> Holders { get; init; } = [];
}

/// <summary>zip を持っているほかの商品。</summary>
/// <param name="LosesLastFile">付け直すと、この商品の手元の物（外していないファイルとフォルダ）が無くなるか。窓で先に言うため。</param>
public sealed record ArchiveHolder(string ItemId, string Name, bool LosesLastFile);
