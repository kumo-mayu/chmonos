namespace Chmonos.Core.Services;

/// <summary>
/// Unity プロジェクトの場所を付け替えた結果（<see cref="Commands.UiCommand.RelocateUnityProject"/>。ユーザ判断 2026-11-10）。
/// 書く物は改変ごとのファイル・保存した検索・控えと別々で、1回では書けない。途中で書けなかった物があっても書けた分は戻さず、
/// どこまで書けたかを返す（もう一度押せば、まだ古い場所を指している物だけを書く）
/// </summary>
public sealed record UnityProjectRelocation
{
    /// <summary>新しい場所に付け替えた改変の数。</summary>
    public required int Modifications { get; init; }

    /// <summary>古い場所を指していたのに書けなかった改変の数（ファイルが読めない・書けない）。</summary>
    public required int FailedModifications { get; init; }

    /// <summary>条件を書き換えた保存した検索の数。</summary>
    public required int SavedSearches { get; init; }

    /// <summary>保存した検索を書けなかったか（書けなければ <see cref="SavedSearches"/> は 0）。</summary>
    public required bool SavedSearchesFailed { get; init; }

    /// <summary>プロジェクトを調べた控えを新しい場所の物として写したか（写さなくても、次に調べ直すだけで困らない）。</summary>
    public required bool ScanCopied { get; init; }
}
