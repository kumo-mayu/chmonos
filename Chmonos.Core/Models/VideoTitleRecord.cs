namespace Chmonos.Core.Models;

/// <summary>
/// YouTube の動画のタイトルの控え（<c>video-titles.json</c>・ユーザ判断 2026-09-14：何の動画か分からないのでタイトルは控える）。
///
/// 動画ごとに1つ。同じ動画が何件の商品に載っていても1回で済み、商品の記録は書き換えない。
/// **30日を過ぎたら取り直すか消す**（YouTube の開発者ポリシー III.E.4：API で取ったデータを置いてよいのは30日まで。
/// 判断は <see cref="Services.VideoTitleBook"/>）。取れなくなった（非公開・削除）動画の控えは残さない。
/// 絵は控えない（利用規約は、許された場合を除いてダウンロードを禁じている。画面が YouTube から直に出す）。
/// </summary>
public sealed record VideoTitleRecord
{
    /// <summary>動画の ID（URL の <c>v=</c> の11文字）。</summary>
    public required string VideoId { get; init; }

    public required string Title { get; init; }

    /// <summary>YouTube から取った日時。ここから30日で取り直す。</summary>
    public DateTimeOffset FetchedAt { get; init; }
}
