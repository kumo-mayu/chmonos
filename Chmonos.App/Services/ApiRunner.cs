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
public sealed class ApiRunner(DataStore store)
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
                _server = await ApiServer.StartAsync(store);
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
    public void NoteBusy(string? kind) => _server?.SetBusy(kind);

    private void Set(ApiRunState state, int port)
    {
        State = state;
        Port = port;
        StateChanged?.Invoke();
    }
}
