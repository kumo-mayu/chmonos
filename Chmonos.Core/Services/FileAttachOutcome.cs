namespace Chmonos.Core.Services;

/// <summary>商品ページからファイルを商品に結んだ結果。画面の1行と窓を書き分けるために、何が起きたかを返す。</summary>
public enum FileAttachResult
{
    /// <summary>この商品に結んだ（前に外していた物なら、外した印を下ろした）。</summary>
    Attached,

    /// <summary>この場所のこのファイルは、もうこの商品に結んである。何も書いていない。</summary>
    AlreadyAttached,

    /// <summary>選んだ場所にファイルが無い（窓で選んだ後に移した・消した）。</summary>
    FileMissing,

    /// <summary>ファイルを読めなかった（ほかのアプリが開いている・権限）。</summary>
    FileUnreadable,

    /// <summary>取り込む種類のファイルではない（単体の unitypackage など）。何も書いていない。</summary>
    NotTarget,

    /// <summary>商品がもう無い。</summary>
    ItemMissing,

    /// <summary>
    /// 管理対象から除外している。何も書いていない。
    /// 除外も人が決めたことなので、聞かずには解かない（「zipで登録し直す」と同じ。ユーザ判断 2026-10-05）。
    /// </summary>
    Excluded,

    /// <summary>
    /// ほかの商品が持っている（外していない）。何も書いていない。持ち主は <see cref="FileAttachOutcome.Holders"/>。
    /// 同じファイルを2つの商品の持ち物にすると容量も二重に数えるので、聞かずには付けない。
    /// </summary>
    OwnedElsewhere,
}

/// <param name="FileName">見せる用のファイル名。</param>
public sealed record FileAttachOutcome(FileAttachResult Result, string FileName)
{
    /// <summary><see cref="FileAttachResult.OwnedElsewhere"/> のときの持ち主。ほかの結果では空。</summary>
    public IReadOnlyList<ArchiveHolder> Holders { get; init; } = [];
}
