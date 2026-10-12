namespace Chmonos.Query;

/// <summary>
/// 問い合わせを受けられない（送られた値が読めない・見つからない）。API は <c>application/problem+json</c> で返す。
/// <see cref="Code"/> は機械が読む名前で、変えない（外の道具との約束）。<see cref="Exception.Message"/> は人が読む説明。
/// </summary>
public sealed class QueryException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;

    public string Code { get; } = code;

    /// <summary>送られた値が読めない（400）。</summary>
    public static QueryException Invalid(string code, string message) => new(400, code, message);

    /// <summary>見つからない（404）。</summary>
    public static QueryException NotFound(string code, string message) => new(404, code, message);
}
