namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 主の窓の商品ページの、流した位置（ユーザ判断 2026-09-30。ショップの画面と同じ決まり）。
///
/// - 進むとき（検索・一覧・説明の中のリンクから開く）は先頭から
/// - 戻る・進むで来たときは、離れたときの位置へ
/// - 同じ商品の開き直し（取り直した後・ファイルを外した後・IDを変えた後）は、今の位置を保つ
///
/// 前は位置を覚える仕組みが無く、使い回された画面に前の商品の位置が残っているだけだった
/// （AからBへ移るとBもAの位置で出て、Bで流して戻るとAはBの位置で出た）。
///
/// 置き場はショップの画面と同じ：**位置は画面（View）しか知らないので、読む手順を View が預け、画面の履歴が控えに入れ、
/// 戻る・進むで開き直したときだけ新しい画面へ渡す。**保存先には書かない（アプリを閉じれば消える）。
/// 履歴から控えが消えれば、位置も一緒に消える。
///
/// ショップの一覧は「先頭に見えていた項目」で覚える（<see cref="ListAnchor"/>）が、ここは流した量（px）で覚える。
/// 商品ページは仮想化していない1枚の縦長で、列の数が幅で変わることも、開き直した直後に1列で組まれることも無い。
/// 目印にできる項目（説明の見出し）は、説明の無い商品には無い。窓の幅を変えてから戻ると折り返しが変わってずれるが、
/// 前（位置が残るだけ）も同じだった
/// </summary>
public sealed partial class ItemViewModel
{
    private double? _restoreScrollOffset;

    /// <summary>View が今の流した位置を読む手順（位置は View しか知らない）。</summary>
    public Func<double>? ScrollReader { get; set; }

    /// <summary>
    /// 同じ商品の開き直しで出す画面か。前の画面の流した位置をそのまま保つ（見ていた所が先頭へ飛ばないように）。
    /// </summary>
    public bool KeepsScrollPosition { get; init; }

    /// <summary>
    /// 戻る・進むで開き直したときの、離れたときの位置。**画面へ差し込む前に入れる**
    /// （View は差し込まれた時点で読む。ショップの一覧は読み終えてから当てるので後から預けられるが、ここは待つ物が無い）。
    /// </summary>
    public double? RestoreScrollOffset
    {
        init => _restoreScrollOffset = value;
    }

    /// <summary>戻る・進むで来た画面か（位置が先頭でも true）。</summary>
    public bool ArrivedByHistory => _restoreScrollOffset is not null;

    /// <summary>離れるときの位置。画面の履歴が控えに入れる。View が付いていなければ先頭。</summary>
    public double CaptureScrollOffset() => ScrollReader?.Invoke() ?? 0;

    /// <summary>
    /// 預かった位置を1回だけ渡す。View が作り直されて同じ画面がもう一度差し込まれても、
    /// 人がその後に流した位置から引き戻さないように
    /// </summary>
    public double? TakeRestoreScrollOffset()
    {
        var offset = _restoreScrollOffset;
        _restoreScrollOffset = null;
        return offset;
    }
}
