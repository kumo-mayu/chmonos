using System.Text.Json;
using System.Text.Json.Nodes;

namespace Chmonos.Mcp;

/// <summary>道具を1つ動かすときに渡す物。</summary>
internal sealed record ToolCall(ChmonosBridge Bridge, ToolArgs Args, CancellationToken CancellationToken);

/// <summary>
/// 道具の定義。<see cref="WriteScope"/> は書き込みの道具が要る API の書き込みの種類（docs/spec/api-clients.md）。
/// 今は読むだけなので全部 null。書き込みの道具を足すときは、ここに種類を書けば、許しを求めるときにその種類も求める（<see cref="ChmonosBridge.Scopes"/>）
/// </summary>
internal sealed record Tool(string Name, string Title, string Description, string InputSchema, string? WriteScope, Func<ToolCall, Task<string>> Run)
{
    public JsonElement Schema => JsonDocument.Parse(InputSchema).RootElement.Clone();

    /// <summary>API の答えを読み、失敗なら AI に渡す文で投げる（道具の答えの isError になる）。</summary>
    public static async Task<JsonNode?> CallAsync(Task<ApiAnswer> call)
    {
        var answer = await call;
        return answer.Failed ? throw new ToolProblemException(answer.Problem!) : answer.Body;
    }
}

/// <summary>道具が答えられない理由（Chmonos が動いていない・許されていない・API が断った）。AI に渡す文を持つ。</summary>
internal sealed class ToolProblemException(string message) : Exception(message);
