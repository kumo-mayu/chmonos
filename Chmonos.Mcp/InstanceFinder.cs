using Chmonos.Core.Storage;

namespace Chmonos.Mcp;

/// <summary>探した結果。見つからない・選べないときは、AI に渡す文（次に何をすればよいか）を持つ。</summary>
internal abstract record FindResult
{
    public sealed record Found(RunningInstanceRecord Instance, int Port) : FindResult;

    public sealed record Problem(string Message) : FindResult;
}

/// <summary>
/// 動いている Chmonos を札の置き場（docs/spec/running-instances.md）から探す。**呼び出しのたびに探し直す**
/// （Chmonos が後から起動した・起動し直した・ポートを変えた、を次の呼び出しで拾う。札を読むのは数ファイルなので軽い）。
/// 読むだけ。落ちた札の片付け（<see cref="RunningInstances.Sweep"/>）は Chmonos 本体に任せる（通訳は置き場に書かない）。
/// </summary>
internal sealed class InstanceFinder(string directory, string? store, Func<int, DateTimeOffset?, bool?>? isAlive = null)
{
    private readonly Func<int, DateTimeOffset?, bool?> _isAlive = isAlive ?? RunningInstances.IsAlive;

    public const string NotRunning =
        "Chmonos が動いていません。Chmonos を起動して、設定の「API」で「APIを使う」をオンにしてから、もう一度試してください。";

    /// <summary>生きている札（確かめられない札は、動いている別の本かもしれないので残す）。</summary>
    public IReadOnlyList<RunningInstanceRecord> Alive()
        => RunningInstances.ReadAll(directory)
            .Where(record => _isAlive(record.ProcessId, record.StartedAt) is not false)
            .OrderBy(record => record.StoreRoot, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public FindResult Find()
    {
        var alive = Alive();
        if (store is not null)
        {
            var wanted = Normalize(store);
            alive = alive.Where(record => string.Equals(Normalize(record.StoreRoot), wanted, StringComparison.OrdinalIgnoreCase)).ToList();
            if (alive.Count == 0)
            {
                return new FindResult.Problem(
                    $"保存先「{store}」の Chmonos が動いていません。その保存先で Chmonos を起動して、設定の「API」で「APIを使う」をオンにしてから、もう一度試してください。");
            }
        }

        if (alive.Count == 0)
        {
            return new FindResult.Problem(NotRunning);
        }

        // 同じ保存先は錠で1本だけ動く（app.lock）。それでも2つ並ぶのは、写しの保存先で開いた確かめのアプリなど、別の保存先
        if (alive.Count > 1)
        {
            return new FindResult.Problem(
                "Chmonos が " + alive.Count + " つ動いています（保存先：" + string.Join(" / ", alive.Select(record => record.StoreRoot)) + "）。"
                + "MCP の設定で、起動の引数に --store <保存先のフォルダ> を足して、使う保存先を選んでください。");
        }

        var instance = alive[0];
        return instance.Port is { } port
            ? new FindResult.Found(instance, port)
            : new FindResult.Problem(
                $"Chmonos（保存先：{instance.StoreRoot}）は動いていますが、API がオフです。Chmonos の設定の「API」で「APIを使う」をオンにしてから、もう一度試してください。");
    }

    /// <summary>保存先の場所を比べられる形に揃える（区切り・末尾の区切り・相対の書き方）。大文字小文字は比べる側で無視する。</summary>
    internal static string Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.Trim();
        }
    }
}
