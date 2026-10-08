using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Chmonos.Core.Services;

/// <summary>
/// 新しい版の確認（ユーザ判断 2026-10-08・`docs/spec/data-format.md`「新しい版の確認」）。
/// GitHub の Releases の最新を聞き、自分の版より新しければ知らせる。自動で入れ替えはしない
/// （署名が無く、落とした物が本物かを確かめる手段と、止められたときに戻す道が無い）。
/// </summary>
public static class UpdateCheck
{
    /// <summary>ダウンロードページ。GitHub と BOOTH で同じ zip を配っている（docs/dev/signing.md）。</summary>
    public const string DownloadPage = "https://github.com/kumo-mayu/chmonos/releases/latest";

    /// <summary>最新の公開版。下書きとプレリリースは返らない（公開の前に中身を確かめる運用に合う）。</summary>
    private const string LatestApi = "https://api.github.com/repos/kumo-mayu/chmonos/releases/latest";

    /// <summary>
    /// 確かめる間隔。版は数週間に1回しか出ないので、起動のたびに聞く理由が無い（相手のサービスにも余計な問い合わせをしない）
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>
    /// 待つ長さ。起動の後に裏で聞くだけで、何も待たせないが、つながらない回線で握り続けない
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>今確かめる頃か。一度も確かめていないか、前に確かめてから1日以上たっていれば。</summary>
    public static bool IsDue(UpdateCheckRecord record, DateTimeOffset now)
        => record.CheckedAt is not { } checkedAt || now - checkedAt >= Interval || checkedAt > now;

    /// <summary>
    /// 帯に出す版（無ければ null）。最新が自分より新しく、×で閉じた版でなければ。
    /// 閉じた版より新しい版が出たら、また出す
    /// </summary>
    public static string? VersionToShow(UpdateCheckRecord record, string currentVersion)
        => record.LatestVersion is { } latest
           && IsNewer(latest, currentVersion)
           && !string.Equals(Normalize(latest), Normalize(record.DismissedVersion), StringComparison.OrdinalIgnoreCase)
            ? Normalize(latest)
            : null;

    /// <summary>
    /// <paramref name="candidate"/> が <paramref name="current"/> より新しいか。tag の頭の v と、版の後ろの +（ビルドの印）は見ない。
    /// 読めない版は新しいとみなさない（知らせを誤って出すより、出さない方が害が少ない）
    /// </summary>
    public static bool IsNewer(string candidate, string current)
        => Version.TryParse(Normalize(candidate), out var newer)
           && Version.TryParse(Normalize(current), out var mine)
           && newer > mine;

    private static string Normalize(string? version)
    {
        var text = (version ?? string.Empty).Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var plus = text.IndexOfAny(['+', '-']);
        return plus >= 0 ? text[..plus] : text;
    }

    /// <summary>
    /// GitHub に最新の版を聞く（tag の名前。聞けなければ null）。BOOTH ではないので BoothClient の門は通さない。
    /// GitHub の API は User-Agent が無い問い合わせを断る
    /// </summary>
    public static async Task<string?> FetchLatestAsync(HttpClient http, string currentVersion, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        request.Headers.UserAgent.ParseAdd($"Chmonos/{Normalize(currentVersion)}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await http.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            // 画面には出さないが、理由は残す（点検26：403・429・5xx が黙って null になり、なぜ知らせが出ないのか追えなかった）
            Diagnostics.AppLog.Warn("新しいバージョンの確認", $"GitHub が {(int)response.StatusCode} を返しました。");
            return null;
        }

        var release = await response.Content.ReadFromJsonAsync<LatestRelease>(timeout.Token);
        return release?.TagName;
    }

    private sealed record LatestRelease([property: JsonPropertyName("tag_name")] string? TagName);
}

/// <summary>
/// 新しい版の確認の記録（<c>update-check.json</c>）。知らせのファイルに種類を足さないのは、
/// v1.0.0 が知らない種類の知らせで知らせのファイルを丸ごと読めなくなるため（保存する列挙に値を足すのは形式を上げる変更）。
/// </summary>
public sealed record UpdateCheckRecord
{
    /// <summary>最後に確かめた日時（聞けなかったときも書く。つながらない間に起動のたび聞きに行かない）。</summary>
    public DateTimeOffset? CheckedAt { get; init; }

    /// <summary>最後に聞けた最新の版（tag の名前）。</summary>
    public string? LatestVersion { get; init; }

    /// <summary>帯を × で閉じた版。この版の間は帯を出さない。</summary>
    public string? DismissedVersion { get; init; }
}
