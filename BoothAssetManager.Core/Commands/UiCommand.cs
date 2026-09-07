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
}

/// <summary>コマンドの実行結果。</summary>
public abstract record CommandResult
{
    private protected CommandResult() { }

    public record Imported(ImportSummary Summary) : CommandResult;

    public record ItemSaved(string ItemId) : CommandResult;

    public record Done : CommandResult;

    public record Failed(string Message) : CommandResult;
}
