using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 動画のタイトルの控えを、いつまで使い、いつ消すかの決まり（<see cref="VideoTitleRecord"/>）。
///
/// YouTube の開発者ポリシー（III.E.4）は、API で取ったデータを置いてよいのを30日までとし、過ぎたら取り直すか消すことを求める。
/// タイトルを取っている oEmbed がこの規約の対象かははっきりしないが、対象と読んでも外れない側に合わせる（ユーザ判断 2026-09-14）。
/// 取り直すのは動画の欄を開いたとき、消すのは控えを書くときと起動したとき——開かれないまま残る控えもあるので、
/// 開いたときの取り直しだけでは30日を越えてしまう。
/// </summary>
public static class VideoTitleBook
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    /// <summary>
    /// まだ使ってよいか。取った日時が先の日付（時計を戻した）なら使わない——そのままだと、時計が追い付くまで30日を数え始めない。
    /// 1日までの先は、時差や時計のずれとして許す。
    /// </summary>
    public static bool IsFresh(VideoTitleRecord record, DateTimeOffset now)
        => now - record.FetchedAt < KeepFor && record.FetchedAt - now < TimeSpan.FromDays(1);

    /// <summary>30日以内に取ったタイトル。無いか古ければ null（取り直す）。</summary>
    public static string? FreshTitle(IEnumerable<VideoTitleRecord> records, string videoId, DateTimeOffset now)
        => records.FirstOrDefault(record => record.VideoId == videoId && IsFresh(record, now))?.Title;

    public static bool HasStale(IEnumerable<VideoTitleRecord> records, DateTimeOffset now)
        => records.Any(record => !IsFresh(record, now));

    /// <summary>
    /// 取った結果を控える。<paramref name="title"/> が null（非公開・削除で取れなかった）なら、その動画の控えを消す。
    /// 書くついでに30日を過ぎた控えも落とす。
    /// </summary>
    public static List<VideoTitleRecord> Remember(
        IEnumerable<VideoTitleRecord> records, string videoId, string? title, DateTimeOffset now)
    {
        var kept = records.Where(record => record.VideoId != videoId && IsFresh(record, now)).ToList();
        if (!string.IsNullOrWhiteSpace(title))
        {
            kept.Add(new VideoTitleRecord { VideoId = videoId, Title = title, FetchedAt = now });
        }

        return kept;
    }

    /// <summary>30日を過ぎた控えを落とす。</summary>
    public static List<VideoTitleRecord> Prune(IEnumerable<VideoTitleRecord> records, DateTimeOffset now)
        => records.Where(record => IsFresh(record, now)).ToList();
}
