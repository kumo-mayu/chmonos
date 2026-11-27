using System.Text.Json;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;

namespace Chmonos.Query;

/// <summary>取り込みの進み具合（<c>/v1/version</c> の <c>progress</c>）。</summary>
/// <param name="Done">商品の情報（①）を取れた数。</param>
/// <param name="Total">取ろうとしている数（周回をまたいで足した数。画面の「n / m 件」と同じ）。</param>
public sealed record ImportProgressRef(int Done, int Total);

/// <summary>
/// 取り込みの進み具合を、取り込みが残す記録（<c>import-state.json</c>）から読む（2026-11-27 に出すと決めた物の③）。
///
/// 出すのは「済んだ数／全体」だけ（docs/spec/local-api-shapes.md。続きに要る対象や取れなかった商品は出さない）。
/// 記録は取り込みが①の1件ごとに書くので、API は数え直さずにそれを読む。小さなファイルで、
/// 読む側（<see cref="JsonFileStore{T}"/>）が大きさと日時が同じ間は読み直さないので、問い合わせのたびに読んでよい。
/// </summary>
public static class ImportProgress
{
    /// <summary>
    /// 今の取り込みの数。分からない間は null。
    ///
    /// - **始めた時より前に書かれた記録は使わない**：取り込みは始めに記録を空で書き直すが、知らせ（busy）はその少し前に届く。
    ///   その間に読むと、前に中断した回の数を今の数として返してしまう。
    /// - **走査の途中は null**：数は①の数で、走査・商品 ID の特定の間は 0 / 0 のまま（<see cref="ImportState.Scanning"/>）。
    ///   0 / 0 を返すと「取る物が無い」と見分けられない。取る物が無い回（全体が 0）も null。
    /// - 記録が読めない（書いている途中・手で壊した）ときも null。進み具合のために <c>/v1/version</c> を落とさない。
    /// </summary>
    public static ImportProgressRef? Read(QuerySnapshot snapshot, DateTimeOffset busySince)
    {
        ImportState state;
        try
        {
            state = snapshot.Store.ImportState.Load();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return Of(state, busySince);
    }

    public static ImportProgressRef? Of(ImportState state, DateTimeOffset busySince)
        => state is { Scanning: false, Total: > 0 } && state.StoppedAt >= busySince
            ? new ImportProgressRef(state.Done, state.Total)
            : null;
}
