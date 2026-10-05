namespace Chmonos.Core.Models;

/// <summary>
/// 未確定の画面で押して、まだ終わっていない「このIDで登録」1件（<c>registration-queue.json</c> の1行。ユーザ判断 2026-10-05 メモ60）。
///
/// 手元に無い商品の登録は BOOTH へ順に問い合わせるので1件に数分かかり、待っている間に閉じることがある。
/// 列を残しておき、次に起動したときに同じ順で続ける。終わった物・やめた物は消す（ファイルは列が空なら空の配列）。
/// 進み具合（何件済んだか・残りの問い合わせ）は書かない——再開すると、済んだ物は未確定から消えているので最初から数え直せる。
/// </summary>
public sealed record QueuedRegistration
{
    /// <summary>登録する先の商品ID。</summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>確かめたときの商品名。画面の知らせ（「〇〇」を登録しました）に使う。</summary>
    public string ItemName { get; init; } = string.Empty;

    /// <summary>登録するファイルの中身のハッシュ（未確定の記録の <c>hash</c>）。束なら全部。</summary>
    public IReadOnlyList<string> FileHashes { get; init; } = [];

    /// <summary>
    /// 押したときに見込んだ BOOTH への問い合わせの数（商品JSON・商品ページ・画像・ショップのアイコン）。
    /// 確かめた時点の商品JSONの画像の枚数と、アイコンが手元にあったかで決まり、後からは出せないので書いておく。分からなければ無し。
    /// </summary>
    public int? EstimatedRequests { get; init; }
}
