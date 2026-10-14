using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Query;

namespace Chmonos.Api;

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
    (UnitySendStatus Started, Task<UnitySendStatus> Finished) Start(ItemRecord item, UnitySendRequest request);

    /// <summary>
    /// 改変の使ったものを順に送り始める（決めた物を1つの列で。画面の「使ったものを順にUnityへ送る」と同じ）。
    /// 送れないときは <see cref="Start"/> と同じく投げる
    /// </summary>
    (UnitySendStatus Started, Task<UnitySendStatus> Finished) StartModification(UnityModificationSend send);

    /// <summary>最後に API から頼まれた送り（無ければ null）。</summary>
    UnitySendStatus? Last { get; }

    /// <summary>覚えている送り（直近の数件）から引く。無ければ null。</summary>
    UnitySendStatus? Find(string sendId);

    /// <summary>
    /// 送りを止める（画面の「中止」と同じ止め方）。覚えていない送りは 404・もう終わった送りは 409 で投げる。
    /// 返すのは止めると頼んだ時点の状態と、終わったときの状態
    /// </summary>
    (UnitySendStatus Current, Task<UnitySendStatus> Finished) Cancel(string sendId);
}

/// <summary>改変から送る列の1件。送らない行（手元に送れる物が無い）は <see cref="Package"/> が null で <see cref="Skipped"/> に理由。</summary>
/// <param name="Index">改変の使ったものの中の位置。</param>
public sealed record UnityModificationStep(int Index, string ItemId, ItemRecord? Item, UnityPackageEntry? Package, string? Skipped);

/// <summary>選んだ物を、送る前に改変の行へ記録する（画面の選ぶ窓と同じ。2つ選べば、その行の場所に2行並ぶ）。</summary>
public sealed record UnityChoiceRecord(int Index, ModificationMember Member, IReadOnlyList<ModificationMember> Members);

/// <summary>改変から送る中身（API が保存先の今の記録で決めて、アプリの送り手へ渡す）。</summary>
/// <param name="Project">送り先の指定（null なら、紐付けたプロジェクトが開いていればそこ、無ければ開いている1つ）。</param>
public sealed record UnityModificationSend(
    ModificationRecord Record, IReadOnlyList<UnityModificationStep> Steps, IReadOnlyList<UnityChoiceRecord> Records, string? Project);
