using BoothAssetManager.Core.Scanning;

namespace BoothAssetManager.Core.Commands;

/// <summary>
/// UIからバックエンドへの依頼。UIはこれを組み立てて渡すだけで、実処理には触れない。
/// 継承をこのファイル内に閉じているので、ケースの一覧はここを見れば分かる。
///
/// 参照実装（vrc-osc-recorder）と違い、結果は相関IDとポーリングではなく戻り値で返す。
/// あちらはOSC受信という継続的な外部イベント源のために非同期のチャネルが要ったが、
/// こちらの操作は全て「ユーザの操作に対する応答」なので、その仕組みは要らない。
/// </summary>
public abstract record UiCommand
{
    private protected UiCommand() { }

    /// <summary>指定フォルダを取り込む（走査 → BoothID解決 → BOOTH取得）。</summary>
    public record ScanFolders(IReadOnlyList<string> Folders) : UiCommand;

    /// <summary>未確定ファイルに商品IDを与えて確定させる。</summary>
    public record AssignItemId(string Hash, string ItemId) : UiCommand;

    /// <summary>1件のitemをBOOTHから取り直す。</summary>
    public record RefreshItem(string ItemId) : UiCommand;

    /// <summary>ファイルを管理対象から外す。再スキャンで未確定に出てこなくなる。</summary>
    public record ExcludeFile(string Hash, IReadOnlyList<string> Paths, string? Reason = null) : UiCommand;

    /// <summary>アーカイブの展開先フォルダを削除する。展開元のzipが残っていることを確かめてから消す。</summary>
    public record RemoveUnpackedFolders(IReadOnlyList<UnpackedFolder> Folders) : UiCommand;

    /// <summary>編集画面の入力を保存する。<c>local</c> ブロックだけを差し替える。</summary>
    public record SaveItemLocal(string ItemId, Models.LocalBlock Local) : UiCommand;

    /// <summary>appTagをマスタへ追加する。<paramref name="Sub"/> を省くとトップだけを足す。</summary>
    public record AddAppTag(string Top, string? Sub = null) : UiCommand;

    /// <summary>属性をマスタへ追加する。</summary>
    public record AddAttribute(string Name) : UiCommand;

    /// <summary>確定する前にIDの中身を見る。既に持っていればBOOTHへは行かない。</summary>
    public record PreviewItem(string ItemId) : UiCommand;

    /// <summary>手掛かりの無いファイルについて、BOOTH内検索から候補を出す。</summary>
    /// <summary>
    /// 手掛かりの無いファイルについて、BOOTH内検索から候補を出す。
    /// このコマンドだけ進捗の受け口を持つ。取得を1件ずつ間隔を空けて行うため
    /// 待ち時間が長く、黙って待たせるわけにいかないため。
    /// </summary>
    public record ProposeCandidates(string FilePath, IProgress<Resolution.ResolveProgress>? Progress = null) : UiCommand;

    /// <summary>フォルダを商品に紐付ける。zipが手元に無く展開したものだけが残っている場合に使う。</summary>
    public record RegisterFolder(string ItemId, string FolderPath) : UiCommand;
}

/// <summary>コマンドの実行結果。</summary>
public abstract record CommandResult
{
    private protected CommandResult() { }

    public record Imported(ImportSummary Summary) : CommandResult;

    public record ItemSaved(string ItemId) : CommandResult;

    public record Done : CommandResult;

    public record UnpackedFoldersRemoved(IReadOnlyList<UnpackedFolderRemoval> Results) : CommandResult;

    public record AppTagsChanged(Models.AppTagMaster Master) : CommandResult;

    public record AttributesChanged(Models.AttributeMaster Master) : CommandResult;

    public record PreviewLoaded(Services.ItemPreview Preview) : CommandResult;

    public record CandidatesProposed(IReadOnlyList<Resolution.ResolutionCandidate> Candidates) : CommandResult;

    public record Failed(string Message) : CommandResult;
}
