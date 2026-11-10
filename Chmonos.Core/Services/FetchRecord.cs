using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// BOOTH から取れたときに商品へ書く取得の記録（取得日時・404 の数・販売終了の印・次の予定日）と、次の取り直し（⑦）の予定日。
/// 取り直し・「このIDで登録」・取り込みの①の3か所が同じ値を書くので1つにする（前は3か所に写してあり、予定日の式も2つあった）。
/// </summary>
public static class FetchRecord
{
    /// <summary>
    /// 次の取り直しの予定日。404 が続いて販売終了と見なした商品は、販売終了の確かめ直しの間隔で見る。
    /// 全商品が同じ日に期限切れにならないよう、商品IDから決まるばらつき（<see cref="RefreshJitter"/>）を足す——
    /// 乱数でないので、同じ商品なら何度計算しても（起動し直しても）同じ日になる。
    /// </summary>
    public static DateTimeOffset NextDue(string itemId, AppSettings settings, DateTimeOffset now, int consecutiveNotFound = 0)
    {
        var days = consecutiveNotFound >= settings.NotFoundThreshold
            ? settings.DelistedRecheckDays
            : settings.RefreshIntervalDays;
        return now.AddDays(days + RefreshJitter.Days(itemId, settings.RefreshJitterDays));
    }

    /// <summary>取れたときの記録を重ねた値。404 の数と販売終了の印を落とし（取れたので今は在る）、取得日時と次の予定日を書く。</summary>
    public static LocalBlock Fetched(LocalBlock local, string itemId, AppSettings settings, DateTimeOffset now)
        => local with
        {
            ConsecutiveNotFoundCount = 0,
            IsDelisted = false,
            LastFetchedAt = now,
            NextFetchDueAt = NextDue(itemId, settings, now),
        };
}
