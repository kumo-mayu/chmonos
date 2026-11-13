using ModelContextProtocol.Server;

namespace Chmonos.Mcp;

/// <summary>
/// 通訳の設定。本番は <see cref="McpOptions.FromArguments"/> で作り、試験は置き場を一時フォルダへ差し替える
/// （本物の <c>%LOCALAPPDATA%\Chmonos.Shared</c> に書かない・読まない）。
/// </summary>
/// <param name="Store">使う人が <c>--store</c> で選んだ保存先（2つ以上動いているときに選ぶ）。</param>
/// <param name="RunningDirectory">動いている Chmonos の札の置き場（docs/spec/running-instances.md）。</param>
/// <param name="TokenFile">ソフトごとのトークンの控え（docs/spec/mcp.md「トークンの控え」）。</param>
/// <param name="ApprovalWait">人が［許可］を押すのを1回の呼び出しで待つ長さ。</param>
public sealed record McpOptions(
    string? Store,
    string RunningDirectory,
    string TokenFile,
    TimeSpan ApprovalWait)
{
    /// <summary>起動の引数（通訳への切り替えの印）。</summary>
    public const string Argument = "--mcp";

    public const string StoreArgument = "--store";

    /// <summary>トークンの控えの場所を差し替える環境変数（試験・確かめの道具）。</summary>
    public const string TokenFileVariable = "CHMONOS_MCP_TOKENS";

    /// <summary>控えのファイルの名前（<c>%LOCALAPPDATA%\Chmonos.Shared\</c> の直下）。</summary>
    public const string TokenFileName = "mcp-tokens.json";

    /// <summary>
    /// 1回の呼び出しで［許可］を待つ既定の長さ。API の <c>wait</c> は30秒まで（docs/spec/api-clients.md）なので、その中に収め、
    /// 道具の時間切れを決めていない使い手でも1回の呼び出しが長く止まらないよう、少し短い25秒にした（プラグインの見本と同じ）。
    /// 待ちきれなければ「押してからもう一度」と返す（同じ求めを覚えておき、次の呼び出しで続きを待つ）
    /// </summary>
    public static readonly TimeSpan DefaultApprovalWait = TimeSpan.FromSeconds(25);

    /// <summary>引数と環境変数から作る。置き場は <c>%LOCALAPPDATA%\Chmonos.Shared</c>（BOOTH の門・動いている一覧と同じ「PC で1つ」の置き場）。</summary>
    public static McpOptions FromArguments(IReadOnlyList<string> args)
    {
        var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Core.Storage.RunningInstances.SharedFolderName);

        // 置き場は通訳が自分で決める。RunningInstances.Directory は「アプリ本体として起動したか」（App の静的な初期化で立つ印）を見るが、
        // 通訳は WPF の App に触れずに分かれるので、その印は立たない（立てると、保存先を開いてよい印まで立ってしまう）
        var running = Environment.GetEnvironmentVariable(Core.Storage.RunningInstances.DirectoryVariable) is { } runningValue && !string.IsNullOrWhiteSpace(runningValue)
            ? Path.GetFullPath(runningValue.Trim())
            : Path.Combine(shared, Core.Storage.RunningInstances.FolderName);
        var tokens = Environment.GetEnvironmentVariable(TokenFileVariable) is { } tokenValue && !string.IsNullOrWhiteSpace(tokenValue)
            ? Path.GetFullPath(tokenValue.Trim())
            : Path.Combine(shared, TokenFileName);

        return new McpOptions(StoreOf(args), running, tokens, DefaultApprovalWait);
    }

    /// <summary><c>--store &lt;保存先&gt;</c> か <c>--store=&lt;保存先&gt;</c>。</summary>
    internal static string? StoreOf(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], StoreArgument, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return args[i + 1];
            }

            if (args[i].StartsWith(StoreArgument + "=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i][(StoreArgument.Length + 1)..];
            }
        }

        return null;
    }
}

/// <summary>
/// <c>Chmonos.exe --mcp</c> の入口。WPF にも保存先にも触れずに、stdio で MCP の通訳として動く。
/// stdout は MCP の通信だけに使う。知らせ・例外は stderr へ（MCP の使い手はそれをログに残す）。
/// </summary>
public static class McpProgram
{
    /// <summary>引数に <c>--mcp</c> があるか。</summary>
    public static bool IsRequested(IEnumerable<string> args)
        => args.Any(arg => string.Equals(arg, McpOptions.Argument, StringComparison.OrdinalIgnoreCase));

    /// <summary>通訳を動かし、使い手が stdin を閉じたら終える。終わりの番号を返す。</summary>
    public static int Run(string[] args)
    {
        // ほかの部品がうっかり Console.Out に書いても、MCP の通信（生の stdout の流れ）に混ざらないよう stderr へ向ける。
        // SDK の stdio の運びは Console.Out ではなく生の流れを開く（確かめは docs/spec/mcp.md「stdout」）
        // stderr のログを UTF-8 で書く。既定は日本語の Windows のコードページ（Shift_JIS）で、MCP の使い手（Claude Code のログなど）は
        // UTF-8 として読むので字化けした（2026-11-13 に実機でつないで確かめた）。BOM は付けない（ログの頭に余計な字が入る）。
        // Console.OutputEncoding は変えない：コンソールの無い起動（使い手がパイプでつなぐ）では当てにならない
        Console.SetError(new StreamWriter(Console.OpenStandardError(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true });
        Console.SetOut(Console.Error);

        try
        {
            var options = McpOptions.FromArguments(args);
            using var cancel = new CancellationTokenSource();
            var transport = new StdioServerTransport(ChmonosMcpServer.ServerName);
            RunAsync(transport, options, Console.Error, cancel.Token).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception exception)
        {
            McpLog.Write(Console.Error, "通訳が止まった", exception);
            return 1;
        }
    }

    /// <summary>運び（stdio・試験はメモリの流れ）を受けて動かす。</summary>
    public static async Task RunAsync(ModelContextProtocol.Protocol.ITransport transport, McpOptions options, TextWriter log, CancellationToken cancellationToken)
    {
        await using (transport)
        {
            using var bridge = new ChmonosBridge(options, log);
            await using var server = ChmonosMcpServer.Create(transport, bridge);
            await server.RunAsync(cancellationToken);
        }
    }
}

/// <summary>stderr へ1行ずつ書く。トークン・問い合わせの中身は書かない（書くのは何をしていたかと例外の種類と文だけ）。</summary>
internal static class McpLog
{
    private static readonly Lock Gate = new();

    public static void Write(TextWriter log, string where, Exception? exception = null, string? message = null)
    {
        var text = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} [Chmonos MCP] {where}"
            + (message is null ? string.Empty : "：" + message)
            + (exception is null ? string.Empty : $"：{exception.GetType().Name}：{exception.Message}");
        lock (Gate)
        {
            try
            {
                log.WriteLine(text);
                log.Flush();
            }
            catch (Exception failure) when (failure is IOException or ObjectDisposedException)
            {
                // stderr が閉じていても通訳は止めない
            }
        }
    }
}
