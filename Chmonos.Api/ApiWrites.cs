using Chmonos.Core.Commands;
using Chmonos.Core.Storage;
using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>外から使う API が書いた物（画面へ知らせる。<paramref name="Kind"/> は items・notifications など、<paramref name="Ids"/> は書いた物の ID）。</summary>
public sealed record ApiWrite(string Kind, IReadOnlyList<string> Ids);

/// <summary>
/// 書き込みの API がアプリから受け取る物（docs/history/headless-core-2026-10-13.md 段3）。API のライブラリはアプリを参照しないので、口を渡してもらう。
/// </summary>
/// <param name="Execute">命令の口。書くのは必ずここから（書き込みは UiCommand を通す決まり）。アプリは起点を API にして呼ぶ（「人が押した」優先度を掛けない）。</param>
/// <param name="Written">書いたことをアプリ（画面）へ知らせる。画面が無ければ何もしない。</param>
/// <param name="ClaimItem">
/// 商品へ書く札を取る。編集画面で開いていれば null（外から書かない。人が後から保存すると API の分が消える。ユーザ判断 2026-10-13）。
/// 書き終えるまで札を持つ（その間に編集画面を開いても、読むのは書いた後になる。点検37）
/// </param>
/// <param name="Unity">Unity へ送る口（アプリが持つ。Unity のエディタの窓を操作するのはアプリの部品）。無ければ送る URL は 503。</param>
/// <param name="ShowItem">窓を開いて商品のページを表示する（段4。画面無しなら窓を作る）。表示できなければ false。無ければ 503。</param>
public sealed record ApiWriteHooks(
    Func<UiCommand, CancellationToken, Task<CommandResult>> Execute,
    Action<ApiWrite>? Written = null,
    Func<string, IDisposable?>? ClaimItem = null,
    IApiUnity? Unity = null,
    Func<string, Task<bool>>? ShowItem = null);

/// <summary>
/// Unity へ送る口（ユーザ判断 2026-10-13：最初の一束に入れる。取り出し・置き場・「使った」の足跡を Chmonos が受け持ち、
/// Unity の拡張などが同じ判断を別に持たない）。送るのは時間がかかる（Unity の取り込み画面で人が考える間を待つ）ので、始めたら返る
/// </summary>
public interface IApiUnity
{
    /// <summary>開いているエディタ。</summary>
    IReadOnlyList<UnityEditorRef> OpenEditors();

    /// <summary>
    /// 送る物と送り先を決めて送り始める。決められない・送れない（もう送っている・開いていない・選べない）なら <see cref="QueryException"/> を投げる。
    /// 返すのは始めたときの状態と、終わったときの状態
    /// </summary>
    (UnitySendStatus Started, Task<UnitySendStatus> Finished) Start(Core.Models.ItemRecord item, UnitySendRequest request);

    /// <summary>最後に API から頼まれた送り（無ければ null）。</summary>
    UnitySendStatus? Last { get; }
}

/// <summary>
/// 書き込みの URL の共通の守りと、書いた後の始末。
/// 守り：書き込みを許していなければ 403・アプリから口を受け取っていなければ 503・保存先を運んでいる間（引越し・書き出し・戻す）は待たせずに 503。
/// 始末：書いたファイルを写しの読み直しに入れ、今すぐ読み直してから答える（直後の GET と同じ答えにする）。
/// まとめて書いている間（取り込み）は写しを差し替えないので、「受け付けた」と返し、終わったら変化の知らせ（SSE・changes）で伝える
/// </summary>
internal sealed class ApiWriter(ApiState state, StoreSync sync, ApiWriteHooks? hooks)
{
    /// <summary>Unity へ送る口（アプリが渡さなければ null）。</summary>
    public IApiUnity? Unity => hooks?.Unity;

    /// <summary>画面で開く口（アプリが渡さなければ null）。</summary>
    public Func<string, Task<bool>>? ShowItem => hooks?.ShowItem;

    /// <summary>書いてよいかを確かめる。だめなら誤りの答えを投げる。</summary>
    public void Guard(HttpContext context)
    {
        if (!state.AllowWrites)
        {
            throw new QueryException(403, "writesDisabled", "書き込みが許されていません。Chmonos の設定の「API」で書き込みを許してください。");
        }

        if (hooks is null)
        {
            throw new QueryException(503, "writesUnavailable", "この API は書き込みを受け付けていません。");
        }

        if (StoreWriteGate.IsHeld)
        {
            context.Response.Headers.RetryAfter = "5";
            throw new QueryException(503, "storeBusy", "保存先を運んでいる途中です。少し待ってから、もう一度送ってください。");
        }
    }

    /// <summary>
    /// 商品に書いてよいかを確かめ（在るか・編集画面で開いていないか）、書く札を返す。**書き終えるまで手放さない**（using で持つ）。
    /// 入口で1回見るだけだと、見た後に開いた編集画面が古い値を読み、後の保存で API の分を消した（点検37）
    /// </summary>
    /// <param name="checkEditing">編集画面で開いているかも見るか（商品の記録を書くとき。足跡のように商品の記録の外へ書くときは見ない）。</param>
    public IDisposable GuardItem(HttpContext context, string itemId, bool checkEditing = true)
    {
        Guard(context);
        if (!state.Snapshot.ItemsById.ContainsKey(itemId))
        {
            throw QueryException.NotFound("itemNotFound", $"商品 {itemId} は見つかりません。");
        }

        if (!checkEditing || hooks!.ClaimItem is not { } claim)
        {
            return NoClaim.Instance;
        }

        return claim(itemId)
            ?? throw new QueryException(409, "itemBeingEdited", "この商品は Chmonos の編集画面で開いています。編集を終えてから、もう一度送ってください。");
    }

    private sealed class NoClaim : IDisposable
    {
        public static readonly NoClaim Instance = new();

        public void Dispose()
        {
        }
    }

    /// <summary>命令を流すだけ（続けて別の命令を流す前段。一覧に足してから商品に付ける、など）。失敗したら 500。</summary>
    /// <param name="writtenKind">書いた物の種類（画面へ知らせる。一覧に足したら候補を作り直させる。点検37）。</param>
    public async Task ExecuteAsync(UiCommand command, CancellationToken cancellationToken, string? writtenKind = null)
    {
        if (await hooks!.Execute(command, cancellationToken) is CommandResult.Failed failed)
        {
            throw new QueryException(500, "commandFailed", failed.Message);
        }

        if (writtenKind is not null)
        {
            hooks.Written?.Invoke(new ApiWrite(writtenKind, []));
        }
    }

    /// <summary>
    /// 命令を流し、書いたファイルを読み直してから返す。写しを差し替えたら null、まとめて書いている途中で差し替えなかったら、その途中の種類（202 で答える）。
    /// 命令が失敗したら 500（commandFailed。詳しくは命令の文）
    /// </summary>
    public async Task<string?> RunAsync(UiCommand command, string kind, IReadOnlyList<string> ids, IEnumerable<string> writtenFiles, CancellationToken cancellationToken)
    {
        var result = await hooks!.Execute(command, cancellationToken);
        if (result is CommandResult.Failed failed)
        {
            throw new QueryException(500, "commandFailed", failed.Message);
        }

        foreach (var file in writtenFiles)
        {
            sync.Note(file);
        }

        hooks.Written?.Invoke(new ApiWrite(kind, ids));
        if (sync.Busy is { } busy)
        {
            return busy;
        }

        // 錠を待つ間に取り込みが始まったら、差し替えずに戻る。そのときは「受け付けた」で答える（点検37）
        return await sync.ApplyPendingAsync(cancellationToken) ? null : sync.Busy ?? "write";
    }
}
