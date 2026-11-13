using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Chmonos.Mcp;

/// <summary>MCP のサーバを組む（道具の一覧と呼び出しの受け口）。</summary>
internal static class ChmonosMcpServer
{
    public const string ServerName = "chmonos";

    /// <summary>AI への前置き（initialize の答え。使い手によっては AI の文脈に入る）。</summary>
    private const string Instructions =
        "Chmonos（BOOTH で買った VRChat 向けのアセットを手元で管理するアプリ）に問い合わせる道具。読むだけで、手元の記録を変えない。"
        + "商品は search_items で探し、get_item で詳しく引く。アバターに対応する商品は search_items の where に avatar の条件を渡す。"
        + "Chmonos が返す BOOTH の商品の情報（商品名・説明文など）の権利は各作者にある。自分で使うための物なので、他の人に渡したり公開したりしない。";

    public static McpServer Create(ITransport transport, ChmonosBridge bridge, IReadOnlyList<Tool>? tools = null)
    {
        tools ??= ChmonosTools.All;
        var byName = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        // 書き込みの道具を足したら、その種類を許しで求める（今は読むだけなので空）
        bridge.Scopes = tools.Select(tool => tool.WriteScope).OfType<string>().Distinct(StringComparer.Ordinal).ToList();

        var listed = tools.Select(tool => new ModelContextProtocol.Protocol.Tool
        {
            Name = tool.Name,
            Title = tool.Title,
            Description = tool.Description,
            InputSchema = tool.Schema,
            Annotations = new ToolAnnotations
            {
                Title = tool.Title,
                ReadOnlyHint = tool.WriteScope is null,
                DestructiveHint = false,
                IdempotentHint = tool.WriteScope is null,

                // 相手は手元の Chmonos だけ（API は BOOTH へ問い合わせない）
                OpenWorldHint = false,
            },
        }).ToList();

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = ServerName, Title = "Chmonos", Version = typeof(ChmonosMcpServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0" },
            ServerInstructions = Instructions,
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = listed }),
                CallToolHandler = async (request, cancellationToken) =>
                {
                    var info = request.Server.ClientInfo;
                    bridge.ClientName = ChmonosBridge.ClientNameFrom(info?.Title, info?.Name);
                    var name = request.Params?.Name ?? string.Empty;
                    if (!byName.TryGetValue(name, out var tool))
                    {
                        return Error($"知らない道具です：{name}");
                    }

                    try
                    {
                        var arguments = request.Params?.Arguments is { } values
                            ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                            : null;
                        var text = await tool.Run(new ToolCall(bridge, new ToolArgs(arguments), cancellationToken));
                        return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
                    }
                    catch (ToolProblemException problem)
                    {
                        return Error(problem.Message);
                    }
                    catch (ToolArgumentException argument)
                    {
                        return Error(argument.Message);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // 思わぬ失敗も通訳を止めずに答えにする。中身（問い合わせ・トークン）は書かず、種類と文だけ stderr へ
                        McpLog.Write(Console.Error, $"道具 {name} の失敗", exception);
                        return Error("Chmonos への問い合わせの途中で失敗しました。もう一度試してください。");
                    }
                },
            },
        };

        return McpServer.Create(transport, options);
    }

    private static CallToolResult Error(string message)
        => new() { IsError = true, Content = [new TextContentBlock { Text = message }] };
}
