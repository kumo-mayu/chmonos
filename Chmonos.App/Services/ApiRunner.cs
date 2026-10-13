using System.IO;
using Chmonos.Api;
using Chmonos.Core.Storage;

namespace Chmonos.App.Services;

/// <summary>API の今の状態。</summary>
public enum ApiRunState
{
    /// <summary>止まっている（設定がオフ・まだ始めていない）。</summary>
    Stopped,

    /// <summary>読み込んで待ち受けを始めている途中。</summary>
    Starting,

    /// <summary>待ち受けている。</summary>
    Running,

    /// <summary>始められなかった（ポートが使われている・読めない）。</summary>
    Failed,
}

/// <summary>
/// 外から使う API を、設定（<c>api-settings.json</c> の <c>enabled</c>・<c>port</c>）に合わせて、アプリと同じプロセスで始める・止める
/// （docs/history/headless-core-2026-10-13.md 段2。ユーザ判断 2026-10-13：オンにしたらすぐ動かし、オフですぐ止める。ポートを変えたらその場で待ち受け直す）。
/// 取り込みの始まりと終わりも、ここから直に API へ伝える（前は別のプロセスへ HTTP で送っていた）。
/// </summary>
/// <param name="writes">書き込みの口（アプリの命令の口・画面への知らせ）。API を始めるたびに渡す。</param>
public sealed class ApiRunner(DataStore store, ApiWriteHooks? writes = null)
{
    /// <summary>始める・止めるを1つずつ（続けて押されても、待ち受けが2つ立たないように）。</summary>
    private readonly SemaphoreSlim _order = new(1, 1);

    private volatile ApiServer? _server;

    public ApiRunState State { get; private set; } = ApiRunState.Stopped;

    /// <summary>待っている（待とうとした）ポート。</summary>
    public int Port { get; private set; }

    /// <summary>状態が変わった。裏のスレッドから来るので、画面は画面のスレッドへ渡す。</summary>
    public event Action? StateChanged;

    /// <summary>
    /// 今の設定に合わせる。オフなら止め、オンで止まっていれば始め、ポートが違えば待ち受け直す。
    /// 読み込みに時間がかかる（2万件で数秒）ので、画面のスレッドを塞がないよう裏で回す
    /// </summary>
    public Task ApplyAsync() => Task.Run(ApplyCoreAsync);

    private async Task ApplyCoreAsync()
    {
        await _order.WaitAsync();
        try
        {
            // 保存先を運んでいる間は始めない（止めた後に設定の変更などで始め直すと、元の保存先を見張って読み直す。点検36）
            if (_suspended)
            {
                return;
            }

            var settings = store.ApiSettings.Load();
            if (!settings.Enabled)
            {
                await StopCoreAsync();
                Set(ApiRunState.Stopped, settings.Port);
                return;
            }

            if (_server is { } running && running.Port == settings.Port)
            {
                return;
            }

            await StopCoreAsync();
            Set(ApiRunState.Starting, settings.Port);
            try
            {
                _server = await ApiServer.StartAsync(store, writes);

                // 取り込みの途中に立て直したら、新しい待ち受けにも「取り込み中」を伝える（前は伝わらず、途中の記録を読み直した。点検36・37）
                if (BusyKind is { } busy)
                {
                    _server.SetBusy(busy);
                }

                Set(ApiRunState.Running, _server.Port);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // ポートがほかのアプリに使われている（IOException）・保存先が読めない。アプリは止めない。設定の画面が理由を言う
                Core.Diagnostics.AppLog.Error($"外から使う API を始める（ポート {settings.Port}）", exception);
                Set(ApiRunState.Failed, settings.Port);
            }
        }
        finally
        {
            _order.Release();
        }
    }

    /// <summary>
    /// 合言葉を作り直す。動いていれば止めて作り直し、同じ設定で待ち受け直す（古い合言葉で答える時間を残さない）。
    /// 止まっていれば作り直すだけ（次にオンにしたときから使われる）
    /// </summary>
    public Task RenewTokenAsync() => Task.Run(RenewTokenCoreAsync);

    private async Task RenewTokenCoreAsync()
    {
        await _order.WaitAsync();
        try
        {
            await StopCoreAsync();
            ApiServer.RenewToken(store.Paths);
        }
        finally
        {
            _order.Release();
        }

        await ApplyCoreAsync();
    }

    /// <summary>止める（アプリを閉じるとき）。</summary>
    public async Task StopAsync()
    {
        await _order.WaitAsync();
        try
        {
            await StopCoreAsync();
            Set(ApiRunState.Stopped, Port);
        }
        finally
        {
            _order.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        if (_server is not { } server)
        {
            return;
        }

        _server = null;
        try
        {
            await server.DisposeAsync();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Error("外から使う API を止める", exception);
        }
    }

    /// <summary>
    /// アプリがまとめて書いている（取り込みなど）・書き終えた（null）を API へ伝える。その間、API は古い答えを返し続ける。
    /// API が動いていなければ何もしない。アプリの書き込みを API の都合で遅らせない
    /// </summary>
    public void NoteBusy(string? kind) => NoteBusy("app", kind);

    /// <summary>
    /// まとめて書いている作業を、知らせ手ごとに覚える（取り込みの係・主画面〔展開先の削除など〕）。どれかが書いていれば「書いている」。
    /// 覚えておくのは、取り込みの途中に API を立て直したときにも伝えるため（点検36・37）
    /// </summary>
    public void NoteBusy(string source, string? kind)
    {
        string? before;
        string? after;
        lock (_busy)
        {
            before = BusyKindLocked();
            if (kind is null)
            {
                _busy.Remove(source);
            }
            else
            {
                _busy[source] = kind;
            }

            after = BusyKindLocked();
        }

        if (before != after)
        {
            _server?.SetBusy(after);
        }
    }

    private readonly Dictionary<string, string> _busy = new(StringComparer.Ordinal);

    private string? BusyKindLocked() => _busy.Values.OrderBy(kind => kind, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>今まとめて書いている作業（無ければ null）。</summary>
    public string? BusyKind
    {
        get
        {
            lock (_busy)
            {
                return BusyKindLocked();
            }
        }
    }

    private volatile bool _suspended;

    /// <summary>
    /// 保存先を運ぶ前に止め、運び終えるまで始めない（設定の変更や合言葉の作り直しが重なっても、元の保存先で始め直さない。点検36）。
    /// 運べたらアプリが開き直すので戻さない。運べなかったら <see cref="ResumeAsync"/>
    /// </summary>
    public async Task SuspendAsync()
    {
        _suspended = true;
        await StopAsync();
    }

    /// <summary>運べなかったとき：始めない印を外し、設定どおりに戻す。</summary>
    public Task ResumeAsync()
    {
        _suspended = false;
        return ApplyAsync();
    }

    private void Set(ApiRunState state, int port)
    {
        State = state;
        Port = port;
        StateChanged?.Invoke();
    }
}
