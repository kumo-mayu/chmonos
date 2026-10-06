using System.Formats.Tar;
using System.Text;

namespace Chmonos.Core.Resolution;

/// <summary>
/// unitypackage の <c>&lt;GUID&gt;/pathname</c> の中身（Unity 上のパス）を、**長さの上限を置いて**読む（外部の点検 2026-10-06）。
///
/// 前は <see cref="StreamReader.ReadLine"/> で1行丸ごと読んでいたので、改行の無い巨大な pathname
/// （壊れた物・わざと作った物）を掴むと、その大きさの文字列を作るまで読み続けた。
/// **項目の大きさを先に見て、上限を超える物は読まずに飛ばす**（文字列を作る前に断る）。
///
/// 上限の決め方：
/// <list type="bullet">
/// <item>手元の購入物の unitypackage 16個・674件で、pathname の項目は最大122バイト・パスは最大99字（中央57字。2026-10-06 に測った）</item>
/// <item>Windows のふつうのパスの上限は260字（MAX_PATH）で、プロジェクトの場所の分もそこに入る。
/// 長いパスを許す設定なら32,767字まで置けるが、そこまで長いアセットのパスは実物に無い</item>
/// <item>実物の10倍・MAX_PATH の約4倍の 1,024字 をパスの上限にする。項目の大きさは、全部3バイトの字でも入り、
/// 古い書き出しが付ける2行目（<c>00</c>）を足しても収まる 4,096バイト</item>
/// </list>
/// </summary>
internal static class UnityPackagePathname
{
    /// <summary>pathname の項目の大きさの上限（バイト）。超える物は読まない。</summary>
    internal const int MaxEntryBytes = 4096;

    /// <summary>パスの字数の上限。1行目がこれより長い物は扱わない。</summary>
    internal const int MaxPathChars = 1024;

    /// <summary>1行目のパス。空・上限を超える・読めない物は null（その項目は扱わない）。</summary>
    internal static string? Read(TarEntry entry)
    {
        if (entry.DataStream is not { } data || entry.Length <= 0 || entry.Length > MaxEntryBytes)
        {
            return null;
        }

        var buffer = new byte[(int)entry.Length];
        var read = data.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var text = Encoding.UTF8.GetString(buffer, 0, read);

        // StreamReader と同じく、頭の BOM を落とし、\r か \n で行を切る
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var end = text.AsSpan().IndexOfAny('\r', '\n');
        var line = (end < 0 ? text : text[..end]).Trim();
        return line.Length is > 0 and <= MaxPathChars ? line : null;
    }
}
