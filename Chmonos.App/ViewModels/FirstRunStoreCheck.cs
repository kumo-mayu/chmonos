using System.IO;
using Chmonos.Core.Storage;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 初回の窓で選んだ場所が、このバージョンで開けるライブラリかを、選んだ時点で確かめる（`docs/spec/data-format.md`「起動したとき」）。
///
/// 前は版を確かめる前に、「はじめる」で場所（location.json）と設定を書いていた。新しいバージョンで使われている場所を選ぶと、
/// 次の起動で「新しいバージョンのデータです」で止まり、初回の窓はもう出ないので選び直せなかった（点検 2026-11-10 で残した穴）。
/// 選んだ時点で断れば、何も書かずに選び直せる
/// </summary>
public static class FirstRunStoreCheck
{
    /// <summary>断る理由の文（窓に出す）。開ける場所（印が無い・同じか古い版）なら null。</summary>
    public static string? RefusalFor(string root)
    {
        int? version;
        try
        {
            version = StoreFormat.ReadMarker(root);
        }
        catch (StoreFormatUnreadableException)
        {
            return $"選んだ場所のデータの形式を確かめられませんでした。{StoreFormat.MarkerFileName} が読めません。\n\n別の場所を選んでください。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"選んだ場所のデータの形式を確かめられませんでした。{Core.Services.FailureText.Cause(exception)}\n\n別の場所を選ぶか、少し待ってから選び直してください。";
        }

        return version is { } value && StoreFormat.Check(value) == StoreFormat.Verdict.TooNew
            ? "選んだ場所のデータは、新しいバージョンのChmonosで使われています。このバージョンでは開けません。\n\n別の場所を選ぶか、新しいバージョンを入れてから開いてください。"
            : null;
    }
}
