using System.Text.RegularExpressions;

namespace Chmonos.Core.Services;

/// <summary>取り込み画面を閉じた後、いまどこにいるか。</summary>
public enum UnityImportState
{
    /// <summary>まだ決められない。待つ。</summary>
    Waiting,

    /// <summary>Import が押され、後処理まで終わった。次を出してよい。</summary>
    Imported,

    /// <summary>Cancel が押された（または閉じられた）。何も入っていない。次を出してよい。</summary>
    Cancelled,

    /// <summary>
    /// 送った物は既に全部プロジェクトに入っていた（Unity は取り込み画面に「Nothing to import!」しか出さない）。次を出してよい。
    /// 送ることの目的（プロジェクトに入っていること）は果たせているので、Cancel ではなく送れたと数える（ユーザ判断 2026-09-19）
    /// </summary>
    AlreadyPresent,
}

/// <summary>
/// 連続で送るとき（#69）、1件の取り込み画面が閉じた後に「次を出してよいか」を決める。
/// 画面にもファイルにも触らず、渡された出来事だけで決めるので試験できる。
///
/// **なぜ要るか：**Packages/ に入る物は、取り込み画面を閉じた後もコンパイルと読み込み直しを9秒ほど続け、
/// その間に次の取り込み画面を開いておくと、見た目はそのままで中身が抜け、Import しても何も入らない（実機で 0/35 §11-2）。
/// 窓が静かになるのを待つだけでは、閉じてから後処理の窓が出るまでの1.2〜1.4秒の隙で取り違え得た。
///
/// **決め方（§11-3・実機で確かめた）：**
/// <list type="number">
/// <item>送った物のパスが <c>Start importing</c> に出るか、送り先のエディタに新しい窓が出たら「動いた」</item>
/// <item>動かないまま <see cref="CancelQuiet"/> たったら Cancel。Cancel では Unity は1行も書かない</item>
/// <item>動いた後、完了の行（<c>Asset Pipeline Refresh … ForceSynchronousImport</c>）が出て、
///       新しい窓が <see cref="CalmAfterDone"/> 出ていなければ Import</item>
/// </list>
///
/// **誰の行かを見分ける：**Editor.log は開いている全エディタが1つのファイルに書く（Unity 2022.3）。
/// 取り込みと関係のない行（<c>TrimDiskCacheJob</c> など）も混じり、これを「動いた」と数えると Cancel を取り違えて待ち続けた。
/// なので「動いた」は、送った物のパスと、送り先のエディタの窓だけで決める。
/// 完了の行そのものには誰の物かが入っていないが、送信は1列に限る（ユーザ判断）ので、動いた後の完了の行は送り先の物とみなす。
/// </summary>
public sealed class UnityImportWatch
{
    /// <summary>
    /// 閉じてから何も動かなければ Cancel とみなすまでの時間。
    /// Import なら閉じてから1.4秒以内に必ず何かが動いた（3種のパッケージで実測）。余裕を見て5秒。
    /// </summary>
    public static readonly TimeSpan CancelQuiet = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 既に全部入っている物の窓が閉じてから、「既に入っていた」と決めるまで。
    /// その窓は「Nothing to import!」で Import が無いので本来は待たなくてよいが、
    /// 調べた後に中身が変わっていて取り込みが起きたときに拾えるよう、Import で何かが動くまでの最長（1.4秒・実測）より少し長く待つ
    /// </summary>
    public static readonly TimeSpan NothingQuiet = TimeSpan.FromSeconds(2);

    /// <summary>完了の行の後、進捗の窓が消えてから次を出すまで。完了の行の直後に「Hold on」が一瞬出るため。</summary>
    public static readonly TimeSpan CalmAfterDone = TimeSpan.FromSeconds(1);

    /// <summary>
    /// ログが1行も来ないまま窓だけが動いたとき、窓が静かになってから終わりとみなすまで。
    /// Unity 6.5 からログはプロジェクトごとに分かれ、全体のログには行が来ない。完了の行を待ち続けて止まらないための控え。
    /// </summary>
    public static readonly TimeSpan CalmWithoutLog = TimeSpan.FromSeconds(5);

    private static readonly Regex CompletionLine = new(
        @"Asset Pipeline Refresh \(id=[0-9a-f]+\): Total: [\d.]+ seconds - Initiated by RefreshV2\(ForceSynchronousImport\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string StartImportingPrefix = "Start importing ";
    private const string GuidMarker = " using Guid(";

    private readonly HashSet<string> _expected;
    private DateTime? _closedAt;
    private DateTime? _movedAt;
    private DateTime? _doneAt;
    private DateTime? _calmSince;
    private bool _sawLogLine;
    private bool _alreadyInProject;

    /// <param name="expectedAssetPaths">送った物の中身のパス（<see cref="UnityHandoff.ReadAssetPaths"/>）。</param>
    public UnityImportWatch(IEnumerable<string> expectedAssetPaths)
    {
        _expected = new HashSet<string>(expectedAssetPaths, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>送った物の取り込みの行を見たか。最後の知らせで「取り込んだ」と言い切ってよいかに使う。</summary>
    public bool SawOwnImportLine { get; private set; }

    /// <summary>取り込み画面が閉じた。それより前の出来事は数えない（利用者が画面を眺めている間）。</summary>
    public void DialogClosed(DateTime at) => _closedAt ??= at;

    /// <summary>
    /// 送る物の中身が、プロジェクトに全部あると分かっている（<see cref="UnityProjectMatcher"/> で送る前に調べる）。
    /// このとき Unity は取り込み画面に「Nothing to import!」しか出さず、OK で閉じても1行も書かないので、
    /// 何も動かないまま閉じられたら Cancel ではなく「既に入っていた」。Cancel と見分けるための
    /// <see cref="CancelQuiet"/> を待たずに <see cref="NothingQuiet"/> で次へ進む
    /// </summary>
    public void AlreadyInProject() => _alreadyInProject = true;

    /// <summary>Editor.log に増えた1行。</summary>
    public void LogLine(string line, DateTime at)
    {
        if (_closedAt is null)
        {
            return;
        }

        _sawLogLine = true;
        if (IsOwnImportLine(line))
        {
            SawOwnImportLine = true;
            _movedAt ??= at;
        }
        else if (_movedAt is not null && _doneAt is null && CompletionLine.IsMatch(line))
        {
            _doneAt = at;
        }
    }

    /// <summary>送り先のエディタに、送る前には無かった窓（進捗・確認）が見えているか。</summary>
    public void Windows(bool anyNewWindow, DateTime at)
    {
        if (_closedAt is null)
        {
            return;
        }

        if (anyNewWindow)
        {
            _movedAt ??= at;
            _calmSince = null;
        }
        else
        {
            _calmSince ??= at;
        }
    }

    public UnityImportState Evaluate(DateTime now)
    {
        if (_closedAt is not { } closed)
        {
            return UnityImportState.Waiting;
        }

        if (_movedAt is null)
        {
            if (_alreadyInProject)
            {
                return now - closed >= NothingQuiet ? UnityImportState.AlreadyPresent : UnityImportState.Waiting;
            }

            return now - closed >= CancelQuiet ? UnityImportState.Cancelled : UnityImportState.Waiting;
        }

        if (_calmSince is not { } quiet)
        {
            // 進捗や確認の窓が今も出ている
            return UnityImportState.Waiting;
        }

        if (_doneAt is { } done)
        {
            // 静かさは「完了の行」と「窓が消えた時」の遅い方から数える。完了の行の直後に「Hold on」が一瞬出るので、
            // 前から静かだった分を足すと、その窓が出る前に次を出してしまう
            var from = quiet > done ? quiet : done;
            return now - from >= CalmAfterDone ? UnityImportState.Imported : UnityImportState.Waiting;
        }

        return !_sawLogLine && now - quiet >= CalmWithoutLog ? UnityImportState.Imported : UnityImportState.Waiting;
    }

    /// <summary><c>Start importing Assets/…/Pen.asset using Guid(…)</c> のパスが、送った物の中身か。</summary>
    private bool IsOwnImportLine(string line)
    {
        var start = line.IndexOf(StartImportingPrefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        start += StartImportingPrefix.Length;
        var end = line.IndexOf(GuidMarker, start, StringComparison.Ordinal);
        var path = (end < 0 ? line[start..] : line[start..end]).Trim();
        return _expected.Contains(path);
    }
}
