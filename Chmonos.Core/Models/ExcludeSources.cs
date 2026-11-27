namespace Chmonos.Core.Models;

/// <summary>
/// 管理から外した操作の出どころ（<see cref="ExcludedEntry.Reason"/>）。画面が書く文と、API が返す英字の値の対応を1か所に置く（ユーザ判断 2026-11-27・案 A）。
///
/// **記録には日本語の文のまま書き続ける**：配った版（v1.0.0〜）の <c>excluded.json</c> もこの文で書かれていて、
/// 設定の「除外したファイル」はそれをそのまま見せる。値に書き換えると、前の版の記録と見せ方が割れる。
/// API の値は、文を言い換えても外の道具が壊れないように、ここで文から引く。文を変えるときは、前の文も表に残す（前に書いた記録が読めるように）。
/// </summary>
public static class ExcludeSources
{
    /// <summary>未確定の画面で1つを外した。</summary>
    public const string ResolveText = "未確定画面から除外";

    /// <summary>未確定の画面で、元の zip と、そこから展開した物を一緒に外した。</summary>
    public const string ResolveArchiveText = "未確定画面から元zipごと除外";

    /// <summary>未確定の画面で、チェックした物をまとめて外した。</summary>
    public const string ResolveBulkText = "未確定画面からまとめて除外";

    /// <summary>フォルダビューで、フォルダの中の未確定をまとめて外した。</summary>
    public const string FolderViewText = "フォルダビューからフォルダごと除外";

    /// <summary>表に無い文（前の版・手で書いた記録・試験の作り物）。</summary>
    public const string Other = "other";

    private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
    {
        [ResolveText] = "resolve",
        [ResolveArchiveText] = "resolveArchive",
        [ResolveBulkText] = "resolveBulk",
        [FolderViewText] = "folderView",
    };

    /// <summary>
    /// 記録の文から API の値を引く。文が無い（null）なら null のまま返す：「出どころの記録が無い」と「知らない文がある」は別の事実なので、
    /// どちらも <see cref="Other"/> にまとめない。
    /// </summary>
    public static string? ValueOf(string? reason)
        => reason is null ? null : Values.GetValueOrDefault(reason, Other);
}
