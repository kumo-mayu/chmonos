namespace BoothAssetManager.Core.Services;

/// <summary>
/// 失敗を画面で言うときの、**原因の見当**（人が読める言葉）。
///
/// 前は .NET の例外の文（<c>exception.Message</c>）をそのまま出していた。英語や内部のパス・型の名前が混ざり、
/// 読んでも次に何をすればよいか分からない（`docs/spec/ui-empty-and-errors.md`「利用者に見せない文」）。
/// 詳しい中身はログ（保存先の logs\app.log）へ書き、画面には見当と次の一手だけを出す。
/// 見当は種類から言えることだけにする（断定しない。当てはまらないこともあるので「〜ことがあります」で結ぶ）。
/// </summary>
public static class FailureText
{
    // Windows のエラー番号（HRESULT の下16ビット）。IOException は種類が1つなので番号で見分ける
    private const int SharingViolation = 32;
    private const int LockViolation = 33;
    private const int DiskFull = 112;
    private const int HandleDiskFull = 39;

    /// <summary>画面に出す原因の見当（1文。句点まで含む）。</summary>
    public static string Cause(Exception exception) => exception switch
    {
        UnauthorizedAccessException =>
            "読み書きの権限がありません。読み取り専用か、管理者の権限が要る場所のことがあります。",
        FileNotFoundException or DirectoryNotFoundException or DriveNotFoundException =>
            "ファイルかフォルダが見つかりません。削除・移動したか、外付けやネットワークのドライブがつながっていないことがあります。",
        PathTooLongException =>
            "パスが長すぎて扱えません。浅いフォルダへ移すと扱えることがあります。",
        InvalidDataException =>
            "ファイルが壊れているか、読めない形式です。ダウンロードし直すと直ることがあります。",
        System.Text.Json.JsonException =>
            "保存先の記録（JSON）が壊れていて読めません。手で直した記録があれば、書き方を確かめてください。",
        System.Net.Http.HttpRequestException =>
            "BOOTHへ届きませんでした。通信を確かめて、少し待ってからもう一度お試しください。",
        IOException io when (io.HResult & 0xFFFF) is SharingViolation or LockViolation =>
            "別のアプリがファイルを開いています。閉じてからもう一度お試しください。",
        IOException io when (io.HResult & 0xFFFF) is DiskFull or HandleDiskFull =>
            "ディスクの空きが足りません。空きを作ってからもう一度お試しください。",
        IOException =>
            "ファイルの読み書きに失敗しました。別のアプリが開いているか、ドライブがつながっていないことがあります。",
        _ => "予期しないエラーが発生しました。",
    };

    /// <summary>
    /// 問い合わせを打ち切った理由の見当（1文。句点まで含む）。打ち切っていなければ null。
    /// 対応アバターの検出の結果（アバターの管理・取り込みの③）で同じ言い方にするため1か所に置く。
    /// つながっていないときに「BOOTHの不調」と言うと、待っても直らない物を待たせる（ユーザ判断 2026-09-29）
    /// </summary>
    public static string? Outage(Booth.BoothOutageKind kind) => kind switch
    {
        Booth.BoothOutageKind.Offline => "ネットにつながっていないようです。",
        Booth.BoothOutageKind.ServerDown => "BOOTHが不調のようです。",
        _ => null,
    };
}
