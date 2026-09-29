using BoothAssetManager.Core.Booth;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 届かない失敗を続けて数え、3件で打ち切りを告げる部品（ユーザ判断 2026-09-29）。
/// 取り込みの①②・画像の段・起動時の裏の作業が同じ決まりで止まるよう、決まりはここにしか無い。
/// </summary>
public class BoothOutageWatchTests
{
    private static BoothFetchResult<string> Ok() => BoothFetchResult<string>.Success("本文");

    private static BoothFetchResult<string> Unreachable() => BoothFetchResult<string>.Unreachable("つながらない");

    private static BoothFetchResult<string> ServerDown() => new()
    {
        Status = BoothFetchStatus.TemporaryFailure,
        Error = "HTTP 503",
        IsServerError = true,
    };

    private static BoothFetchResult<string> Busy() => BoothFetchResult<string>.RateLimited("混雑", null);

    private static BoothFetchResult<string> NotFound() => BoothFetchResult<string>.NotFound();

    /// <summary>読めない応答・5xx でも 429 でもない一時失敗（403 など）。BOOTH は応答している。</summary>
    private static BoothFetchResult<string> OtherTemporary() => BoothFetchResult<string>.Temporary("HTTP 403");

    private static BoothOutageWatch Watch(params BoothFetchResult<string>[] results)
    {
        var watch = new BoothOutageWatch();
        foreach (var result in results)
        {
            watch.Note(result);
        }

        return watch;
    }

    [Fact]
    public void StartsNotStopped()
    {
        var watch = new BoothOutageWatch();

        Assert.False(watch.IsStopped);
        Assert.Equal(BoothOutageKind.None, watch.Stopped);
    }

    [Fact]
    public void TwoFailuresAreNotEnough()
    {
        var watch = Watch(Unreachable(), ServerDown());

        Assert.False(watch.IsStopped);
    }

    [Fact]
    public void StopsAtThreeUnreachableInARow()
    {
        var watch = Watch(Unreachable(), Unreachable(), Unreachable());

        Assert.True(watch.IsStopped);
        Assert.Equal(BoothOutageKind.Offline, watch.Stopped);
    }

    [Fact]
    public void StopsAtThreeServerErrorsInARow()
    {
        var watch = Watch(ServerDown(), ServerDown(), ServerDown());

        Assert.True(watch.IsStopped);
        Assert.Equal(BoothOutageKind.ServerDown, watch.Stopped);
    }

    /// <summary>どちらも BOOTH に届いていない失敗なので、混ざっても続けて数える。</summary>
    [Fact]
    public void CountsUnreachableAndServerErrorsTogether()
    {
        Assert.True(Watch(ServerDown(), Unreachable(), ServerDown()).IsStopped);
        Assert.True(Watch(Unreachable(), ServerDown(), Unreachable()).IsStopped);
    }

    /// <summary>理由は最後に数えた失敗の種類（今の様子に近い方）。</summary>
    [Fact]
    public void ReasonIsTheKindOfTheLastFailure()
    {
        Assert.Equal(BoothOutageKind.ServerDown, Watch(Unreachable(), Unreachable(), ServerDown()).Stopped);
        Assert.Equal(BoothOutageKind.Offline, Watch(ServerDown(), ServerDown(), Unreachable()).Stopped);
    }

    [Fact]
    public void ASuccessInBetweenCountsAgain()
    {
        var watch = Watch(Unreachable(), Unreachable(), Ok(), Unreachable(), Unreachable());

        Assert.False(watch.IsStopped);

        watch.Note(Unreachable());
        Assert.True(watch.IsStopped);
    }

    /// <summary>429 はこちらの出し過ぎ。打ち切りには数えず、BOOTH が応答したとして数え直す。</summary>
    [Fact]
    public void BusyAnswerCountsAgain()
    {
        Assert.False(Watch(ServerDown(), ServerDown(), Busy(), ServerDown(), ServerDown()).IsStopped);
        Assert.False(Watch(Busy(), Busy(), Busy(), Busy()).IsStopped);
    }

    /// <summary>404・そのほかの応答も BOOTH が応答しているので数え直す。</summary>
    [Fact]
    public void OtherAnswersFromBoothCountAgain()
    {
        Assert.False(Watch(Unreachable(), Unreachable(), NotFound(), Unreachable(), Unreachable()).IsStopped);
        Assert.False(Watch(ServerDown(), ServerDown(), OtherTemporary(), ServerDown(), ServerDown()).IsStopped);
    }

    /// <summary>一度立ったら、その回は立ったまま（後から取れても残りを問い合わせ直さない）。</summary>
    [Fact]
    public void StaysStoppedForTheRestOfTheRun()
    {
        var watch = Watch(ServerDown(), ServerDown(), ServerDown(), Ok());

        Assert.True(watch.IsStopped);
        Assert.Equal(BoothOutageKind.ServerDown, watch.Stopped);
    }

    /// <summary>結果を直に持たない所（⑦）が種類で数えても、同じ決まりで止まる。</summary>
    [Fact]
    public void CountsByKindTheSameWay()
    {
        var watch = new BoothOutageWatch();
        watch.Note(BoothOutageKind.Offline);
        watch.Note(BoothOutageKind.Offline);
        watch.Note(BoothOutageKind.None);
        watch.Note(BoothOutageKind.ServerDown);
        watch.Note(BoothOutageKind.Offline);
        Assert.False(watch.IsStopped);

        watch.Note(BoothOutageKind.ServerDown);
        Assert.Equal(BoothOutageKind.ServerDown, watch.Stopped);
    }
}
