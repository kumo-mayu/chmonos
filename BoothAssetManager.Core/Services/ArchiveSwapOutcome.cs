namespace BoothAssetManager.Core.Services;

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
}

/// <param name="ArchiveName">見せる用のzipのファイル名。分からなければ null。</param>
public sealed record ArchiveSwapOutcome(ArchiveSwapResult Result, string? ArchiveName);
