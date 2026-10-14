namespace Chmonos.Query;

/// <summary>開いている Unity のエディタ（送り先を選ぶため）。<see cref="ProjectPath"/> は Hub・VCC の一覧で言い当てられたときだけ入る。</summary>
public sealed record UnityEditorRef(int ProcessId, string? ProjectName, string? ProjectPath);

/// <summary>
/// Unity へ送る要求。送る物（<see cref="Entry"/>＝zip の中のパス、<see cref="ZipHash"/>＝どの zip か）は、商品の unitypackage が1つなら省ける。
/// 送り先（<see cref="Project"/>＝プロジェクトのフォルダ）は、開いているエディタが1つなら省ける
/// </summary>
public sealed record UnitySendRequest(string? Entry, string? ZipHash, string? Project);

/// <summary>
/// Unity へ送る送りと、その1件ずつの状態の値。**取りうる値はここだけで決める**（API の答え・変化の知らせ・説明文が同じ物を指す）。
/// </summary>
public static class UnitySendStates
{
    /// <summary>まだ送っていない（連続送りで前の物を待っている）。1件ずつの状態だけ。</summary>
    public const string Queued = "queued";

    /// <summary>送っている（取り出し中・Unity の取り込みの窓で人が選んでいる間・後処理の間）。</summary>
    public const string Sending = "sending";

    /// <summary>取り込んだ（Import が押され、後処理まで終わった）。</summary>
    public const string Imported = "imported";

    /// <summary>送った物は既に全部プロジェクトに入っていた。送る目的は果たせているので、送れたと数える。</summary>
    public const string AlreadyPresent = "alreadyPresent";

    /// <summary>Unity の取り込みの窓で取り消された（何も入っていない）。</summary>
    public const string Cancelled = "cancelled";

    /// <summary>止めた（画面の「中止」か、API の止める道）。Unity に取り込みの窓が残っていることがある。</summary>
    public const string Stopped = "stopped";

    /// <summary>送っている途中で Unity が閉じられた（何も入っていないとみなす）。</summary>
    public const string EditorClosed = "editorClosed";

    /// <summary>送れなかった（problem に理由）。</summary>
    public const string Failed = "failed";

    /// <summary>送らなかった（改変の使ったもので、手元に送れる物が無い行）。1件ずつの状態だけ。</summary>
    public const string Skipped = "skipped";

    /// <summary>説明文に並べる順。</summary>
    public static readonly IReadOnlyList<string> All = [Queued, Sending, Imported, AlreadyPresent, Cancelled, Stopped, EditorClosed, Failed, Skipped];

    /// <summary>入った（「使った」の足跡を付ける）か。</summary>
    public static bool IsTaken(string state) => state is Imported or AlreadyPresent;

    /// <summary>終わった状態か。</summary>
    public static bool IsEnded(string state) => state is not (Queued or Sending);

    /// <summary>
    /// 送り全体の状態を1件ずつの状態から決める。止めた・閉じた・送れなかったが1件でもあればそれ（先の物ほど強い。人が止めたのを失敗と言わない）、
    /// 無ければ取り込んだ物があれば imported、既に入っていた物があれば alreadyPresent、それ以外（全部取り消し）は cancelled。
    /// 送らなかった行は数えない。1件の送りは、その1件の状態と同じになる
    /// </summary>
    public static string Overall(IEnumerable<string> steps)
    {
        var states = steps.Where(state => state != Skipped).ToList();
        if (states.Any(state => !IsEnded(state)))
        {
            return Sending;
        }

        foreach (var strong in new[] { Stopped, EditorClosed, Failed, Imported })
        {
            if (states.Contains(strong))
            {
                return strong;
            }
        }

        return states.Contains(AlreadyPresent) ? AlreadyPresent
            : states.Count > 0 ? Cancelled
            : Failed;
    }
}

/// <summary>送りの中の1件。</summary>
/// <param name="Index">改変の使ったものの中の位置（改変から送ったときだけ。外した行も数える）。</param>
/// <param name="Package">表示用の名前（拡張子なし）。送らなかった行は null。</param>
/// <param name="Entry">zip の中のパス（頼んだ物・決めた物そのまま）。</param>
/// <param name="ZipHash">どの zip か（登録したフォルダの中の物は null）。</param>
/// <param name="State"><see cref="UnitySendStates"/> の値。</param>
public sealed record UnitySendStep(int? Index, string ItemId, string? Package, string? Entry, string? ZipHash, string State, string? Problem);

/// <summary>
/// Unity へ送る送り1つ（1件の送りも、改変の使ったものをまとめて送る送りも同じ形）。
/// 1件の送りでは上の <see cref="ItemId"/>・<see cref="Package"/>・<see cref="Entry"/>・<see cref="ZipHash"/> に頼んだ物が入り、
/// 改変から送ったときは <see cref="ModificationId"/> が入って、送る物は <see cref="Steps"/> にだけ並ぶ
/// </summary>
/// <param name="SendId">送りの ID（<c>/v1/unity/sends/{sendId}</c>・変化の知らせの sendId）。</param>
/// <param name="State">送り全体の状態（<see cref="UnitySendStates.Overall"/>）。</param>
public sealed record UnitySendStatus(
    string SendId,
    string? ItemId,
    string? ModificationId,
    string? Package,
    string? Entry,
    string? ZipHash,
    string Project,
    string State,
    string? Problem,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<UnitySendStep> Steps);

/// <summary>改変から送るときに、選ぶ必要がある行で選んだ物（同じ行に2つ選べば2つとも送り、2行として記録する）。</summary>
public sealed record UnitySendChoice(int Index, string? ZipHash, string Entry);

/// <summary>改変の使ったものを順に Unity へ送る要求。送り先を省くと、改変に紐付けたプロジェクトが開いていればそこ、無ければ開いている1つ。</summary>
public sealed record ModificationSendRequest(string? Project, IReadOnlyList<UnitySendChoice>? Choices);

/// <summary>送る物1つ（送る物・選ぶ候補）。</summary>
public sealed record UnitySendTarget(string Package, string Entry, string? ZipHash, bool InFolder);

/// <summary>改変の使ったもの1行の送り方。</summary>
/// <param name="Status"><c>send</c>（送る物が決まっている）・<c>choose</c>（候補から選ぶ）・<c>unavailable</c>（送れない）。</param>
/// <param name="Send">送る物（<c>send</c> のとき。並びの順に送る）。</param>
/// <param name="Candidates">選ぶ候補（<c>choose</c> のとき）。</param>
/// <param name="Reason">送れない理由の種類（<c>itemMissing</c>・<c>recordedFileMissing</c>・<c>noUnityPackage</c>）。</param>
public sealed record UnitySendPlanRow(
    int Index, string ItemId, string Status, IReadOnlyList<UnitySendTarget> Send, IReadOnlyList<UnitySendTarget> Candidates, string? Reason, string? Problem);

/// <summary>改変の使ったものを順に送るときの、行ごとの送り方（外した行は入らない）。</summary>
/// <param name="Project">改変に紐付けたプロジェクト（無ければ null）。</param>
public sealed record UnitySendPlan(string ModificationId, string? Project, IReadOnlyList<UnitySendPlanRow> Rows);
